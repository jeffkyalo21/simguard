<#
.SYNOPSIS
    Smoke test of the scoring pipeline (issue #14). Contract in
    scoring-pipeline.md and api-tier-recommendations.md.

.DESCRIPTION
    Runs against a running Development API on a THROWAWAY database that holds
    the demo data (backend/scripts/import-demo-data.ps1) and an ML service
    started with READER=postgres and the shared secret. The script adds one
    SIM with 15 days of usage and changes tier recommendation state.

    Checks:
      - roles: Run now and scoring status are Admin only (SecurityOfficer and
        Auditor get 403, no token 401); Admin and Auditor get 403 on a tier
        run and on tier settings changes; scoring status holds totals only
      - Run now twice: the second run scores nothing, no duplicate risk
        scores or alerts, at most one open alert per SIM
      - a usage import queues a scoring run (trigger import, actor system)
        that scores the new SIM-day
      - tier interval outside 90 to 365 is rejected with 400, a change is
        audited with the old and new value
      - a tier run within the interval gets 409 with the next allowed date;
        force=true works in Development
      - dismissing a recommendation, then rerunning, does not recreate it
      - with -SecondaryBaseUrl (a second API on the same database, started
        with ASPNETCORE_ENVIRONMENT=Production, its own Jwt__Key and
        MlService__BaseUrl pointing at a closed port): force=true is ignored
        (409), and a run with the ML service down ends failed, keeps every
        score and alert, and is audited as system

    The first tier run of the database is timed. Accounts are found like
    imports-smoke.ps1 does; the SecurityOfficer secrets file is required for
    the secondary checks. Passwords, tokens and MFA secrets are never
    printed. Start the API with RateLimiting__Auth__PermitLimit=1000.

.EXAMPLE
    powershell -NoProfile -File backend/docs/scoring-smoke.ps1 -AdminSecretsFile $env:TEMP\simguard-smoke-admin.json -SecondaryBaseUrl http://localhost:5276
#>
param(
    [string]$BaseUrl = 'http://localhost:5275',
    [string]$AdminSecretsFile = (Join-Path $env:TEMP 'simguard-smoke-admin.json'),
    [string]$OfficerSecretsFile = (Join-Path $env:TEMP 'simguard-smoke-officer.json'),
    [string]$AuditorSecretsFile = (Join-Path $env:TEMP 'simguard-smoke-auditor.json'),
    [string]$SamplesDir = (Join-Path $PSScriptRoot '..\..\ml-service\data\import_samples'),
    [string]$SecondaryBaseUrl = ''
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http

$handler = New-Object System.Net.Http.HttpClientHandler
$handler.AllowAutoRedirect = $false
$client = New-Object System.Net.Http.HttpClient($handler)
$client.Timeout = [TimeSpan]::FromMinutes(10)
$script:results = New-Object System.Collections.Generic.List[object]
$script:timings = New-Object System.Collections.Generic.List[string]
$utf8 = New-Object System.Text.UTF8Encoding($false)

function Read-Response($response) {
    $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    $data = $null

    if ($text -and ($text.TrimStart().StartsWith('{') -or $text.TrimStart().StartsWith('['))) {
        $data = $text | ConvertFrom-Json
    }

    [pscustomobject]@{
        Status      = [int]$response.StatusCode
        Data        = $data
        Text        = $text
        ContentType = if ($response.Content.Headers.ContentType) { $response.Content.Headers.ContentType.MediaType } else { $null }
    }
}

function Invoke-Api {
    param(
        [string]$Method,
        [string]$Path,
        $Body = $null,
        [string]$Token = $null,
        [string]$Base = $BaseUrl
    )

    $request = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::new($Method), "$Base$Path")

    if ($Token) {
        $request.Headers.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue('Bearer', $Token)
    }

    if ($null -ne $Body) {
        $json = $Body | ConvertTo-Json -Depth 6 -Compress
        $request.Content = New-Object System.Net.Http.StringContent($json, [System.Text.Encoding]::UTF8, 'application/json')
    }

    Read-Response ($client.SendAsync($request).GetAwaiter().GetResult())
}

# POST /api/imports/{type} with the bytes as the multipart field "file".
function Invoke-Import {
    param(
        [string]$Type,
        [byte[]]$Bytes,
        [string]$FileName,
        [string]$Token,
        [string]$Query = 'mode=preview'
    )

    $request = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Post, "$BaseUrl/api/imports/$Type`?$Query")

    if ($Token) {
        $request.Headers.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue('Bearer', $Token)
    }

    $content = New-Object System.Net.Http.MultipartFormDataContent
    $file = New-Object System.Net.Http.ByteArrayContent(, $Bytes)
    $file.Headers.ContentType = New-Object System.Net.Http.Headers.MediaTypeHeaderValue('text/csv')
    $content.Add($file, 'file', $FileName)
    $request.Content = $content

    Read-Response ($client.SendAsync($request).GetAwaiter().GetResult())
}

# CSV text from lines, CRLF line ends, UTF-8 without BOM.
function New-Csv([string[]]$Lines) {
    return $utf8.GetBytes(($Lines -join "`r`n") + "`r`n")
}

function Check {
    param([string]$Step, [int]$Expected, $Result, [string]$Note = '')

    $actual = if ($Result -is [int]) { $Result } else { $Result.Status }
    $script:results.Add([pscustomobject]@{
        Step     = $Step
        Expected = $Expected
        Actual   = $actual
        Result   = if ($actual -eq $Expected) { 'PASS' } else { 'FAIL' }
        Note     = $Note
    })
}

function Check-True {
    param([string]$Step, [bool]$Condition, [string]$Note = '')

    $script:results.Add([pscustomobject]@{
        Step     = $Step
        Expected = 'true'
        Actual   = "$Condition".ToLowerInvariant()
        Result   = if ($Condition) { 'PASS' } else { 'FAIL' }
        Note     = $Note
    })
}

function Counts($r) {
    if ($null -eq $r.Data) { return "status=$($r.Status)" }
    $d = $r.Data
    return "status=$($d.status) total=$($d.totalRows) valid=$($d.validRows) ins=$($d.inserted) upd=$($d.updated) same=$($d.unchanged) err=$($d.errorCount) warn=$($d.warningCount)"
}

function Has-Error($r, [int]$Row, [string]$Column) {
    return @($r.Data.errors | Where-Object { $_.row -eq $Row -and $_.column -eq $Column }).Count -gt 0
}

# Same as auth-smoke.ps1: RFC 6238 TOTP (SHA-1, 6 digits, 30 seconds).
function Get-Totp {
    param([string]$Base32Secret)

    $alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'
    $clean = $Base32Secret.ToUpperInvariant().Replace(' ', '').TrimEnd('=')
    $bits = ''
    foreach ($c in $clean.ToCharArray()) {
        $bits += [Convert]::ToString($alphabet.IndexOf($c), 2).PadLeft(5, '0')
    }
    $keyBytes = New-Object byte[] ([math]::Floor($bits.Length / 8))
    for ($i = 0; $i -lt $keyBytes.Length; $i++) {
        $keyBytes[$i] = [Convert]::ToByte($bits.Substring($i * 8, 8), 2)
    }

    $counter = [long][math]::Floor([DateTimeOffset]::UtcNow.ToUnixTimeSeconds() / 30)
    $counterBytes = [BitConverter]::GetBytes($counter)
    [Array]::Reverse($counterBytes)

    $hmac = [System.Security.Cryptography.HMACSHA1]::new($keyBytes)
    $hash = $hmac.ComputeHash($counterBytes)
    $offset = $hash[$hash.Length - 1] -band 0x0F
    $binary = (([int]$hash[$offset] -band 0x7F) -shl 24) -bor ([int]$hash[$offset + 1] -shl 16) -bor ([int]$hash[$offset + 2] -shl 8) -bor [int]$hash[$offset + 3]
    return ($binary % 1000000).ToString('000000')
}

function New-TestPassword {
    $bytes = New-Object byte[] 18
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    return 'Sg-' + [Convert]::ToBase64String($bytes).Replace('+', 'x').Replace('/', 'y')
}

function Get-SecretFromUri([string]$Uri) {
    if ($Uri -match 'secret=([A-Z2-7]+)') { return $Matches[1] }
    throw 'The authenticator URI has no secret.'
}

function Get-Token {
    param([string]$Label, [string]$Email, [string]$Password, [string]$TotpSecret, [string]$Base = $BaseUrl)

    $login = Invoke-Api POST '/api/auth/login' @{ email = $Email; password = $Password } $null $Base
    if ($login.Status -ne 200 -or $login.Data.status -ne 'mfaRequired') {
        throw "$Label login failed (status $($login.Status))."
    }
    $verify = Invoke-Api POST '/api/auth/mfa/verify' @{ mfaToken = $login.Data.mfaToken; code = (Get-Totp $TotpSecret) } $null $Base
    if ($verify.Status -ne 200) {
        throw "$Label MFA verify failed (status $($verify.Status))."
    }
    return $verify.Data.accessToken
}

function Complete-FirstSignIn {
    param([string]$Label, [string]$Email, [string]$TemporaryPassword)

    $login = Invoke-Api POST '/api/auth/login' @{ email = $Email; password = $TemporaryPassword }
    if ($login.Status -ne 200 -or $login.Data.status -ne 'mfaEnrollmentRequired') {
        throw "$Label first login failed (status $($login.Status))."
    }
    $start = Invoke-Api POST '/api/auth/mfa/enroll/start' $null $login.Data.mfaToken
    $secret = Get-SecretFromUri $start.Data.authenticatorUri
    $confirm = Invoke-Api POST '/api/auth/mfa/enroll/confirm' @{ code = (Get-Totp $secret) } $login.Data.mfaToken
    $newPassword = New-TestPassword
    $changed = Invoke-Api POST '/api/auth/change-password' @{ currentPassword = $TemporaryPassword; newPassword = $newPassword } $confirm.Data.accessToken
    Check "$Label first sign-in (login, MFA enrolment, password change)" 200 $changed
    return [pscustomobject]@{ Token = $changed.Data.accessToken; Password = $newPassword; TotpSecret = $secret }
}

function Get-RoleToken {
    param([string]$Label, [string]$Role, [string]$SecretsFile, [string]$AdminToken)

    if (Test-Path $SecretsFile) {
        $saved = Get-Content -Raw $SecretsFile | ConvertFrom-Json
        $token = Get-Token $Label $saved.email $saved.password $saved.totpSecret
        return [pscustomobject]@{ Token = $token; Email = $saved.email }
    }

    $email = "scoring-$($Role.ToLowerInvariant())-$runId@simguard.local"
    $temp = New-TestPassword
    Check "Admin creates a throwaway $Role" 201 (Invoke-Api POST '/api/users' @{ email = $email; name = "Scoring $Label"; role = $Role; temporaryPassword = $temp } $AdminToken)
    $session = Complete-FirstSignIn $Label $email $temp
    return [pscustomobject]@{ Token = $session.Token; Email = $email }
}

function Write-Results {
    $script:results | Format-Table -AutoSize | Out-String -Width 240 | Write-Host
    $failed = @($script:results | Where-Object { $_.Result -eq 'FAIL' }).Count
    Write-Host "$($script:results.Count - $failed) passed, $failed failed"
    if ($failed -gt 0) { exit 1 }
}

# --- Accounts ----------------------------------------------------------------
$adminEmail = $null
$adminPassword = $null
$adminTotpSecret = $null

if (Test-Path $AdminSecretsFile) {
    $saved = Get-Content -Raw $AdminSecretsFile | ConvertFrom-Json
    $adminEmail = $saved.email
    $adminPassword = $saved.password
    $adminTotpSecret = $saved.totpSecret
}
if ($env:SIMGUARD_SMOKE_ADMIN_EMAIL) { $adminEmail = $env:SIMGUARD_SMOKE_ADMIN_EMAIL }
if ($env:SIMGUARD_SMOKE_ADMIN_PASSWORD) { $adminPassword = $env:SIMGUARD_SMOKE_ADMIN_PASSWORD }
if ($env:SIMGUARD_SMOKE_ADMIN_TOTP_SECRET) { $adminTotpSecret = $env:SIMGUARD_SMOKE_ADMIN_TOTP_SECRET }

if (-not $adminEmail -or -not $adminPassword -or -not $adminTotpSecret) {
    throw 'Admin credentials missing. Provide -AdminSecretsFile or SIMGUARD_SMOKE_ADMIN_EMAIL, SIMGUARD_SMOKE_ADMIN_PASSWORD and SIMGUARD_SMOKE_ADMIN_TOTP_SECRET.'
}

$runId = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()

$admin = Get-Token 'Admin' $adminEmail $adminPassword $adminTotpSecret
$officerAccount = Get-RoleToken 'Officer' 'SecurityOfficer' $OfficerSecretsFile $admin
$auditorAccount = Get-RoleToken 'Auditor' 'Auditor' $AuditorSecretsFile $admin
$officer = $officerAccount.Token
$auditor = $auditorAccount.Token

$departmentsHeader = 'name'
$employeesHeader = 'name,email,department,job_role'
$simsHeader = 'iccid,phone_number,carrier,tier,status'
$usageHeader = 'iccid,date,total_download_mb,total_upload_mb,data_session_count,data_duration_s,hotspot_sessions,public_apn_sessions,unique_towers,geo_flag_count,peak_mb,offpeak_mb,total_mb,roaming_sessions,billed_mb,tier,voice_minutes_used,sms_count,voice_minutes_remaining,sms_remaining'

function Wait-ScoringRun {
    param([string]$Base, [string]$Token, [int]$Id, [int]$TimeoutSeconds = 900)

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        Start-Sleep -Seconds 2
        $s = Invoke-Api GET '/api/admin/scoring/status' $null $Token $Base
        $run = @($s.Data.runs) | Where-Object { $_.id -eq $Id }
    } while (($null -eq $run -or $run.status -in @('queued', 'running')) -and (Get-Date) -lt $deadline)
    return [pscustomobject]@{ Run = $run; Status = $s.Data }
}

function Start-ScoringRun {
    param([string]$Base, [string]$Token, [string]$Label)

    $r = Invoke-Api POST '/api/admin/scoring/run' $null $Token $Base
    Check "$Label Run now" 202 $r
    $t = [Diagnostics.Stopwatch]::StartNew()
    $done = Wait-ScoringRun $Base $Token $r.Data.runId
    $script:timings.Add("$Label scoring run $($r.Data.runId): $([math]::Round($t.Elapsed.TotalSeconds, 1)) s, status $($done.Run.status), SIM-days $($done.Run.simDaysScored)")
    return $done
}

function Wait-TierRun {
    param([string]$Token, [int]$Id, [int]$TimeoutSeconds = 900)

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        Start-Sleep -Seconds 2
        $s = Invoke-Api GET '/api/tier-recommendations/status' $null $Token
    } while (($s.Data.running -or $s.Data.lastRun.id -ne $Id) -and (Get-Date) -lt $deadline)
    return $s.Data
}

# Every item of a paged list.
function Get-All {
    param([string]$Path, [string]$Token)

    $items = New-Object System.Collections.Generic.List[object]
    $page = 1
    do {
        $sep = if ($Path.Contains('?')) { '&' } else { '?' }
        $r = Invoke-Api GET "$Path$($sep)page=$page&pageSize=100" $null $Token
        @($r.Data.items) | ForEach-Object { $items.Add($_) }
        $page++
    } while ($r.Status -eq 200 -and @($r.Data.items).Count -gt 0 -and $items.Count -lt $r.Data.total)
    return $items
}

function Get-OpenAlertCheck {
    param([string]$Token)

    $open = @(Get-All '/api/alerts?status=flagged' $Token) + @(Get-All '/api/alerts?status=escalated' $Token)
    # Alerts raised by scoring runs carry detectedOn; others (seeded by
    # earlier smokes or created before issue #14) are not counted per SIM.
    $pipeline = @($open | Where-Object { $null -ne $_.detectedOn })
    $sims = @($pipeline | ForEach-Object { $_.simId } | Sort-Object -Unique)
    return [pscustomobject]@{ Open = $open.Count; Pipeline = $pipeline.Count; Sims = $sims.Count }
}

# 19-digit ICCID: 8925400, 11 digits, Luhn check digit.
function New-Iccid([long]$Number) {
    $body = '8925400' + $Number.ToString('00000000000')
    $sum = 0
    $double = $true
    for ($i = $body.Length - 1; $i -ge 0; $i--) {
        $d = [int][string]$body[$i]
        if ($double) { $d *= 2; if ($d -gt 9) { $d -= 9 } }
        $sum += $d
        $double = -not $double
    }
    return $body + ((10 - ($sum % 10)) % 10)
}

# --- Roles -------------------------------------------------------------------
foreach ($pair in @(@('Officer', $officer), @('Auditor', $auditor))) {
    $label = $pair[0]; $token = $pair[1]
    Check "$label POST /api/admin/scoring/run" 403 (Invoke-Api POST '/api/admin/scoring/run' $null $token)
    Check "$label GET /api/admin/scoring/status" 403 (Invoke-Api GET '/api/admin/scoring/status' $null $token)
}
Check 'No token POST /api/admin/scoring/run' 401 (Invoke-Api POST '/api/admin/scoring/run')
Check 'No token GET /api/admin/scoring/status' 401 (Invoke-Api GET '/api/admin/scoring/status')
Check 'Anonymous GET /health' 200 (Invoke-Api GET '/health')

foreach ($pair in @(@('Admin', $admin), @('Auditor', $auditor))) {
    $label = $pair[0]; $token = $pair[1]
    Check "$label POST /api/tier-recommendations/run" 403 (Invoke-Api POST '/api/tier-recommendations/run' $null $token)
    Check "$label PUT /api/tier-recommendations/settings" 403 (Invoke-Api PUT '/api/tier-recommendations/settings' @{ intervalDays = 120 } $token)
}
Check 'Auditor GET /api/tier-recommendations/status' 200 (Invoke-Api GET '/api/tier-recommendations/status' $null $auditor)

$status = Invoke-Api GET '/api/admin/scoring/status' $null $admin
Check 'Admin GET /api/admin/scoring/status' 200 $status
Check-True 'Scoring status holds totals only (no SIM ids or phone numbers)' (($status.Text -notmatch '"simId"|"simIds"|"scopeSimIds"|"phoneNumber"|"iccid"') -and ($null -ne $status.Data.totals)) "scores=$($status.Data.totals.riskScores)"
Check-True 'Scoring status: ML service reachable' ($status.Data.mlService.reachable -eq $true) "status=$($status.Data.mlService.status) model=$($status.Data.mlService.modelVersion)"

# --- Run twice, no duplicates ------------------------------------------------
$first = Start-ScoringRun $BaseUrl $admin 'First'
Check-True 'First run succeeded, actor is the Admin' (($first.Run.status -eq 'succeeded') -and ($first.Run.trigger -eq 'manual') -and ($first.Run.requestedBy -eq $adminEmail)) "status=$($first.Run.status) simDays=$($first.Run.simDaysScored)"
$alertsBefore = Get-OpenAlertCheck $auditor
$scoresBefore = $first.Status.totals.riskScores

$second = Start-ScoringRun $BaseUrl $admin 'Second'
$alertsAfter = Get-OpenAlertCheck $auditor
Check-True 'Second run scores nothing new' (($second.Run.status -eq 'succeeded') -and ($second.Run.simDaysScored -eq 0) -and ($second.Run.alertsCreated -eq 0)) "simDays=$($second.Run.simDaysScored) created=$($second.Run.alertsCreated)"
Check-True 'Risk score total unchanged by the second run' ($second.Status.totals.riskScores -eq $scoresBefore) "before=$scoresBefore after=$($second.Status.totals.riskScores)"
Check-True 'Open alerts unchanged by the second run' ($alertsAfter.Open -eq $alertsBefore.Open) "before=$($alertsBefore.Open) after=$($alertsAfter.Open)"
Check-True 'At most one open scoring alert per SIM' (($alertsAfter.Pipeline -gt 0) -and ($alertsAfter.Pipeline -eq $alertsAfter.Sims)) "open=$($alertsAfter.Pipeline) sims=$($alertsAfter.Sims)"

$runAudit = Invoke-Api GET '/api/audit-logs?action=scoring.run_requested&pageSize=5' $null $admin
Check-True 'Run now audited with the Admin email (category system)' (@(@($runAudit.Data.items) | Where-Object { $_.actorEmail -eq $adminEmail -and $_.category -eq 'system' }).Count -gt 0) "rows=$($runAudit.Data.total)"

# --- A usage import triggers a run ---------------------------------------------
$demoSim = (Get-Content (Join-Path $SamplesDir 'sims.csv') -TotalCount 2)[1].Split(',')
$demoUsage = (Get-Content (Join-Path $SamplesDir 'usage.csv') -TotalCount 2)[1].Split(',')
$iccid = New-Iccid (90000000000 + ($runId % 1000000000))
$phone = '+2547' + (20000000 + ($runId % 10000000)).ToString('00000000')
$tier = $demoSim[3]
$simCsv = New-Csv @($simsHeader, "$iccid,$phone,$($demoSim[2]),$tier,available")
Check 'Officer imports one new SIM' 200 (Invoke-Import 'sims' $simCsv 'scoring-sim.csv' $officer 'mode=commit')

$usageLines = @($usageHeader)
foreach ($day in 0..14) {
    $row = $demoUsage.Clone()
    $row[0] = $iccid
    $row[1] = ([datetime]'2025-03-17').AddDays($day).ToString('yyyy-MM-dd')
    $row[15] = $tier
    $usageLines += ($row -join ',')
}
$scoresBeforeImport = (Invoke-Api GET '/api/admin/scoring/status' $null $admin).Data.totals.riskScores
$usageCommit = Invoke-Import 'usage' (New-Csv $usageLines) 'scoring-usage.csv' $officer 'mode=commit'
Check 'Officer imports 15 days of usage for it' 200 $usageCommit
Start-Sleep -Seconds 1
$importRun = @((Invoke-Api GET '/api/admin/scoring/status' $null $admin).Data.runs) | Where-Object { $_.trigger -eq 'import' } | Select-Object -First 1
$importDone = if ($importRun) { Wait-ScoringRun $BaseUrl $admin $importRun.id } else { $null }
Check-True 'The usage import queued a scoring run (trigger import, actor system, 1 SIM)' (($null -ne $importRun) -and ($importRun.requestedBy -eq 'system') -and ($importRun.scopeSimCount -eq 1)) "run=$($importRun.id)"
Check-True 'The import run scored the one new SIM-day (2025-03-31)' (($importDone.Run.status -eq 'succeeded') -and ($importDone.Run.simDaysScored -eq 1) -and ($importDone.Status.totals.riskScores -eq $scoresBeforeImport + 1)) "status=$($importDone.Run.status) simDays=$($importDone.Run.simDaysScored)"

# --- Tier recommendations ------------------------------------------------------
Check 'Tier interval 89 rejected' 400 (Invoke-Api PUT '/api/tier-recommendations/settings' @{ intervalDays = 89 } $officer)
Check 'Tier interval 366 rejected' 400 (Invoke-Api PUT '/api/tier-recommendations/settings' @{ intervalDays = 366 } $officer)
Check 'Tier interval 120 accepted' 200 (Invoke-Api PUT '/api/tier-recommendations/settings' @{ intervalDays = 120 } $officer)
Check 'Tier interval back to 90' 200 (Invoke-Api PUT '/api/tier-recommendations/settings' @{ intervalDays = 90 } $officer)
$settingsAudit = Invoke-Api GET '/api/audit-logs?action=tier_recommendation.settings_changed&pageSize=1' $null $auditor
$lastChange = @($settingsAudit.Data.items)[0]
Check-True 'Tier settings change audited with old and new value' (($lastChange.oldValues.intervalDays -eq 120) -and ($lastChange.newValues.intervalDays -eq 90)) "rows=$($settingsAudit.Data.total)"

$tierStatus = Invoke-Api GET '/api/tier-recommendations/status' $null $officer
if ($null -eq $tierStatus.Data.lastRun) {
    $t = [Diagnostics.Stopwatch]::StartNew()
    $firstTier = Invoke-Api POST '/api/tier-recommendations/run' $null $officer
    Check 'First tier run is allowed' 202 $firstTier
    $tierDone = Wait-TierRun $officer $firstTier.Data.runId
    $script:timings.Add("First tier run: $([math]::Round($t.Elapsed.TotalSeconds, 1)) s for $($tierDone.lastRun.simsEvaluated) SIMs, $($tierDone.lastRun.recommendationsCreated) recommendations")
    Check-True 'First tier run succeeded' ($tierDone.lastRun.status -eq 'succeeded') "status=$($tierDone.lastRun.status)"
}

$refused = Invoke-Api POST '/api/tier-recommendations/run' $null $officer
Check 'Tier run inside the interval' 409 $refused
Check-True 'The 409 states the next allowed date' (($refused.Data.nextAllowedRunDate -match '^\d{4}-\d{2}-\d{2}$') -and ($refused.Data.detail -match [regex]::Escape([string]$refused.Data.nextAllowedRunDate))) "next=$($refused.Data.nextAllowedRunDate)"

$target = @(Get-All '/api/tier-recommendations?status=open' $officer) | Select-Object -First 1
if ($null -eq $target) {
    Check-True 'An open tier recommendation exists to dismiss' $false
} else {
    Check "Dismiss tier recommendation $($target.id)" 200 (Invoke-Api POST "/api/tier-recommendations/$($target.id)/dismiss" $null $officer)
    Check 'Dismiss it again' 409 (Invoke-Api POST "/api/tier-recommendations/$($target.id)/dismiss" $null $officer)

    $forced = Invoke-Api POST '/api/tier-recommendations/run?force=true' $null $officer
    Check 'Forced tier run in Development' 202 $forced
    $forcedDone = Wait-TierRun $officer $forced.Data.runId
    Check-True 'Forced tier run succeeded' ($forcedDone.lastRun.status -eq 'succeeded') "status=$($forcedDone.lastRun.status) evaluated=$($forcedDone.lastRun.simsEvaluated)"
    $again = @(Get-All '/api/tier-recommendations?status=open' $officer) | Where-Object { $_.simId -eq $target.simId -and $_.recommendedTier -eq $target.recommendedTier }
    Check-True 'Dismissed recommendation not recreated by the rerun' (@($again).Count -eq 0) "sim=$($target.simId) tier=$($target.recommendedTier)"
}

# --- Second instance: Production, ML service unreachable -------------------------
if (-not $SecondaryBaseUrl) {
    Write-Host 'No -SecondaryBaseUrl: the ML-down and force-outside-Development checks are skipped.'
} else {
    $admin2 = Get-Token 'Admin (secondary)' $adminEmail $adminPassword $adminTotpSecret $SecondaryBaseUrl
    $officerSaved = Get-Content -Raw $OfficerSecretsFile | ConvertFrom-Json
    $officer2 = Get-Token 'Officer (secondary)' $officerSaved.email $officerSaved.password $officerSaved.totpSecret $SecondaryBaseUrl

    $ignored = Invoke-Api POST '/api/tier-recommendations/run?force=true' $null $officer2 $SecondaryBaseUrl
    Check 'force=true ignored outside Development (409)' 409 $ignored "next=$($ignored.Data.nextAllowedRunDate)"

    $s2 = Invoke-Api GET '/api/admin/scoring/status' $null $admin2 $SecondaryBaseUrl
    Check-True 'Secondary reports the ML service unreachable' ($s2.Data.mlService.reachable -eq $false) "status=$($s2.Data.mlService.status)"
    $scoresBeforeDown = $s2.Data.totals.riskScores
    $alertsBeforeDown = Get-OpenAlertCheck $auditor

    $down = Start-ScoringRun $SecondaryBaseUrl $admin2 'ML down'
    Check-True 'Run with the ML service down ends failed with a reason' (($down.Run.status -eq 'failed') -and ($down.Run.errorSummary -match 'unreachable|unavailable')) "status=$($down.Run.status)"
    Check-True 'Existing scores kept after the failed run' ($down.Status.totals.riskScores -eq $scoresBeforeDown) "before=$scoresBeforeDown after=$($down.Status.totals.riskScores)"
    Check-True 'Open alerts unchanged after the failed run' ((Get-OpenAlertCheck $auditor).Open -eq $alertsBeforeDown.Open)
    $failAudit = Invoke-Api GET '/api/audit-logs?action=scoring.run_failed&pageSize=1' $null $admin
    $failRow = @($failAudit.Data.items)[0]
    Check-True 'Failed run audited as system' (($failRow.actorEmail -eq 'system') -and ($failRow.category -eq 'system') -and ($failRow.newValues.runId -eq $down.Run.id)) "rows=$($failAudit.Data.total)"
}

if ($timings.Count -gt 0) {
    Write-Host 'Timings:'
    $timings | ForEach-Object { Write-Host "  $_" }
}

Write-Results
