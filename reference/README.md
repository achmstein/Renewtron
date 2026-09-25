# Reference code

`AsicEnquiryBrowser.cs` — the Playwright automation that filled in ASIC's online enquiry form
(https://www.edge.asic.gov.au/008/inquiryV001?start/landingPage) from the Renewtron server:
landing page → "Business Name" / "Maintain information" → details page → reference number
from the receipt. It ran headless in the server image and as Edge locally.

It was taken out of the server on 2026-09-26 because ASIC's reCAPTCHA v3 scores the
Lightsail address 0.1 against a 0.5 minimum, so the server now only keeps the queue and a
person submits the form. The same automation lives on in the desktop tool
(`Asic.KeyTool/Asic/BrowserAsicKeyRequestClient.cs`). Kept here for reuse against the same
site; it's not part of any project build.
