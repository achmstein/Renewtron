using Spectre.Console;
using Spectre.Console.Rendering;

namespace Asic.KeyTool;

/// <summary>
/// ASIC key requests, run by hand from a residential connection.
///
/// ASIC's enquiry form sits behind reCAPTCHA v3 and hands the submitting IP to Google, so
/// the same 0.9-tier 2Captcha token that passes from a home line scores 0.1 from a
/// datacenter and ASIC rejects it ("minimum score require : 0.5"). That is why this is a
/// desktop tool and not a server job: run it where the IP is residential.
///
/// New sales come from Ontraport directly — the same paid contacts Renewtron's sales sync
/// reads — and what's already been asked for is remembered in a local history file.
/// </summary>
public static class Program
{
    private static KeyToolSettings _settings = new();
    private static TwoCaptchaSolver _solver = null!;
    private static OntraportClient _ontraport = null!;

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        _settings = KeyToolSettings.Load();
        _solver = new TwoCaptchaSolver(_settings);
        _ontraport = new OntraportClient(_settings);

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
                // "--sync 3" caps how many enquiries this run sends (newest first); each one
                // buys at least one captcha token, so a first run should be small.
                int? limit = args.Length > 1 && int.TryParse(args[1], out var n) && n > 0 ? n : null;
                return await SyncAsync(pick: false, ct, limit) ? 0 : 1;

            case "check":
                Header();
                await CheckAsync(ct);
                return 0;

            case var help:
                AnsiConsole.MarkupLine("""
                    [bold]asic-keytool[/] — ask ASIC for a business name's ASIC key.

                      asic-keytool            interactive menu
                      asic-keytool --sync     request a key for every new paid sale in Ontraport
                      asic-keytool --sync N   the same, but stop after N enquiries
                      asic-keytool --check    Ontraport, egress IP and 2Captcha balance

                    A sale counts as new until ASIC accepts a request for it; that's kept in
                    %APPDATA%\Renewtron\asic-keytool-history.json.
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
                .AddChoices("New sales from Ontraport", "Type in one enquiry", "Recent requests", "Check connection", "Settings", "Quit"));

            switch (choice)
            {
                case "New sales from Ontraport":
                    await SyncAsync(pick: true, ct);
                    break;
                case "Type in one enquiry":
                    await SingleAsync(ct);
                    break;
                case "Recent requests":
                    ShowHistory();
                    break;
                case "Check connection":
                    await CheckAsync(ct);
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

    // ---- Ontraport → ASIC ---------------------------------------------------------------

    /// <param name="pick">Interactive: show the list and let the operator choose. Off: send them all.</param>
    /// <param name="limit">Non-interactive cap on how many to send this run; null = all.</param>
    private static async Task<bool> SyncAsync(bool pick, CancellationToken ct, int? limit = null)
    {
        if (!Configured()) return false;
        if (!_ontraport.IsConfigured)
        {
            AnsiConsole.MarkupLine("[red]No Ontraport API credentials — Settings → Ontraport App ID / API key.[/]");
            return false;
        }

        List<Sale> sales = [];
        Exception? failure = null;
        await AnsiConsole.Status().StartAsync("Reading new sales from Ontraport…", async _ =>
        {
            try { sales = await _ontraport.FetchPaidSalesAsync(ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { failure = ex; }
        });
        if (failure != null)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(failure.Message)}[/]");
            return false;
        }

        var history = History.Load();
        var heldBack = sales.Count(s => s.IneligibleReason != null);
        var candidates = sales
            .Where(s => s.IneligibleReason == null && !history.AlreadyRequested(s))
            .ToList();

        AnsiConsole.MarkupLine(
            $"[grey]{sales.Count} paid sale(s) in Ontraport · {heldBack} held back (cancelled, refunded or disputed) · " +
            $"{sales.Count - heldBack - candidates.Count} already requested[/]");

        if (candidates.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]Nothing new to ask ASIC for.[/]");
            return true;
        }

        var enquiries = candidates.Select(s => s.ToEnquiry()).ToList();
        var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey);
        table.AddColumns("#", "Business name", "ABN", "Contact", "Reply to", "Due", "");
        for (var i = 0; i < candidates.Count; i++)
        {
            var problem = enquiries[i].Problem();
            var previous = history.LastFailure(candidates[i]);
            var note = problem != null
                ? $"[red]{Markup.Escape(problem)}[/]"
                : previous != null
                    ? $"[yellow]tried {previous.At.ToLocalTime():d MMM}[/]"
                    : "";
            table.AddRow(
                $"[grey]{i + 1}[/]",
                Markup.Escape(candidates[i].BusinessName),
                Markup.Escape(candidates[i].Abn),
                Markup.Escape(candidates[i].ContactName),
                candidates[i].Email.Length > 0 ? Markup.Escape(candidates[i].Email) : "[grey]—[/]",
                candidates[i].RenewalDueDate?.ToLocalTime().ToString("d MMM yyyy") ?? "[grey]—[/]",
                note);
        }
        AnsiConsole.Write(table);

        var sendable = enquiries.Where(e => e.Problem() == null).ToList();
        if (sendable.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]Nothing to send — every new sale has a problem with its details.[/]");
            return false;
        }

        if (pick)
        {
            // Numbered so two contacts holding the same business name can't collide.
            var labels = sendable
                .Select((e, i) => (Label: $"{i + 1}. {e.BusinessName} · {e.Abn}", Enquiry: e))
                .ToDictionary(x => x.Label, x => x.Enquiry, StringComparer.Ordinal);
            var prompt = new MultiSelectionPrompt<string>()
                .Title($"Send which? [grey](space to toggle, enter to send — each buys at least one captcha token)[/]")
                .NotRequired()
                .PageSize(15)
                .MoreChoicesText("[grey](move up and down for more)[/]")
                .HighlightStyle(new Style(foreground: Color.SpringGreen3));
            foreach (var label in labels.Keys)
                prompt.AddChoice(label).Select();

            var chosen = AnsiConsole.Prompt(prompt);
            sendable = chosen.Select(l => labels[l]).ToList();
            if (sendable.Count == 0)
            {
                AnsiConsole.MarkupLine("[grey]Nothing selected.[/]");
                return true;
            }
        }

        if (limit is { } cap && sendable.Count > cap)
        {
            AnsiConsole.MarkupLine($"[grey]Sending the first {cap} of {sendable.Count}.[/]");
            sendable = sendable.Take(cap).ToList();
        }

        var sent = 0;
        foreach (var (enquiry, index) in sendable.Select((e, i) => (e, i)))
        {
            if (ct.IsCancellationRequested) break;
            await SubmitAsync(enquiry, $"[{index + 1}/{sendable.Count}] ", ct);
            Report(enquiry);
            if (enquiry.Submitted) sent++;

            // Written after every request, not at the end: a crash (or Ctrl+C) must not lose
            // the record of something ASIC has already accepted.
            history.Record(enquiry);
            TrySave(history);
        }

        var failed = sendable.Count - sent;
        AnsiConsole.MarkupLine(
            $"[bold]{sent}[/] submitted, [bold]{failed}[/] failed · " +
            $"[grey]{sendable.Sum(e => e.CaptchaSolves)} {(_settings.UsesBrowser ? "page load(s)" : "captcha token(s) spent")}[/]");
        return failed == 0;
    }

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

        var history = History.Load();
        history.Record(enquiry);
        TrySave(history);
    }

    /// <summary>One enquiry = one client = one ASIC session, so nothing carries between rows.</summary>
    private static async Task SubmitAsync(Enquiry enquiry, string? label, CancellationToken ct)
    {
        // The label is "[1/50] " — square brackets are Spectre markup, so it must be escaped too.
        var title = $"{Markup.Escape(label ?? "")}{Markup.Escape(enquiry.BusinessName)}";

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(new Style(foreground: Color.SpringGreen3))
            .StartAsync($"{title} — starting…", async ctx =>
            {
                void OnProgress(string message) => ctx.Status($"{title} — {Markup.Escape(message)}…");
                _solver.Progress += OnProgress;
                try
                {
                    var result = _settings.UsesBrowser
                        ? await new BrowserAsicKeyRequestClient(_settings.BrowserChannel, OnProgress).SubmitAsync(
                            enquiry.ToInput(_settings),
                            _settings.MaxCaptchaAttempts,
                            ct)
                        : await new AsicKeyRequestClient(_solver).SubmitAsync(
                            enquiry.ToInput(_settings),
                            _settings.MinCaptchaScore,
                            _settings.MaxCaptchaAttempts,
                            _settings.ProxyUrl,
                            ct);

                    enquiry.Submitted = result.Success;
                    enquiry.ReferenceNumber = result.ReferenceNumber;
                    enquiry.Error = result.ErrorMessage;
                    enquiry.CaptchaSolves += result.CaptchaAttempts;
                }
                finally
                {
                    _solver.Progress -= OnProgress;
                }
            });
    }

    private static void Report(Enquiry enquiry)
    {
        var unit = _settings.UsesBrowser ? "attempt" : "token";
        var solves = enquiry.CaptchaSolves == 1 ? $"1 {unit}" : $"{enquiry.CaptchaSolves} {unit}s";
        if (enquiry.Submitted)
        {
            AnsiConsole.MarkupLine(
                $"[green]✔[/] {Markup.Escape(enquiry.BusinessName)} — reference [bold]{Markup.Escape(enquiry.ReferenceNumber ?? "")}[/] [grey]({solves})[/]");
            return;
        }

        AnsiConsole.MarkupLine($"[red]✘[/] {Markup.Escape(enquiry.BusinessName)} [grey]({solves})[/]");
        AnsiConsole.MarkupLine($"  [red]{Markup.Escape(enquiry.Error ?? "failed")}[/]");

        // ASIC's own wording says which problem this is: a low score can pass on a retry,
        // a refused token means the connection (or the key) is what needs fixing.
        if (enquiry.Error?.Contains("minimum score", StringComparison.OrdinalIgnoreCase) == true)
            AnsiConsole.MarkupLine(_settings.UsesBrowser
                ? "  [yellow]Google scored the browser as a bot. Try again from a home connection with no VPN; signing the browser profile into a Google account also helps.[/]"
                : "  [yellow]Bought tokens are scoring too low. Switch Settings → Submit via to Browser, which uses this machine's own Chrome.[/]");
    }

    private static void ShowHistory()
    {
        var history = History.Load();
        if (history.Entries.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]Nothing requested from this machine yet.[/]");
            return;
        }

        var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey);
        table.AddColumns("When", "Business name", "Reference", "");
        foreach (var entry in history.Entries.OrderByDescending(e => e.At).Take(15))
            table.AddRow(
                entry.At.ToLocalTime().ToString("d MMM HH:mm"),
                Markup.Escape(entry.BusinessName),
                entry.Submitted ? $"[green]{Markup.Escape(entry.ReferenceNumber ?? "")}[/]" : "[red]—[/]",
                entry.Submitted ? "" : $"[red]{Markup.Escape(Shorten(entry.Error ?? "failed", 60))}[/]");
        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"[grey]{history.Entries.Count} in total · {Markup.Escape(History.Path)}[/]");
    }

    private static async Task CheckAsync(CancellationToken ct)
    {
        var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey).HideHeaders();
        table.AddColumns("", "");

        var client = new AsicKeyRequestClient(_solver);
        await AnsiConsole.Status().StartAsync("Checking…", async ctx =>
        {
            ctx.Status("Asking Ontraport for paid sales…");
            if (_ontraport.IsConfigured)
            {
                try
                {
                    var sales = await _ontraport.FetchPaidSalesAsync(ct);
                    var history = History.Load();
                    var fresh = sales.Count(s => s.IneligibleReason == null && !history.AlreadyRequested(s));
                    table.AddRow("[green]✔[/] Ontraport", $"{sales.Count} paid sale(s), [bold]{fresh}[/] not yet requested");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    table.AddRow("[red]✘[/] Ontraport", $"[red]{Markup.Escape(ex.Message)}[/]");
                }
            }
            else
            {
                table.AddRow("[yellow]•[/] Ontraport", "[yellow]no API credentials set (Settings)[/]");
            }

            ctx.Status("Asking what IP ASIC will see…");
            try
            {
                var ip = await client.GetEgressIpAsync(_settings.ProxyUrl, ct);
                var via = string.IsNullOrWhiteSpace(_settings.ProxyUrl) ? "this machine" : "the proxy";
                table.AddRow("[green]✔[/] Egress IP", $"{Markup.Escape(ip)} [grey](via {via})[/]");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                table.AddRow("[red]✘[/] Egress IP", $"[red]{Markup.Escape(ex.Message)}[/]");
            }

            if (_settings.UsesBrowser)
            {
                ctx.Status("Starting the browser and loading ASIC's form…");
                var (browser, token, error) = await new BrowserAsicKeyRequestClient(_settings.BrowserChannel).ProbeAsync(ct);
                table.AddRow(
                    token ? "[green]✔[/] Browser" : "[red]✘[/] Browser",
                    token
                        ? $"{Markup.Escape(browser)} [grey]opened ASIC's form and got a reCAPTCHA token[/]"
                        : $"[red]{Markup.Escape(error ?? "failed")}[/]");
            }
            else if (_solver.IsConfigured)
            {
                ctx.Status("Checking the 2Captcha balance…");
                try
                {
                    var balance = await _solver.GetBalanceAsync(ct: ct);
                    var style = balance < 1m ? "yellow" : "green";
                    table.AddRow($"[{style}]✔[/] 2Captcha", $"[{style}]${balance:0.00}[/] [grey]balance[/]");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    table.AddRow("[red]✘[/] 2Captcha", $"[red]{Markup.Escape(ex.Message)}[/]");
                }
            }
            else
            {
                table.AddRow("[yellow]•[/] 2Captcha", "[yellow]no API key set (Settings)[/]");
            }
        });

        table.AddRow("[grey]Submit via[/]", _settings.UsesBrowser ? "a visible browser on this machine" : "2Captcha tokens");
        table.AddRow("[grey]Key emailed to[/]", Markup.Escape(_settings.RequestEmail));
        table.AddRow("[grey]Settings file[/]", Markup.Escape(KeyToolSettings.UserSettingsPath));
        AnsiConsole.Write(table);
    }

    // ---- settings -----------------------------------------------------------------------

    private static void EditSettings()
    {
        while (true)
        {
            var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey);
            table.AddColumns("Setting", "Value");
            table.AddRow("Ontraport App ID", Mask(_settings.OntraportApiAppId));
            table.AddRow("Ontraport API key", Mask(_settings.OntraportApiKey));
            table.AddRow("Minimum amount paid", _settings.MinimumAmountPaid > 0 ? _settings.MinimumAmountPaid.ToString("0.00") : "[grey]no guard[/]");
            table.AddRow("Submit via", _settings.UsesBrowser ? "Browser (Chrome/Edge on this machine)" : "2Captcha tokens");
            table.AddRow("Browser", _settings.BrowserChannel.Length == 0 ? "[grey]Chrome, then Edge[/]" : BrowserAsicKeyRequestClient.Describe(_settings.BrowserChannel));
            table.AddRow("2Captcha API key", _settings.UsesBrowser ? "[grey]not used[/]" : Mask(_settings.TwoCaptchaApiKey));
            table.AddRow("Captcha score", _settings.UsesBrowser ? "[grey]not used[/]" : _settings.MinCaptchaScore.ToString("0.0#"));
            table.AddRow("Captcha attempts", _settings.MaxCaptchaAttempts.ToString());
            table.AddRow("Key delivery email", Markup.Escape(_settings.RequestEmail));
            table.AddRow("Fallback phone", Markup.Escape($"{_settings.DefaultPhonePrefix} {_settings.DefaultPhoneNumber}".Trim()));
            table.AddRow("Enquiry text", Markup.Escape(Shorten(_settings.MessageTemplate, 60)));
            table.AddRow("Proxy", _settings.ProxyUrl.Length == 0 ? "[grey]none (this machine's own connection)[/]" : Mask(_settings.ProxyUrl));
            AnsiConsole.Write(table);

            var choice = AnsiConsole.Prompt(new SelectionPrompt<string>()
                .Title("Change what?")
                .HighlightStyle(new Style(foreground: Color.SpringGreen3))
                .AddChoices("Ontraport App ID", "Ontraport API key", "Minimum amount paid", "Submit via", "Browser",
                            "2Captcha API key", "Captcha score", "Captcha attempts", "Key delivery email", "Fallback phone",
                            "Enquiry text", "Proxy", "Back"));

            switch (choice)
            {
                case "Ontraport App ID":
                    _settings.OntraportApiAppId = AnsiConsole.Prompt(
                        new TextPrompt<string>("Ontraport App ID:").Secret().AllowEmpty()).Trim();
                    break;

                case "Ontraport API key":
                    _settings.OntraportApiKey = AnsiConsole.Prompt(
                        new TextPrompt<string>("Ontraport API key:").Secret().AllowEmpty()).Trim();
                    break;

                case "Minimum amount paid":
                    AnsiConsole.MarkupLine("[grey]Sales paying less than this are held back. 0 turns the guard off.[/]");
                    _settings.MinimumAmountPaid = AnsiConsole.Prompt(new TextPrompt<decimal>("Minimum:")
                        .DefaultValue(_settings.MinimumAmountPaid)
                        .Validate(v => v >= 0 ? ValidationResult.Success() : ValidationResult.Error("[red]Can't be negative[/]")));
                    break;

                case "Submit via":
                    AnsiConsole.MarkupLine("[grey]Browser opens Chrome/Edge on this machine and lets it mint its own captcha token. 2Captcha buys tokens, which ASIC has been scoring 0.1.[/]");
                    _settings.SubmitVia = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("Submit via:")
                        .AddChoices(KeyToolSettings.SubmitViaBrowser, KeyToolSettings.SubmitVia2Captcha));
                    break;

                case "Browser":
                    var pick = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("Browser to drive:")
                        .AddChoices("Chrome, then Edge", "Google Chrome", "Microsoft Edge"));
                    _settings.BrowserChannel = pick switch { "Google Chrome" => "chrome", "Microsoft Edge" => "msedge", _ => "" };
                    break;

                case "2Captcha API key":
                    _settings.TwoCaptchaApiKey = AnsiConsole.Prompt(
                        new TextPrompt<string>("2Captcha API key:").Secret().AllowEmpty()).Trim();
                    break;

                case "Captcha score":
                    // ASIC needs ≥ 0.5 and 2Captcha prices by score; 0.9 is what passes.
                    _settings.MinCaptchaScore = double.Parse(AnsiConsole.Prompt(
                        new SelectionPrompt<string>().Title("Score to buy:").AddChoices("0.9", "0.7", "0.5", "0.3")));
                    break;

                case "Captcha attempts":
                    _settings.MaxCaptchaAttempts = AnsiConsole.Prompt(new TextPrompt<int>("Tokens to try per enquiry:")
                        .DefaultValue(_settings.MaxCaptchaAttempts)
                        .Validate(v => v is >= 1 and <= 5 ? ValidationResult.Success() : ValidationResult.Error("[red]1 to 5[/]")));
                    break;

                case "Key delivery email":
                    AnsiConsole.MarkupLine("[grey]Goes in the enquiry text as {Email}. The form's reply-to is always the client's own address from Ontraport.[/]");
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

                case "Proxy":
                    AnsiConsole.MarkupLine("[grey]Only needed if this machine's IP is scored as a datacenter. Blank = direct.[/]");
                    _settings.ProxyUrl = AnsiConsole.Prompt(
                        new TextPrompt<string>("http://user:pass@host:port").Secret().AllowEmpty()).Trim();
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
        AnsiConsole.MarkupLine("[grey]ASIC's enquiry form, asked from this machine's connection.[/]\n");
    }

    private static void WarnIfUnconfigured()
    {
        if (_settings.Problem() is { } problem)
            AnsiConsole.Write(new Panel($"[yellow]{Markup.Escape(problem)}[/]").BorderColor(Color.Yellow).Header(" Not ready "));
    }

    private static bool Configured()
    {
        if (_settings.Problem() is not { } problem) return true;
        AnsiConsole.MarkupLine($"[red]{Markup.Escape(problem)}[/]");
        return false;
    }

    private static void TrySave(History history)
    {
        try
        {
            history.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AnsiConsole.MarkupLine($"[red]Could not update the history file: {Markup.Escape(ex.Message)}[/]");
        }
    }

    private static IRenderable Field(string label, string value) =>
        new Markup($"[grey]{label,-14}[/] {Markup.Escape(value)}");

    private static string Mask(string value) =>
        value.Length == 0 ? "[grey]not set[/]" : value.Length <= 4 ? "••••" : $"••••{Markup.Escape(value[^4..])}";

    private static string Shorten(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}
