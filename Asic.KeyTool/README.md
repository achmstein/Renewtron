# asic-keytool

Reads new paid renewals from Ontraport and asks ASIC for each business name's ASIC key
through its public [online enquiry form](https://www.edge.asic.gov.au/008/inquiryV001?start/landingPage).
ASIC emails the key to the inbox Renewtron scans, and the existing inbox → PDF → Ontraport
half of the pipeline takes it from there.

## Why it's a desktop tool

The form is behind reCAPTCHA v3 and ASIC hands the submitting IP to Google when it verifies
the token. The same 0.9-tier 2Captcha token that passes from a home connection scores 0.1
from a datacenter — ASIC bounces it with *"CAPTCHA validation failed, Score :0.1; minimum
score require : 0.5"*. Renewtron runs on Lightsail, so it could never submit these itself
without renting a residential proxy. Run this where the IP is already residential.

## Running it

```
dotnet run --project Asic.KeyTool            # interactive menu
dotnet run --project Asic.KeyTool -- --check # Ontraport, egress IP, 2Captcha balance
dotnet run --project Asic.KeyTool -- --sync  # request a key for every new sale, no prompts
```

To hand someone a single file instead:

```
dotnet publish Asic.KeyTool -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

The exe lands in `Asic.KeyTool/bin/Release/net10.0/win-x64/publish/asic-keytool.exe`.

## First run

Menu → **Settings** → the Ontraport App ID and API key, then the 2Captcha API key. Those
three are the only things the tool can't work out for itself — copy them from the server's
own configuration, since `/api/admin/settings` masks every secret to its last four
characters. Everything else already matches production: the reply-to address is the inbox
the scanner reads, the captcha score is 0.9 over three attempts, and the enquiry wording is
the one Renewtron used. Leaving the proxy blank means "use this machine's connection",
which is the point.

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
keeps the ones that have a business name (`f5062`) and an ABN (`f5063`).

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

## Costs and failures

Each submission buys at least one 2Captcha token (`MaxCaptchaAttempts`, default 3, caps how
many it will try per enquiry). Failures keep ASIC's own wording, which tells you which
problem you have:

| ASIC says | What it means |
| --- | --- |
| `Score :0.1; minimum score require : 0.5` | The token was weak — usually the IP. Retrying may work; changing connection definitely does. |
| `The response parameter is invalid` / score `0.0` | The token was refused outright. Retrying only spends credit. |
| A field validation message | Our data — check the ABN and business name against ASIC's register. |
