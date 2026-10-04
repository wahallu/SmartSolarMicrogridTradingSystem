# Smart Solar Microgrid Trading System

**SE4040 - Enterprise Application Development | Assignment 1 | Group 12**

The Smart Solar Microgrid Trading System is a client-server application for managing
solar-energy reservations between prosumers and microgrid nodes. It contains a React
web application for Backoffice officers and Grid Operators, a pure native Android
application for Prosumers and Grid Operators, and an ASP.NET Core Web API connected to
MongoDB.

## Submission information

- **Git repository:** [wahallu/SmartSolarMicrogridTradingSystem](https://github.com/wahallu/SmartSolarMicrogridTradingSystem)
- **Demonstration video:** [EAD Video - YouTube](https://youtu.be/GdkVf_qbJcI)
- **Video duration:** 4 minutes 24 seconds, within the assignment's 5-minute limit
- **Detailed project report:** [EAD Project Report - Group 12](docs/EAD%20Project%20Report%20-%20Group%2012.docx)
- **Assignment deadline:** 30 September 2026 at 11:59 PM

## Team members and individual contributions

### IT22207272 - Fonseka G N V S (Nisal)

**Microgrid Nodes, Booking Slots and Geolocation**

- Implemented microgrid node registration, editing, schedules, activation and
  deactivation.
- Implemented booking-slot creation, bulk generation, editing, blocking and deletion.
- Implemented server-side nearby-station distance calculation.
- Developed the web station, slot and map screens.
- Developed the Android station map and station-detail features using Google Maps.
- Implemented station ownership restrictions for Grid Operators.
- Contributed to system diagrams, report architecture content and repository
  documentation.

### IT22228062 - Jayasinghe I A S A (Akash)

**Reservation Workflow and Business Rules**

- Implemented reservation creation, updating, cancellation, approval and rejection.
- Implemented the seven-day booking window and twelve-hour update/cancellation rules.
- Implemented duplicate-booking prevention and reservation status transitions.
- Developed the web reservation list and reservation-detail screens.
- Developed the Android booking, editing, detail and summary screens.
- Implemented reservation search, filters and server-computed action availability.
- Contributed to the system diagrams and demonstration-video narration.

### IT22049872 - Navod W D C (Chathushka)

**Identity, Access Control and Local Session**

- Implemented login, registration, password hashing and JWT authentication.
- Implemented Backoffice, Grid Operator and Prosumer role authorization.
- Implemented web-user and Prosumer account management.
- Implemented pending activation, deactivation, reactivation and NIC uniqueness rules.
- Developed the web login, users, Prosumers, pending-activation and profile screens.
- Developed the Android splash, login, registration and profile screens.
- Implemented Android SQLite session persistence and validation utilities.
- Responsible for completing the mobile screenshots and opening-screen evidence.

### IT22246332 - Hettiarachchi H A S L (Sandun)

**QR Verification, Dashboards and Platform Integration**

- Implemented secure QR token generation, verification and one-time completion.
- Implemented live dashboard counts and upcoming reservation data.
- Implemented MongoDB configuration, indexes and demonstration-data seeding.
- Implemented the shared web and Android API communication layers.
- Developed the web dashboard, landing page, layout and shared interface components.
- Developed the Android QR scanner, verification, dashboards and booking-list screens.
- Implemented error handling, API health checks and IIS hosting configuration.
- Contributed to web screenshots, database documentation and deployment instructions.

## Main features

### Web application

- Role-based login for Backoffice officers and Grid Operators
- Web-user and Prosumer account management
- Pending Prosumer activation
- Microgrid node, schedule and battery-availability management
- Booking-slot creation and bulk generation
- Reservation review, approval, rejection and cancellation
- Dashboards, station map and profile management

### Android application

- Prosumer registration and role-based login
- SQLite session and offline-cache support
- Profile editing and account-deactivation requests
- Nearby microgrid nodes using Google Maps
- Reservation creation, editing, cancellation, history and search
- Approved reservation QR display
- Grid Operator QR scanning, server verification and transfer completion

### Central Web API

- ASP.NET Core 8 REST API hosted through IIS
- MongoDB database with users, stations, slots and reservations
- JWT authentication and role/ownership authorization
- All business rules enforced in the service layer
- Standard JSON response and error format for both clients

Detailed architecture, database design, API routes, diagrams, business rules, source
extracts, testing evidence, challenges, references and AI-use declarations are included
in the project report.

## Technology stack

| Component | Technology |
|---|---|
| Web application | React 18, TypeScript, Vite, Tailwind CSS 3 |
| Android application | Native Java, XML layouts, SQLite |
| Web API | ASP.NET Core 8, C# |
| Server database | MongoDB |
| Authentication | JWT bearer tokens and PBKDF2 password hashing |
| Maps | Google Maps on Android; Leaflet and OpenStreetMap on web |
| QR | QRCoder generation and ZXing scanning |

## Prerequisites

| Component | Required version |
|---|---|
| .NET SDK | 8.0 |
| MongoDB Server | 6.0 or newer |
| Node.js | 18 or newer |
| Android Studio / Android SDK | API level 35 |
| JDK | 17 |
| Google Maps API key | Maps SDK for Android enabled |

## Running the project

### 1. Start MongoDB

Windows:

```powershell
net start MongoDB
```

macOS with Homebrew:

```bash
brew services start mongodb-community@7.0
```

The default MongoDB address is `mongodb://localhost:27017`.

### 2. Start the Web API

```bash
cd backend
dotnet restore
dotnet run --project SmartSolar.Api --urls "http://localhost:5218"
```

Verify the API at <http://localhost:5218/api/v1/health> and open Swagger at
<http://localhost:5218/swagger>.

### 3. Start the web application

```bash
cd web
npm install
npm run dev
```

Open <http://localhost:5173>. The web client expects:

```dotenv
VITE_API_BASE_URL=http://localhost:5218/api/v1
```

### 4. Configure and run Android

Create `mobile/local.properties`:

```properties
sdk.dir=C\:\\Users\\<you>\\AppData\\Local\\Android\\Sdk
MAPS_API_KEY=<your Google Maps Android API key>
API_BASE_URL_DEBUG=http\://10.0.2.2\:5218/api/v1/
```

`10.0.2.2` is the Android emulator's alias for the host machine. For a physical
device, use the computer's LAN address or run:

```bash
adb reverse tcp:5218 tcp:5218
```

Build and install:

```bash
cd mobile
bash gradlew installDebug
```

## Demo accounts

| Login | Password | Role | Purpose |
|---|---|---|---|
| `admin` | `Admin@123` | Backoffice | Full administration |
| `backoffice2` | `Back@123` | Backoffice | Second officer |
| `operator1` | `Operator@123` | Grid Operator | Assigned to STN-001 |
| `operator2` | `Operator@123` | Grid Operator | Assigned to STN-002 |
| `200112301234` | `Prosumer@123` | Prosumer | Active account with bookings |
| `199845600789` | `Prosumer@123` | Prosumer | Active account with bookings |
| `200234501122` | `Prosumer@123` | Prosumer | Pending activation example |
| `199756403311` | `Prosumer@123` | Prosumer | Deactivated-account example |

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

**Video:** [EAD Video - YouTube](https://youtu.be/GdkVf_qbJcI)

**Duration:** 4 minutes 24 seconds

This is the application demonstration required by the assignment brief.

## Before submission

- Confirm that the final report is committed and opens correctly.
- Add all mobile UI screenshots and the required main opening-screen screenshot.
- Confirm the Android map uses a valid Google Maps API key.
- Complete every member's AI-use declaration and reflection in the report.
- Run a fresh API, web and Android build on the submission machine.
- Confirm lecturers can access the GitHub repository.
- Confirm the video link opens without requesting permission.
- Commit all required screenshots and report files before creating the ZIP because
  `git archive` includes tracked files only.

Example archive command:

```bash
git status --short
git archive --format=zip --output=IT22207272.zip HEAD
```

---

*Prepared for SE4040 Enterprise Application Development, 2026.*
