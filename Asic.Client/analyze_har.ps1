# Analyze HTTPArchive.har for payment/3DS changes
$har = Get-Content "C:\Users\Admin\source\repos\achmstein\Renewtron\Asic.Client\HTTPArchive.har" -Raw | ConvertFrom-Json
Write-Host "=== HTTPArchive.har - Payment/3DS Analysis ==="
Write-Host "Total entries: $($har.log.entries.Count)"

$i = 0
foreach ($e in $har.log.entries) {
    $i++
    $url = $e.request.url

    # Show relevant payment/3DS entries (expanded patterns)
    if ($url -like '*methodurl*' -or
        $url -like '*rsa3dsauth*' -or
        $url -like '*cReqWebBased*' -or
        $url -like '*challengeSubmit*' -or
        $url -like '*3ds*' -or
        $url -like '*result*' -or
        $url -like '*payment*' -or
        $url -like '*otp*' -or
        $url -like '*acs*' -or
        $url -like '*cardinal*' -or
        $url -like '*challenge*' -or
        $url -like '*redirect*' -or
        $url -like '*handlerequest*' -or
        $url -like '*creq*') {

        $shortUrl = if ($url.Length -gt 150) { $url.Substring(0, 150) + "..." } else { $url }

        Write-Host "`n=== Entry $i ==="
        Write-Host "URL: $shortUrl"
        Write-Host "Method: $($e.request.method)"
        Write-Host "Status: $($e.response.status)"

        # Show POST data if present
        if ($e.request.postData -and $e.request.postData.params) {
            Write-Host "POST params:"
            foreach ($p in $e.request.postData.params) {
                $val = $p.value
                if ($val -and $val.Length -gt 400) { $val = $val.Substring(0, 400) + "..." }
                Write-Host "  $($p.name) = $val"
            }
        }
        elseif ($e.request.postData -and $e.request.postData.text) {
            $text = $e.request.postData.text
            if ($text.Length -gt 600) { $text = $text.Substring(0, 600) + "..." }
            Write-Host "POST body: $text"
        }

        # Show response preview for important endpoints
        if ($url -like '*challengeSubmit*' -or $url -like '*result*' -or $url -like '*redirect*' -or $url -like '*handlerequest*' -or $url -like '*challenge*') {
            $text = $e.response.content.text
            if ($text -and $text.Length -gt 0) {
                Write-Host "Response preview:"
                $preview = if ($text.Length -gt 2500) { $text.Substring(0, 2500) + "..." } else { $text }
                Write-Host $preview
            }
        }
    }
}
