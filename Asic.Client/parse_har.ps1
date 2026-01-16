# Analyze HTTPArchive4.har - focus on challengeSubmit and acsTransID issue
$har = Get-Content "C:\Users\Admin\source\repos\achmstein\Renewtron\Asic.Client\HTTPArchive4.har" -Raw | ConvertFrom-Json
Write-Host "=== HTTPArchive4.har - challengeSubmit analysis ==="

$i = 0
foreach ($e in $har.log.entries) {
    $i++
    $url = $e.request.url

    # Only show relevant 3DS/payment entries
    if ($url -like '*methodurl*' -or
        $url -like '*rsa3dsauth*' -or
        $url -like '*cReqWebBased*' -or
        $url -like '*challengeSubmit*' -or
        $url -like '*result*') {

        $shortUrl = if ($url.Length -gt 120) { $url.Substring(0, 120) + "..." } else { $url }

        Write-Host "`n=== Entry $i ==="
        Write-Host "URL: $shortUrl"
        Write-Host "Method: $($e.request.method)"
        Write-Host "Status: $($e.response.status)"

        # Show POST data if present
        if ($e.request.postData -and $e.request.postData.params) {
            Write-Host "POST params:"
            foreach ($p in $e.request.postData.params) {
                $val = $p.value
                if ($val -and $val.Length -gt 200) { $val = $val.Substring(0, 200) + "..." }
                Write-Host "  $($p.name) = $val"
            }
        }

        # Show response preview
        $text = $e.response.content.text
        if ($text -and $text.Length -gt 0) {
            Write-Host "Response preview:"
            Write-Host $text.Substring(0, [Math]::Min(2000, $text.Length))
        }
    }
}
