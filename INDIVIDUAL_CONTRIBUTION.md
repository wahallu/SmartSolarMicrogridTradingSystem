# Individual Contribution & Viva Preparation
### Smart Solar Microgrid Trading System · SE4040 Enterprise Application Development

| | |
|---|---|
| **Module** | SE4040 – Enterprise Application Development |
| **Assignment** | Assignment 1 – Client-Server Application (Web, Mobile and Web Service) |
| **Group size** | 4 members |
| **Repository** | _[add your GitHub URL here]_ |

---

## 0. Read this first — how the marking actually works

This is the single most important thing in this document, and it changes how you
should divide the work.

The assignment has **two** mark tables:

| Table | Marks | Awarded |
|---|---|---|
| Table 1 – Group Contribution | 35 | **Once for the group.** Everyone gets the same mark. |
| Table 2 – Individual Contribution | 65 | **Per student.** Each of you is marked separately. |

Table 2's 65 marks are made up of six criteria:

| # | Criterion | Marks |
|---|---|---|
| A | Web Application Features and Business Rules | 18 |
| B | Mobile Authentication and Account Management | 9 |
| C | Reservation Workflow and Booking Management | 9 |
| D | Booking Views and Operational Dashboards | 10 |
| E | Grid Operator Verification and Map Features | 7 |
| F | Service Integration, Local Persistence and Device Capabilities | 12 |

**Every student is assessed against all six.** Nobody is marked on "their bit"
only. That has two consequences:

1. **Do not split horizontally.** "I'll do the backend, you do the web app, he
   does Android" is the obvious split and it is the wrong one. Three of you
   would then be unable to answer anything about criteria B, C, E or F and would
   lose those marks individually, even though the software works perfectly.

2. **Split vertically instead.** Each member owns a *feature* end-to-end — its
   C# service, its web screens and its Android screens. You each then touch
   authentication, business rules, the API boundary and at least one device
   integration, which is exactly the shape of the six criteria.

The allocation below does that. But note the second consequence too: **owning a
slice is necessary, not sufficient.** Section 6 lists the cross-cutting material
every member must be able to explain regardless of who wrote it. Budget real
time for it.

> **On the AI-use declaration.** The assignment is set at AI Assessment Scale
> Level 2, and section 8 is where each of you declares your own tool use. Fill it
> in yourselves and truthfully — I have left it as a template rather than
> pre-writing claims about who did what. The viva questions in this document are
> written to be answerable from the code as it stands, which is the thing that
> actually protects your individual marks.

---

## 1. Allocation at a glance

| Member | Slice | Owns end-to-end | Individual marks in scope |
|---|---|---|---|
| **Chathushka** | Identity, Access Control & Local Session | Auth, web users, prosumers, SQLite session | A(8) + B(9) + F.SQLite(3) = **20** |
| **Nisal** | Microgrid Nodes, Slots & Geolocation | Stations, booking slots, Google Maps | A(5) + E.map(5) + F.Maps(3) = **13** |
| **Akash** | Reservation Workflow & Business Rules | Create/update/cancel, the 7-day and 12-hour rules | A(5) + C(9) + D.filter(2) = **16** |
| **Sandun** | QR Verification, Dashboards & Platform | QR lifecycle, dashboards, API client layer, deployment | D(8) + E.QR(2) + F(6) = **16** |
| | | | **65 total** |

**A note on Nisal's number.** 13 is lower than the others, but the map is the single
largest line item in Table 2 (5 marks) and the hardest integration in the
project — Google Maps SDK, runtime location permission, FusedLocationProvider,
and the async `getMapAsync` race. Effort and marks don't line up perfectly here.
To balance total workload, **Nisal should lead the group deliverables** in section 7
(the report, diagrams and deployment write-up — 35 group marks).

---

## 2. Chathushka — Identity, Access Control & Local Session

**Name:** Chathushka  **IT Number:** _______________________

### 2.1 Scope

Everything to do with *who someone is and what they are allowed to do*: password
storage, token issue, the three roles, the prosumer lifecycle keyed on NIC, and
the SQLite session that lets the Android app resume without a fresh login.

### 2.2 Files owned

**Web API — `backend/SmartSolar.Api/`**

| File | Responsibility |
|---|---|
| `Security/PasswordHasher.cs` | PBKDF2-SHA256 hashing, 100 000 iterations, 16-byte per-account salt |
| `Security/JwtTokenService.cs` | Builds and signs the 8-hour bearer token and its claim set |
| `Security/CurrentUser.cs` | Projects JWT claims into a framework-free caller object |
| `Services/Implementations/AuthService.cs` | Login, self-registration, `/me`, change password |
| `Services/Implementations/UserService.cs` | Backoffice and GridOperator account administration |
| `Services/Implementations/ProsumerService.cs` | Prosumer CRUD keyed on NIC, activation, deactivation requests |
| `Controllers/AuthController.cs` | 4 endpoints |
| `Controllers/UsersController.cs` | 8 endpoints, `[Authorize(Roles = "Backoffice")]` at class level |
| `Controllers/ProsumersController.cs` | 9 endpoints |
| `Models/User.cs` | The `Users` collection document |
| `DTOs/Auth/AuthDtos.cs`, `DTOs/Users/UserDtos.cs` | Request/response contracts + validation attributes |

**Web — `web/src/`**

`context/AuthContext.tsx` · `components/ProtectedRoute.tsx` · `pages/LoginPage.tsx` ·
`pages/UsersPage.tsx` · `pages/ProsumersPage.tsx` · `pages/PendingActivationsPage.tsx` ·
`pages/ProfilePage.tsx`

**Mobile — `mobile/app/src/main/`**

`java/.../activities/SplashActivity.java` · `LoginActivity.java` ·
`RegisterActivity.java` · `ProfileActivity.java` ·
`java/.../db/SessionManager.java` · `java/.../db/DatabaseHelper.java` ·
`java/.../models/UserModel.java` · `java/.../util/ValidationUtils.java` ·
`res/layout/activity_login.xml`, `activity_register.xml`, `activity_profile.xml`,
`activity_splash.xml`, `dialog_reason_input.xml`

### 2.3 Business rules owned

| Rule | Statement | Error code |
|---|---|---|
| **R5** | A `Deactivated` account cannot log in; **only Backoffice** may reactivate | `ACCOUNT_DEACTIVATED` |
| **R6** | Self-registered prosumers start `Pending` and cannot log in until activated | `ACCOUNT_PENDING` |
| **R7** | NIC is unique across all accounts | `NIC_ALREADY_EXISTS` |
| **R10** | Only Backoffice may manage web users and activate accounts | `FORBIDDEN` |
| **R11** | A prosumer may only read or modify their own data | `FORBIDDEN` |

### 2.4 Viva questions and answers

**Q1. Why is the NIC index `Unique = true, Sparse = true`? What breaks without sparse?**

The NIC is the prosumer's primary key so it must be unique. But Backoffice and
GridOperator accounts have no NIC — the field is null. A plain unique index
treats `null` as a value, so the *second* staff account you create collides with
the first and is rejected. `Sparse = true` tells MongoDB to index only documents
where the field is actually present, so uniqueness applies to prosumers and staff
accounts are simply not indexed. Same reason `email` and `qrToken` are sparse.
It's in `MongoDbContext.EnsureIndexesAsync`, index `ux_users_nic`.

**Q2. Walk me through what happens when a prosumer types their NIC and password.**

`LoginActivity` posts `{identifier, password}` to `POST /api/v1/auth/login`.
`AuthService.LoginAsync` looks up one user where NIC **or** username **or** email
matches, using an anchored case-insensitive regex with the input passed through
`Regex.Escape` first. If no user is found, or `PasswordHasher.Verify` fails, it
returns `Unauthorized` with the *same* message for both — so the endpoint doesn't
disclose which accounts exist. Then two status gates: `Pending` → 403
`ACCOUNT_PENDING` (R6), `Deactivated` → 403 `ACCOUNT_DEACTIVATED` (R5). On
success it stamps `LastLoginAt`, issues the JWT, and returns token + user.
`LoginActivity` writes that to SQLite via `SessionManager.saveSession` and routes
by role.

**Q3. Why escape the identifier before building the regex?**

Because the identifier is user input going into a query language. Without
`Regex.Escape`, someone could submit `.*` as their identifier and the anchored
pattern `^.*$` would match the first user in the collection — a trivial
authentication bypass. It's the NoSQL equivalent of SQL injection.

**Q4. Why PBKDF2 with 100 000 iterations rather than just hashing the password?**

A bare SHA-256 of a password is computed in microseconds, so an attacker with the
database can brute-force it at billions of guesses per second. PBKDF2 deliberately
repeats the hash 100 000 times to make each guess expensive. The 16-byte
per-account salt means two users with the same password get different hashes, so
one cracked password doesn't reveal others and precomputed rainbow tables are
useless. Verification uses `CryptographicOperations.FixedTimeEquals` so the
comparison itself doesn't leak information through timing.

**Q5. Show me where R5 is enforced and prove a Grid Operator cannot reactivate an account.**

`ProsumerService.ActivateAsync` checks `caller.IsBackoffice` first and returns
`Forbidden` otherwise. Belt and braces: `ProsumersController` also carries
`[Authorize(Roles = "Backoffice")]` on that action, so the request is rejected
before the service is even reached. Two layers, because the attribute protects
the HTTP route and the service check protects the rule even if the endpoint were
ever called from elsewhere. Verified in `docs/verify-api.ps1` — "R5 non-Backoffice
cannot activate (403)".

**Q6. What is the difference between "request deactivation" and "deactivate"?**

They are deliberately different operations. A prosumer on the phone calls
`POST /prosumers/{nic}/request-deactivation`, which sets `deactivationRequested =
true` and records a reason and timestamp — but **does not change `status`**. The
account stays usable. The request then appears in the Backoffice queue at
`GET /prosumers/pending`. Only `PATCH /prosumers/{nic}/deactivate`, which is
Backoffice-only, actually sets `status = Deactivated`. That split exists because
R5 says only a Backoffice officer may make that decision.

**Q7. Why does the pending-activation queue return two different kinds of record?**

`GetPendingActivationsAsync` filters `Role == Prosumer AND (Status == Pending OR
DeactivationRequested == true)`. It is a *work queue*, not a status filter — both
are decisions a Backoffice officer has to make. The web page explains this in an
info panel and shows separate counts, and each card renders different buttons:
"Activate account" for a new sign-up, "Approve deactivation" / "Dismiss request"
for a deactivation request. Dismissing calls `activate`, which clears the flag.

**Q8. Why is the session in SQLite and not SharedPreferences?**

The assignment asks specifically for login details to persist in SQLite on
Android, so the token and profile go into a `session` table in
`DatabaseHelper`. It holds at most one row — the primary key is fixed at 1 and
`SessionManager.saveSession` uses `insertWithOnConflict(..., CONFLICT_REPLACE)`,
which gives an upsert without a separate existence check. `SplashActivity` calls
`session.isLoggedIn()`, a local SQLite read with no network call, which is why
resume is instant. `logout()` clears the session *and* the cached stations and
reservations, so the next user never sees the previous user's data.

**Q9. A prosumer edits their profile. Which endpoint, and why is it different for staff?**

Prosumers go to `PUT /prosumers/{nic}` because they are keyed by NIC; staff go to
`PUT /users/{id}` because they are keyed by Mongo id. `ProfilePage.tsx` branches
on `user.role` for exactly that reason. On Android the edit card is hidden
entirely for a GridOperator, with a note that staff profiles are maintained in
the web application.

**Q10. How does R11 stop one prosumer reading another's profile?**

`ProsumerService.GetByNicAsync` checks: if `caller.IsProsumer` and `caller.Nic`
does not equal the requested NIC (case-insensitively), return `Forbidden`. The
NIC comes from the **JWT claim**, not from the request body, so a prosumer cannot
put someone else's NIC in a payload and get their record. Same pattern in
`ReservationService` — a prosumer's own NIC is forced onto every booking.

**Q11. Your JWT has `ClockSkew = TimeSpan.Zero`. Why?**

The default skew is five minutes, so an expired token keeps working for up to
five more minutes. Zeroing it means expiry is exact. It is set in `Program.cs` in
the `TokenValidationParameters`.

---

## 3. Nisal — Microgrid Nodes, Slots & Geolocation

**Name:** Nisal  **IT Number:** _______________________

### 3.1 Scope

The physical side of the system: registering microgrid nodes with GPS position
and capacity, maintaining their weekly schedules and battery-bay availability,
publishing the bookable energy slots, and both map screens.

### 3.2 Files owned

**Web API**

| File | Responsibility |
|---|---|
| `Services/Implementations/StationService.cs` | Node CRUD, schedule, availability, **Haversine nearby**, R4 guard |
| `Services/Implementations/SlotService.cs` | Slot CRUD and the bulk generator |
| `Controllers/StationsController.cs` | 10 endpoints |
| `Controllers/SlotsController.cs` | 8 endpoints |
| `Models/SolarStationInfo.cs` | `SolarStationInfo` collection + `GeoLocation`, `OperatingHours`, `DaySchedule` |
| `Models/EnergyBookingSlot.cs` | `EnergyBookingSlots` collection |
| `DTOs/Stations/StationDtos.cs`, `DTOs/Slots/SlotDtos.cs` | Contracts including `NearbyStationDto.distanceKm` |

**Web** — `pages/StationsPage.tsx` · `pages/SlotsPage.tsx` · `pages/MapPage.tsx`

**Mobile** — `activities/StationsMapActivity.java` · `activities/StationDetailActivity.java` ·
`adapters/StationAdapter.java` · `adapters/SlotAdapter.java` ·
`models/StationModel.java` · `models/SlotModel.java` ·
`res/layout/activity_stations_map.xml`, `activity_station_detail.xml`,
`item_station.xml`, `item_slot.xml` · `res/xml/network_security_config.xml`

### 3.3 Business rules owned

| Rule | Statement | Error code |
|---|---|---|
| **R4** | A node cannot be deactivated while it holds future Pending/Approved bookings | `STATION_HAS_ACTIVE_RES` |
| **R8** | A slot must be Available, have capacity, be in the future, and its node Active | `SLOT_UNAVAILABLE` |
| **R12** | A node closed on that weekday cannot be booked | `STATION_CLOSED` |

### 3.4 Viva questions and answers

**Q1. Where is the distance between a phone and a node calculated, and why there?**

On the **server**, in `StationService.HaversineKm`, using earth radius 6371 km.
It is deliberately not on the client. Ranking nodes by proximity is business
logic, and the assignment requires the FAT-service pattern — all business logic
in the central API. `GET /stations/nearby?lat=&lng=&radiusKm=` returns
`NearbyStationDto` objects already carrying `distanceKm` and already sorted
ascending. Both clients just render the number they were given; neither contains
a distance formula.

**Q2. Explain the Haversine formula.**

It gives the great-circle distance between two points on a sphere — the distance
over the earth's curved surface rather than a straight line through it. It takes
the differences in latitude and longitude in radians, combines them through the
`sin²(Δ/2)` terms weighted by the cosines of both latitudes, then converts the
resulting central angle to a distance with `2 · R · atan2(√a, √(1−a))`. The
`atan2` form is used instead of `asin` because it stays numerically stable for
very small distances.

**Q3. I want to deactivate a node that has bookings. What happens?**

`StationService.DeactivateAsync` calls `CountActiveReservationsAsync`, which
counts reservations on that node with status `Pending` or `Approved` and
`ReservationStartUtc >= now`. If that count is greater than zero it returns
**409 Conflict** with `STATION_HAS_ACTIVE_RES` and a message naming the count.
That is rule R4 — those prosumers have booked a slot and would otherwise have
nowhere to go.

**Q4. Does the web UI stop me clicking Deactivate when bookings exist?**

No, and that is deliberate. The same helper populates
`StationDto.ActiveReservationCount`, so the card shows an amber hint like "2
future bookings held", but the button stays enabled. The click goes to the API,
and if the API returns 409 the page shows the server's message in a toast.
Hiding the button would be a *courtesy*; the rule lives in the service. If the UI
pre-checked and refused, we'd have duplicated the rule in the client — exactly
what the FAT-service pattern forbids.

**Q5. What is `isBookable` on a slot and who decides it?**

The **server** decides it, in `DomainMapper.ToDto` for a slot:

```csharp
IsBookable = stationActive
             && slot.Status == SlotStatus.Available
             && remaining > 0
             && slot.StartUtc > utcNow
```

That is rule R8 evaluated once, centrally. `SlotAdapter` on Android greys out and
disables any row where `isBookable()` is false; the web slots table shows a green
check. Neither client recomputes capacity, blocking or whether the slot has
already started.

**Q6. Why does a slot store both `startTime` as a string and `startUtc` as a date?**

`"08:00"` is for display only. `startUtc` is the absolute instant, and **every
business rule compares only against `startUtc`**. Storing wall-clock strings
alone made every rule ambiguous about time zones; storing the absolute instant
alongside removed a whole class of bugs. The slot-code/date/station triple is
also uniquely indexed (`ux_slots_station_date_code`).

**Q7. Explain the bulk slot generator.**

`SlotService.GenerateAsync` takes a station, a date range, an hour window, a slot
duration and a bay count, and lays out the whole grid in one call — otherwise an
operator would create slots one at a time. Three details worth knowing: it loads
the existing slot codes for the range into a `HashSet` keyed `"yyyy-MM-dd|CODE"`
**once** so duplicate detection needs no per-slot query; it skips days the node's
`weeklySchedule` marks closed; and it caps the total at 2000 to stop an
accidental multi-year request from flooding the database. It inserts with a
single `InsertManyAsync`.

**Q8. Why Leaflet on the web but Google Maps on Android?**

The specification names the Google Maps API, and that is what the Android client
uses — `SupportMapFragment`, `OnMapReadyCallback`, markers coloured by node
status. On the web I used Leaflet with OpenStreetMap tiles because the Google
Maps JavaScript API needs a billing-enabled key; Leaflet needs none, so the map
screen works on any machine the project is opened on, including a marker's. The
requirement is satisfied where it was asked for.

**Q9. Your Android map has an async race. How did you handle it?**

`getMapAsync` delivers the `GoogleMap` on a callback, and the station list
arrives from a separate HTTP call — either can finish first. Both paths store
their result in a field and then call a single `plotMarkers()` method that
no-ops until both are present. A `HashMap<Marker, StationModel>` maps each marker
back to its node so the info-window click can resolve which station was tapped.

**Q10. What happens if the user denies location permission?**

The screen still works. Without permission it centres on the fallback constants
`FALLBACK_LAT` / `FALLBACK_LNG` (Malabe, 6.9061 / 79.9696) and lists every node.
With permission it uses `FusedLocationProviderClient.getLastLocation()` and calls
`setMyLocationEnabled(true)` inside a `try/catch (SecurityException)` — required
because the compiler cannot prove the permission was granted. The web map does
the same: `MapPage` falls back to the Malabe centre and says "Location
unavailable".

**Q11. I changed the network and the app stopped reaching the API. Why?**

The original `network_security_config.xml` listed individual hosts, and a
`<domain>` entry matches a host name **exactly** — listing `192.168.1.1` does not
cover `192.168.1.6`. It was replaced with a single `base-config
cleartextTrafficPermitted="true"`, documented as development-only, because the
laptop's address changes with every network. A production deployment would serve
the API over HTTPS and delete the file entirely.

---

## 4. Akash — Reservation Workflow & Business Rules

**Name:** Akash  **IT Number:** _______________________

### 4.1 Scope

The heart of the domain: creating, changing and cancelling energy reservations,
and the two headline rules the assignment names explicitly — the 7-day booking
window and the 12-hour modification notice.

### 4.2 Files owned

**Web API**

| File | Responsibility |
|---|---|
| `Services/Implementations/ReservationService.cs` | `CreateAsync`, `UpdateAsync`, `CancelAsync`, `ApproveAsync`, `RejectAsync`, `GetReservationsAsync`, `GetMyReservationsAsync`, `GetHistoryAsync` |
| `Controllers/ReservationsController.cs` | 13 endpoints (shared with Sandun for the QR half) |
| `Models/EnergyReservation.cs` | The `EnergyReservation` collection |
| `DTOs/Reservations/ReservationDtos.cs` | Contracts, incl. `canModify` / `canCancel` |
| `Common/ServiceResult.cs`, `Common/ErrorCodes.cs`, `Common/IClock.cs` | The result type, the error-code catalogue, the testable clock |
| `Services/Implementations/DomainMapper.cs` | Where `canModify` / `canCancel` are computed |

**Web** — `pages/ReservationsPage.tsx` · `pages/ReservationDetailPage.tsx`

**Mobile** — `activities/NewBookingActivity.java` · `EditBookingActivity.java` ·
`BookingDetailActivity.java` · `BookingSummaryActivity.java` ·
`adapters/ReservationAdapter.java` · `models/ReservationModel.java` ·
`util/DateUtils.java` · `res/layout/activity_new_booking.xml`,
`activity_edit_booking.xml`, `activity_booking_detail.xml`,
`activity_booking_summary.xml`, `item_reservation.xml`

### 4.3 Business rules owned

| Rule | Statement | Error code |
|---|---|---|
| **R1** | Must start in the future and within **7 days** | `RES_WINDOW_7_DAYS` |
| **R2** | Updates require **≥ 12 hours** notice | `RES_UPDATE_12H` |
| **R3** | Cancellations require **≥ 12 hours** notice | `RES_CANCEL_12H` |
| **R13** | No two active bookings by one prosumer on one slot | `DUPLICATE_RESERVATION` |

### 4.4 Viva questions and answers

**Q1. How does the mobile app know whether to enable the Cancel button?**

It doesn't decide — it is told. The API computes the rule per record in
`DomainMapper.ToDto`:

```csharp
var hoursUntilStart = (reservation.ReservationStartUtc - utcNow).TotalHours;
var isChangeableState = reservation.Status is ReservationStatus.Pending
                                           or ReservationStatus.Approved;
var hasNotice = hoursUntilStart >= noticeHours;

CanModify = isChangeableState && hasNotice,
CanCancel = isChangeableState && hasNotice,
```

and ships `canModify` / `canCancel` as booleans. Android writes
`btnCancel.setEnabled(reservation.canCancel())`; React writes
`disabled={!reservation.canCancel}`. **Neither client ever subtracts two dates.**
That is the whole point of the FAT-service pattern — the client is given the
*answer*, not the inputs to a rule.

**Q2. So if I disable the button, the rule is enforced?**

No. Disabling a button is presentation, not a control — a stale page, a replayed
request or a direct API call would bypass it. Every rule is re-checked
server-side on the write path. `CancelAsync` recomputes the hours of notice and
returns 400 `RES_CANCEL_12H` regardless of what the UI allowed. The boolean
exists so the UI is *honest*, not so the rule is *enforced*.

**Q3. Walk me through creating a booking and name every rule that fires.**

1. Resolve the NIC — if the caller is a prosumer it is **forced** to their own
   JWT claim and `request.ProsumerNic` is ignored (R11).
2. Prosumer must exist and be `Active`, else 403.
3. Slot must exist and belong to the requested station.
4. Station must exist and be `Active`.
5. **R13** — does this NIC already hold a Pending/Approved booking on this slot?
6. **R1** — is `slot.StartUtc` in the future and within 7 days?
7. **R12** — is the node open on that weekday?
8. **R8** — is the slot Available with capacity left?
9. Energy requested must not exceed the slot's capacity.
10. Insert as `Pending`, then atomically `$inc` the slot's `bookedCount`.

**Q4. Why is R13 checked before R8? Isn't the order arbitrary?**

It is not arbitrary, and this was an actual bug found in testing. A slot normally
has `maxBookings = 1`. A prosumer's own first booking therefore fills it, so when
they tried to book the same slot twice, R8 fired first and told them **"this slot
is full"** — technically true but misleading, because the thing filling it was
their own booking. Moving R13 first gives the accurate message: "You already hold
an active reservation for this slot." Getting a rule *right* is not the same as
getting it *helpful*.

**Q5. Why read the current time from `IClock` instead of `DateTime.UtcNow`?**

Because every rule in this system is a comparison against *now*, and a rule that
calls `DateTime.UtcNow` directly cannot be unit tested — you can't write a test
for "reject a cancellation 11 hours before the slot" without waiting for real
time to pass. `IClock` is injected, so a test can freeze the clock at any
instant. `SystemClock` is the production implementation. Constructors and entity
defaults still use `DateTime.UtcNow`; only *rules* go through the clock.

**Q6. I move an approved booking to a different slot. What happens to its QR code?**

It is re-issued — a fresh `QrToken`, `QrPayload` and `QrIssuedAt`. If it weren't,
a prosumer who had already screenshotted the old code could present it against
the new slot. The old token stops working the moment it is overwritten. The same
reasoning applies on cancel, where the token is set to null so a cancelled
booking can never be scanned.

**Q7. Two prosumers book the last place on a slot simultaneously. What stops both succeeding?**

`AdjustSlotCountAsync` uses MongoDB's atomic `$inc`:

```csharp
Builders<EnergyBookingSlot>.Update.Inc(s => s.BookedCount, delta)
```

rather than read-modify-write. A read-modify-write would let both requests read
`bookedCount = 0`, both write `1`, and both succeed. `$inc` is applied atomically
by the server, so the two increments serialise. After the increment the slot is
reloaded and flipped to `Full` when `bookedCount >= maxBookings` — never
overwriting a `Blocked` status.

**Q8. How is the reservation number generated, and why the retry loop?**

`NextReservationNoAsync` formats `RSV-{yyyyMMdd}-{seq:0000}`, counting today's
reservations and using count + 1. Two concurrent creates could compute the same
sequence, so it probes upward while that number already exists, up to 20
attempts. `reservationNo` also has a unique index, which is the real guarantee —
the probe just avoids surfacing the collision to the user.

**Q9. The mobile date picker caps at 7 days. Is that enforcing R1?**

No — it is a convenience, and the code says so in a comment. The API remains the
authority and will reject anything outside the window regardless of what the
picker allowed. If the picker were the only check, a modified client or a direct
API call would sail straight past it.

**Q10. Where is the summary page and why does it override the back button?**

`BookingSummaryActivity`, shown after every create, update, cancel and completed
transfer — a marked requirement. It overrides `onBackPressed` to go to the role
home rather than returning into the form the user just submitted, which would
otherwise invite an accidental duplicate submission.

**Q11. How does the staff reservation search work?**

`GET /reservations` accepts `nic`, `stationId`, `status`, `fromDate`, `toDate`,
`search` and paging. `search` is an escaped case-insensitive regex across
reservation number, prosumer name, NIC, station name and station code. If the
caller is a prosumer, `query.Nic` is **forced** to their own NIC before anything
else runs (R11), so the endpoint is safe to expose. The web page debounces the
search box by 400 ms so it sends one request per pause in typing, not one per
keystroke.

---

## 5. Sandun — QR Verification, Dashboards & Platform

**Name:** Sandun  **IT Number:** _______________________

### 5.1 Scope

The operator's field workflow (scan → verify → finalise), every live dashboard
figure, the shared API-client layer on both clients, and the platform concerns —
MongoDB wiring, seeding, middleware and IIS deployment.

### 5.2 Files owned

**Web API**

| File | Responsibility |
|---|---|
| `Services/Implementations/QrCodeGenerator.cs` | Token creation + PNG rendering via QRCoder |
| `ReservationService` — QR half | `GetQrCodeAsync`, `VerifyQrAsync`, `CompleteAsync`, `ResolveApprovedByTokenAsync`, `GetDashboardAsync` |
| `Data/MongoDbContext.cs` | The four collections, all indexes, health ping |
| `Data/DataSeeder.cs` | Idempotent demo data |
| `Program.cs` | DI, JWT, CORS, pipeline order, start-up seeding |
| `Middleware/ExceptionHandlingMiddleware.cs` | Unhandled errors → standard JSON envelope |
| `Middleware/MethodOverrideMiddleware.cs` | `X-HTTP-Method-Override` → real verb |
| `Common/ApiResponse.cs`, `Common/PagedResult.cs` | The response envelope and paging |
| `Controllers/HealthController.cs` | `/health` |
| `web.config` | IIS / ASP.NET Core Module |

**Web** — `api/client.ts` · `api/services.ts` · `types/index.ts` · `utils/format.ts` ·
`components/ui.tsx` · `components/Layout.tsx` · `pages/DashboardPage.tsx` ·
`pages/HomePage.tsx` · `App.tsx` · `main.tsx`

**Mobile** — `api/ApiClient.java` · `api/ApiResult.java` · `db/LocalDataStore.java` ·
`activities/BaseActivity.java` · `util/Constants.java` ·
`activities/ScannerActivity.java` · `VerifyResultActivity.java` ·
`OperatorHomeActivity.java` · `OperatorBookingsActivity.java` ·
`MyBookingsActivity.java` · `ProsumerHomeActivity.java` ·
`models/DashboardModel.java`

### 5.3 Business rules owned

| Rule | Statement | Error code |
|---|---|---|
| **R9** | A scanned QR must exist, be `Approved`, and fall within ±2 h of its window | `QR_INVALID` / `QR_NOT_APPROVED` / `QR_OUT_OF_WINDOW` |

### 5.4 Viva questions and answers

**Q1. Someone edits the QR payload to a different reservation number. What happens?**

Nothing. The payload is
`{"v":1,"t":"<token>","r":"<reservationNo>","n":"<nic>","s":"<stationCode>","e":"<startUtc>"}`,
but **the server trusts only `t`, the token**. `ResolveApprovedByTokenAsync`
looks the reservation up by `qrToken` and re-reads every other field from
MongoDB. The reservation number, NIC and station code in the payload are never
read during verification — they are there so a human can eyeball the code. A
tampered payload resolves to either the same booking or no booking at all.

**Q2. Why does the verify endpoint accept both a bare token and the whole JSON?**

Convenience with no loss of safety. Different scanners return different things,
so if the input starts with `{` the service parses it with `JsonDocument` and
extracts `t`; if parsing fails it treats the whole string as the token. Either
way only the token is trusted, so accepting both forms costs nothing.

**Q3. What stops the same QR code being scanned twice?**

`CompleteAsync` sets `QrToken` and `QrPayload` to null as it flips the status to
`Completed`. A second scan then finds no reservation and returns `QR_INVALID`.
The `qrToken` index is unique and sparse — sparse precisely *because* completed
and cancelled bookings have no token and would otherwise all collide on null.

**Q4. Why the ±2 hour scan window?**

R9. A prosumer shouldn't be able to have their transfer finalised days early or
days late — the code should only work around the time the transfer is actually
happening. The grace is configurable via `QrGraceHoursBefore` /
`QrGraceHoursAfter` in `appsettings.json`. Outside it the operator gets
`QR_OUT_OF_WINDOW` with a message stating the valid window. In testing this is
the branch you hit most, because seeded bookings are days out — that is the rule
working, not a bug.

**Q5. Why `PngByteQRCode` rather than QRCoder's normal renderer?**

The obvious `QRCode` renderer depends on `System.Drawing.Common`, which is
Windows-only on .NET 8 and throws at runtime elsewhere. `PngByteQRCode` produces
the PNG bytes directly with no platform graphics dependency. Rendering
server-side also means the phone needs no QR *encoding* library at all — only a
scanner. The API returns base64 and `BookingDetailActivity` decodes it with
`Base64.decode` + `BitmapFactory.decodeByteArray`.

**Q6. Which dashboard number answers the "count of approved future reservations" criterion?**

`DashboardDto.ApprovedFutureReservations`, computed as `Status == Approved AND
ReservationStartUtc > now`. Every figure on every dashboard comes from
`GET /reservations/dashboard` — none is counted, derived or hard-coded on a
client. For a prosumer caller the scope is forced to their own NIC and the
staff-only counters (`PendingProsumerActivations`, `TotalProsumers`) return 0.
For a GridOperator the web and mobile dashboards pass their `assignedStationId`
so the numbers are scoped to their node.

**Q7. Explain the response envelope and why every endpoint uses it.**

Every response — success or failure — is
`{success, message, data, errorCode, errors, timestamp}`. Because the shape never
varies, each client needs exactly **one** deserialiser and **one** error handler
instead of one per endpoint. `errorCode` is a stable machine-readable string like
`RES_CANCEL_12H`, so the clients branch on the code and never parse English
message text — wording can change without breaking either client. Model-binding
failures are funnelled into the same envelope through
`ApiBehaviorOptions.InvalidModelStateResponseFactory` in `Program.cs`.

**Q8. Why does Android use `HttpURLConnection` instead of Retrofit?**

The assignment requires pure native Android with no cross-platform framework, so
`ApiClient` uses `java.net.HttpURLConnection` from the Java platform and
`org.json` from the Android framework — no Retrofit, OkHttp, Volley, Gson or
Room anywhere in the module. Requests run on a four-thread `ExecutorService` and
results are posted back to the main looper with a `Handler`, which is the
standard native pattern for keeping the UI thread free. That is why callbacks can
touch views directly.

**Q9. There is a `MethodOverrideMiddleware`. Why does it exist and what went wrong with it?**

Android's `HttpURLConnection` normally accepts `PATCH`, but some vendor
implementations reject it, so `ApiClient` falls back to `POST` plus
`X-HTTP-Method-Override: PATCH`. The middleware translates that header back into
the real verb so controllers only ever see `[HttpPatch]`. It was registered but
**never ran** — `WebApplication` automatically inserts `UseRouting()` at the very
*front* of the pipeline when endpoints are mapped, so routing had already
rejected the request with 405 before the header was read. The fix was to call
`app.UseRouting()` explicitly after the middleware, which suppresses the
automatic insertion and pins routing to the right position. Only `PATCH`, `PUT`
and `DELETE` are honoured, and only on a `POST`.

**Q10. What is SQLite actually for on the phone, if the API is the source of truth?**

Two jobs, neither authoritative. First, the session — the JWT and profile, so the
app resumes without a fresh login. Second, an offline cache: when a list loads
successfully it is written to the `stations` / `reservations` tables inside a
single transaction, and if a later request fails with a *network* error the
cached rows are shown with a visible "showing saved data" banner. Every **write**
— create, update, cancel, approve, complete — goes straight to the API. The
`sync_log` table records what was cached and when.

**Q11. Is the seeder safe to run repeatedly?**

Yes — it checks each collection's document count and only seeds one that is
empty, logging what it created and what it skipped. It also increments
`bookedCount` on every slot consumed by a seeded Pending/Approved reservation, so
the seeded state is exactly what `ReservationService` would have produced and the
availability screens are truthful.

**Q12. Take me through deploying this to IIS.**

Install the **.NET 8 Hosting Bundle** first — without it IIS returns 500.19 or
500.31. Publish with `dotnet publish -c Release -o C:\inetpub\SmartSolarApi`.
Create an application pool set to **No Managed Code**, because Kestrel runs the
app and the CLR host is not used. Point a site at the published folder, grant the
pool identity read/execute, set the Mongo connection string in the published
`appsettings.json`, and verify `/api/v1/health` reports `"database":
"Connected"`. `web.config` configures the ASP.NET Core Module in-process and is
copied by `dotnet publish` automatically.

---

## 6. What **every** member must be able to explain

Because Table 2 is marked per student across all six criteria, these are not
optional for anyone. Expect at least one of these regardless of your slice.

**Q. What is the FAT-service pattern and how does this project prove it follows it?**

All business logic lives in the API; the clients are UI only. Four pieces of
evidence: (1) controllers contain no `if` statement over domain data — each action
binds a DTO, calls one service method and maps `ServiceResult` to HTTP through a
shared `Respond<T>` helper; (2) rule *verdicts* are shipped to clients as booleans
(`canModify`, `canCancel`, `isBookable`) so no client re-implements a rule; (3)
every rule is re-checked server-side on the write path; (4) neither client has a
MongoDB driver — the web app's only network dependency is axios, and Android uses
platform `HttpURLConnection`.

**Q. Name the four collections and what each holds.**

`Users` (every principal — Backoffice, GridOperator, Prosumer, with NIC as the
prosumer key), `SolarStationInfo` (microgrid nodes with GPS, kWh capacity,
battery bays, weekly schedule), `EnergyBookingSlots` (bookable windows), and
`EnergyReservation` (the bookings). Database `SmartSolarMicrogridDb`.

**Q. Why is data denormalised, and what does that cost you?**

`stationCode`, `stationName`, `prosumerName` and `prosumerPhone` are copied onto
each reservation so a list of twenty bookings is one query rather than twenty-one.
The cost is a consistency obligation: renaming a node must propagate, which
`StationService.UpdateAsync` does explicitly with `UpdateManyAsync` over the
affected slots and reservations. It is the standard document-database trade-off —
read speed for write complexity.

**Q. List the thirteen rules and where they live.**

R1 7-day window · R2 update notice · R3 cancel notice · R4 node deactivation
guard · R5 reactivation is Backoffice-only · R6 pending on registration · R7 NIC
unique · R8 slot availability · R9 QR validity · R10 Backoffice-only
administration · R11 prosumers see only their own data · R12 node closed that day
· R13 no duplicate booking on one slot. All in
`backend/SmartSolar.Api/Services/`, each returning a stable `errorCode`.

**Q. How do the three roles differ, and which clients does each use?**

`Backoffice` — administration, web only. `GridOperator` — operations, **both**
web and mobile (the only role on both). `Prosumer` — mobile, keyed by NIC. Login
routes by role: Backoffice and GridOperator to the dashboard, Prosumer to their
bookings.

**Q. How was the system verified?**

`docs/verify-api.ps1` — 90 assertions against a running API and MongoDB, covering
authentication, seed data, the map endpoint, the reservation workflow, every
rule, role enforcement, account management and the response envelope. All 90
pass. Builds: `dotnet build` 0 warnings 0 errors; `npm run build` (`tsc -b` +
`vite build`) clean; `gradlew assembleDebug` produces a 6.7 MB APK.

**Q. Show me the app running against the API on a real phone.**

`adb reverse tcp:5218 tcp:5218` maps the phone's own `127.0.0.1:5218` to the
laptop's, so it works on any network with no firewall rule. Start the API with
`dotnet run --launch-profile device` (binds `0.0.0.0`). `10.0.2.2` in
`local.properties` is the *emulator's* alias for the host and means nothing on a
physical device — that was the original misconfiguration.

---

## 7. Group deliverables — Table 1 (35 marks, shared)

These are marked once for the group, but somebody has to do them.

| Deliverable | Lead | Support |
|---|---|---|
| Report §1–2 (introduction, architecture, FAT-service justification) | Nisal | Sandun |
| Report §3 (database design) | Sandun | Nisal |
| Report §4 (business rules) | Akash | Chathushka |
| Report §5–7 (API, web, mobile walkthroughs) | each writes their own slice | — |
| All diagrams (`docs/diagrams/DIAGRAMS.md` → export PNGs) | Nisal | Akash |
| Web screenshots (already captured, `docs/screenshots/web`) | Sandun | — |
| **Mobile screenshots + opening-screen shot** | Chathushka | Akash |
| Deployment / IIS section + reproducible steps | Sandun | Nisal |
| README, repo hygiene, meaningful commit history | Nisal | all |
| Source code pasted as text into the report | all — own slice | — |
| **5-minute demonstration video** | Akash narrates | all appear |

### Still outstanding

- [ ] **Google Maps API key** — `local.properties` still has the
      `AIzaSyDUMMY_...` placeholder, so the map renders blank tiles. That is
      5 marks. Get a real key from the Cloud Console with "Maps SDK for Android"
      enabled.
- [ ] Mobile screenshots — `docs/CAPTURE_MOBILE_SCREENSHOTS.md` has the exact
      `adb` commands and the full screen list.
- [ ] Git repository link in the README and the report.
- [ ] Demonstration video and its link.
- [ ] Section 8 below, completed by each member.

---

## 8. Declared use of AI tools

The assignment is levelled at **AI Assessment Scale Level 2 — AI Planning**: AI
tools may be used during the planning stage only, and the implementation is
expected to be your own. Each member completes their own entry, honestly. No
marks are awarded for the disclosure itself — but at the viva you will be asked
structured questions about your work and your use of AI, and **any code, diagram
or documentation you cannot explain or modify may receive reduced or zero
individual marks**.

### Chathushka
- **Tools used:** _______________________
- **Stage(s) used:** _______________________
- **What it was used for:** _______________________
- **Reflection — how did the initial idea become the final solution?** _______________________

### Nisal
- **Tools used:** _______________________
- **Stage(s) used:** _______________________
- **What it was used for:** _______________________
- **Reflection:** _______________________

### Akash
- **Tools used:** _______________________
- **Stage(s) used:** _______________________
- **What it was used for:** _______________________
- **Reflection:** _______________________

### Sandun
- **Tools used:** _______________________
- **Stage(s) used:** _______________________
- **What it was used for:** _______________________
- **Reflection:** _______________________

---

## 9. Pre-viva checklist

Work through this **as a group**, out loud, a few days before.

**Each member, on your own slice:**

- [ ] Open every file you own and explain what it does without reading the comments.
- [ ] Point to the exact line where each of your rules is enforced.
- [ ] Trace one request from a button tap through to MongoDB and back.
- [ ] Deliberately break one of your rules in the running app and show the error.
- [ ] Change something small in your slice and rebuild — prove you can modify it.

**Each member, on the whole system:**

- [ ] Answer every question in section 6.
- [ ] Name the four collections and the three roles from memory.
- [ ] Explain why the clients hold no business logic, with a concrete example.
- [ ] Log in as all three roles and show what each can and cannot do.

**As a group, rehearse the end-to-end demo:**

1. `/health` responding, MongoDB connected
2. Backoffice creates a node, generates slots
3. Prosumer registers on the phone → appears in the web pending queue → activated
4. Prosumer books a slot → operator approves → QR appears
5. Operator scans that QR and finalises the transfer
6. The 12-hour rule blocking a late cancellation
7. Rule R4 blocking a node deactivation

Have the API, MongoDB, the web app and the phone **already running** before you
walk in. Run `adb reverse tcp:5218 tcp:5218` again after any reconnect.

---

*Prepared for SE4040 Enterprise Application Development, 2026.*
