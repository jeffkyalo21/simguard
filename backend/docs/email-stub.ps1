<#
.SYNOPSIS
    Stand-in for Brevo's transactional email API, for the smoke scripts and
    manual runs. Never sends anything.

.DESCRIPTION
    Listens on the given port and answers POST /v3/smtp/email with 201. Each
    request is appended to -LogFile as one JSON line: { apiKey, body }, where
    apiKey is the api-key header and body is the JSON the API sent.

    Start the API with Brevo__ApiKey set to any value (the smoke script expects
    test-key) and Brevo__BaseUrl=http://localhost:<port>. Stop the stub with
    Ctrl+C or by stopping its process.

.EXAMPLE
    powershell -NoProfile -File backend/docs/email-stub.ps1 -Port 5391 -LogFile $env:TEMP\simguard-email.jsonl
#>
param(
    [Parameter(Mandatory)][int]$Port,
    [Parameter(Mandatory)][string]$LogFile
)

$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding($false)
$listener = New-Object System.Net.HttpListener
$listener.Prefixes.Add("http://localhost:$Port/")
$listener.Start()
Write-Host "Email stub listening on port $Port, logging to $LogFile"

try {
    while ($listener.IsListening) {
        $context = $listener.GetContext()
        $reader = New-Object System.IO.StreamReader($context.Request.InputStream, $utf8)
        $text = $reader.ReadToEnd()

        if ($context.Request.HttpMethod -eq 'POST' -and $context.Request.Url.AbsolutePath -eq '/v3/smtp/email') {
            $entry = [ordered]@{
                apiKey = $context.Request.Headers['api-key']
                body   = ($text | ConvertFrom-Json)
            }
            [System.IO.File]::AppendAllText($LogFile, (($entry | ConvertTo-Json -Depth 8 -Compress) + "`n"), $utf8)
            $context.Response.StatusCode = 201
        }
        else {
            $context.Response.StatusCode = 404
        }

        $bytes = $utf8.GetBytes('{"messageId":"stub"}')
        $context.Response.ContentType = 'application/json'
        $context.Response.OutputStream.Write($bytes, 0, $bytes.Length)
        $context.Response.Close()
    }
}
finally {
    $listener.Stop()
}
