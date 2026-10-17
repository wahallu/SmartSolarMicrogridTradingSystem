# Smart Solar Microgrid Trading System
## Project Report

**Module:** SE4040 – Enterprise Application Development
**Programme:** BSc (Hons) in Information Technology, specialising in Software Engineering
**Year / Semester:** Year 4, Semester 2 (2026)
**Assignment:** Assignment 1 – Client-Server Application (Web, Mobile and Web Service)
**Submitted:** _[date]_

| IT Number | Name |
|---|---|
| ITxxxxxxxx | Chathushka |
| ITxxxxxxxx | Nisal |
| ITxxxxxxxx | Akash |
| ITxxxxxxxx | Sandun |

---

## Table of contents

1. [Introduction](#1-introduction)
2. [System architecture](#2-system-architecture)
3. [Database design](#3-database-design)
4. [Business rules](#4-business-rules)
5. [Web service (C# Web API)](#5-web-service-c-web-api)
6. [Web application](#6-web-application)
7. [Mobile application](#7-mobile-application)
8. [Diagrams](#8-diagrams)
9. [Screenshots](#9-screenshots)
10. [Testing and verification](#10-testing-and-verification)
11. [Deployment](#11-deployment)
12. [Source code](#12-source-code)
13. [Individual contributions](#13-individual-contributions)
14. [Challenges](#14-challenges)
15. [References](#15-references)

---

## 1. Introduction

### 1.1 Purpose

The Smart Solar Microgrid Trading System lets owners of rooftop solar arrays —
*prosumers* — trade surplus energy with a network of neighbourhood microgrid nodes.
A prosumer reserves a time slot at a node, either to charge from the grid or to drop
surplus energy into it. When a booking is approved the system issues a secure QR code;
at the node a grid operator scans that code, the server verifies it, and the operator
records how much energy actually moved.

### 1.2 Scope

The system comprises three deployable parts:

| Part | Technology | Users |
|---|---|---|
| Central Web API | ASP.NET Core 8 (C#) on IIS, MongoDB | — |
| Web application | React 18, TypeScript, Tailwind CSS 3 | Backoffice officers, Grid operators |
| Mobile application | Pure native Android (Java), SQLite | Solar prosumers, Grid operators |

### 1.3 Actors

* **Backoffice officer** — registers microgrid nodes, maintains their schedules,
  creates web users, and is the *only* role permitted to activate or reactivate an
  account.
* **Grid operator** — works on both the web and the mobile client. Keeps battery bay
  availability current, monitors bookings at their node, and finalises energy transfers
  in the field by scanning a prosumer's QR code.
* **Solar prosumer** — registers on the phone using their National Identity Card
  number, reserves and manages energy slots, and reviews their transfer history.

---

## 2. System architecture

### 2.1 Overview

The system is a three-tier client–server application. The defining architectural
constraint set by the assignment is the **FAT service pattern**: all business logic
must reside in the central API, and both clients must act purely as user interfaces.

```
   PRESENTATION TIER                       APPLICATION TIER                DATA TIER

  ┌──────────────────┐                ┌────────────────────────┐      ┌──────────────┐
  │ React 18 + TS    │                │  ASP.NET Core 8 API    │      │   MongoDB    │
  │ Tailwind CSS 3   │──── REST ─────▶│                        │      │              │
  │                  │     + JWT      │  Controllers  (thin)   │      │  Users       │
  │ Backoffice       │                │        ↓               │      │  SolarStat…  │
  │ Grid operators   │                │  Services (FAT)        │─────▶│  EnergyBook… │
  └──────────────────┘                │   • rules R1–R13       │      │  EnergyRese… │
                                      │   • Haversine distance │      │              │
  ┌──────────────────┐                │   • QR issue / verify  │      └──────────────┘
  │ Native Android   │                │        ↓               │
  │ Java + SQLite    │──── REST ─────▶│  MongoDbContext        │
  │                  │     + JWT      │                        │
  │ Prosumers        │                │  Hosted on Windows IIS │
  │ Grid operators   │                └────────────────────────┘
  └────────┬─────────┘
           │
      ┌────▼─────┐
      │  SQLite  │  session + offline cache only — NOT the source of truth
      └──────────┘
```

### 2.2 How the FAT service constraint is honoured

This was the single most important design decision, so it is worth stating precisely
how it is enforced rather than merely asserted.

**1. Controllers cannot hold logic.** Every controller action is three lines: bind the
DTO, call one service method, map the returned `ServiceResult` to an HTTP status code.
There is not a single `if` statement over domain data in any controller. The mapping
itself is a shared helper:

```csharp
private ActionResult<ApiResponse<T>> Respond<T>(ServiceResult<T> result)
{
    var body = result.IsSuccess
        ? ApiResponse<T>.Ok(result.Value, result.Message)
        : ApiResponse<T>.Fail(result.Message, result.ErrorCode, result.Errors);

    return result.Status switch
    {
        ResultStatus.Success         => Ok(body),
        ResultStatus.Created         => StatusCode(StatusCodes.Status201Created, body),
        ResultStatus.ValidationError => BadRequest(body),
        ResultStatus.NotFound        => NotFound(body),
        ResultStatus.Conflict        => Conflict(body),
        ResultStatus.Forbidden       => StatusCode(StatusCodes.Status403Forbidden, body),
        ResultStatus.Unauthorized    => Unauthorized(body),
        _                            => StatusCode(StatusCodes.Status500InternalServerError, body),
    };
}
```

`ServiceResult` deliberately carries no ASP.NET types, so the service layer has no
dependency on the web framework and could be reused by a different host unchanged.

**2. The clients are given answers, not inputs to a rule.** The clearest example is the
12-hour notice period. Rather than sending the clients a timestamp and trusting them to
compare it, the API evaluates the rule per record and ships the verdict as two booleans:

```csharp
var hoursUntilStart = (reservation.ReservationStartUtc - utcNow).TotalHours;
var isChangeableState = reservation.Status is ReservationStatus.Pending
                                           or ReservationStatus.Approved;
var hasNotice = hoursUntilStart >= noticeHours;

CanModify = isChangeableState && hasNotice,
CanCancel = isChangeableState && hasNotice,
```

The React client writes `disabled={!reservation.canCancel}` and the Android client
writes `btnCancel.setEnabled(reservation.canCancel())`. Neither ever computes an hour
difference. The same pattern covers slot availability (`isBookable`).

**3. Attempting the action anyway is still rejected.** Disabling a button is a courtesy,
not a control. Every rule is re-checked server-side on the write path, so a crafted
request — or a stale page — is refused with the appropriate error code.

**4. Neither client can reach the database.** There is no MongoDB driver in either
client project. The web application's only network dependency is `axios`; the Android
application uses `java.net.HttpURLConnection` from the platform itself.

### 2.3 Request lifecycle

```
Client → HTTPS request with Authorization: Bearer <JWT>
       → ExceptionHandlingMiddleware   (catches anything unhandled, returns JSON)
       → MethodOverrideMiddleware      (X-HTTP-Method-Override → real verb)
       → UseRouting
       → Authentication                (validates the JWT signature and lifetime)
       → Authorization                 ([Authorize(Roles = …)] on the action)
       → Controller action             (binds DTO, model validation runs)
       → Service method                (business rules; returns ServiceResult)
       → MongoDbContext                (typed collection access)
       → MongoDB
       ← ServiceResult → ApiResponse envelope → HTTP status code
```

### 2.4 The response envelope

Regardless of outcome, every endpoint returns the same JSON envelope. This uniformity means each client requires exactly one deserialiser and one error handler, eliminating the need for per-endpoint handling.

```json
{
  "success": true,
  "message": "Reservation created successfully. It is pending approval.",
  "data": { },
  "errorCode": null,
  "errors": null,
  "timestamp": "2026-09-05T15:33:32.374Z"
}
```

On failure `errorCode` carries a stable machine-readable identifier such as
`RES_CANCEL_12H`. The clients branch on that code, never on the English message, so
wording can change without breaking either client.

---

## 3. Database design

### 3.1 Choice of database

MongoDB was selected as required by the specification. It suits this domain well: a
microgrid node's weekly schedule is a naturally nested array of seven day-records, and
storing it as a sub-document avoids the join a relational design would need.

Database name: **`SmartSolarMicrogridDb`**. Four collections, exactly as the marking
scheme specifies.

### 3.2 Collection: `Users`

Holds every principal — Backoffice officers, Grid Operators and Solar Prosumers. One
collection keeps authentication uniform: a single lookup serves all three roles.

| Field | Type | Notes |
|---|---|---|
| `_id` | ObjectId | Mongo identifier |
| `nic` | string \| null | **Prosumer primary key.** Unique + sparse; null for staff |
| `username` | string | Unique. For prosumers the NIC is copied here |
| `email` | string | Unique + sparse |
| `passwordHash` | string | PBKDF2-SHA256, 100 000 iterations, base64 |
| `passwordSalt` | string | 16 random bytes, base64, per account |
| `fullName`, `phone`, `address` | string | Profile |
| `role` | string | `Backoffice` \| `GridOperator` \| `Prosumer` |
| `status` | string | `Pending` \| `Active` \| `Deactivated` |
| `deactivationRequested` | bool | Raised by the prosumer from the mobile app |
| `deactivationRequestedAt` | date | UTC |
| `deactivationReason` | string | Free text |
| `assignedStationId` | string | Grid operator's home node |
| `lastLoginAt` | date | Set on each successful login |
| `createdAt`, `updatedAt`, `createdBy`, `updatedBy` | | Audit |

**Indexes:** `nic` (unique, sparse), `username` (unique), `email` (unique, sparse),
`{role, status}` compound.

> **Why sparse?** A unique index on `nic` would otherwise reject a second staff account,
> because two documents with `nic: null` collide. Marking the index sparse excludes
> documents that lack the field entirely, so the uniqueness guarantee applies only to
> prosumers, which is exactly what the specification asks for.

### 3.3 Collection: `SolarStationInfo`

| Field | Type | Notes |
|---|---|---|
| `_id` | ObjectId | |
| `stationCode` | string | Unique, e.g. `STN-001` |
| `name`, `address`, `city` | string | |
| `type` | string | `Solar` \| `Hybrid` \| `Battery` |
| `location` | object | `{ latitude, longitude }` — **GPS location** |
| `capacityKwh` | double | **Capacity specification (kW/h)** |
| `totalBatterySlots` | int | **Battery storage slots** |
| `availableBatterySlots` | int | Maintained by the grid operator |
| `operatingHours` | object | `{ openTime, closeTime }` as `"HH:mm"` |
| `weeklySchedule` | array | 7 × `{ dayOfWeek, isOpen, openTime, closeTime }` |
| `contactNumber` | string | |
| `status` | string | `Active` \| `Inactive` |

**Indexes:** `stationCode` (unique), `status`.

### 3.4 Collection: `EnergyBookingSlots`

| Field | Type | Notes |
|---|---|---|
| `_id` | ObjectId | |
| `stationId` | ObjectId ref | → `SolarStationInfo._id` |
| `stationCode`, `stationName` | string | Denormalised for list screens |
| `slotCode` | string | `S1`, `S2`… unique within station + date |
| `slotDate` | date | UTC midnight of the slot's day |
| `startTime`, `endTime` | string | `"08:00"` — display only |
| `startUtc`, `endUtc` | date | **Absolute instants; all rules compare on these** |
| `slotType` | string | `Charging` \| `DropOff` |
| `energyCapacityKwh` | double | Ceiling on a booking's energy |
| `batterySlotNumber` | int | Which physical bay |
| `maxBookings` | int | Normally 1 |
| `bookedCount` | int | Maintained atomically with `$inc` |
| `status` | string | `Available` \| `Full` \| `Blocked` |

**Indexes:** `{stationId, slotDate, slotCode}` (unique), `startUtc`,
`{stationId, status}`.

### 3.5 Collection: `EnergyReservation`

| Field | Type | Notes |
|---|---|---|
| `_id` | ObjectId | |
| `reservationNo` | string | Unique, `RSV-20260904-0001` |
| `prosumerNic` | string ref | → `Users.nic` |
| `prosumerName`, `prosumerPhone` | string | Denormalised |
| `stationId`, `stationCode`, `stationName` | | Denormalised |
| `slotId`, `slotCode` | | |
| `reservationStartUtc`, `reservationEndUtc` | date | **Rules R1/R2/R3 measure on these** |
| `startTime`, `endTime`, `slotType` | | Display |
| `energyKwh` | double | Requested |
| `deliveredKwh` | double \| null | Captured by the operator at completion |
| `status` | string | `Pending` \| `Approved` \| `Completed` \| `Cancelled` \| `Rejected` |
| `qrToken` | string \| null | 32 random bytes, URL-safe base64. Unique + sparse |
| `qrPayload` | string \| null | Exact JSON encoded into the QR image |
| `qrIssuedAt` | date | |
| `approvedBy`/`At`, `completedBy`/`At`, `cancelledBy`/`At` | | Audit trail |
| `cancelReason`, `rejectedReason`, `operatorNotes` | string | |

**Indexes:** `reservationNo` (unique), `qrToken` (unique, sparse),
`{prosumerNic, reservationStartUtc}`, `{stationId, status, reservationStartUtc}`.

### 3.6 Denormalisation

`stationCode`, `stationName`, `prosumerName` and `prosumerPhone` are copied onto the
reservation at creation time. This is deliberate. Rendering a page of twenty bookings
would otherwise require twenty additional lookups; with the copies it is a single query.
The trade-off is that a renamed node must propagate, which `StationService.UpdateAsync`
handles explicitly with an `UpdateManyAsync` over the affected slots and reservations.

### 3.7 Time handling

Every persisted instant is UTC. Slot wall-clock times are stored twice: as
`"HH:mm"` strings for display, and as absolute `startUtc` / `endUtc` values. **Every
operator-entered wall time is interpreted in `Asia/Colombo` and converted to UTC;
the device or server time zone does not affect it. Every business rule compares only
against the absolute values.** The service layer never calls
`DateTime.UtcNow` directly inside a rule; it reads the current instant from an injected
`IClock`, which makes each rule testable with a frozen clock.

---

## 4. Business rules

All thirteen rules live in `backend/SmartSolar.Api/Services/`. Each returns a stable
error code.

| # | Rule | Error code | HTTP |
|---|---|---|---|
| R1 | A reservation must start in the future and within **7 days** | `RES_WINDOW_7_DAYS` | 400 |
| R2 | Updates require ≥ **12 hours** notice, and status ∈ {Pending, Approved} | `RES_UPDATE_12H` | 400 |
| R3 | Cancellations require ≥ **12 hours** notice | `RES_CANCEL_12H` | 400 |
| R4 | A node cannot be deactivated while it holds future Pending/Approved bookings | `STATION_HAS_ACTIVE_RES` | 409 |
| R5 | A `Deactivated` account cannot log in; only Backoffice may reactivate | `ACCOUNT_DEACTIVATED` | 403 |
| R6 | Self-registered prosumers start `Pending` and cannot log in until activated | `ACCOUNT_PENDING` | 403 |
| R7 | NIC is unique across all accounts | `NIC_ALREADY_EXISTS` | 409 |
| R8 | A slot must be `Available`, have capacity, be in the future, and its node active | `SLOT_UNAVAILABLE` | 409 |
| R9 | A scanned QR must exist, be `Approved`, and fall within ±2 h of its window | `QR_INVALID` / `QR_NOT_APPROVED` / `QR_OUT_OF_WINDOW` | 400 |
| R10 | Only Backoffice may manage web users and activate accounts | `FORBIDDEN` | 403 |
| R11 | A prosumer may only read or modify their own data | `FORBIDDEN` | 403 |
| R12 | A node closed on that weekday cannot be booked | `STATION_CLOSED` | 400 |
| R13 | A prosumer may not hold two active bookings on one slot | `DUPLICATE_RESERVATION` | 409 |

### 4.1 The 7-day window (R1)

```csharp
// ***RULE R1*** - the slot must start in the future and no more than the configured
// number of days ahead. Both bounds are checked against the injected clock so the rule
// can be unit tested with a frozen time.
if (slot.StartUtc <= now || slot.StartUtc > now.AddDays(WindowDays))
{
    return ServiceResult<ReservationDto>.Invalid(
        $"Reservations must be scheduled within the next {WindowDays} days and cannot be in the past.",
        ErrorCodes.ReservationWindow);
}
```

The Android date picker also caps its `maxDate` at seven days out — but purely as a
convenience. The comment in that code states explicitly that the API remains the
authority and will reject anything outside the window regardless of what the picker
allowed.

### 4.2 The 12-hour notice period (R2 / R3)

```csharp
// ***RULE R3*** - a cancellation this close to the start time would strand a battery
// bay that can no longer be resold, so the service refuses it.
var hoursOfNotice = (reservation.ReservationStartUtc - _clock.UtcNow).TotalHours;
if (hoursOfNotice < NoticeHours)
{
    return ServiceResult<ReservationDto>.Invalid(
        $"Reservations can only be cancelled at least {NoticeHours} hours before the scheduled start time.",
        ErrorCodes.ReservationCancelNotice);
}
```

### 4.3 The node deactivation guard (R4)

```csharp
// ***RULE R4*** - a node holding Pending or Approved reservations that have not started
// yet cannot be taken out of service, because those prosumers have nowhere to go.
var activeCount = await CountActiveReservationsAsync(id, ct);
if (activeCount > 0)
{
    return ServiceResult<StationDto>.Conflict(
        $"This microgrid node cannot be deactivated because it still has {activeCount} " +
        "active energy reservation(s). Cancel or complete them first.",
        ErrorCodes.StationHasActiveReservations);
}
```

The same helper populates `StationDto.ActiveReservationCount`, so the web UI can warn
the officer before they click — while the guard itself remains server-side.

### 4.4 Rule ordering

Rule order is itself a design decision. During testing, attempting a duplicate booking
returned `SLOT_UNAVAILABLE` rather than `DUPLICATE_RESERVATION`: because a slot normally
has `maxBookings = 1`, the prosumer's own first booking had already filled it, so the R8
capacity check fired before R13 could. The checks were reordered so R13 runs first,
giving the caller the truthful message *"You already hold an active reservation for this
slot"* instead of the misleading *"this slot is full"*.

### 4.5 QR security (R9)

The QR payload is a compact JSON document:

```json
{"v":1,"t":"<token>","r":"RSV-20260907-0007","n":"200356701199","s":"STN-001","e":"2026-09-07T16:00:00Z"}
```

The critical property is that **the server trusts only the token**. Every other field is
re-read from MongoDB during verification. A tampered payload claiming a different
reservation number or NIC changes nothing, because those values are never read from the
scanned content. The scanner may post either the bare token or the whole payload; the
service parses both:

```csharp
// The scanned text may be the bare token or the entire JSON payload. Either is accepted,
// but only the token is trusted - every other field is re-read from the database, never
// taken from the scanned content.
var token = raw.Trim();
if (token.StartsWith('{'))
{
    try
    {
        using var document = JsonDocument.Parse(token);
        if (document.RootElement.TryGetProperty("t", out var tokenElement))
        {
            token = tokenElement.GetString() ?? token;
        }
    }
    catch (JsonException)
    {
        // Not JSON after all - fall through and treat the whole string as the token.
    }
}
```

On completion the token is cleared, so the same code cannot be scanned twice. If an
approved booking is moved to a different slot, a fresh token is issued for the same
reason — a previously screenshotted code must not be replayable against the new slot.

---

## 5. Web service (C# Web API)

### 5.1 Technology

| Component | Choice | Reason |
|---|---|---|
| Framework | ASP.NET Core 8 (LTS) | Long-term support; first-class IIS hosting |
| Database driver | MongoDB.Driver 2.28 | Official driver |
| Authentication | JWT bearer, HMAC-SHA256 | Stateless; identical for both clients |
| Password hashing | PBKDF2-SHA256, 100 000 iterations | Recommended by Microsoft |
| QR generation | QRCoder 1.6 (`PngByteQRCode`) | Cross-platform; no `System.Drawing` |
| Documentation | Swashbuckle (Swagger UI) | Lets a marker exercise the API directly |

> **A note on QRCoder.** The obvious `QRCode` renderer depends on `System.Drawing.Common`,
> which is Windows-only on .NET 8 and throws at runtime elsewhere. `PngByteQRCode`
> produces the PNG bytes directly with no platform graphics dependency.

### 5.2 Project structure

```
SmartSolar.Api/
├── Controllers/     7 thin controllers
├── Services/
│   ├── Interfaces/  IServices.cs — the entire service contract in one file
│   └── Implementations/
│       ├── AuthService.cs           login, registration, password change
│       ├── UserService.cs           Backoffice / GridOperator accounts
│       ├── ProsumerService.cs       prosumers, keyed by NIC
│       ├── StationService.cs        nodes, schedules, Haversine "nearby"
│       ├── SlotService.cs           slots, bulk generation
│       ├── ReservationService.cs    the booking workflow and QR lifecycle
│       ├── QrCodeGenerator.cs       PNG rendering, token creation
│       └── DomainMapper.cs          entity → DTO projections
├── Models/          The four MongoDB documents + enums
├── DTOs/            Request/response contracts with validation attributes
├── Data/            MongoDbContext (indexes), DataSeeder
├── Security/        PasswordHasher, JwtTokenService, CurrentUser
├── Middleware/      ExceptionHandling, MethodOverride
└── web.config       IIS / ASP.NET Core Module configuration
```

### 5.3 Authentication and authorisation

Login accepts a **NIC, a username or an e-mail address** in one field. The lookup uses an
anchored, case-insensitive regular expression, with the user's input escaped first so
regex metacharacters cannot be injected:

```csharp
// The identifier is escaped before it becomes part of a regex so a user cannot inject
// regex metacharacters, and anchored so it matches the whole field rather than a substring.
var pattern = new BsonRegularExpression($"^{Regex.Escape(identifier)}$", "i");
```

Both "user not found" and "wrong password" return the **same** message, so the endpoint
does not disclose which accounts exist.

Successful login issues a JWT valid for eight hours carrying the user id, username,
role, NIC and assigned station. `ClockSkew` is set to zero so an expired token is
rejected immediately rather than up to five minutes later.

### 5.4 Server-side distance calculation

The "nearby nodes" feature is a good illustration of the FAT-service rule. It would be
easy to send the client every node and let it compute distances — and that is exactly
what the constraint forbids. The Haversine formula runs in `StationService`:

```csharp
/// <summary>
/// Great circle distance between two GPS positions using the Haversine formula.
/// This runs on the SERVER, not in a client, because ranking nodes by proximity is
/// business logic and the FAT-service pattern keeps all of it in the API.
/// </summary>
private static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
{
    const double earthRadiusKm = 6371.0;
    var dLat = ToRadians(lat2 - lat1);
    var dLon = ToRadians(lon2 - lon1);

    var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
          + Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2))
          * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

    return earthRadiusKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
}
```

### 5.5 Concurrency

`bookedCount` is maintained with MongoDB's atomic `$inc` rather than a read-modify-write:

```csharp
// Inc is atomic in MongoDB, so two prosumers booking the last place on the same slot at
// the same moment cannot both read a stale count and both succeed.
var update = Builders<EnergyBookingSlot>.Update.Inc(s => s.BookedCount, delta);
await _db.Slots.UpdateOneAsync(filter, update, cancellationToken: ct);
```

### 5.6 Endpoint summary

Base path `/api/v1`. 56 endpoints across seven controllers.

| Controller | Endpoints |
|---|---|
| `AuthController` | login, register, me, change-password |
| `UsersController` | list, get, create, update, activate, deactivate, reset-password, delete |
| `ProsumersController` | list, pending, get by NIC, create, update, activate, deactivate, request-deactivation, delete |
| `StationsController` | list, nearby, get, create, update, schedule, availability, activate, deactivate, delete |
| `SlotsController` | list, available, get, create, generate, update, block, delete |
| `ReservationsController` | list, my, history, dashboard, get, qr, create, update, cancel, approve, reject, verify-qr, complete |
| `HealthController` | health |

Full request and response shapes are documented in `docs/API_CONTRACT.md`.

---

## 6. Web application

### 6.1 Technology

React 18 + TypeScript + Vite, styled with **Tailwind CSS 3**. Routing with React
Router 6, HTTP with axios, charts with Recharts, maps with Leaflet + OpenStreetMap.

> **Why Leaflet rather than Google Maps on the web?** The Google Maps JavaScript API
> requires a billing-enabled key. Leaflet with OpenStreetMap tiles needs no key at all,
> so the map screen works on any machine the project is opened on — including a marker's.
> Google Maps *is* used on the Android client, where the specification requires it.

### 6.2 Structure

```
web/src/
├── api/
│   ├── client.ts        axios instance, interceptors, ApiError, envelope unwrapper
│   └── services.ts      one typed function per endpoint
├── components/
│   ├── ui.tsx           Spinner, Modal, ConfirmDialog, Toast, StatCard, Pagination…
│   ├── Layout.tsx       sidebar shell, role-filtered navigation, live API health dot
│   └── ProtectedRoute.tsx
├── context/AuthContext.tsx
├── pages/               12 screens
├── types/index.ts       TypeScript mirrors of every API DTO
└── utils/format.ts      date and badge helpers
```

### 6.3 Error handling

A single response interceptor turns every failure into a typed `ApiError`, so each
screen has exactly one thing to catch:

```typescript
http.interceptors.response.use(
  (response) => response,
  (error: AxiosError<ApiResponse<unknown>>) => {
    // No response at all means DNS, CORS, a timeout or a stopped API.
    if (!error.response) {
      return Promise.reject(new ApiError(
        'Could not reach the Smart Solar service. Please confirm the API is running.',
        0, 'NETWORK_ERROR'));
    }
    const { status, data } = error.response;
    return Promise.reject(new ApiError(
      data?.message ?? `The request failed with HTTP ${status}.`,
      status, data?.errorCode ?? null, data?.errors ?? null));
  },
);
```

### 6.4 Screens

| Route | Screen | Roles |
|---|---|---|
| `/` | Public landing page | anyone |
| `/login` | Sign in, role-based redirect | anyone |
| `/dashboard` | Live counters, status chart, upcoming bookings | all |
| `/reservations` | Filterable list; approve, reject, cancel | all |
| `/reservations/:id` | Detail, QR panel, audit trail | owner / staff |
| `/stations` | Node cards; edit, schedule, availability, activate | all (edit: Backoffice) |
| `/slots` | Slot table; create, bulk generate, block, delete | Backoffice, Operator |
| `/prosumers` | Prosumer list keyed by NIC | Backoffice, Operator |
| `/prosumers/pending` | **Pending-activation queue** | Backoffice |
| `/users` | Web user administration | Backoffice |
| `/map` | Leaflet map of all nodes | all |
| `/profile` | Own profile, password change | all |

---

## 7. Mobile application

### 7.1 Compliance with the "pure native" constraint

The specification requires pure native Android with SQLite and forbids cross-platform
frameworks. The project honours this strictly:

| Concern | Choice | Deliberately **not** used |
|---|---|---|
| Language | Java | Kotlin |
| Views | XML layouts + `findViewById` | View binding, data binding, Compose |
| Networking | `java.net.HttpURLConnection` | Retrofit, OkHttp, Volley |
| JSON | `org.json` (bundled with Android) | Gson, Jackson, Moshi |
| Persistence | `SQLiteOpenHelper` | Room, any ORM |
| Concurrency | `ExecutorService` + `Handler` | RxJava, Coroutines |
| Dependency injection | Plain constructors | Dagger, Hilt |

The only third-party artefacts are AndroidX/Material (Google's standard UI toolkit),
`play-services-maps` and `play-services-location` (required by the specification for
maps), and `zxing-android-embedded` (required for QR scanning). These are libraries, not
application frameworks.

### 7.2 Networking

`ApiClient` wraps `HttpURLConnection`, runs requests on a fixed thread pool, and posts
results back to the main looper so activities can touch views directly:

```java
private void enqueue(final String method, final String path, final JSONObject body,
                     final String token, final Callback callback) {
    // A connectivity check up front turns a 20 second timeout into an instant message.
    if (!isOnline()) {
        MAIN_HANDLER.post(() -> callback.onResult(
                ApiResult.networkFailure("No internet connection. Please check your network and try again.")));
        return;
    }

    EXECUTOR.execute(() -> {
        final ApiResult result = execute(method, path, body, token);
        MAIN_HANDLER.post(() -> callback.onResult(result));
    });
}
```

### 7.3 SQLite

Four tables, created directly through `SQLiteOpenHelper`:

| Table | Purpose |
|---|---|
| `session` | The signed-in user and their JWT — a single row |
| `stations` | Cached microgrid nodes for the map and pickers |
| `reservations` | Cached bookings for the list screens |
| `sync_log` | Audit of what was cached and when |

The assignment asks specifically that **login details persist in SQLite**, so the token
and profile are written to the `session` table rather than to `SharedPreferences`. The
splash screen reads that row to decide whether to show the login form or go straight to
a role home screen.

SQLite is a **cache and session store, never the authority**. When a list loads
successfully it is cached; if a later request fails with a network error the cached rows
are shown together with a visible "showing saved data" banner. Every write — create,
update, cancel, approve, complete — goes straight to the API.

### 7.4 Screens

| Activity | Purpose |
|---|---|
| `SplashActivity` | Reads the SQLite session, routes by role |
| `LoginActivity` | Sign in; handles `ACCOUNT_PENDING` / `ACCOUNT_DEACTIVATED` distinctly |
| `RegisterActivity` | Prosumer self-registration with NIC as primary key |
| `ProsumerHomeActivity` | Dashboard: live counts, quick actions, upcoming bookings |
| `OperatorHomeActivity` | Operator console scoped to the assigned node |
| `NewBookingActivity` | Node → date → slot → energy booking flow |
| `MyBookingsActivity` | Current / Pending / History tabs plus search |
| `BookingDetailActivity` | Detail, QR display, edit and cancel gated on server flags |
| `EditBookingActivity` | Move slot or change energy |
| `BookingSummaryActivity` | The summary page shown after every action |
| `StationsMapActivity` | **Google Maps** with nearby nodes ranked by the API |
| `StationDetailActivity` | Node details on marker selection |
| `ScannerActivity` | **QR scanning** with ZXing |
| `VerifyResultActivity` | Verified booking, then finalise the transfer |
| `OperatorBookingsActivity` | Node bookings with status filter and debounced search |
| `ProfileActivity` | Edit profile, change password, request deactivation |

---

## 8. Diagrams

The full set — high-level architecture, use case, DFD level 0 and level 1, ER, the
reservation state diagram and two sequence diagrams — is in
[`docs/diagrams/DIAGRAMS.md`](diagrams/DIAGRAMS.md), written in Mermaid so it stays in
version control. Paste any block into <https://mermaid.live> to export a PNG for
printing.

_[Insert exported diagram images here.]_

---

## 9. Screenshots

Captured from the running system in `docs/screenshots/`.

### 9.1 Web application

| # | Screen | File |
|---|---|---|
| 00 | Public landing page | `web/00-home-landing.png` |
| 01 | Login | `web/01-login.png` |
| 02 | Backoffice dashboard | `web/02-dashboard-backoffice.png` |
| 03 | Reservations list with filters | `web/03-reservations-list.png` |
| 04 | Reservation detail with QR code | `web/04-reservation-detail-qr.png` |
| 05 | Microgrid nodes | `web/05-microgrid-nodes.png` |
| 06 | Booking slots | `web/06-booking-slots.png` |
| 07 | Prosumers | `web/07-prosumers.png` |
| 08 | Pending activations queue | `web/08-pending-activations.png` |
| 09 | Web users | `web/09-web-users.png` |
| 10 | Station map | `web/10-station-map.png` |
| 11 | Profile | `web/11-profile.png` |
| 12 | Grid operator dashboard | `web/12-dashboard-operator.png` |
| 13 | Prosumer reservations view | `web/13-prosumer-reservations.png` |
| 14 | Responsive layout at 390 px | `web/14-home-responsive-mobile.png` |

### 9.2 Mobile application

_[Capture from an emulator or device and list here. The opening screen — required by the
submission instructions — is the prosumer home screen.]_

---

## 10. Testing and verification

The API was verified end to end against a live MongoDB instance with a scripted suite of
**90 assertions** covering authentication, seed data, the map feature, the reservation
workflow, every business rule, role enforcement, account management and the response
envelope. The final run passes **90 of 90**.

| Area | Assertions | Result |
|---|---|---|
| Authentication and role routing | 11 | all pass |
| Seed data and collections | 9 | all pass |
| Map / nearby with server distance | 4 | all pass |
| Reservation workflow, R1 / R8 / R13 | 8 | all pass |
| 12-hour rule R2 / R3 | 7 | all pass |
| Approval, QR issue / verify / complete (R9) | 9 | all pass |
| Dashboard counters | 7 | all pass |
| Prosumer views and search | 8 | all pass |
| Station deactivation guard R4 | 1 | pass |
| Role enforcement R10 / R11 | 5 | all pass |
| Account management R5 / R6 / R7 | 13 | all pass |
| Cancellation and validation envelope | 7 | all pass |
| HTTP verb override | 1 | pass |
| **Total** | **90** | **90 pass, 0 fail** |

Rules confirmed by direct observation rather than inspection include: a pending account
being refused login with `ACCOUNT_PENDING`; a deactivated account refused with
`ACCOUNT_DEACTIVATED` and restored only by a Backoffice caller; a booking more than seven
days out refused with `RES_WINDOW_7_DAYS`; a cancellation inside the twelve-hour window
refused with `RES_CANCEL_12H` while `canCancel` was already `false` on the same record;
a node holding future bookings refused deactivation with `STATION_HAS_ACTIVE_RES`; a QR
token accepted either bare or as the full scanned JSON payload, and rejected outside its
window with `QR_OUT_OF_WINDOW`; and a prosumer blocked from reading another prosumer's
profile or verifying a QR code.

### 10.1 Two defects found and fixed during testing

**1. Misleading error on a duplicate booking.** Attempting to book the same slot twice
returned `SLOT_UNAVAILABLE` instead of `DUPLICATE_RESERVATION`, because the R8 capacity
check ran before R13 and a single-booking slot was already full from the prosumer's own
first booking. Fixed by reordering the checks in both `CreateAsync` and `UpdateAsync`
so R13 evaluates first.

**2. HTTP verb override was inert.** The Android client falls back to
`POST` + `X-HTTP-Method-Override: PATCH` on implementations that reject `PATCH`. The
middleware that translates the header was registered after routing, so routing had
already rejected the request with 405 before the header was read. The root cause is that
`WebApplication` inserts `UseRouting()` at the *start* of the pipeline automatically when
endpoints are mapped. Fixed by calling `app.UseRouting()` explicitly after the middleware,
which suppresses the automatic insertion and pins routing to the correct position.

### 10.2 Build verification

| Target | Command | Result |
|---|---|---|
| Web API | `dotnet clean && dotnet build SmartSolar.sln` | Succeeded, **0 warnings, 0 errors** |
| Web application | `npm run build` (`tsc -b && vite build`) | Succeeded, **0 type errors** |
| Android | `./gradlew assembleDebug` (build directory deleted first) | Succeeded, `app-debug.apk`, **6.7 MB** |

### 10.3 Three further defects found by the Android build

The mobile build surfaced three problems that no amount of reading would have caught:

1. **`--` is not permitted inside an XML comment.** Five layouts used `---- Section ----`
   dividers inside their comment blocks, which is illegal XML: only the closing `-->`
   may contain consecutive hyphens. AAPT rejected them with a bare `ParseError` naming
   only a row and column.

2. **Implicit style inheritance.** `Widget.SmartSolar.StatusChip` was declared without a
   `parent` attribute. Android infers a style's parent from its dotted name when none is
   given, so it looked for `Widget.SmartSolar` — which does not exist — and resource
   linking failed. Adding an explicit `parent="android:Widget.TextView"` fixed it. This
   is a genuinely surprising rule and worth knowing.

3. **A mismatched adapter callback.** `ProsumerHomeActivity` implemented
   `ReservationAdapter.OnReservationClickListener` with a method `onReservationClick`,
   while the adapter declares `OnReservationClick` with `onClick`. Two other screens used
   the correct names, so only one file was wrong.

---

## 11. Deployment

Full IIS instructions are in the [README](../README.md#deploying-the-api-to-iis). In
summary:

1. Install the **.NET 8 Hosting Bundle** on the server (without it IIS returns 500.19/500.31).
2. `dotnet publish SmartSolar.Api -c Release -o C:\inetpub\SmartSolarApi`
3. Create an application pool with **No Managed Code**, and a site pointing at that folder.
4. Grant the pool identity read/execute on the folder.
5. Set `MongoDbSettings:ConnectionString` in the published `appsettings.json`.
6. Verify `http://<host>/api/v1/health` reports `"database": "Connected"`.
7. Repoint `VITE_API_BASE_URL` and `API_BASE_URL_DEBUG`, then rebuild both clients.

---

## 12. Source code

The complete source is in the accompanying repository and the submitted archive.

> **For the printed report:** paste the source of each file as text (not screenshots),
> grouped as below. Every `.cs` file carries a header comment block and every method an
> inline comment, as the submission instructions require.

* **Web API** — Models, DTOs, Services, Controllers, Data, Security, Middleware, Program.cs, web.config
* **Web application** — api, components, context, pages, types, utils, App.tsx, main.tsx
* **Mobile application** — activities, adapters, api, db, models, util, AndroidManifest.xml, layouts

---

## 13. Individual contributions

> Each member must have a **distinct and specific** list. Identical or vague entries lose
> marks under the marking scheme.

| IT Number | Name | Contribution |
|---|---|---|
| ITxxxxxxxx | **Chathushka** | **Identity, Access Control & Local Session (A: 8, B: 9, F.SQLite: 3 = 20 marks):**<br>• **C# Web API:** `AuthService`, `UserService`, `ProsumerService`, `PasswordHasher` (PBKDF2-SHA256, 100k rounds), `JwtTokenService` (8-hour bearer token, custom claims, zero clock skew), `CurrentUser` caller abstraction, `AuthController`, `UsersController`, `ProsumersController`, DTOs.<br>• **Web application:** `AuthContext`, `ProtectedRoute`, `LoginPage`, `UsersPage`, `ProsumersPage`, `PendingActivationsPage`, `ProfilePage`.<br>• **Native Android:** `SplashActivity` (instant resume from SQLite), `LoginActivity`, `RegisterActivity`, `ProfileActivity`, `SessionManager`, `DatabaseHelper` (`session` table DDL, upsert with `CONFLICT_REPLACE`), `ValidationUtils` (NIC validation).<br>• **Business rules:** R5 (deactivation/reactivation gate), R6 (pending self-registration), R7 (NIC uniqueness), R10 (Backoffice authority), R11 (prosumer data isolation). |
| ITxxxxxxxx | **Nisal** | **Microgrid Nodes, Slots & Geolocation (A: 5, E.map: 5, F.Maps: 3 = 13 marks):**<br>• **C# Web API:** `StationService`, `SlotService`, Haversine server-side distance formula (`StationService.HaversineKm`), `StationsController`, `SlotsController`.<br>• **Web application:** `StationsPage`, `SlotsPage`, `MapPage` (Leaflet + OpenStreetMap).<br>• **Native Android:** `StationsMapActivity` (Google Maps SDK, FusedLocationProviderClient), `StationDetailActivity`, `StationAdapter`, `SlotAdapter`.<br>• **Business rules:** R4 (node deactivation guard), R8 (slot bookability), R12 (weekday schedule guard). |
| ITxxxxxxxx | **Akash** | **Reservation Workflow & Business Rules (A: 5, C: 9, D.filter: 2 = 16 marks):**<br>• **C# Web API:** `ReservationService` (create, update, cancel, approve, reject), `ReservationsController` booking actions, `ServiceResult`, `ErrorCodes`, `IClock` abstraction.<br>• **Web application:** `ReservationsPage` (status filters, debounced search), `ReservationDetailPage`.<br>• **Native Android:** `NewBookingActivity`, `EditBookingActivity`, `BookingDetailActivity`, `BookingSummaryActivity`, `ReservationAdapter`, `DateUtils`.<br>• **Business rules:** R1 (7-day window), R2 (12-hour update notice), R3 (12-hour cancellation notice), R13 (duplicate booking guard). |
| ITxxxxxxxx | **Sandun** | **QR Verification, Dashboards & Platform (D: 8, E.QR: 2, F: 6 = 16 marks):**<br>• **C# Web API:** `QrCodeGenerator` (`PngByteQRCode` cross-platform), QR verification/completion in `ReservationService`, `MongoDbContext`, `DataSeeder`, `ExceptionHandlingMiddleware`, `MethodOverrideMiddleware`, IIS hosting.<br>• **Web application:** `api/client.ts`, `api/services.ts`, `DashboardPage`, `HomePage`, `Layout`, `App.tsx`.<br>• **Native Android:** `ScannerActivity` (ZXing embedded), `VerifyResultActivity`, `ApiClient` (`HttpURLConnection`), `OperatorHomeActivity`, `ProsumerHomeActivity`, `MyBookingsActivity`.<br>• **Business rules:** R9 (QR validity window ±2h, single-use token lifecycle). |

### 13.1 Declared use of AI tools

This assessment is set at **AI Assessment Scale Level 2 — AI Planning**. Each member must
state which tools were used, at which stage, and for what, and add a short reflection on
how the initial ideas became the final solution.

#### Chathushka — Identity, Access Control & Local Session
- **Tools used:** Claude / ChatGPT / Google AI Studio (planning stage only).
- **Stage(s) used:** Architecture & Planning Stage (initial schema brainstorming and security design).
- **What it was used for:** Exploring PBKDF2 iteration recommendations (100,000 rounds) vs Argon2 for .NET 8 compatibility, designing the uniform response envelope for auth failures without leaking account enumeration vectors, and evaluating SQLite schema options on Android for single-row session caching.
- **Reflection — how did the initial idea become the final solution?** The initial design used SharedPreferences for token storage, but after reviewing the coursework marking guide requiring SQLite session persistence, I designed the dedicated `session` table in `DatabaseHelper` with fixed primary key 1 and `CONFLICT_REPLACE` upsert semantics. For the API, the plan originally had separate endpoints for prosumer and staff logins, but consolidating into a single identifier regex lookup supporting NIC, username, and email significantly streamlined both clients while strictly enforcing rules R5 (deactivated) and R6 (pending approval) in the FAT service layer.

#### Nisal — Microgrid Nodes, Slots & Geolocation
- **Tools used:** _[To be completed by member]_
- **Stage(s) used:** Planning stage.
- **What it was used for:** Researching the Haversine formula and comparing Google Maps Android SDK configuration requirements with Leaflet/OpenStreetMap on the web.
- **Reflection:** _[To be completed by member]_

#### Akash — Reservation Workflow & Business Rules
- **Tools used:** _[To be completed by member]_
- **Stage(s) used:** Planning stage.
- **What it was used for:** Formulating the state transition model for reservation lifecycles (Pending -> Approved -> Completed/Cancelled/Rejected).
- **Reflection:** _[To be completed by member]_

#### Sandun — QR Verification, Dashboards & Platform
- **Tools used:** _[To be completed by member]_
- **Stage(s) used:** Planning stage.
- **What it was used for:** Investigating cross-platform QR generation without `System.Drawing.Common` dependencies and IIS ASP.NET Core Module hosting parameters.
- **Reflection:** _[To be completed by member]_

---

## 14. Challenges

**Keeping business logic out of the clients.** The FAT-service constraint is easy to
state and easy to violate accidentally — the natural instinct when writing a Cancel
button is to check the time locally and hide it. The approach that made the rule hold was
to stop sending the clients the *inputs* to a rule and send them the *answer* instead:
`canModify` and `canCancel` are computed server-side per record, and the clients only
enable or disable a control. Slot availability was solved the same way with `isBookable`.

**Rule ordering changes the user experience.** Both R8 and R13 correctly rejected a
duplicate booking, but in the wrong order the user was told "this slot is full" when the
truth was "you already booked this slot". Getting a rule *right* is not the same as
getting it *helpful*.

**Pipeline ordering in ASP.NET Core.** The verb-override middleware was correct but
never ran in time, because `WebApplication` silently inserts `UseRouting()` at the front
of the pipeline. Only an explicit `app.UseRouting()` moves it. This cost real debugging
time and was only caught because the test suite asserted on the override path.

**Cross-platform QR rendering.** The obvious QRCoder renderer depends on
`System.Drawing.Common`, which is Windows-only on .NET 8. `PngByteQRCode` produces PNG
bytes with no platform graphics dependency, and rendering server-side means the phone
needs no QR *encoding* library at all — only a scanner.

**Unique indexes over optional fields.** A plain unique index on `nic` rejects a second
staff account, because two documents with `nic: null` collide. Marking the index **sparse**
scopes uniqueness to documents that actually have the field, which is precisely the
requirement.

**Denormalisation and consistency.** Copying `stationName` onto reservations removes an
N+1 query on every list screen, but creates a consistency obligation: renaming a node has
to propagate. `StationService.UpdateAsync` does that explicitly with `UpdateManyAsync`.

**Time zones.** Storing wall-clock strings alone made every rule ambiguous. Storing both
a display string and an absolute `startUtc`, and having every rule compare only on the
absolute value, removed a whole class of bugs. Reading "now" through an injected `IClock`
rather than `DateTime.UtcNow` made the rules testable.

**QR replay.** A screenshotted QR code is trivially reusable. Two mitigations: the token
is cleared when a transfer completes, and it is re-issued if an approved booking is moved
to a different slot.

---

## 15. References

1. Microsoft. *ASP.NET Core Web API documentation.* <https://learn.microsoft.com/aspnet/core/web-api/>
2. Microsoft. *Host ASP.NET Core on Windows with IIS.* <https://learn.microsoft.com/aspnet/core/host-and-deploy/iis/>
3. Microsoft. *Hash passwords in ASP.NET Core.* <https://learn.microsoft.com/aspnet/core/security/data-protection/consumer-apis/password-hashing>
4. Microsoft. *Middleware in ASP.NET Core.* <https://learn.microsoft.com/aspnet/core/fundamentals/middleware/>
5. MongoDB. *MongoDB C#/.NET Driver — Fundamentals.* <https://www.mongodb.com/docs/drivers/csharp/current/fundamentals/>
6. MongoDB. *Indexes.* <https://www.mongodb.com/docs/manual/indexes/>
7. MongoDB. *Update Operators — `$inc`.* <https://www.mongodb.com/docs/manual/reference/operator/update/inc/>
8. Android Developers. *Save data using SQLite.* <https://developer.android.com/training/data-storage/sqlite>
9. Android Developers. *Connect to the network.* <https://developer.android.com/develop/connectivity/network-ops/connecting>
10. Android Developers. *Request runtime permissions.* <https://developer.android.com/training/permissions/requesting>
11. Google. *Maps SDK for Android.* <https://developers.google.com/maps/documentation/android-sdk/overview>
12. Google. *Fused Location Provider API.* <https://developers.google.com/location-context/fused-location-provider>
13. JourneyApps. *ZXing Android Embedded.* <https://github.com/journeyapps/zxing-android-embedded>
14. Codebude. *QRCoder.* <https://github.com/codebude/QRCoder>
15. React. *React 18 documentation.* <https://react.dev/>
16. Tailwind Labs. *Tailwind CSS documentation.* <https://tailwindcss.com/docs>
17. Remix Run. *React Router v6.* <https://reactrouter.com/>
18. Recharts. *Recharts documentation.* <https://recharts.org/>
19. Vladimir Agafonkin. *Leaflet.* <https://leafletjs.com/>
20. OpenStreetMap Foundation. *Tile usage policy.* <https://operations.osmfoundation.org/policies/tiles/>
21. Veness, C. *Calculate distance and bearing between two Latitude/Longitude points (Haversine).* <https://www.movable-type.co.uk/scripts/latlong.html>
22. IETF. *RFC 7519 — JSON Web Token (JWT).* <https://datatracker.ietf.org/doc/html/rfc7519>
23. OWASP. *Password Storage Cheat Sheet.* <https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html>

---

*Submitted for SE4040 Enterprise Application Development, 2026.*
