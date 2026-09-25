# asic-keytool

Reads new paid renewals from Ontraport and asks ASIC for each business name's ASIC key
through its public [online enquiry form](https://www.edge.asic.gov.au/008/inquiryV001?start/landingPage).
ASIC emails the key to the inbox Renewtron scans, and the existing inbox → PDF → Ontraport
half of the pipeline takes it from there.

## Why it's a desktop tool that opens a browser

The form is behind reCAPTCHA v3 and ASIC insists on a score of 0.5. Tokens bought from a
solving service (2Captcha, 0.9 tier) score 0.1 whichever machine submits them — ASIC
bounces every one with *"CAPTCHA validation failed, Score :0.1; minimum score require :
0.5"* — and Renewtron's Lightsail host scores as a datacenter on top of that. The token a
real browser mints on a home connection passes. So the tool drives a visible Google Chrome
(or Microsoft Edge) on the machine it runs on: the page generates its own token, the tool
picks the enquiry type, fills in the details and reads the reference number off the receipt.

The browser runs with its own profile in `%APPDATA%\Renewtron\asic-keytool-browser`, so it
never touches your everyday Chrome and its cookies build up between runs. A window opens
for each enquiry and closes when it's done; leave it alone while it works. The old 2Captcha
path is still there under **Settings → Submit via** in case it ever starts scoring again.

## To do from Renewtron

Renewtron keeps its own list of ASIC key requests (one per paid sale whose contact has no
key) and never submits them itself — ASIC's captcha refuses the server's address. Menu →
**To do from Renewtron** logs in with your admin account (Settings → Renewtron server), shows
what's waiting, and for the one you pick prints every value for ASIC's form, box by box.
Fill the form in, then type the reference number from the receipt; the server records it and
the inbox scanner closes the request when the key email lands. Leave the reference blank to
mark it as not sent, with a note. The same list, with the same "Do it" dialog, is on the
admin site under ASIC Key Requests.

## Running it

```
dotnet run --project Asic.KeyTool              # interactive menu
dotnet run --project Asic.KeyTool -- --check   # Ontraport, egress IP, browser + token
dotnet run --project Asic.KeyTool -- --sync    # request a key for every new sale, no prompts
dotnet run --project Asic.KeyTool -- --sync 1  # the same, but stop after one enquiry
```

To hand someone a folder to run:

```
dotnet publish Asic.KeyTool -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

That gives `Asic.KeyTool/bin/Release/net10.0/win-x64/publish/` with `asic-keytool.exe`,
`appsettings.json` and a `.playwright` folder. **Ship the whole folder** — the browser driver
lives in `.playwright` and the exe won't start a browser without it. The machine needs the
.NET 10 runtime and Google Chrome or Microsoft Edge installed.

## First run

Menu → **Settings** → the Ontraport App ID and API key. Those two are the only things the
tool can't work out for itself — copy them from the server's own configuration, since
`/api/admin/settings` masks every secret to its last four characters. Everything else
already matches production: the key delivery address in the enquiry text is the inbox the
scanner reads, and the enquiry wording is the one Renewtron used. Leaving the proxy blank
means "use this machine's connection", which is the point.

Run **Check connection** first. The browser row starts Chrome (or Edge), loads ASIC's form
and waits for it to mint a token — the first launch creates the profile and can take a
minute, so give it that. No 2Captcha key is needed in browser mode.

## Two email addresses

ASIC's form has a contact email box, and the enquiry text separately says where to send the
key. The tool fills them differently:

- **Reply to** (the form's contact email) is the **client's own address**, read from the
  Ontraport contact with each sale. ASIC's acknowledgement and any questions about the
  enquiry go to them. **Type in one enquiry** asks for it.
- **Key delivery email** (Settings) is `businessnamerenewals@gmail.com`, the inbox Renewtron
  scans. It only appears inside the enquiry text as `{Email}`, so the key itself still lands
  where the pipeline can pick it up.

A sale whose Ontraport contact has no email address is held back and marked
*contact has no email address* in the list, since ASIC won't accept the form without one.

Settings are saved to `%APPDATA%\Renewtron\asic-keytool.json`, not into the install folder,
so publishing over the top never clobbers the keys. `appsettings.json` beside the exe holds
the shipped defaults. To avoid storing secrets on disk, set `ASIC_KEYTOOL_ONTRAPORT_APPID`,
`ASIC_KEYTOOL_ONTRAPORT_KEY`, `ASIC_KEYTOOL_2CAPTCHA_KEY` and `ASIC_KEYTOOL_PROXY_URL` in
the environment instead — those win over the saved file.

**Check connection** prints how many paid sales Ontraport has, how many haven't been asked
about yet, and the IP ASIC will see. If that IP is a datacenter address the captcha will be
refused, so either run the tool somewhere else or put a residential proxy in Settings.

## What counts as a new sale

**New sales from Ontraport** pulls the most recent paid contacts — `Stripe Payment Recieved`
(`f5194`) = `yes`, newest activity first, the same query Renewtron's sales sync uses — and
keeps the ones that have a business name (`f5062`) and an ABN (`f5063`). The contact's email
becomes the form's reply-to address (see above).

Held back, with the reason shown in the count line:

- the payment field no longer reads `yes` (a dispute),
- the cancel field (`f5418`) is set — the customer is cancelling, not renewing,
- Ontraport has a refund on file,
- the amount paid is under **Minimum amount paid**, if that guard is switched on (it is off
  by default; set it to the one-year renewal fee to catch customers who only paid the
  cancellation fee).

A sale stays "new" until ASIC accepts a request for it. That record lives in
`%APPDATA%\Renewtron\asic-keytool-history.json`, written after every single request so an
interrupted run can't ask ASIC for the same name twice. A failed request comes back around
next time, tagged with when it was last tried. **Recent requests** shows the last 15.

The list is pre-selected: press enter to send them all, or space to deselect. **Type in one
enquiry** is there for a name that isn't in Ontraport.

## Failures

If ASIC rejects the browser's token the tool reloads the page for a fresh one, up to
`MaxCaptchaAttempts` (default 3) times. Failures keep ASIC's own wording, which tells you
which problem you have:

| ASIC says | What it means |
| --- | --- |
| `Score :0.1; minimum score require : 0.5` | Google scored the browser as a bot. Turn off any VPN, run from a home connection, and consider signing the tool's browser profile into a Google account once. |
| `never produced a reCAPTCHA token` | google.com isn't reachable from the browser, or the first launch was slow — run Check connection again. |
| A field validation message | Our data — check the ABN and business name against ASIC's register. |

When something unexpected comes back, the page is saved as
`%APPDATA%\Renewtron\asic-keytool-last-page.html` with a screenshot beside it.

In 2Captcha mode each attempt buys a token instead (`TwoCaptchaApiKey`, `MinCaptchaScore`),
and a `The response parameter is invalid` / score `0.0` reply means the token was refused
outright, so retrying only spends credit.
