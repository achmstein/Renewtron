using Spectre.Console;
using Spectre.Console.Rendering;

namespace Asic.KeyTool;

/// <summary>
/// ASIC key requests, sent from a person's machine through a visible browser.
///
/// ASIC's enquiry form sits behind reCAPTCHA v3 and hands the submitting IP to Google. The
/// server's datacenter address scores 0.1 against ASIC's 0.5 minimum, so Renewtron only keeps
/// the list of requests; this tool fetches that list, opens Chrome (or Edge) in front of the
/// operator, fills in and submits the form, and records ASIC's reference back on the server.
/// The captcha is invisible — a real browser on a home connection mints a token that passes,
/// nothing is solved or bought.
/// </summary>
public static class Program
{
    /// <summary>Two captcha refusals in a row mean the address is being scored down; more only pushes it lower.</summary>
    private const int StopAfterCaptchaRefusals = 2;

    private static KeyToolSettings _settings = new();
    private static RenewtronServer _server = null!;

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        _settings = KeyToolSettings.Load();
        _server = new RenewtronServer(_settings);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
            AnsiConsole.MarkupLine("[yellow]Stopping after the current request…[/]");
        };

        try
        {
            return args.Length > 0
                ? await RunNonInteractiveAsync(args, cts.Token)
                : await RunMenuAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine("[yellow]Cancelled.[/]");
            return 130;
        }
    }

    // ---- entry points -------------------------------------------------------------------

    private static async Task<int> RunNonInteractiveAsync(string[] args, CancellationToken ct)
    {
        switch (args[0].TrimStart('-', '/').ToLowerInvariant())
        {
            case "sync":
                Header();
                // "--sync 3" caps how many enquiries this run sends.
                int? limit = args.Length > 1 && int.TryParse(args[1], out var n) && n > 0 ? n : null;
                return await SendAllFromServerAsync(limit, ct) ? 0 : 1;

            case "check":
                Header();
                await CheckAsync(ct);
                return 0;

            case "submit":
                Header();
                return await SubmitFromArgsAsync(args, ct) ? 0 : 1;

            case var help:
                AnsiConsole.MarkupLine("""
                    [bold]asic-keytool[/] — ask ASIC for a business name's ASIC key, through a browser on this machine.

                      asic-keytool            interactive menu
                      asic-keytool --sync     send everything on Renewtron's list, no prompts
                      asic-keytool --sync N   the same, but stop after N enquiries
                      asic-keytool --check    Renewtron login, egress IP, browser and token
                      asic-keytool --submit --business "NAME" --abn 11111111111 --contact "Given Family" --email who@example.com --phone 0412345678
                                              one enquiry, no prompts, nothing recorded on the server

                    Renewtron keeps the list (one request per paid sale whose contact has no key)
                    and records what ASIC said; the admin site shows the same list.
                    """);
                return help is "help" or "h" or "?" ? 0 : 2;
        }
    }

    private static async Task<int> RunMenuAsync(CancellationToken ct)
    {
        Header();
        WarnIfUnconfigured();

        while (!ct.IsCancellationRequested)
        {
            var choice = AnsiConsole.Prompt(new SelectionPrompt<string>()
                .Title("What would you like to do?")
                .HighlightStyle(new Style(foreground: Color.SpringGreen3))
                .AddChoices("To do from Renewtron", "Type in one enquiry", "Check connection", "Sign in to Google", "Settings", "Quit"));

            switch (choice)
            {
                case "To do from Renewtron":
                    await ServerQueueAsync(ct);
                    break;
                case "Type in one enquiry":
                    await SingleAsync(ct);
                    break;
                case "Check connection":
                    await CheckAsync(ct);
                    break;
                case "Sign in to Google":
                    await SignInToGoogleAsync(ct);
                    break;
                case "Settings":
                    EditSettings();
                    break;
                default:
                    return 0;
            }
            AnsiConsole.WriteLine();
        }
        return 0;
    }

    // ---- Renewtron's list → ASIC ----------------------------------------------------------

    /// <summary>
    /// The requests Renewtron is waiting on. Pick one and the browser opens and sends it;
    /// pick "Do them all" and it works through the list with a pause between each. Either
    /// way ASIC's reference goes straight back to the server, where the inbox scanner takes
    /// it from there when the key email lands.
    /// </summary>
    private static async Task ServerQueueAsync(CancellationToken ct)
    {
        if (!Configured() || !ServerConfigured()) return;

        while (!ct.IsCancellationRequested)
        {
            var rows = await FetchQueueAsync(ct);
            if (rows == null) return;
            if (rows.Count == 0)
            {
                AnsiConsole.MarkupLine("[green]Nothing waiting.[/]");
                return;
            }
            ShowQueue(rows);

            var labels = rows.Select((r, i) => $"{i + 1}. {r.BusinessName} · {r.Abn}").ToList();
            var choices = labels.ToList();
            if (rows.Count > 1) choices.Add($"Do them all ({rows.Count})");
            choices.Add("Back");

            var choice = AnsiConsole.Prompt(new SelectionPrompt<string>()
                .Title("Which one? [grey](the browser opens and sends it)[/]")
                .PageSize(15)
                .MoreChoicesText("[grey](move up and down for more)[/]")
                .HighlightStyle(new Style(foreground: Color.SpringGreen3))
                .AddChoices(choices));
            if (choice == "Back") return;

            if (choice.StartsWith("Do them all", StringComparison.Ordinal))
            {
                await SendBatchAsync(rows, ct);
                continue;
            }

            await SendServerRequestAsync(rows[labels.IndexOf(choice)], label: null, manualFallback: true, ct);
            AnsiConsole.WriteLine();
        }
    }

    /// <summary>--sync: everything on the list, newest last, no prompts.</summary>
    private static async Task<bool> SendAllFromServerAsync(int? limit, CancellationToken ct)
    {
        if (!Configured() || !ServerConfigured()) return false;
        var rows = await FetchQueueAsync(ct);
        if (rows == null) return false;
        if (rows.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]Nothing waiting.[/]");
            return true;
        }
        ShowQueue(rows);
        if (limit is { } cap && rows.Count > cap)
        {
            AnsiConsole.MarkupLine($"[grey]Sending the first {cap} of {rows.Count}.[/]");
            rows = rows.Take(cap).ToList();
        }
        return await SendBatchAsync(rows, ct);
    }

    private static async Task<List<ServerRequest>?> FetchQueueAsync(CancellationToken ct)
    {
        List<ServerRequest> rows = [];
        Exception? failure = null;
        await AnsiConsole.Status().StartAsync($"Asking {Markup.Escape(_server.Host)} what needs doing…", async _ =>
        {
            try { rows = await _server.ListAsync(200, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { failure = ex; }
        });
        if (failure == null) return rows;
        AnsiConsole.MarkupLine($"[red]{Markup.Escape(failure.Message)}[/]");
        return null;
    }

    private static void ShowQueue(List<ServerRequest> rows)
    {
        var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey);
        table.AddColumns("#", "Business name", "ABN", "Contact", "Status", "Note");
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var problem = ToEnquiry(r).Problem(_settings);
            var note = problem != null
                ? $"[red]{Markup.Escape(problem)}[/]"
                : r.ErrorMessage == null ? "" : $"[grey]{Markup.Escape(Shorten(r.ErrorMessage, 44))}[/]";
            table.AddRow(
                $"[grey]{i + 1}[/]",
                Markup.Escape(r.BusinessName),
                Markup.Escape(r.Abn),
                Markup.Escape(r.ContactName),
                Markup.Escape(r.Status),
                note);
        }
        AnsiConsole.Write(table);
    }

    /// <summary>
    /// Works through the rows with the configured pause between each, and stops after two
    /// captcha refusals in a row — the rest keep for a later run, when the score has recovered.
    /// </summary>
    private static async Task<bool> SendBatchAsync(List<ServerRequest> rows, CancellationToken ct)
    {
        var sent = 0;
        var failed = 0;
        var skipped = 0;
        var started = 0;
        var refusals = 0;
        for (var i = 0; i < rows.Count; i++)
        {
            if (ct.IsCancellationRequested) break;
            var row = rows[i];
            var label = $"[{i + 1}/{rows.Count}] ";
            if (ToEnquiry(row).Problem(_settings) is { } problem)
            {
                AnsiConsole.MarkupLine($"[yellow]•[/] {Markup.Escape(label)}{Markup.Escape(row.BusinessName)} [grey]skipped — {Markup.Escape(problem)}[/]");
                skipped++;
                continue;
            }

            if (started++ > 0 && !await PauseAsync(ct)) break;
            var result = await SendServerRequestAsync(row, label, manualFallback: false, ct);
            if (result == null) { failed++; continue; }
            if (result.Success) sent++; else failed++;

            refusals = result.CaptchaRejected ? refusals + 1 : 0;
            if (refusals >= StopAfterCaptchaRefusals && i < rows.Count - 1)
            {
                AnsiConsole.MarkupLine($"[yellow]{refusals} captcha refusals in a row — stopping here; {rows.Count - i - 1} left for later.[/]");
                break;
            }
        }

        AnsiConsole.MarkupLine($"[bold]{sent}[/] submitted, [bold]{failed}[/] failed, [bold]{skipped}[/] skipped");
        return failed == 0;
    }

    /// <summary>
    /// One row: claim it on the server, send it through the browser, record the outcome.
    /// With <paramref name="manualFallback"/>, a failure prints the form values so the person
    /// can fill ASIC's page in themselves and type the reference. Returns null when the row
    /// never got as far as the browser.
    /// </summary>
    private static async Task<AsicKeyRequestResult?> SendServerRequestAsync(ServerRequest row, string? label, bool manualFallback, CancellationToken ct)
    {
        var enquiry = ToEnquiry(row);
        if (enquiry.Problem(_settings) is { } problem)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(row.BusinessName)}: {Markup.Escape(problem)}[/]");
            return null;
        }

        try
        {
            await _server.ClaimAsync(row.Id, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(row.BusinessName)}: {Markup.Escape(ex.Message)}[/]");
            return null;
        }

        AsicKeyRequestResult result;
        try
        {
            result = await SubmitAsync(enquiry, label, ct);
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C mid-form: give the row back rather than leave it "being handled" by nobody.
            await TryRequeueAsync(row);
            throw;
        }
        Report(enquiry);

        if (result.Success)
        {
            await RecordAsync(row, enquiry.ReferenceNumber!, null, ct);
            return result;
        }

        if (manualFallback)
        {
            AnsiConsole.MarkupLine("[grey]The browser didn't get it through. To do it by hand, fill in ASIC's form with this and type the reference number:[/]");
            ShowFormValues(enquiry.ToInput(_settings));
            var reference = AnsiConsole.Prompt(new TextPrompt<string>("Reference number from ASIC's receipt [grey](blank = record the failure)[/]:").AllowEmpty()).Trim();
            if (reference.Length > 0)
            {
                await RecordAsync(row, reference, null, ct);
                return AsicKeyRequestResult.Succeeded(reference, result.Attempts);
            }
        }

        await RecordAsync(row, null, enquiry.Error ?? "failed", ct);
        return result;
    }

    private static async Task RecordAsync(ServerRequest row, string? reference, string? note, CancellationToken ct)
    {
        try
        {
            await _server.RecordAsync(row.Id, reference, note, ct);
            AnsiConsole.MarkupLine(reference != null
                ? $"  [grey]recorded on {Markup.Escape(_server.Host)}[/]"
                : $"  [grey]recorded as not sent on {Markup.Escape(_server.Host)}; it stays on the list[/]");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AnsiConsole.MarkupLine(reference != null
                ? $"  [red]Could not record it on the server: {Markup.Escape(ex.Message)} — keep reference {Markup.Escape(reference)} and enter it on the admin page.[/]"
                : $"  [red]Could not record the failure on the server: {Markup.Escape(ex.Message)}[/]");
        }
    }

    private static async Task TryRequeueAsync(ServerRequest row)
    {
        try { await _server.ReleaseAsync(row.Id, CancellationToken.None); }
        catch (Exception ex) { AnsiConsole.MarkupLine($"[yellow]Could not put {Markup.Escape(row.BusinessName)} back in the queue: {Markup.Escape(ex.Message)}[/]"); }
    }

    private static Enquiry ToEnquiry(ServerRequest row) => new()
    {
        BusinessName = row.BusinessName,
        Abn = row.Abn,
        Email = row.Email,
        Phone = row.Phone ?? "",
        GivenNames = row.GivenNames,
        FamilyName = row.FamilyName,
    };

    /// <summary>Everything for the form, box by box, in the order ASIC's page shows them.</summary>
    private static void ShowFormValues(AsicKeyRequestInput input)
    {
        AnsiConsole.Write(new Panel(new Rows(
                new Markup("[grey]My question is about a:[/] Business Name"),
                new Markup("[grey]I would like to know how to:[/] Maintain information"),
                new Text(""),
                Field("My question is", input.Question),
                Field("Entity number", input.Abn),
                Field("Entity name", input.BusinessName),
                Field("Your given names", input.GivenNames),
                Field("Your family name", input.FamilyName),
                Field("Your telephone number", $"{input.PhonePrefix} {input.PhoneNumber}".Trim()),
                Field("Your email address", input.Email),
                new Markup("[grey]Attach any documents:[/] No · tick both declarations")))
            .Header(" Fill in ASIC's form with this ")
            .BorderColor(Color.Grey));
        AnsiConsole.MarkupLine($"[grey]{BrowserAsicKeyRequestClient.LandingUrl}[/]\n");
    }

    /// <summary>The pause between two enquiries in a run. False when Ctrl+C ended the wait.</summary>
    private static async Task<bool> PauseAsync(CancellationToken ct)
    {
        var seconds = _settings.PauseBetweenRequestsSeconds;
        if (seconds <= 0) return true;
        try
        {
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(new Style(foreground: Color.Grey))
                .StartAsync($"[grey]Waiting {seconds} s before the next one…[/]", _ => Task.Delay(TimeSpan.FromSeconds(seconds), ct));
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    // ---- one-off enquiries ----------------------------------------------------------------

    /// <summary>
    /// One enquiry from the command line, for a scripted test where there is no one to
    /// answer prompts. Nothing is recorded on the server.
    /// </summary>
    private static async Task<bool> SubmitFromArgsAsync(string[] args, CancellationToken ct)
    {
        if (!Configured()) return false;

        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < args.Length - 1; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal)) options[args[i][2..]] = args[++i];
        }

        var enquiry = new Enquiry
        {
            BusinessName = options.GetValueOrDefault("business", ""),
            Abn = options.GetValueOrDefault("abn", ""),
            Email = options.GetValueOrDefault("email", ""),
            Phone = options.GetValueOrDefault("phone", ""),
        };
        enquiry.SetContactName(options.GetValueOrDefault("contact", ""));

        if (enquiry.Problem(_settings) is { } problem)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(problem)}[/] — usage: --submit --business NAME --abn N --contact \"Given Family\" --email E --phone P (ASIC requires a phone)");
            return false;
        }

        var input = enquiry.ToInput(_settings);
        AnsiConsole.MarkupLine($"[grey]{Markup.Escape(input.BusinessName)} · ABN {input.Abn} · {Markup.Escape(input.GivenNames)} {Markup.Escape(input.FamilyName)} · reply to {Markup.Escape(input.Email)}[/]");

        await SubmitAsync(enquiry, label: null, ct);
        Report(enquiry);
        return enquiry.Submitted;
    }

    /// <summary>A name that isn't on Renewtron's list. Nothing is recorded on the server.</summary>
    private static async Task SingleAsync(CancellationToken ct)
    {
        if (!Configured()) return;

        var enquiry = new Enquiry
        {
            BusinessName = AnsiConsole.Prompt(new TextPrompt<string>("Business name:")
                .Validate(v => v.Trim().Length > 0 ? ValidationResult.Success() : ValidationResult.Error("[red]Required[/]"))),
            Abn = AnsiConsole.Prompt(new TextPrompt<string>("ABN:")
                .Validate(v => Enquiry.DigitsOnly(v).Length == 11
                    ? ValidationResult.Success()
                    : ValidationResult.Error($"[red]Expected 11 digits, got {Enquiry.DigitsOnly(v).Length}[/]"))),
        };
        enquiry.SetContactName(AnsiConsole.Prompt(new TextPrompt<string>("Contact name:")
            .Validate(v => v.Trim().Contains(' ')
                ? ValidationResult.Success()
                : ValidationResult.Error("[red]Given and family name, e.g. Jane Smith[/]"))));
        enquiry.Email = AnsiConsole.Prompt(new TextPrompt<string>("Client's email (ASIC replies here):")
            .Validate(v => Enquiry.LooksLikeEmail(v) ? ValidationResult.Success() : ValidationResult.Error("[red]Not an email address[/]"))).Trim();
        enquiry.Phone = AnsiConsole.Prompt(new TextPrompt<string>("Phone:")
            .AllowEmpty()
            .DefaultValue(_settings.DefaultPhoneNumber.Length > 0
                ? $"{_settings.DefaultPhonePrefix}{_settings.DefaultPhoneNumber}"
                : "")
            .ShowDefaultValue(_settings.DefaultPhoneNumber.Length > 0));

        if (enquiry.Problem(_settings) is { } problem)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(problem)}[/]");
            return;
        }

        var input = enquiry.ToInput(_settings);
        AnsiConsole.Write(new Panel(new Rows(
                Field("Entity name", input.BusinessName),
                Field("Entity number", input.Abn),
                Field("Contact", $"{input.GivenNames} {input.FamilyName}"),
                Field("Phone", $"{input.PhonePrefix} {input.PhoneNumber}".Trim()),
                Field("Reply to", input.Email),
                Field("Key emailed to", _settings.RequestEmail),
                new Text(""),
                new Markup($"[grey]{Markup.Escape(input.Question)}[/]")))
            .Header(" This goes to ASIC ")
            .BorderColor(Color.Grey));

        if (!AnsiConsole.Confirm("Send it?")) return;

        await SubmitAsync(enquiry, label: null, ct);
        Report(enquiry);
    }

    /// <summary>One enquiry = one browser window = one ASIC session, so nothing carries between rows.</summary>
    private static async Task<AsicKeyRequestResult> SubmitAsync(Enquiry enquiry, string? label, CancellationToken ct)
    {
        // The label is "[1/50] " — square brackets are Spectre markup, so it must be escaped too.
        var title = $"{Markup.Escape(label ?? "")}{Markup.Escape(enquiry.BusinessName)}";
        AsicKeyRequestResult result = null!;

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(new Style(foreground: Color.SpringGreen3))
            .StartAsync($"{title} — starting…", async ctx =>
            {
                void OnProgress(string message) => ctx.Status($"{title} — {Markup.Escape(message)}…");
                result = await new BrowserAsicKeyRequestClient(_settings.BrowserChannel, OnProgress)
                    .SubmitAsync(enquiry.ToInput(_settings), _settings.MaxTokenAttempts, ct);
            });

        enquiry.Submitted = result.Success;
        enquiry.ReferenceNumber = result.ReferenceNumber;
        enquiry.Error = result.ErrorMessage;
        enquiry.Attempts += result.Attempts;
        return result;
    }

    private static void Report(Enquiry enquiry)
    {
        var attempts = enquiry.Attempts == 1 ? "1 attempt" : $"{enquiry.Attempts} attempts";
        if (enquiry.Submitted)
        {
            AnsiConsole.MarkupLine(
                $"[green]✔[/] {Markup.Escape(enquiry.BusinessName)} — reference [bold]{Markup.Escape(enquiry.ReferenceNumber ?? "")}[/] [grey]({attempts})[/]");
            return;
        }

        AnsiConsole.MarkupLine($"[red]✘[/] {Markup.Escape(enquiry.BusinessName)} [grey]({attempts})[/]");
        AnsiConsole.MarkupLine($"  [red]{Markup.Escape(enquiry.Error ?? "failed")}[/]");

        // ASIC's own wording says which problem this is: a low score is about this browser
        // and this connection, not about the data.
        if (enquiry.Error?.Contains("minimum score", StringComparison.OrdinalIgnoreCase) == true)
            AnsiConsole.MarkupLine("  [yellow]Google scored the browser as a bot. Sign the tool's browser into Google (menu → Sign in to Google), turn off any VPN, and try again from a home connection.[/]");
    }

    // ---- browser profile ------------------------------------------------------------------

    /// <summary>
    /// Google's score is about the browser it sees. A profile that is signed into a Google
    /// account is one it knows; this opens the tool's profile on the sign-in page once.
    /// </summary>
    private static async Task SignInToGoogleAsync(CancellationToken ct)
    {
        AnsiConsole.MarkupLine("[grey]A browser window opens on Google's sign-in page. Sign in with any Google account, then close the window. This is the browser the tool uses for ASIC's form, not your everyday one.[/]");
        var (browser, signedIn, error) = await new BrowserAsicKeyRequestClient(_settings.BrowserChannel).SignInAsync(ct);
        if (error != null)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(error)}[/]");
            return;
        }
        AnsiConsole.MarkupLine(signedIn
            ? $"[green]✔[/] {Markup.Escape(browser)} window closed — whatever you signed into is kept in the tool's profile."
            : $"[yellow]•[/] {Markup.Escape(browser)} window closed.");
    }

    private static async Task CheckAsync(CancellationToken ct)
    {
        var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey).HideHeaders();
        table.AddColumns("", "");

        await AnsiConsole.Status().StartAsync("Checking…", async ctx =>
        {
            ctx.Status($"Logging in to {Markup.Escape(_server.Host)}…");
            if (_server.IsConfigured)
            {
                try
                {
                    var rows = await _server.ListAsync(200, ct);
                    table.AddRow("[green]✔[/] Renewtron", $"{Markup.Escape(_server.Host)} · [bold]{rows.Count}[/] request(s) waiting");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    table.AddRow("[red]✘[/] Renewtron", $"[red]{Markup.Escape(ex.Message)}[/]");
                }
            }
            else
            {
                table.AddRow("[yellow]•[/] Renewtron", "[yellow]no admin login set (Settings)[/]");
            }

            ctx.Status("Asking what IP ASIC will see…");
            try
            {
                var ip = await BrowserAsicKeyRequestClient.GetEgressIpAsync(ct);
                table.AddRow("[green]✔[/] Egress IP", $"{Markup.Escape(ip)} [grey](a home address passes; a datacenter or VPN address is refused)[/]");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                table.AddRow("[red]✘[/] Egress IP", $"[red]{Markup.Escape(ex.Message)}[/]");
            }

            ctx.Status("Starting the browser and loading ASIC's form…");
            var (browser, token, signedIn, error) = await new BrowserAsicKeyRequestClient(_settings.BrowserChannel).ProbeAsync(ct);
            table.AddRow(
                token ? "[green]✔[/] Browser" : "[red]✘[/] Browser",
                token
                    ? $"{Markup.Escape(browser)} [grey]opened ASIC's form and got a reCAPTCHA token[/]"
                    : $"[red]{Markup.Escape(error ?? "failed")}[/]");
            if (browser.Length > 0)
                table.AddRow(
                    signedIn ? "[green]✔[/] Google" : "[yellow]•[/] Google",
                    signedIn ? "[grey]the tool's browser is signed in[/]" : "[yellow]not signed in — menu → Sign in to Google lifts the captcha score[/]");
        });

        table.AddRow("[grey]Key emailed to[/]", Markup.Escape(_settings.RequestEmail));
        table.AddRow("[grey]Settings file[/]", Markup.Escape(KeyToolSettings.DevelopmentSettingsInUse ? KeyToolSettings.DevelopmentSettingsPath : KeyToolSettings.UserSettingsPath));
        AnsiConsole.Write(table);
    }

    // ---- settings -----------------------------------------------------------------------

    private static void EditSettings()
    {
        if (KeyToolSettings.DevelopmentSettingsInUse)
            AnsiConsole.MarkupLine($"[yellow]appsettings.Development.json is in use and wins over anything saved here:[/] [grey]{Markup.Escape(KeyToolSettings.DevelopmentSettingsPath)}[/]");

        while (true)
        {
            var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey);
            table.AddColumns("Setting", "Value");
            table.AddRow("Renewtron server", Markup.Escape(_settings.ServerUrl));
            table.AddRow("Renewtron login", _settings.ServerEmail.Length == 0 ? "[grey]not set[/]" : $"{Markup.Escape(_settings.ServerEmail)} / {Mask(_settings.ServerPassword)}");
            table.AddRow("Browser", _settings.BrowserChannel.Length == 0 ? "[grey]Chrome, then Edge[/]" : BrowserAsicKeyRequestClient.Describe(_settings.BrowserChannel));
            table.AddRow("Token attempts", _settings.MaxTokenAttempts.ToString());
            table.AddRow("Pause between requests", $"{_settings.PauseBetweenRequestsSeconds} s");
            table.AddRow("Key delivery email", Markup.Escape(_settings.RequestEmail));
            table.AddRow("Fallback phone", Markup.Escape($"{_settings.DefaultPhonePrefix} {_settings.DefaultPhoneNumber}".Trim()));
            table.AddRow("Enquiry text", Markup.Escape(Shorten(_settings.MessageTemplate, 60)));
            AnsiConsole.Write(table);

            var choice = AnsiConsole.Prompt(new SelectionPrompt<string>()
                .Title("Change what?")
                .HighlightStyle(new Style(foreground: Color.SpringGreen3))
                .AddChoices("Renewtron server", "Browser", "Token attempts", "Pause between requests",
                            "Key delivery email", "Fallback phone", "Enquiry text", "Back"));

            switch (choice)
            {
                case "Renewtron server":
                    _settings.ServerUrl = AnsiConsole.Prompt(new TextPrompt<string>("Renewtron URL:").DefaultValue(_settings.ServerUrl)).Trim();
                    _settings.ServerEmail = AnsiConsole.Prompt(new TextPrompt<string>("Admin email:").DefaultValue(_settings.ServerEmail).AllowEmpty()).Trim();
                    _settings.ServerPassword = AnsiConsole.Prompt(new TextPrompt<string>("Admin password:").Secret().AllowEmpty());
                    _server = new RenewtronServer(_settings);
                    break;

                case "Browser":
                    var pick = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("Browser to drive:")
                        .AddChoices("Chrome, then Edge", "Google Chrome", "Microsoft Edge"));
                    _settings.BrowserChannel = pick switch { "Google Chrome" => "chrome", "Microsoft Edge" => "msedge", _ => "" };
                    break;

                case "Token attempts":
                    AnsiConsole.MarkupLine("[grey]Page reloads to try when ASIC scores the token too low, half a minute apart.[/]");
                    _settings.MaxTokenAttempts = AnsiConsole.Prompt(new TextPrompt<int>("Attempts per enquiry:")
                        .DefaultValue(_settings.MaxTokenAttempts)
                        .Validate(v => v is >= 1 and <= 5 ? ValidationResult.Success() : ValidationResult.Error("[red]1 to 5[/]")));
                    break;

                case "Pause between requests":
                    AnsiConsole.MarkupLine("[grey]Seconds between two enquiries in one run. Back-to-back submissions drag the captcha score down; 0 = none.[/]");
                    _settings.PauseBetweenRequestsSeconds = AnsiConsole.Prompt(new TextPrompt<int>("Seconds:")
                        .DefaultValue(_settings.PauseBetweenRequestsSeconds)
                        .Validate(v => v is >= 0 and <= 3600 ? ValidationResult.Success() : ValidationResult.Error("[red]0 to 3600[/]")));
                    break;

                case "Key delivery email":
                    AnsiConsole.MarkupLine("[grey]Goes in the enquiry text as {Email}. The form's reply-to is always the client's own address from Renewtron.[/]");
                    _settings.RequestEmail = AnsiConsole.Prompt(new TextPrompt<string>("Inbox ASIC should email the key to:")
                        .DefaultValue(_settings.RequestEmail)
                        .Validate(v => Enquiry.LooksLikeEmail(v) ? ValidationResult.Success() : ValidationResult.Error("[red]Not an email address[/]"))).Trim();
                    break;

                case "Fallback phone":
                    _settings.DefaultPhonePrefix = AnsiConsole.Prompt(
                        new TextPrompt<string>("Area code:").DefaultValue(_settings.DefaultPhonePrefix).AllowEmpty()).Trim();
                    _settings.DefaultPhoneNumber = AnsiConsole.Prompt(
                        new TextPrompt<string>("Number:").DefaultValue(_settings.DefaultPhoneNumber).AllowEmpty()).Trim();
                    break;

                case "Enquiry text":
                    AnsiConsole.MarkupLine("[grey]Placeholders: {FirstName} {LastName} {Abn} {BusinessName} {Email} (key delivery inbox) {ClientEmail}[/]");
                    _settings.MessageTemplate = AnsiConsole.Prompt(new TextPrompt<string>("Enquiry:")
                        .DefaultValue(_settings.MessageTemplate).ShowDefaultValue(false)).Trim();
                    break;

                default:
                    return;
            }

            try
            {
                _settings.Save();
                AnsiConsole.MarkupLine($"[green]Saved[/] [grey]{Markup.Escape(KeyToolSettings.UserSettingsPath)}[/]\n");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AnsiConsole.MarkupLine($"[red]Could not save settings: {Markup.Escape(ex.Message)}[/]\n");
            }
        }
    }

    // ---- chrome -------------------------------------------------------------------------

    private static void Header()
    {
        AnsiConsole.Write(new Rule("[bold]ASIC key requests[/]").LeftJustified().RuleStyle(Style.Parse("grey")));
        AnsiConsole.MarkupLine("[grey]ASIC's enquiry form, sent through a browser on this machine.[/]\n");
    }

    private static void WarnIfUnconfigured()
    {
        var problem = _settings.Problem() ?? (_server.IsConfigured ? null : "No Renewtron login — Settings → Renewtron server (URL, email, password).");
        if (problem != null)
            AnsiConsole.Write(new Panel($"[yellow]{Markup.Escape(problem)}[/]").BorderColor(Color.Yellow).Header(" Not ready "));
    }

    private static bool Configured()
    {
        if (_settings.Problem() is not { } problem) return true;
        AnsiConsole.MarkupLine($"[red]{Markup.Escape(problem)}[/]");
        return false;
    }

    private static bool ServerConfigured()
    {
        if (_server.IsConfigured) return true;
        AnsiConsole.MarkupLine("[red]No Renewtron login — Settings → Renewtron server (URL, email, password).[/]");
        return false;
    }

    private static IRenderable Field(string label, string value) =>
        new Markup($"[grey]{label,-14}[/] {Markup.Escape(value)}");

    private static string Mask(string value) =>
        value.Length == 0 ? "[grey]not set[/]" : value.Length <= 4 ? "••••" : $"••••{Markup.Escape(value[^4..])}";

    private static string Shorten(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}
