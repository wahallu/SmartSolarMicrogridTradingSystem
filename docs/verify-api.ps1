# End-to-end verification of the Smart Solar Microgrid API business rules.
$ErrorActionPreference = 'Continue'
$BASE = 'http://localhost:5218/api/v1'
$script:pass = 0; $script:fail = 0

function Check($name, $condition, $detail) {
    if ($condition) { Write-Host "  PASS  $name" -ForegroundColor Green; $script:pass++ }
    else { Write-Host "  FAIL  $name  -> $detail" -ForegroundColor Red; $script:fail++ }
}

function Call($method, $path, $body, $token) {
    $headers = @{ 'Accept' = 'application/json' }
    if ($token) { $headers['Authorization'] = "Bearer $token" }
    $params = @{ Uri = "$BASE/$path"; Method = $method; Headers = $headers; TimeoutSec = 20 }
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

Write-Host "`n=== 1. AUTHENTICATION & ROLE ROUTING ===" -ForegroundColor Cyan

$admin = Call POST 'auth/login' @{ identifier = 'admin'; password = 'Admin@123' } $null
Check 'Backoffice login (admin)' ($admin.status -eq 200 -and $admin.json.data.token) $admin.json.message
$adminTok = $admin.json.data.token
Check 'Admin role claim is Backoffice' ($admin.json.data.user.role -eq 'Backoffice') $admin.json.data.user.role

$op = Call POST 'auth/login' @{ identifier = 'operator1'; password = 'Operator@123' } $null
Check 'GridOperator login (operator1)' ($op.status -eq 200) $op.json.message
$opTok = $op.json.data.token
Check 'Operator has assigned station' ($op.json.data.user.assignedStationCode -eq 'STN-001') $op.json.data.user.assignedStationCode

$pro = Call POST 'auth/login' @{ identifier = '200112301234'; password = 'Prosumer@123' } $null
Check 'Prosumer login by NIC' ($pro.status -eq 200) $pro.json.message
$proTok = $pro.json.data.token
$proNic = $pro.json.data.user.nic
Check 'Prosumer NIC is the primary key' ($proNic -eq '200112301234') $proNic

$bad = Call POST 'auth/login' @{ identifier = 'admin'; password = 'WrongPassword' } $null
Check 'Wrong password rejected (401)' ($bad.status -eq 401) "status=$($bad.status)"

# R6 - pending account cannot log in
$pending = Call POST 'auth/login' @{ identifier = '200234501122'; password = 'Prosumer@123' } $null
Check 'R6 pending account blocked (403 ACCOUNT_PENDING)' ($pending.status -eq 403 -and $pending.json.errorCode -eq 'ACCOUNT_PENDING') "status=$($pending.status) code=$($pending.json.errorCode)"

# R5 - deactivated account cannot log in
$deact = Call POST 'auth/login' @{ identifier = '199756403311'; password = 'Prosumer@123' } $null
Check 'R5 deactivated account blocked (403 ACCOUNT_DEACTIVATED)' ($deact.status -eq 403 -and $deact.json.errorCode -eq 'ACCOUNT_DEACTIVATED') "status=$($deact.status) code=$($deact.json.errorCode)"

$me = Call GET 'auth/me' $null $proTok
Check 'GET /auth/me returns the caller' ($me.status -eq 200 -and $me.json.data.nic -eq $proNic) $me.json.message

$noTok = Call GET 'auth/me' $null $null
Check 'Unauthenticated call rejected (401)' ($noTok.status -eq 401) "status=$($noTok.status)"


Write-Host "`n=== 2. SEED DATA & COLLECTIONS ===" -ForegroundColor Cyan

$stations = Call GET 'stations?pageSize=50' $null $adminTok
Check 'Stations seeded (6)' ($stations.json.data.totalCount -eq 6) "count=$($stations.json.data.totalCount)"
$stn1 = $stations.json.data.items | Where-Object { $_.stationCode -eq 'STN-001' } | Select-Object -First 1
Check 'STN-001 has GPS coordinates' ($stn1.latitude -gt 6 -and $stn1.longitude -gt 79) "$($stn1.latitude),$($stn1.longitude)"
Check 'STN-001 has capacity kWh' ($stn1.capacityKwh -gt 0) $stn1.capacityKwh
Check 'STN-001 has battery slots' ($stn1.totalBatterySlots -gt 0) $stn1.totalBatterySlots
Check 'STN-001 has 7-day schedule' ($stn1.weeklySchedule.Count -eq 7) $stn1.weeklySchedule.Count

$prosumers = Call GET 'prosumers?pageSize=50' $null $adminTok
Check 'Prosumers seeded (>=6)' ($prosumers.json.data.totalCount -ge 6) "count=$($prosumers.json.data.totalCount)"

$pendingList = Call GET 'prosumers/pending' $null $adminTok
Check 'Pending-activation view returns rows' ($pendingList.status -eq 200 -and $pendingList.json.data.Count -ge 1) "count=$($pendingList.json.data.Count)"

$allRes = Call GET 'reservations?pageSize=100' $null $adminTok
Check 'Reservations seeded (>=20)' ($allRes.json.data.totalCount -ge 20) "count=$($allRes.json.data.totalCount)"
$statuses = ($allRes.json.data.items | Select-Object -ExpandProperty status -Unique | Sort-Object) -join ','
Check 'All 5 reservation statuses present' ($statuses -eq 'Approved,Cancelled,Completed,Pending,Rejected') $statuses


Write-Host "`n=== 3. MAP FEATURE (nearby, server-computed distance) ===" -ForegroundColor Cyan

$near = Call GET 'stations/nearby?lat=6.9061&lng=79.9696&radiusKm=30' $null $proTok
Check 'Nearby returns stations' ($near.status -eq 200 -and $near.json.data.Count -ge 1) "count=$($near.json.data.Count)"
Check 'Nearby rows carry distanceKm' ($near.json.data[0].distanceKm -ne $null) $near.json.data[0].distanceKm
$d = $near.json.data | ForEach-Object { $_.distanceKm }
$sorted = ($d | Sort-Object) -join ','
Check 'Nearby sorted by distance ascending' (($d -join ',') -eq $sorted) ($d -join ',')
$narrow = Call GET 'stations/nearby?lat=6.9061&lng=79.9696&radiusKm=1' $null $proTok
Check 'Radius filter narrows results' ($narrow.json.data.Count -lt $near.json.data.Count) "1km=$($narrow.json.data.Count) 30km=$($near.json.data.Count)"


Write-Host "`n=== 4. RESERVATION WORKFLOW + RULES R1/R8/R13 ===" -ForegroundColor Cyan

$stationId = $stn1.id
$slots = Call GET "slots/available?stationId=$stationId" $null $proTok
Check 'Available slots returned' ($slots.status -eq 200 -and $slots.json.data.Count -ge 1) "count=$($slots.json.data.Count)"

# Pick a slot at least 24h out so the 12-hour rule permits changes.
$goodSlot = $slots.json.data | Where-Object { $_.isBookable -eq $true } | Select-Object -Skip 12 -First 1
if (-not $goodSlot) { $goodSlot = $slots.json.data | Select-Object -First 1 }

$create = Call POST 'reservations' @{ stationId = $stationId; slotId = $goodSlot.id; energyKwh = 10 } $proTok
Check 'Create reservation (201)' ($create.status -eq 201) "status=$($create.status) $($create.json.message)"
$resId = $create.json.data.id
Check 'Reservation number allocated' ($create.json.data.reservationNo -like 'RSV-*') $create.json.data.reservationNo
Check 'New reservation is Pending' ($create.json.data.status -eq 'Pending') $create.json.data.status
Check 'Prosumer NIC forced to own (R11)' ($create.json.data.prosumerNic -eq $proNic) $create.json.data.prosumerNic

# R13 - duplicate on the same slot
$dup = Call POST 'reservations' @{ stationId = $stationId; slotId = $goodSlot.id; energyKwh = 5 } $proTok
Check 'R13 duplicate reservation blocked (409)' ($dup.status -eq 409 -and $dup.json.errorCode -eq 'DUPLICATE_RESERVATION') "status=$($dup.status) code=$($dup.json.errorCode)"

# R1 - beyond the 7-day window
$farSlots = Call GET "slots?stationId=$stationId&pageSize=400&upcomingOnly=true" $null $adminTok
$farSlot = $farSlots.json.data.items | Where-Object { ([datetime]$_.startUtc) -gt (Get-Date).ToUniversalTime().AddDays(8) } | Select-Object -First 1
if ($farSlot) {
    $far = Call POST 'reservations' @{ stationId = $stationId; slotId = $farSlot.id; energyKwh = 5 } $proTok
    Check 'R1 beyond 7-day window blocked (400)' ($far.status -eq 400 -and $far.json.errorCode -eq 'RES_WINDOW_7_DAYS') "status=$($far.status) code=$($far.json.errorCode)"
} else { Check 'R1 beyond 7-day window blocked' $false 'no slot >8 days found' }

# Energy above slot capacity
$over = Call POST 'reservations' @{ stationId = $stationId; slotId = ($slots.json.data | Select-Object -Skip 13 -First 1).id; energyKwh = 9999 } $proTok
Check 'Energy above slot capacity rejected (400)' ($over.status -eq 400) "status=$($over.status) $($over.json.message)"


Write-Host "`n=== 5. RULES R2/R3 (12-hour notice) ===" -ForegroundColor Cyan

$detail = Call GET "reservations/$resId" $null $proTok
Check 'Server computes canModify' ($detail.json.data.canModify -ne $null) $detail.json.data.canModify
Check 'Server computes canCancel' ($detail.json.data.canCancel -ne $null) $detail.json.data.canCancel
Check 'hoursUntilStart supplied' ($detail.json.data.hoursUntilStart -gt 0) $detail.json.data.hoursUntilStart

$otherSlot = $slots.json.data | Where-Object { $_.id -ne $goodSlot.id -and $_.isBookable } | Select-Object -Skip 14 -First 1
$upd = Call PUT "reservations/$resId" @{ slotId = $otherSlot.id; energyKwh = 12 } $proTok
Check 'Update reservation succeeds when >12h notice' ($upd.status -eq 200) "status=$($upd.status) $($upd.json.message)"
Check 'Update moved the slot' ($upd.json.data.slotId -eq $otherSlot.id) $upd.json.data.slotCode

# Find a seeded reservation inside the 12-hour window and confirm it is locked.
$soon = $allRes.json.data.items | Where-Object { $_.status -in @('Pending','Approved') -and $_.hoursUntilStart -lt 12 -and $_.hoursUntilStart -gt -1000 } | Select-Object -First 1
if ($soon) {
    Check 'R2/R3 canModify=false inside 12h window' ($soon.canModify -eq $false) $soon.canModify
    $lateCancel = Call PATCH "reservations/$($soon.id)/cancel" @{ reason = 'test' } $adminTok
    Check 'R3 cancel blocked inside 12h (400 RES_CANCEL_12H)' ($lateCancel.status -eq 400 -and $lateCancel.json.errorCode -eq 'RES_CANCEL_12H') "status=$($lateCancel.status) code=$($lateCancel.json.errorCode)"
} else {
    Write-Host "  NOTE  no seeded reservation inside the 12h window; checking flag logic only" -ForegroundColor Yellow
    Check 'R2/R3 flags exist on all rows' (($allRes.json.data.items | Where-Object { $_.canCancel -eq $null }).Count -eq 0) 'some rows missing canCancel'
}


Write-Host "`n=== 6. APPROVAL + QR ISSUE/VERIFY/COMPLETE (R9) ===" -ForegroundColor Cyan

$appr = Call PATCH "reservations/$resId/approve" $null $adminTok
Check 'Approve reservation (200)' ($appr.status -eq 200 -and $appr.json.data.status -eq 'Approved') "status=$($appr.status) $($appr.json.message)"
Check 'QR token issued on approval' ($appr.json.data.qrToken) 'no token'

$qr = Call GET "reservations/$resId/qr" $null $proTok
Check 'QR endpoint returns payload' ($qr.status -eq 200 -and $qr.json.data.qrPayload) $qr.json.message
Check 'QR PNG rendered (base64)' ($qr.json.data.qrImageBase64.Length -gt 200) "len=$($qr.json.data.qrImageBase64.Length)"
$payloadOk = $qr.json.data.qrPayload -match '"v":1' -and $qr.json.data.qrPayload -match '"t":'
Check 'QR payload has the agreed shape' $payloadOk $qr.json.data.qrPayload

$token = $qr.json.data.qrToken

# Prosumer must not be able to verify a QR code.
$proVerify = Call POST 'reservations/verify-qr' @{ qrToken = $token } $proTok
Check 'Prosumer cannot verify QR (403)' ($proVerify.status -eq 403) "status=$($proVerify.status)"

$verify = Call POST 'reservations/verify-qr' @{ qrToken = $token } $opTok
# The slot is >2h away, so R9 must refuse it as out of window.
Check 'R9 QR out-of-window refused (400)' ($verify.status -eq 400 -and $verify.json.errorCode -eq 'QR_OUT_OF_WINDOW') "status=$($verify.status) code=$($verify.json.errorCode)"

# Whole-payload scan must be accepted by the parser (still out of window, but not QR_INVALID).
$payloadVerify = Call POST 'reservations/verify-qr' @{ qrToken = $qr.json.data.qrPayload } $opTok
Check 'Scanner may post the whole JSON payload' ($payloadVerify.json.errorCode -ne 'QR_INVALID') "code=$($payloadVerify.json.errorCode)"

$badQr = Call POST 'reservations/verify-qr' @{ qrToken = 'not-a-real-token' } $opTok
Check 'R9 unknown QR rejected (QR_INVALID)' ($badQr.json.errorCode -eq 'QR_INVALID') "code=$($badQr.json.errorCode)"

# Verify + complete against a seeded Approved booking that is inside the scan window.
$approvedSeed = $allRes.json.data.items | Where-Object { $_.status -eq 'Approved' } | Select-Object -First 1
if ($approvedSeed) {
    $sq = Call GET "reservations/$($approvedSeed.id)/qr" $null $adminTok
    $sv = Call POST 'reservations/verify-qr' @{ qrToken = $sq.json.data.qrToken } $opTok
    Write-Host "  NOTE  seeded approved booking verify -> status=$($sv.status) code=$($sv.json.errorCode)" -ForegroundColor Yellow
}


Write-Host "`n=== 7. DASHBOARD COUNTERS (live from API) ===" -ForegroundColor Cyan

$dash = Call GET 'reservations/dashboard' $null $adminTok
Check 'Dashboard loads for staff' ($dash.status -eq 200) $dash.json.message
Check 'pendingReservations present' ($dash.json.data.pendingReservations -ne $null) $dash.json.data.pendingReservations
Check 'approvedFutureReservations present' ($dash.json.data.approvedFutureReservations -ne $null) $dash.json.data.approvedFutureReservations
Check 'pendingProsumerActivations for staff' ($dash.json.data.pendingProsumerActivations -ge 1) $dash.json.data.pendingProsumerActivations
Check 'Dashboard upcoming list populated' ($dash.json.data.upcoming.Count -ge 1) $dash.json.data.upcoming.Count

$proDash = Call GET 'reservations/dashboard' $null $proTok
Check 'Dashboard loads for prosumer' ($proDash.status -eq 200) $proDash.json.message
Check 'Prosumer sees 0 staff counters (R11)' ($proDash.json.data.pendingProsumerActivations -eq 0) $proDash.json.data.pendingProsumerActivations


Write-Host "`n=== 8. PROSUMER VIEWS & SEARCH ===" -ForegroundColor Cyan

foreach ($scope in @('Current','Pending','History','All')) {
    $r = Call GET "reservations/my?scope=$scope" $null $proTok
    Check "GET /reservations/my?scope=$scope" ($r.status -eq 200) "status=$($r.status)"
}

$hist = Call GET 'reservations/history' $null $proTok
Check 'Booking history for prosumer' ($hist.status -eq 200) $hist.json.message

$search = Call GET 'reservations?search=Sanduni&pageSize=50' $null $adminTok
Check 'Filter/search by prosumer name' ($search.status -eq 200 -and $search.json.data.totalCount -ge 1) "count=$($search.json.data.totalCount)"

$byStatus = Call GET 'reservations?status=Completed&pageSize=50' $null $adminTok
Check 'Filter by status=Completed' ($byStatus.status -eq 200 -and $byStatus.json.data.totalCount -ge 1) "count=$($byStatus.json.data.totalCount)"

$byNic = Call GET "reservations?nic=$proNic&pageSize=50" $null $adminTok
Check 'Filter by NIC' ($byNic.status -eq 200) "count=$($byNic.json.data.totalCount)"


Write-Host "`n=== 9. RULE R4 (station deactivation guard) ===" -ForegroundColor Cyan

$busy = $stations.json.data.items | Where-Object { $_.activeReservationCount -gt 0 } | Select-Object -First 1
if (-not $busy) {
    $stationsAfter = Call GET 'stations?pageSize=50' $null $adminTok
    $busy = $stationsAfter.json.data.items | Where-Object { $_.activeReservationCount -gt 0 } | Select-Object -First 1
}
if ($busy) {
    $deactStn = Call PATCH "stations/$($busy.id)/deactivate" $null $adminTok
    Check 'R4 deactivation blocked while bookings exist (409)' ($deactStn.status -eq 409 -and $deactStn.json.errorCode -eq 'STATION_HAS_ACTIVE_RES') "status=$($deactStn.status) code=$($deactStn.json.errorCode)"
} else { Check 'R4 station with active reservations found' $false 'none had activeReservationCount>0' }

$idle = $stations.json.data.items | Where-Object { $_.activeReservationCount -eq 0 } | Select-Object -First 1
if ($idle) {
    $offOk = Call PATCH "stations/$($idle.id)/deactivate" $null $adminTok
    Check 'Idle station deactivates cleanly' ($offOk.status -eq 200) "status=$($offOk.status) $($offOk.json.message)"
    $onOk = Call PATCH "stations/$($idle.id)/activate" $null $adminTok
    Check 'Station reactivates' ($onOk.status -eq 200) "status=$($onOk.status)"
}


Write-Host "`n=== 10. ROLE ENFORCEMENT (R10) ===" -ForegroundColor Cyan

$proUsers = Call GET 'users' $null $proTok
Check 'Prosumer cannot list web users (403)' ($proUsers.status -eq 403) "status=$($proUsers.status)"

$opUsers = Call GET 'users' $null $opTok
Check 'Operator cannot list web users (403)' ($opUsers.status -eq 403) "status=$($opUsers.status)"

$adminUsers = Call GET 'users' $null $adminTok
Check 'Backoffice can list web users' ($adminUsers.status -eq 200 -and $adminUsers.json.data.totalCount -ge 4) "count=$($adminUsers.json.data.totalCount)"

$proCreateStation = Call POST 'stations' @{ stationCode = 'HACK-1'; name = 'Nope'; address = 'x'; city = 'y'; latitude = 1; longitude = 1; capacityKwh = 1; totalBatterySlots = 1; availableBatterySlots = 1 } $proTok
Check 'Prosumer cannot create a station (403)' ($proCreateStation.status -eq 403) "status=$($proCreateStation.status)"

$otherNic = '199845600789'
$peek = Call GET "prosumers/$otherNic" $null $proTok
Check 'R11 prosumer cannot read another profile (403)' ($peek.status -eq 403) "status=$($peek.status)"


Write-Host "`n=== 11. ACCOUNT MANAGEMENT (R5/R6/R7) ===" -ForegroundColor Cyan

$newNic = '2003' + (Get-Random -Minimum 10000000 -Maximum 99999999)
$reg = Call POST 'auth/register' @{ nic = $newNic; fullName = 'Test Prosumer'; email = "t$((Get-Random))@test.lk"; phone = '0771111111'; address = '10 Test Road, Colombo'; password = 'Test@123' } $null
Check 'Self-registration succeeds (201)' ($reg.status -eq 201) "status=$($reg.status) $($reg.json.message)"
Check 'R6 new registration is Pending' ($reg.json.data.status -eq 'Pending') $reg.json.data.status

$dupNic = Call POST 'auth/register' @{ nic = $newNic; fullName = 'Dup'; email = "d$((Get-Random))@test.lk"; phone = '0772222222'; address = '11 Test Road, Colombo'; password = 'Test@123' } $null
Check 'R7 duplicate NIC rejected (409)' ($dupNic.status -eq 409 -and $dupNic.json.errorCode -eq 'NIC_ALREADY_EXISTS') "status=$($dupNic.status) code=$($dupNic.json.errorCode)"

$cantLogin = Call POST 'auth/login' @{ identifier = $newNic; password = 'Test@123' } $null
Check 'R6 pending user cannot log in yet' ($cantLogin.status -eq 403) "status=$($cantLogin.status)"

$act = Call PATCH "prosumers/$newNic/activate" $null $adminTok
Check 'R5 Backoffice activates the account' ($act.status -eq 200 -and $act.json.data.status -eq 'Active') "status=$($act.status)"

$canLogin = Call POST 'auth/login' @{ identifier = $newNic; password = 'Test@123' } $null
Check 'Activated user can now log in' ($canLogin.status -eq 200) "status=$($canLogin.status)"
$newTok = $canLogin.json.data.token

$prosumerActivate = Call PATCH "prosumers/$newNic/activate" $null $newTok
Check 'R5 non-Backoffice cannot activate (403)' ($prosumerActivate.status -eq 403) "status=$($prosumerActivate.status)"

$editSelf = Call PUT "prosumers/$newNic" @{ fullName = 'Test Prosumer Edited'; email = "e$((Get-Random))@test.lk"; phone = '0773333333'; address = '12 Test Road, Colombo' } $newTok
Check 'Prosumer edits own profile' ($editSelf.status -eq 200 -and $editSelf.json.data.fullName -eq 'Test Prosumer Edited') "status=$($editSelf.status)"

$reqDeact = Call POST "prosumers/$newNic/request-deactivation" @{ reason = 'Moving house' } $newTok
Check 'Prosumer requests deactivation' ($reqDeact.status -eq 200 -and $reqDeact.json.data.deactivationRequested -eq $true) "status=$($reqDeact.status)"
Check 'Request does NOT change status itself' ($reqDeact.json.data.status -eq 'Active') $reqDeact.json.data.status

$pendingAfter = Call GET 'prosumers/pending' $null $adminTok
$found = $pendingAfter.json.data | Where-Object { $_.nic -eq $newNic }
Check 'Request appears in back-office queue' ($found -ne $null) 'not found in /prosumers/pending'

$doDeact = Call PATCH "prosumers/$newNic/deactivate" @{ reason = 'User requested' } $adminTok
Check 'Backoffice deactivates the account' ($doDeact.status -eq 200 -and $doDeact.json.data.status -eq 'Deactivated') "status=$($doDeact.status)"

$deadLogin = Call POST 'auth/login' @{ identifier = $newNic; password = 'Test@123' } $null
Check 'R5 deactivated user cannot log in' ($deadLogin.status -eq 403 -and $deadLogin.json.errorCode -eq 'ACCOUNT_DEACTIVATED') "status=$($deadLogin.status)"


Write-Host "`n=== 12. CANCELLATION + VALIDATION ENVELOPE ===" -ForegroundColor Cyan

$cancel = Call PATCH "reservations/$resId/cancel" @{ reason = 'Smoke test cancel' } $proTok
Check 'Cancel reservation succeeds (>12h notice)' ($cancel.status -eq 200 -and $cancel.json.data.status -eq 'Cancelled') "status=$($cancel.status) $($cancel.json.message)"
Check 'QR token cleared on cancel' ([string]::IsNullOrEmpty($cancel.json.data.qrToken)) 'token still present'

$reCancel = Call PATCH "reservations/$resId/cancel" @{ reason = 'again' } $proTok
Check 'Cancelling twice is refused' ($reCancel.status -eq 400) "status=$($reCancel.status)"

$badReg = Call POST 'auth/register' @{ nic = 'not-a-nic'; fullName = 'X'; email = 'bad'; phone = '1'; address = 'y'; password = '1' } $null
Check 'Validation returns 400 with field errors' ($badReg.status -eq 400 -and $badReg.json.errors -ne $null) "status=$($badReg.status)"
Check 'Envelope carries errorCode' ($badReg.json.errorCode -eq 'VALIDATION_FAILED') $badReg.json.errorCode

$notFound = Call GET 'reservations/000000000000000000000000' $null $adminTok
Check 'Unknown id returns 404' ($notFound.status -eq 404) "status=$($notFound.status)"

$malformed = Call GET 'reservations/not-an-objectid' $null $adminTok
Check 'Malformed id returns 404 not 500' ($malformed.status -eq 404) "status=$($malformed.status)"


Write-Host "`n=== 13. PATCH VERB OVERRIDE (Android fallback path) ===" -ForegroundColor Cyan
try {
    $ovBody = (@{ reason = 'override probe' } | ConvertTo-Json -Compress)
    $ovHeaders = @{ 'Authorization' = "Bearer $adminTok"; 'X-HTTP-Method-Override' = 'PATCH'; 'Accept' = 'application/json' }
    $ov = Invoke-WebRequest -Uri "$BASE/reservations/000000000000000000000000/cancel" -Method POST -Headers $ovHeaders -Body $ovBody -ContentType 'application/json' -UseBasicParsing -TimeoutSec 15
    Check 'X-HTTP-Method-Override routes POST as PATCH' ($false) "unexpected 2xx"
} catch {
    $s = [int]$_.Exception.Response.StatusCode
    # 404 proves the route matched the PATCH endpoint; 405 would mean the override failed.
    Check 'X-HTTP-Method-Override routes POST as PATCH' ($s -eq 404) "status=$s (405 would mean override failed)"
}


Write-Host "`n===============================================" -ForegroundColor Cyan
Write-Host " PASSED: $script:pass    FAILED: $script:fail" -ForegroundColor $(if ($script:fail -eq 0) { 'Green' } else { 'Yellow' })
Write-Host "===============================================`n" -ForegroundColor Cyan
