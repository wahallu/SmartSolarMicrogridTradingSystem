# Smart Solar Microgrid Trading System — Master Contract (v1.0)

> **This file is the single source of truth.** The C# Web API, the React web
> application and the native Android application are all implemented against it.
> No client may invent a field, a route, a status string or a rule that is not
> written here.

---

## 1. Solution layout

```
Smart Solar Microgrid Trading System/
├── backend/                     ASP.NET Core 8 Web API  (C#)  — the FAT service
│   └── SmartSolar.Api/
├── web/                         React 18 + Vite + TypeScript + Tailwind CSS 3
├── mobile/                      Pure native Android (Java) + SQLite
├── docs/                        Report, diagrams, contract, study guide
└── README.md
```

## 2. Architectural rules (marking-scheme critical)

| Rule | Statement |
|------|-----------|
| A1 | **FAT service.** 100 % of business logic (validation, 7-day rule, 12-hour rule, deactivation guards, QR issue/verify, counts) lives in `backend/SmartSolar.Api/Services/*`. |
| A2 | Controllers are thin: authorise → bind DTO → call service → map `ServiceResult` to HTTP. No `if` on domain data in a controller. |
| A3 | Clients (web + Android) are **UI only**. They never open a database connection to MongoDB and never re-implement a rule. They may *display* a rule's message returned by the API. |
| A4 | Android keeps **local persistence in SQLite only** (session, cached profile, cached stations, cached reservations, offline outbox). SQLite is a cache/session store, never the authority. |
| A5 | Every request/response is JSON over HTTP. Auth is `Authorization: Bearer <JWT>`. |
| A6 | The API is hosted on **IIS** (`web.config` + ANCM v2, out-of-process is fine, in-process preferred). |
| A7 | Every `.cs` file starts with a **comment header block**; every method starts with an **inline comment**. Same convention applied to `.java`, `.ts/.tsx`. |

## 3. Roles

| Role literal | Who | Clients |
|---|---|---|
| `Backoffice` | System administrator | Web |
| `GridOperator` | Microgrid site operator | Web **and** Mobile |
| `Prosumer` | Property owner with solar panels | Mobile (web: read-only profile) |

## 4. MongoDB — database `SmartSolarMicrogridDb`, exactly **4 collections**

### 4.1 `Users` — *"User's detail"*

| Field | BSON type | Notes |
|---|---|---|
| `_id` | ObjectId → string | Mongo id |
| `nic` | string \| null | **Primary business key for Prosumers.** Unique sparse index. `null` for web users. |
| `username` | string | Unique. For Prosumers the system stores the NIC here too. |
| `email` | string | Unique sparse |
| `passwordHash` | string | PBKDF2-SHA256, 100 000 iterations, base64 |
| `passwordSalt` | string | 16 random bytes, base64 |
| `fullName` | string | |
| `phone` | string | |
| `address` | string | |
| `role` | string | `Backoffice` \| `GridOperator` \| `Prosumer` |
| `status` | string | `Pending` \| `Active` \| `Deactivated` |
| `deactivationRequested` | bool | Set by the Prosumer from mobile |
| `deactivationRequestedAt` | DateTime? | UTC |
| `deactivationReason` | string \| null | |
| `assignedStationId` | string \| null | Grid operator's home node |
| `assignedStationCode` | string \| null | denormalised for display |
| `lastLoginAt` | DateTime? | UTC |
| `createdAt` / `updatedAt` | DateTime | UTC |
| `createdBy` / `updatedBy` | string \| null | username |

**Indexes:** `nic` unique+sparse, `username` unique, `email` unique+sparse, `role`, `status`.

### 4.2 `SolarStationInfo` — *microgrid nodes / solar grid hubs*

| Field | Type | Notes |
|---|---|---|
| `_id` | string | |
| `stationCode` | string | Unique, `STN-001` |
| `name` | string | |
| `type` | string | `Solar` \| `Hybrid` \| `Battery` |
| `address`, `city` | string | |
| `location` | object | `{ latitude: double, longitude: double }` — **GPS location** |
| `capacityKwh` | double | **Capacity spec kW/h** |
| `totalBatterySlots` | int | **Battery storage slots** |
| `availableBatterySlots` | int | Maintained by Grid Operator |
| `operatingHours` | object | `{ openTime: "06:00", closeTime: "22:00" }` |
| `weeklySchedule` | array | 7 × `{ dayOfWeek: 0..6, isOpen: bool, openTime, closeTime }` — **update schedules** |
| `contactNumber` | string | |
| `status` | string | `Active` \| `Inactive` |
| `createdAt`/`updatedAt`/`createdBy`/`updatedBy` | | |

**Indexes:** `stationCode` unique, `status`.

### 4.3 `EnergyBookingSlots` — *bookable energy windows at a node*

| Field | Type | Notes |
|---|---|---|
| `_id` | string | |
| `stationId` | string | ref `SolarStationInfo._id` |
| `stationCode` | string | denormalised |
| `slotCode` | string | `S1`… unique **within** station+date |
| `slotDate` | DateTime | UTC midnight of the slot's day |
| `startTime` / `endTime` | string | `"08:00"` / `"10:00"` (node local wall-clock) |
| `startUtc` / `endUtc` | DateTime | absolute instants — all rules use these |
| `slotType` | string | `Charging` \| `DropOff` |
| `energyCapacityKwh` | double | |
| `batterySlotNumber` | int | which battery bay |
| `maxBookings` | int | default 1 |
| `bookedCount` | int | maintained by the service |
| `status` | string | `Available` \| `Full` \| `Blocked` |
| `createdAt`/`updatedAt` | | |

**Indexes:** `{stationId, slotDate}`, `{startUtc}`, `{stationId, slotDate, slotCode}` unique.

### 4.4 `EnergyReservation` — *power-trading reservations*

| Field | Type | Notes |
|---|---|---|
| `_id` | string | |
| `reservationNo` | string | Unique, `RSV-20260904-0001` |
| `prosumerNic` | string | ref `Users.nic` |
| `prosumerName`, `prosumerPhone` | string | denormalised |
| `stationId`, `stationCode`, `stationName` | string | |
| `slotId`, `slotCode` | string | ref `EnergyBookingSlots` |
| `reservationStartUtc` / `reservationEndUtc` | DateTime | copied from slot |
| `startTime` / `endTime` | string | display |
| `slotType` | string | `Charging` \| `DropOff` |
| `energyKwh` | double | requested |
| `status` | string | `Pending` \| `Approved` \| `Completed` \| `Cancelled` \| `Rejected` |
| `qrToken` | string \| null | 32-byte URL-safe base64, issued at approval |
| `qrPayload` | string \| null | JSON string encoded into the QR image |
| `qrIssuedAt` | DateTime? | |
| `approvedBy`/`approvedAt` | | |
| `completedBy`/`completedAt` | | |
| `cancelledBy`/`cancelledAt`/`cancelReason` | | |
| `rejectedReason` | | |
| `deliveredKwh` | double? | finalised by operator |
| `operatorNotes` | string \| null | |
| `createdAt`/`updatedAt`/`createdBy`/`updatedBy` | | |

**Indexes:** `reservationNo` unique, `prosumerNic`, `stationId`, `status`, `reservationStartUtc`, `qrToken` unique+sparse.

## 5. Business rules — implemented **only** in the API service layer

| # | Rule | Error code | HTTP |
|---|---|---|---|
| **R1** | A reservation's start must be **in the future** and **within 7 days** of *now*. | `RES_WINDOW_7_DAYS` | 400 |
| **R2** | A reservation may be **updated** only while `status ∈ {Pending, Approved}` **and** `reservationStartUtc − now ≥ 12 h`. | `RES_UPDATE_12H` | 400 |
| **R3** | A reservation may be **cancelled** only while `status ∈ {Pending, Approved}` **and** `reservationStartUtc − now ≥ 12 h`. | `RES_CANCEL_12H` | 400 |
| **R4** | A station **cannot be deactivated** while it holds reservations with `status ∈ {Pending, Approved}` and `reservationStartUtc ≥ now`. | `STATION_HAS_ACTIVE_RES` | 409 |
| **R5** | A `Deactivated` user cannot log in. **Only a Backoffice user may reactivate.** | `ACCOUNT_DEACTIVATED` | 403 |
| **R6** | Self-registered Prosumers are created `Pending`; they cannot log in until a Backoffice user activates them. | `ACCOUNT_PENDING` | 403 |
| **R7** | NIC is unique across `Users`; duplicate registration is rejected. | `NIC_ALREADY_EXISTS` | 409 |
| **R8** | A slot can be booked only while `bookedCount < maxBookings`, `status = Available`, and its station is `Active`. | `SLOT_UNAVAILABLE` | 409 |
| **R9** | QR verification requires: token exists, `status = Approved`, reservation not expired (start − 2 h ≤ now ≤ end + 2 h). | `QR_INVALID` / `QR_NOT_APPROVED` / `QR_OUT_OF_WINDOW` | 400 |
| **R10** | Only `Backoffice` may create/modify web users and activate/deactivate accounts. | `FORBIDDEN` | 403 |
| **R11** | A Prosumer may read/modify **only their own** profile and reservations. | `FORBIDDEN` | 403 |
| **R12** | Booking a slot for a station whose `weeklySchedule` marks that day closed is rejected. | `STATION_CLOSED` | 400 |
| **R13** | A Prosumer may not hold two active (`Pending`/`Approved`) reservations on the **same slot**. | `DUPLICATE_RESERVATION` | 409 |

`now` is always **UTC**, taken from `IClock` so it is testable.

## 6. HTTP envelope

Every response — success or failure — uses this envelope:

```jsonc
{
  "success": true,
  "message": "Reservation created successfully.",
  "data":  { /* payload, or null */ },
  "errorCode": null,          // string when success === false
  "errors": null,             // { "field": ["msg", ...] } for validation
  "timestamp": "2026-09-04T10:15:30.000Z"
}
```

Paged payloads:

```jsonc
{ "items": [...], "page": 1, "pageSize": 20, "totalCount": 137, "totalPages": 7 }
```

Status codes: `200` OK, `201` Created, `400` validation/rule, `401` unauthenticated,
`403` forbidden/inactive, `404` not found, `409` conflict, `500` unexpected.

## 7. Endpoints — base path `/api/v1`

### 7.1 Auth `/auth`

| Method | Route | Auth | Body / Query | Returns |
|---|---|---|---|---|
| POST | `/auth/login` | anon | `{ identifier, password }` — identifier = NIC \| username \| email | `AuthResponse` |
| POST | `/auth/register` | anon | `RegisterProsumerRequest` | `UserDto` (status `Pending`) |
| GET | `/auth/me` | any | — | `UserDto` |
| POST | `/auth/change-password` | any | `{ currentPassword, newPassword }` | `null` |

`AuthResponse` = `{ token, expiresAtUtc, user: UserDto }`.
JWT claims: `sub` (id), `unique_name` (username), `role`, `nic`, `stationId`, `fullName`.
Lifetime **8 hours**.

`RegisterProsumerRequest` = `{ nic, fullName, email, phone, address, password }`.

### 7.2 Users `/users` — **Backoffice only**

| Method | Route | Body / Query |
|---|---|---|
| GET | `/users` | `?role=&status=&search=&page=1&pageSize=20` |
| GET | `/users/{id}` | |
| POST | `/users` | `{ username, email, fullName, phone, address, password, role, assignedStationId? }` |
| PUT | `/users/{id}` | `{ email, fullName, phone, address, role, assignedStationId?, status? }` |
| PATCH | `/users/{id}/activate` | |
| PATCH | `/users/{id}/deactivate` | `{ reason? }` |
| POST | `/users/{id}/reset-password` | `{ newPassword }` |
| DELETE | `/users/{id}` | hard delete, Backoffice only, blocked for self |

### 7.3 Prosumers `/prosumers`

| Method | Route | Auth | Notes |
|---|---|---|---|
| GET | `/prosumers` | Backoffice, GridOperator | `?status=&search=&page=&pageSize=` |
| GET | `/prosumers/pending` | Backoffice | **pending-activation view** |
| GET | `/prosumers/{nic}` | Backoffice, GridOperator, self | |
| POST | `/prosumers` | Backoffice | `{ nic, fullName, email, phone, address, password, status? }` |
| PUT | `/prosumers/{nic}` | Backoffice, self | `{ fullName, email, phone, address }` |
| PATCH | `/prosumers/{nic}/activate` | **Backoffice** | R5 |
| PATCH | `/prosumers/{nic}/deactivate` | Backoffice | `{ reason? }` |
| POST | `/prosumers/{nic}/request-deactivation` | self (Prosumer) | `{ reason? }` → sets `deactivationRequested` |
| DELETE | `/prosumers/{nic}` | Backoffice | |

### 7.4 Stations `/stations`

| Method | Route | Auth | Notes |
|---|---|---|---|
| GET | `/stations` | any auth | `?status=&search=&city=&page=&pageSize=` |
| GET | `/stations/{id}` | any auth | |
| GET | `/stations/nearby` | any auth | `?lat=&lng=&radiusKm=25&limit=50` → `NearbyStationDto[]` with `distanceKm`, sorted ascending — **map feature** |
| POST | `/stations` | Backoffice | `CreateStationRequest` |
| PUT | `/stations/{id}` | Backoffice | `UpdateStationRequest` |
| PATCH | `/stations/{id}/schedule` | Backoffice, GridOperator | `{ operatingHours, weeklySchedule[] }` |
| PATCH | `/stations/{id}/availability` | Backoffice, GridOperator | `{ availableBatterySlots }` |
| PATCH | `/stations/{id}/activate` | Backoffice | |
| PATCH | `/stations/{id}/deactivate` | Backoffice | **R4** |
| DELETE | `/stations/{id}` | Backoffice | blocked if any reservation exists |

`CreateStationRequest` = `{ stationCode, name, type, address, city, latitude, longitude, capacityKwh, totalBatterySlots, availableBatterySlots, contactNumber, operatingHours{openTime,closeTime}, weeklySchedule[] }`

### 7.5 Slots `/slots`

| Method | Route | Auth | Notes |
|---|---|---|---|
| GET | `/slots` | any auth | `?stationId=&date=YYYY-MM-DD&status=&slotType=` |
| GET | `/slots/available` | any auth | `?stationId=&date=` — only bookable ones |
| GET | `/slots/{id}` | any auth | |
| POST | `/slots` | Backoffice, GridOperator | `CreateSlotRequest` |
| POST | `/slots/generate` | Backoffice, GridOperator | `{ stationId, fromDate, toDate, startHour, endHour, slotDurationHours, slotType, energyCapacityKwh, batterySlots }` |
| PUT | `/slots/{id}` | Backoffice, GridOperator | |
| PATCH | `/slots/{id}/block` | Backoffice, GridOperator | |
| DELETE | `/slots/{id}` | Backoffice, GridOperator | blocked if `bookedCount > 0` |

### 7.6 Reservations `/reservations`

| Method | Route | Auth | Notes |
|---|---|---|---|
| GET | `/reservations` | Backoffice, GridOperator | `?nic=&stationId=&status=&fromDate=&toDate=&search=&page=&pageSize=` — **filter criteria** |
| GET | `/reservations/{id}` | owner \| staff | |
| GET | `/reservations/my` | Prosumer | `?scope=current\|pending\|history\|all` |
| GET | `/reservations/history` | any auth | `?nic=` (staff may pass any NIC; Prosumer forced to own) |
| GET | `/reservations/dashboard` | any auth | `?stationId=&nic=` → `DashboardDto` |
| POST | `/reservations` | Prosumer, Backoffice, GridOperator | `CreateReservationRequest` → 201 + `ReservationDto` |
| PUT | `/reservations/{id}` | owner \| staff | `UpdateReservationRequest` — **R2** |
| PATCH | `/reservations/{id}/cancel` | owner \| staff | `{ reason? }` — **R3** |
| PATCH | `/reservations/{id}/approve` | Backoffice, GridOperator | issues `qrToken` |
| PATCH | `/reservations/{id}/reject` | Backoffice, GridOperator | `{ reason }` |
| GET | `/reservations/{id}/qr` | owner \| staff | `{ reservationNo, qrToken, qrPayload, qrImageBase64 }` (PNG, no data-URI prefix) |
| POST | `/reservations/verify-qr` | GridOperator, Backoffice | `{ qrToken }` → `ReservationDto` — **R9** |
| POST | `/reservations/complete` | GridOperator, Backoffice | `{ qrToken, deliveredKwh, notes? }` → `ReservationDto` (status `Completed`) |

`CreateReservationRequest` = `{ prosumerNic?, stationId, slotId, energyKwh, notes? }`
(`prosumerNic` ignored/forced to self when the caller is a Prosumer.)

`UpdateReservationRequest` = `{ slotId?, energyKwh? }`

`DashboardDto`:
```jsonc
{
  "pendingReservations": 4,          // status = Pending
  "approvedFutureReservations": 9,   // status = Approved AND start > now
  "todayReservations": 3,
  "completedReservations": 27,
  "cancelledReservations": 2,
  "totalReservations": 45,
  "activeStations": 6,
  "totalStations": 7,
  "pendingProsumerActivations": 2,   // staff only, else 0
  "totalProsumers": 31,              // staff only, else 0
  "totalEnergyKwh": 812.5,
  "upcoming": [ ReservationDto x5 ]
}
```

### 7.7 Health `/health` — anon → `{ status, database, utcNow, version }`

## 8. Shared DTO shapes (JSON, camelCase)

```jsonc
// UserDto
{ "id","nic","username","email","fullName","phone","address","role","status",
  "deactivationRequested","deactivationRequestedAt","deactivationReason",
  "assignedStationId","assignedStationCode","lastLoginAt","createdAt","updatedAt" }

// StationDto
{ "id","stationCode","name","type","address","city",
  "latitude","longitude","capacityKwh","totalBatterySlots","availableBatterySlots",
  "operatingHours": {"openTime","closeTime"},
  "weeklySchedule": [ {"dayOfWeek","dayName","isOpen","openTime","closeTime"} ],
  "contactNumber","status","activeReservationCount","createdAt","updatedAt" }

// NearbyStationDto = StationDto + { "distanceKm": 3.42 }

// SlotDto
{ "id","stationId","stationCode","stationName","slotCode","slotDate",
  "startTime","endTime","startUtc","endUtc","slotType","energyCapacityKwh",
  "batterySlotNumber","maxBookings","bookedCount","remainingCapacity","status" }

// ReservationDto
{ "id","reservationNo","prosumerNic","prosumerName","prosumerPhone",
  "stationId","stationCode","stationName","slotId","slotCode",
  "reservationStartUtc","reservationEndUtc","startTime","endTime","slotType",
  "energyKwh","deliveredKwh","status","qrToken","qrIssuedAt",
  "approvedBy","approvedAt","completedBy","completedAt",
  "cancelledBy","cancelledAt","cancelReason","rejectedReason","operatorNotes",
  "canModify","canCancel","hoursUntilStart","createdAt","updatedAt" }
```

`canModify` / `canCancel` are **computed by the API** (R2/R3) so clients never
re-implement the 12-hour rule — they only enable/disable buttons.

## 9. QR payload

`qrPayload` is the exact string encoded in the QR image:

```json
{"v":1,"t":"<qrToken>","r":"RSV-20260904-0001","n":"200112301234","s":"STN-001","e":"2026-09-05T08:00:00Z"}
```

Scanner reads the string → parses JSON → sends `t` to `POST /reservations/verify-qr`.
It must **not** trust any other field in the payload; the server is the authority.

## 10. Seed data (created on first run by `DataSeeder`)

| Login | Password | Role |
|---|---|---|
| `admin` | `Admin@123` | Backoffice |
| `backoffice2` | `Back@123` | Backoffice |
| `operator1` | `Operator@123` | GridOperator (STN-001) |
| `operator2` | `Operator@123` | GridOperator (STN-002) |
| `200112301234` | `Prosumer@123` | Prosumer (Active) |
| `199845600789` | `Prosumer@123` | Prosumer (Active) |
| `200234501122` | `Prosumer@123` | Prosumer (**Pending** — for the pending-activation demo) |
| `199756403311` | `Prosumer@123` | Prosumer (**Deactivated** — for the reactivation demo) |

Plus 6 stations around Colombo/Malabe (real coordinates), 14 days of slots per
station, and ~25 reservations spread across every status.

## 11. Configuration keys

```jsonc
// backend/SmartSolar.Api/appsettings.json
{
  "MongoDbSettings": {
    "ConnectionString": "mongodb://localhost:27017",
    "DatabaseName": "SmartSolarMicrogridDb",
    "UsersCollection": "Users",
    "StationsCollection": "SolarStationInfo",
    "SlotsCollection": "EnergyBookingSlots",
    "ReservationsCollection": "EnergyReservation"
  },
  "JwtSettings": {
    "Secret": "<64+ chars>",
    "Issuer": "SmartSolarMicrogridApi",
    "Audience": "SmartSolarMicrogridClients",
    "ExpiryHours": 8
  },
  "BusinessRules": {
    "ReservationWindowDays": 7,
    "ModificationNoticeHours": 12,
    "QrGraceHoursBefore": 2,
    "QrGraceHoursAfter": 2,
    "DefaultNearbyRadiusKm": 25
  },
  "SeedData": { "Enabled": true }
}
```

Web: `web/.env` → `VITE_API_BASE_URL=http://localhost:5218/api/v1`
Android: `mobile/app/src/main/res/values/config.xml` → `api_base_url`, and
`local.properties` → `MAPS_API_KEY`.

## 12. Naming/commenting conventions

* `.cs` — file header block: `/* ===== FILE / AUTHOR / DESCRIPTION ===== */`, then
  an inline `//` comment as the first line inside **every** method.
* `.java` — Javadoc header block per file; `//` comment first line of every method.
* `.tsx/.ts` — banner comment per file; `//` comment first line of every exported function.
* Android: Activities `XxxActivity`, layouts `activity_xxx.xml`, adapters `XxxAdapter`.
