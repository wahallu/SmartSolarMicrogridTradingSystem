# =============================================================================
# File        : verify-auth-rules.ps1
# Project     : Smart Solar Microgrid Trading System - SE4040 EAD
# Description : Verification test suite for Identity, Access Control &
#               Local Session rules (R5, R6, R7, R10, R11) owned by Chathushka.
# Author      : Chathushka - Identity, Access Control & Local Session
# =============================================================================

$ErrorActionPreference = 'Continue'
$BASE = 'http://localhost:5218/api/v1'
$script:pass = 0; $script:fail = 0

function Check($name, $condition, $detail) {
    if ($condition) {
        Write-Host "  [PASS] $name" -ForegroundColor Green
        $script:pass++
    } else {
        Write-Host "  [FAIL] $name -> $detail" -ForegroundColor Red
        $script:fail++
    }
}

function Call($method, $path, $body, $token) {
    $headers = @{ 'Accept' = 'application/json' }
    if ($token) { $headers['Authorization'] = "Bearer $token" }
    $params = @{ Uri = "$BASE/$path"; Method = $method; Headers = $headers; TimeoutSec = 15 }
    if ($body -ne $null) {
        $params['Body'] = ($body | ConvertTo-Json -Depth 8 -Compress)
        $params['ContentType'] = 'application/json'
    }
    try {
        $resp = Invoke-WebRequest @params -UseBasicParsing
        return @{ status = [int]$resp.StatusCode; json = ($resp.Content | ConvertFrom-Json) }
    } catch {
        $s = 0; $j = $null
        if ($_.Exception.Response) {
            $s = [int]$_.Exception.Response.StatusCode
            try {
                $sr = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
                $j = ($sr.ReadToEnd() | ConvertFrom-Json)
            } catch {}
        }
        return @{ status = $s; json = $j }
    }
}

Write-Host "`n========================================================" -ForegroundColor Cyan
Write-Host " CHATHUSHKA'S SLICE: IDENTITY & ACCESS CONTROL VERIFICATION " -ForegroundColor Cyan
Write-Host "========================================================" -ForegroundColor Cyan

# 1. Health check
$health = Call GET 'health' $null $null
Check 'API is reachable and database is connected' ($health.status -eq 200 -and $health.json.data.database -eq 'Connected') $health.status

# 2. Login by different identifiers
$admin = Call POST 'auth/login' @{ identifier = 'admin'; password = 'Admin@123' } $null
Check 'Backoffice login by username' ($admin.status -eq 200 -and $admin.json.data.token) $admin.json.message
$adminToken = $admin.json.data.token

$prosumer = Call POST 'auth/login' @{ identifier = '200112301234'; password = 'Prosumer@123' } $null
Check 'Prosumer login by NIC' ($prosumer.status -eq 200 -and $prosumer.json.data.token) $prosumer.json.message
$prosumerToken = $prosumer.json.data.token

# 3. Rule R6 - Pending accounts cannot log in
$pending = Call POST 'auth/login' @{ identifier = '200234501122'; password = 'Prosumer@123' } $null
Check 'Rule R6: Pending prosumer blocked from login (403 ACCOUNT_PENDING)' ($pending.status -eq 403 -and $pending.json.errorCode -eq 'ACCOUNT_PENDING') "Status: $($pending.status), Code: $($pending.json.errorCode)"

# 4. Rule R5 - Deactivated accounts cannot log in
$deact = Call POST 'auth/login' @{ identifier = '199756403311'; password = 'Prosumer@123' } $null
Check 'Rule R5: Deactivated prosumer blocked from login (403 ACCOUNT_DEACTIVATED)' ($deact.status -eq 403 -and $deact.json.errorCode -eq 'ACCOUNT_DEACTIVATED') "Status: $($deact.status), Code: $($deact.json.errorCode)"

# 5. Rule R7 - Duplicate NIC registration rejected
$dupNic = Call POST 'auth/register' @{
    nic = '200112301234'
    fullName = 'Test Duplicate'
    email = 'unique_email_test@example.com'
    phone = '0771234567'
    password = 'Password@123'
    confirmPassword = 'Password@123'
} $null
Check 'Rule R7: Duplicate NIC registration rejected with 409 NIC_ALREADY_EXISTS' ($dupNic.status -eq 409 -and $dupNic.json.errorCode -eq 'NIC_ALREADY_EXISTS') "Status: $($dupNic.status), Code: $($dupNic.json.errorCode)"

# 6. Rule R10 - Only Backoffice may manage web users and activate accounts
$opToken = (Call POST 'auth/login' @{ identifier = 'operator1'; password = 'Operator@123' } $null).json.data.token
$opAttemptActivate = Call PATCH 'prosumers/200234501122/activate' $null $opToken
Check 'Rule R10: Operator cannot activate prosumer accounts (403 Forbidden)' ($opAttemptActivate.status -eq 403) "Status: $($opAttemptActivate.status)"

$adminActivate = Call PATCH 'prosumers/200234501122/activate' $null $adminToken
Check 'Rule R10: Backoffice officer can activate prosumer accounts (200 OK)' ($adminActivate.status -eq 200) "Status: $($adminActivate.status)"

# 7. Rule R11 - Prosumer cannot read another prosumer's profile
$otherProsumer = Call GET 'prosumers/199845600789' $null $prosumerToken
Check 'Rule R11: Prosumer cannot read foreign profile (403 FORBIDDEN)' ($otherProsumer.status -eq 403) "Status: $($otherProsumer.status)"

$ownProfile = Call GET 'prosumers/200112301234' $null $prosumerToken
Check 'Rule R11: Prosumer can read their own profile (200 OK)' ($ownProfile.status -eq 200) "Status: $($ownProfile.status)"

# 8. Password change integrity
$badPwdChange = Call POST 'auth/change-password' @{ currentPassword = 'Wrong'; newPassword = 'NewPassword@123' } $prosumerToken
Check 'Wrong current password rejected (400 WRONG_CURRENT_PASSWORD)' ($badPwdChange.status -eq 400) "Status: $($badPwdChange.status)"

Write-Host "`nSummary: $script:pass passed, $script:fail failed." -ForegroundColor $(if ($script:fail -eq 0) { 'Green' } else { 'Red' })
