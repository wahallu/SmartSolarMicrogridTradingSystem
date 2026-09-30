# Smart Solar Microgrid Trading System

**SE4040 – Enterprise Application Development · Year 4 Semester 2 · Assignment 1**
BSc (Hons) in Information Technology, specialising in Software Engineering

An end-to-end client–server system for trading rooftop solar energy with a network of
microgrid nodes. A **C# Web API** holds all business logic and data, a **React web
application** serves back-office administrators and grid operators, and a **pure native
Android application** serves solar prosumers and operators in the field.

---

## Table of contents

1. [Architecture](#architecture)
2. [Repository layout](#repository-layout)
3. [Prerequisites](#prerequisites)
4. [Running the system](#running-the-system)
5. [Demo accounts](#demo-accounts)
6. [Business rules](#business-rules)
7. [Deploying the API to IIS](#deploying-the-api-to-iis)
8. [Video demonstration](#video-demonstration)
9. [Individual contributions](#individual-contributions)
10. [References](#references)

---

## Architecture

```
        ┌──────────────────────────┐        ┌──────────────────────────┐
        │   React 18 + Tailwind    │        │  Pure native Android     │
        │   Web application        │        │  (Java + SQLite)         │
        │                          │        │                          │
        │  Backoffice officers     │        │  Solar prosumers         │
        │  Grid operators          │        │  Grid operators          │
        └────────────┬─────────────┘        └────────────┬─────────────┘
                     │                                   │
                     │      HTTPS / REST + JWT bearer    │
                     └─────────────────┬─────────────────┘
                                       ▼
                     ┌──────────────────────────────────────┐
                     │   ASP.NET Core 8 Web API  (C#)       │
                     │   ── the FAT service ──              │
                     │                                      │
                     │   Controllers  (thin, HTTP mapping)  │
                     │        ↓                             │
                     │   Services     (ALL business logic)  │
                     │        ↓                             │
                     │   MongoDbContext                     │
                     │                                      │
                     │   Hosted on Windows IIS              │
                     └──────────────────┬───────────────────┘
                                        ▼
                     ┌──────────────────────────────────────┐
                     │   MongoDB  ·  SmartSolarMicrogridDb  │
                     │                                      │
                     │   Users · SolarStationInfo           │
                     │   EnergyBookingSlots                 │
                     │   EnergyReservation                  │
                     └──────────────────────────────────────┘
```

### The FAT service pattern

Every validation, every rule and every calculation lives in
`backend/SmartSolar.Api/Services/`. The two clients are **user interfaces only**:

* Neither client opens a database connection. All data moves over REST.
* Neither client evaluates the 7-day booking window or the 12-hour notice period.
  The API computes `canModify` / `canCancel` / `isBookable` per record and the
  clients merely enable or disable a button from those booleans.
* Distance for "nearby nodes" is calculated on the **server** (Haversine in
  `StationService`), not in the browser or on the phone.
* QR codes are generated **and** verified server-side; the phone only displays and
  scans them.

The Android client keeps a local **SQLite** database, but strictly as a session store
and an offline cache — MongoDB behind the API remains the single source of truth.

---

## Repository layout

```
Smart Solar Microgrid Trading System/
├── backend/
│   ├── SmartSolar.sln
│   └── SmartSolar.Api/
│       ├── Controllers/       Thin HTTP controllers
│       ├── Services/          ALL business logic (the FAT service)
│       ├── Models/            The four MongoDB documents
│       ├── DTOs/              Request and response contracts
│       ├── Data/              MongoDbContext + DataSeeder
│       ├── Security/          PBKDF2 hashing, JWT issue, CurrentUser
│       ├── Middleware/        Exception handling, HTTP verb override
│       └── web.config         IIS hosting configuration
├── web/                       React 18 + Vite + TypeScript + Tailwind CSS 3
│   └── src/
│       ├── api/               axios client + one function per endpoint
│       ├── components/        Shared UI primitives, layout shell
│       ├── context/           AuthContext (session)
│       ├── pages/             One file per screen
│       └── types/             TypeScript mirrors of the API DTOs
├── mobile/                    Pure native Android (Java, no frameworks)
│   └── app/src/main/
│       ├── java/lk/sliit/smartsolar/
│       │   ├── activities/    One Activity per screen
│       │   ├── adapters/      RecyclerView adapters
│       │   ├── api/           HttpURLConnection client
│       │   ├── db/            SQLiteOpenHelper, session, local cache
│       │   ├── models/        JSON → typed model classes
│       │   └── util/          Constants, dates, validation
│       └── res/               Layouts, drawables, themes, strings
├── docs/
│   ├── API_CONTRACT.md        The single source of truth for all three tiers
│   ├── REPORT.md              The submitted project report
│   └── diagrams/              High-level, use case and DFD diagrams
└── README.md
```

---

## Prerequisites

| Component | Version | Notes |
|---|---|---|
| .NET SDK | **8.0** | `dotnet --version` |
| MongoDB Server | **6.0+** | Must be running on `localhost:27017` |
| Node.js | **18+** | For the web client |
| Android Studio | **Ladybug+** | Or the command-line SDK, API level 35 |
| JDK | **17** | Required by Android Gradle Plugin 8.7 |

---

## Running the system

### 1 — MongoDB

Start the service (it installs as a Windows service and usually auto-starts):

```bash
net start MongoDB
```

No manual schema setup is needed. On first run the API creates all indexes and
seeds six microgrid nodes, ten accounts, two weeks of booking slots and about
twenty-five reservations spread across every status.

### 2 — The Web API

```bash
cd backend
dotnet restore
dotnet run --project SmartSolar.Api --urls "http://localhost:5218"
```

Confirm it is healthy:

```bash
curl http://localhost:5218/api/v1/health
```

Interactive API documentation: <http://localhost:5218/swagger>

### 3 — The web application

```bash
cd web
npm install
npm run dev
```

Open <http://localhost:5173>. The API base URL lives in `web/.env`:

```
VITE_API_BASE_URL=http://localhost:5218/api/v1
```

### 4 — The Android application

Create `mobile/local.properties` (it is deliberately not committed):

```properties
sdk.dir=C\:\\Users\\<you>\\AppData\\Local\\Android\\Sdk
MAPS_API_KEY=<your Google Maps Android API key>
API_BASE_URL_DEBUG=http\://10.0.2.2\:5218/api/v1/
```

> `10.0.2.2` is the Android emulator's alias for the host machine's `localhost`.
> On a physical phone use the laptop's LAN address instead, e.g. `http://192.168.1.5:5218/api/v1/`,
> and make sure both devices are on the same network.

Then build and install:

```bash
cd mobile
./gradlew installDebug
```

A Google Maps API key is required for the map screen. Create one in the
[Google Cloud Console](https://console.cloud.google.com/) with the **Maps SDK for
Android** enabled. Every other screen works without a key.

---

## Demo accounts

The seeder creates these on first run. They exist so the system can be demonstrated
immediately.

| Login | Password | Role | Purpose |
|---|---|---|---|
| `admin` | `Admin@123` | Backoffice | Full administration |
| `backoffice2` | `Back@123` | Backoffice | Second officer |
| `operator1` | `Operator@123` | Grid Operator | Assigned to STN-001 |
| `operator2` | `Operator@123` | Grid Operator | Assigned to STN-002 |
| `200112301234` | `Prosumer@123` | Prosumer | Active, has bookings |
| `199845600789` | `Prosumer@123` | Prosumer | Active, has bookings |
| `200234501122` | `Prosumer@123` | Prosumer | **Pending** — demonstrates rule R6 |
| `199756403311` | `Prosumer@123` | Prosumer | **Deactivated** — demonstrates rule R5 |

Prosumers authenticate via their NIC, which is their primary key. Staff authenticate via a username. The login endpoint accepts any one of the three identifiers: a NIC, a username, or an e-mail address.

---

## Business rules

All thirteen rules are enforced **only** in the API service layer. Each returns a
stable `errorCode` the clients branch on rather than parsing English text.

| # | Rule | Error code |
|---|---|---|
| R1 | A reservation must start in the future and within **7 days** | `RES_WINDOW_7_DAYS` |
| R2 | Updates require at least **12 hours** notice | `RES_UPDATE_12H` |
| R3 | Cancellations require at least **12 hours** notice | `RES_CANCEL_12H` |
| R4 | A node cannot be deactivated while future bookings exist | `STATION_HAS_ACTIVE_RES` |
| R5 | A deactivated account can only be reactivated by a **Backoffice** officer | `ACCOUNT_DEACTIVATED` |
| R6 | Self-registered prosumers start `Pending` and cannot sign in until activated | `ACCOUNT_PENDING` |
| R7 | NIC is unique across all accounts | `NIC_ALREADY_EXISTS` |
| R8 | A slot must be available, have capacity and belong to an active node | `SLOT_UNAVAILABLE` |
| R9 | A scanned QR must exist, be `Approved`, and fall inside its time window | `QR_INVALID` / `QR_NOT_APPROVED` / `QR_OUT_OF_WINDOW` |
| R10 | Only Backoffice may manage web users and activate accounts | `FORBIDDEN` |
| R11 | A prosumer may only read or modify their own data | `FORBIDDEN` |
| R12 | A node closed on that weekday cannot be booked | `STATION_CLOSED` |
| R13 | A prosumer may not hold two active bookings on one slot | `DUPLICATE_RESERVATION` |

Full detail, including every endpoint and DTO, is in
[`docs/API_CONTRACT.md`](docs/API_CONTRACT.md).

---

## Deploying the API to IIS

1. **Install the .NET 8 Hosting Bundle** on the server. Without it IIS returns
   HTTP 500.19 or 500.31.
   <https://dotnet.microsoft.com/download/dotnet/8.0>

2. **Publish** the API:

   ```bash
   cd backend
   dotnet publish SmartSolar.Api -c Release -o C:\inetpub\SmartSolarApi
   ```

3. **Create the site** in IIS Manager:
   * Add a new Application Pool named `SmartSolarPool`
   * Set its .NET CLR version to **No Managed Code** (Kestrel runs the app, not the CLR host)
   * Add a Website pointing at `C:\inetpub\SmartSolarApi`, bound to port `8080`
   * Assign it the `SmartSolarPool` pool

4. **Grant permissions** so the pool identity can read the folder and write logs:

   ```powershell
   icacls "C:\inetpub\SmartSolarApi" /grant "IIS AppPool\SmartSolarPool:(OI)(CI)RX" /T
   ```

5. **Point the API at MongoDB.** Edit
   `C:\inetpub\SmartSolarApi\appsettings.json` and set
   `MongoDbSettings:ConnectionString`. For MongoDB Atlas use the SRV string:
   `mongodb+srv://user:password@cluster.mongodb.net`

6. **Verify** by browsing to `http://localhost:8080/api/v1/health`. It should report
   `"database": "Connected"`.

7. **Repoint the clients**: set `VITE_API_BASE_URL` in `web/.env` and
   `API_BASE_URL_DEBUG` in `mobile/local.properties` to the IIS address, then rebuild
   both clients.

`web.config` in the project root configures the ASP.NET Core Module (in-process
hosting) and is copied automatically by `dotnet publish`.

---

## Video demonstration

> **Video link:** _add the YouTube or OneDrive URL here before submitting_

The demonstration (maximum 5 minutes) should cover:

1. The API running on IIS and the `/health` endpoint responding
2. Backoffice login → creating a microgrid node and generating booking slots
3. Registering a prosumer on the phone → the account appearing in the web
   pending-activation queue → the officer activating it
4. The prosumer reserving an energy slot → an operator approving it → the QR code appearing
5. The operator scanning that QR code and finalising the transfer
6. The 12-hour rule blocking a late cancellation, and rule R4 blocking a node deactivation

---

## Individual contributions

> _Complete this table before submitting. Each member must have a distinct,
> specific list of work — identical entries lose marks._

| IT number | Name | Contribution |
|---|---|---|
| ITxxxxxxxx | **Chathushka** | **Identity, Access Control & Local Session:** C# Web API (AuthService, UserService, ProsumerService, PasswordHasher, JwtTokenService, CurrentUser), Web application (AuthContext, ProtectedRoute, LoginPage, UsersPage, ProsumersPage, PendingActivationsPage, ProfilePage), Native Android client (SplashActivity, LoginActivity, RegisterActivity, ProfileActivity, DatabaseHelper SQLite session, SessionManager, ValidationUtils), and business rules R5, R6, R7, R10, R11 |
| ITxxxxxxxx | **Nisal** | **Microgrid Nodes, Slots & Geolocation:** C# Web API (StationService, SlotService, Haversine nearby), Web (StationsPage, SlotsPage, MapPage), Android (StationsMapActivity, StationDetailActivity, Google Maps SDK), and rules R4, R8, R12 |
| ITxxxxxxxx | **Akash** | **Reservation Workflow & Business Rules:** C# Web API (ReservationService core, ServiceResult, ErrorCodes, IClock), Web (ReservationsPage, ReservationDetailPage), Android (NewBookingActivity, EditBookingActivity, BookingDetailActivity, BookingSummaryActivity), and rules R1, R2, R3, R13 |
| ITxxxxxxxx | **Sandun** | **QR Verification, Dashboards & Platform:** C# Web API (QrCodeGenerator, QR verification/complete, MongoDbContext, DataSeeder, Middleware, IIS hosting), Web (client layer, DashboardPage, HomePage), Android (ScannerActivity, VerifyResultActivity, ApiClient, Dashboards), and rule R9 |

### Declared use of AI tools

The assignment permits AI assistance **only during the planning stage** (AI Assessment
Scale Level 2). State here exactly which tools were used, at which stage, and for what —
then add a short reflection on how the initial ideas became the final solution.

> _Each member: complete honestly. No marks are awarded for the disclosure itself, but
> at the viva you will be asked to explain and justify any part of the submitted work._

---

## Submission checklist

Before zipping, work through this list. Items marked **TODO** need a human.

| Item | Status |
|---|---|
| C# Web API, MongoDB, all business rules | done |
| React web application, all pages | done |
| Native Android application, all screens | done |
| Comment header block on every `.cs` file | done |
| Inline comment at the start of every method | done |
| Screenshots of all web UIs (`docs/screenshots/web`) | done |
| Screenshots of all mobile UIs | **TODO** — capture from an emulator or device |
| Screenshot of the main opening screen | **TODO** — the Android prosumer home screen |
| High-level, use case and DFD diagrams | done (`docs/diagrams/DIAGRAMS.md`) |
| Database design | done (report §3) |
| References | done |
| Source code pasted as text in the report | **TODO** — paste from the repository |
| Git repository link | **TODO** — add to this README and the report |
| Individual contributions per member | **TODO** — must be distinct per member |
| Declared use of AI tools + reflection | **TODO** — required by the assignment |
| Challenges section | done (report §14) |
| 5-minute demonstration video + link | **TODO** |

Then produce the archive. The file name must contain your IT number:

```bash
git archive --format=zip --output=ITxxxxxxxx.zip HEAD
```

> `git archive` excludes everything in `.gitignore`, so `node_modules`, `bin`, `obj`
> and the Gradle build folders are left out automatically. If your marker expects the
> `docs/screenshots` folder inside the archive, confirm it is committed first.

---

## References

1. Microsoft. *ASP.NET Core Web API documentation.*
   <https://learn.microsoft.com/aspnet/core/web-api/>
2. Microsoft. *Host ASP.NET Core on Windows with IIS.*
   <https://learn.microsoft.com/aspnet/core/host-and-deploy/iis/>
3. Microsoft. *Hash passwords in ASP.NET Core (PBKDF2).*
   <https://learn.microsoft.com/aspnet/core/security/data-protection/consumer-apis/password-hashing>
4. MongoDB. *MongoDB C#/.NET Driver — Fundamentals.*
   <https://www.mongodb.com/docs/drivers/csharp/current/fundamentals/>
5. MongoDB. *Indexes.* <https://www.mongodb.com/docs/manual/indexes/>
6. Android Developers. *Save data using SQLite.*
   <https://developer.android.com/training/data-storage/sqlite>
7. Android Developers. *Connect to the network (HttpURLConnection).*
   <https://developer.android.com/develop/connectivity/network-ops/connecting>
8. Google. *Maps SDK for Android.*
   <https://developers.google.com/maps/documentation/android-sdk/overview>
9. JourneyApps. *ZXing Android Embedded.*
   <https://github.com/journeyapps/zxing-android-embedded>
10. Codebude. *QRCoder — a pure C# QR code generator.*
    <https://github.com/codebude/QRCoder>
11. React. *React 18 documentation.* <https://react.dev/>
12. Tailwind Labs. *Tailwind CSS documentation.* <https://tailwindcss.com/docs>
13. Remix Run. *React Router v6.* <https://reactrouter.com/>
14. Vladimir Agafonkin. *Leaflet — an open-source JavaScript library for maps.*
    <https://leafletjs.com/>
15. OpenStreetMap contributors. *OpenStreetMap tile usage policy.*
    <https://operations.osmfoundation.org/policies/tiles/>
16. Veness, C. *Calculate distance and bearing between two Latitude/Longitude points
    (Haversine formula).* <https://www.movable-type.co.uk/scripts/latlong.html>
17. IETF. *RFC 7519 — JSON Web Token (JWT).* <https://datatracker.ietf.org/doc/html/rfc7519>

---

*Submitted for SE4040 Enterprise Application Development, 2026.*
