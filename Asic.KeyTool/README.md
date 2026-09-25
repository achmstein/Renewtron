# asic-keytool

Sends Renewtron's ASIC key requests through ASIC's public
[online enquiry form](https://www.edge.asic.gov.au/008/inquiryV001?start/landingPage), from a
person's machine, in a browser you can watch. ASIC emails the key to the inbox Renewtron
scans, and the existing inbox → PDF → Ontraport half of the pipeline takes it from there.

## How it works

Renewtron keeps the list: one ASIC key request per paid sale whose contact has no key, from
its own Ontraport sync. It never submits them itself — the form is behind reCAPTCHA v3, ASIC
insists on a score of 0.5, and the server's Lightsail address scores 0.1. Bought captcha
tokens score 0.1 as well, whichever machine submits them.

What passes is the token a real browser mints on a home connection. So the tool logs in to
Renewtron with your admin account, fetches the list, and for each request opens a visible
Google Chrome (or Microsoft Edge) on this machine: the page generates its own token, the tool
picks the enquiry type, fills in the details, submits, reads the reference number off the
receipt and records it on the server. There is no captcha solver and nothing to buy — the
captcha is invisible and the browser handles it. Leave the window alone while it works; it
closes when it's done.

The browser runs with the tool's own profile in `%APPDATA%\Renewtron\asic-keytool-browser`.
It can't drive your everyday Chrome profile (Chromium 136 and later refuses remote control of
the default profile), so use **Sign in to Google** once: it opens that profile on Google's
sign-in page, you sign in with any Google account and close the window. From then on Google
sees a browser it knows when ASIC's page asks for a token, which is what lifts the score.

## Running it

```
dotnet run --project Asic.KeyTool              # interactive menu
dotnet run --project Asic.KeyTool -- --check   # Renewtron login, egress IP, browser + token, Google sign-in
dotnet run --project Asic.KeyTool -- --sync    # send everything on Renewtron's list, no prompts
dotnet run --project Asic.KeyTool -- --sync 1  # the same, but stop after one enquiry
```

Menu:

- **To do from Renewtron** — the list, with each row's status and last note. Pick one and
  the browser opens and sends it; pick **Do them all** and the tool works through the list,
  waiting **Pause between requests** (default 120 s) between enquiries and stopping after two
  captcha refusals in a row, leaving the rest for later. If the browser doesn't get a request
  through, the tool prints every value for ASIC's form, box by box, so you can fill it in by
  hand and type the reference number; leaving that blank records the failure on the server
  with ASIC's wording, and the row stays on the list. Ctrl+C mid-form puts the row back.
- **Type in one enquiry** — a name that isn't on Renewtron's list. Nothing is recorded on
  the server.
- **Check connection** — logs in to Renewtron, shows the IP ASIC will see, starts the
  browser, loads the form, waits for a token, and says whether the profile is signed into
  Google. The first launch creates the profile and can take a minute.
- **Sign in to Google** — see above.

To hand someone a folder to run:

```
dotnet publish Asic.KeyTool -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

That gives `Asic.KeyTool/bin/Release/net10.0/win-x64/publish/` with `asic-keytool.exe`,
`appsettings.json` and a `.playwright` folder. **Ship the whole folder** — the browser driver
lives in `.playwright` and the exe won't start a browser without it. The machine needs the
.NET 10 runtime and Google Chrome or Microsoft Edge installed.

## Settings

The only thing the tool can't work out for itself is the Renewtron admin login (**Settings →
Renewtron server**). Everything else already matches production: the key delivery address in
the enquiry text is the inbox the scanner reads, and the wording is the one Renewtron used.

Settings are read in this order, later wins:

1. `appsettings.json` beside the exe — the shipped defaults.
2. `%APPDATA%\Renewtron\asic-keytool.json` — what **Settings** saves. Outside the install
   folder so publishing over the top never clobbers the login.
3. `appsettings.Development.json` beside the exe — the developer's own copy with the login
   filled in. Gitignored, copied to the build output but never to a publish folder. When it
   exists the Settings screen says so, since it wins over anything saved there.
4. `ASIC_KEYTOOL_SERVER_EMAIL` and `ASIC_KEYTOOL_SERVER_PASSWORD` in the environment, for
   running without the login on disk.

## Two email addresses

ASIC's form has a contact email box, and the enquiry text separately says where to send the
key. The tool fills them differently:

- **Reply to** (the form's contact email) is the **client's own address**, which comes with
  each row from Renewtron. ASIC's acknowledgement and any questions about the enquiry go to
  them.
- **Key delivery email** (Settings) is `businessnamerenewals@gmail.com`, the inbox Renewtron
  scans. It only appears inside the enquiry text as `{Email}`, so the key itself still lands
  where the pipeline can pick it up.

ASIC's form also requires a phone number. A row with no usable mobile number uses **Fallback
phone** (Settings); with neither, the row is skipped and the list says why.

## Failures

If ASIC rejects the browser's token the tool waits 30 s and reloads the page for a fresh one,
up to **Token attempts** (default 3) times. Failures keep ASIC's own wording, which tells you
which problem you have:

| ASIC says | What it means |
| --- | --- |
| `Score :0.1; minimum score require : 0.5` | Google scored the browser as a bot. Sign the tool's browser into Google, turn off any VPN, and run from a home connection. |
| `never produced a reCAPTCHA token` | google.com isn't reachable from the browser, or the first launch was slow — run Check connection again. |
| A field validation message | Our data — check the ABN and business name against ASIC's register. |

When something unexpected comes back, the page is saved as
`%APPDATA%\Renewtron\asic-keytool-last-page.html` with a screenshot beside it.
