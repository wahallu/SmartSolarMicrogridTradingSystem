# System Diagrams
### Smart Solar Microgrid Trading System · SE4040

These diagrams are written in Mermaid so they stay in version control alongside the
code. GitHub renders them directly; to export images for the report, paste a block
into <https://mermaid.live> and download the PNG or SVG.

---

## 1. High-level architecture diagram

Shows the three tiers, what each is responsible for, and the single direction in which
data flows. Note that **neither client touches MongoDB** — that is the FAT-service
constraint the assignment sets.

```mermaid
graph TB
    subgraph CLIENTS["PRESENTATION TIER — user interfaces only"]
        WEB["React 18 + TypeScript<br/>Tailwind CSS 3<br/><br/>Backoffice officers<br/>Grid operators"]
        AND["Pure native Android<br/>Java + SQLite<br/><br/>Solar prosumers<br/>Grid operators"]
        SQL[("SQLite<br/>session + offline cache<br/>NOT the source of truth")]
        AND -.->|"local persistence"| SQL
    end

    subgraph API["APPLICATION TIER — ASP.NET Core 8 Web API on IIS"]
        CTRL["Controllers<br/><i>thin: bind DTO, call service,<br/>map ServiceResult to HTTP</i>"]
        SVC["Service Layer — THE FAT SERVICE<br/><b>100% of business logic</b><br/>AuthService · UserService · ProsumerService<br/>StationService · SlotService · ReservationService<br/><br/>Rules R1–R13 · Haversine distance · QR issue &amp; verify"]
        SEC["Security<br/>JWT bearer · PBKDF2 hashing<br/>Role authorisation"]
        CTX["MongoDbContext<br/>collections + indexes"]
        CTRL --> SVC
        SEC -.->|"authenticates<br/>every request"| CTRL
        SVC --> CTX
    end

    subgraph DATA["DATA TIER — NoSQL"]
        DB[("MongoDB<br/>SmartSolarMicrogridDb")]
        C1["Users"]
        C2["SolarStationInfo"]
        C3["EnergyBookingSlots"]
        C4["EnergyReservation"]
        DB --- C1
        DB --- C2
        DB --- C3
        DB --- C4
    end

    WEB -->|"HTTPS / REST + JWT"| CTRL
    AND -->|"HTTPS / REST + JWT"| CTRL
    CTX --> DB

    classDef client fill:#FEF3C7,stroke:#D97706,stroke-width:2px,color:#78350F
    classDef service fill:#DBEAFE,stroke:#2563EB,stroke-width:2px,color:#1E3A8A
    classDef data fill:#D1FAE5,stroke:#059669,stroke-width:2px,color:#065F46
    classDef store fill:#F1F5F9,stroke:#64748B,stroke-width:1px,color:#334155

    class WEB,AND client
    class CTRL,SVC,SEC,CTX service
    class DB,C1,C2,C3,C4 data
    class SQL store
```

---

## 2. Use case diagram

Three actors. Note that **Grid Operator is the only role on both clients**, and that
reactivating an account is reserved to the Backoffice officer (rule R5).

```mermaid
graph LR
    BO(["👤 Backoffice<br/>Officer"])
    GO(["👤 Grid<br/>Operator"])
    PR(["👤 Solar<br/>Prosumer"])

    subgraph SYS["Smart Solar Microgrid Trading System"]
        subgraph UM["Account management"]
            U1(["Create web user"])
            U2(["Manage prosumer<br/>profiles by NIC"])
            U3(["Activate / reactivate<br/>account &lpar;R5&rpar;"])
            U4(["Deactivate account"])
            U5(["Self-register<br/>with NIC"])
            U6(["Edit own profile"])
            U7(["Request own<br/>deactivation"])
            U8(["View pending<br/>activations"])
        end

        subgraph NM["Microgrid node management"]
            N1(["Create node with<br/>GPS + capacity"])
            N2(["Update node<br/>schedule"])
            N3(["Update battery<br/>availability"])
            N4(["Deactivate node<br/>&lpar;blocked if active<br/>bookings — R4&rpar;"])
            N5(["View nodes<br/>on map"])
        end

        subgraph RM["Reservation management"]
            R1(["Create booking<br/>&lpar;within 7 days — R1&rpar;"])
            R2(["Update booking<br/>&lpar;12h notice — R2&rpar;"])
            R3(["Cancel booking<br/>&lpar;12h notice — R3&rpar;"])
            R4(["Approve / reject<br/>booking"])
            R5(["View bookings<br/>&amp; history"])
            R6(["Search / filter<br/>bookings"])
            R7(["View dashboard<br/>counts"])
        end

        subgraph QR["Energy transfer"]
            Q1(["Receive QR code<br/>on approval"])
            Q2(["Scan prosumer<br/>QR code"])
            Q3(["Verify against<br/>server &lpar;R9&rpar;"])
            Q4(["Finalise transfer<br/>as done"])
        end

        subgraph SM["Slot management"]
            S1(["Create booking slot"])
            S2(["Generate slots<br/>in bulk"])
            S3(["Block / delete slot"])
        end
    end

    BO --- U1
    BO --- U2
    BO --- U3
    BO --- U4
    BO --- U8
    BO --- N1
    BO --- N2
    BO --- N4
    BO --- S1
    BO --- S2
    BO --- S3
    BO --- R4
    BO --- R6
    BO --- R7

    GO --- N2
    GO --- N3
    GO --- N5
    GO --- S1
    GO --- S2
    GO --- R4
    GO --- R5
    GO --- R6
    GO --- R7
    GO --- Q2
    GO --- Q3
    GO --- Q4

    PR --- U5
    PR --- U6
    PR --- U7
    PR --- N5
    PR --- R1
    PR --- R2
    PR --- R3
    PR --- R5
    PR --- R6
    PR --- R7
    PR --- Q1

    Q2 -.->|"&laquo;include&raquo;"| Q3
    Q4 -.->|"&laquo;include&raquo;"| Q3
    R2 -.->|"&laquo;include&raquo;"| R1

    classDef actor fill:#FEF3C7,stroke:#B45309,stroke-width:2px,color:#78350F
    class BO,GO,PR actor
```

---

## 3. Data flow diagram — Level 0 (context diagram)

```mermaid
graph LR
    BO["Backoffice<br/>Officer"]
    GO["Grid<br/>Operator"]
    PR["Solar<br/>Prosumer"]

    SYS(("0<br/><br/>Smart Solar<br/>Microgrid Trading<br/>System"))

    BO -->|"user &amp; node details,<br/>activation decisions"| SYS
    SYS -->|"account lists, pending queue,<br/>dashboards, booking reports"| BO

    PR -->|"registration, profile edits,<br/>booking requests, cancellations"| SYS
    SYS -->|"booking confirmation, QR code,<br/>history, nearby nodes"| PR

    GO -->|"battery availability, schedules,<br/>scanned QR, delivered kWh"| SYS
    SYS -->|"node bookings, verification result,<br/>operational dashboard"| GO

    classDef ext fill:#FEF3C7,stroke:#B45309,stroke-width:2px,color:#78350F
    classDef proc fill:#DBEAFE,stroke:#2563EB,stroke-width:3px,color:#1E3A8A
    class BO,GO,PR ext
    class SYS proc
```

---

## 4. Data flow diagram — Level 1

The four data stores map one-to-one onto the four MongoDB collections.

```mermaid
graph TB
    BO["Backoffice<br/>Officer"]
    GO["Grid<br/>Operator"]
    PR["Solar<br/>Prosumer"]

    P1(("1.0<br/>Authenticate<br/>&amp; Manage<br/>Accounts"))
    P2(("2.0<br/>Manage<br/>Microgrid<br/>Nodes"))
    P3(("3.0<br/>Publish<br/>Booking<br/>Slots"))
    P4(("4.0<br/>Process<br/>Reservations"))
    P5(("5.0<br/>Verify QR &amp;<br/>Finalise<br/>Transfer"))
    P6(("6.0<br/>Generate<br/>Dashboards<br/>&amp; Reports"))

    D1[("D1 · Users")]
    D2[("D2 · SolarStationInfo")]
    D3[("D3 · EnergyBookingSlots")]
    D4[("D4 · EnergyReservation")]

    PR -->|"NIC, credentials,<br/>profile, deactivation request"| P1
    BO -->|"web user details,<br/>activate / deactivate"| P1
    GO -->|"credentials"| P1
    P1 <-->|"account records"| D1
    P1 -->|"JWT, profile,<br/>pending queue"| BO
    P1 -->|"session token"| PR

    BO -->|"node details, GPS,<br/>capacity, schedule"| P2
    GO -->|"battery availability,<br/>schedule updates"| P2
    P2 <-->|"node records"| D2
    P2 -->|"R4 check:<br/>active booking count"| D4
    P2 -->|"node list, map data,<br/>distance ranking"| PR

    BO -->|"slot definition,<br/>bulk generate"| P3
    GO -->|"slot definition"| P3
    P3 <-->|"slot records"| D3
    P3 -->|"reads opening days"| D2
    P3 -->|"available slots"| PR

    PR -->|"booking request,<br/>update, cancellation"| P4
    BO -->|"approve / reject"| P4
    GO -->|"approve / reject"| P4
    P4 -->|"validate R1, R8, R12, R13"| D3
    P4 -->|"validate node active"| D2
    P4 -->|"validate account active"| D1
    P4 <-->|"reservation records"| D4
    P4 -->|"updates bookedCount"| D3
    P4 -->|"confirmation + QR token"| PR

    GO -->|"scanned QR token,<br/>delivered kWh, notes"| P5
    P5 -->|"look up by qrToken,<br/>validate R9"| D4
    P5 -->|"status = Completed,<br/>token cleared"| D4
    P5 -->|"verification result"| GO

    P6 -->|"counts by status"| D4
    P6 -->|"active node counts"| D2
    P6 -->|"pending activations"| D1
    P6 -->|"dashboard figures"| BO
    P6 -->|"dashboard figures"| GO
    P6 -->|"my booking counts"| PR

    classDef ext fill:#FEF3C7,stroke:#B45309,stroke-width:2px,color:#78350F
    classDef proc fill:#DBEAFE,stroke:#2563EB,stroke-width:2px,color:#1E3A8A
    classDef store fill:#D1FAE5,stroke:#059669,stroke-width:2px,color:#065F46
    class BO,GO,PR ext
    class P1,P2,P3,P4,P5,P6 proc
    class D1,D2,D3,D4 store
```

---

## 5. Database design — collection relationships

MongoDB is schemaless, but the application enforces this structure. Denormalised
copies (`stationCode`, `prosumerName`, …) are stored deliberately so a list screen
never needs a second query — a standard document-database trade-off.

```mermaid
erDiagram
    USERS ||--o{ ENERGY_RESERVATION : "places (by nic)"
    SOLAR_STATION_INFO ||--o{ ENERGY_BOOKING_SLOTS : "publishes"
    SOLAR_STATION_INFO ||--o{ ENERGY_RESERVATION : "hosts"
    ENERGY_BOOKING_SLOTS ||--o{ ENERGY_RESERVATION : "is booked by"
    SOLAR_STATION_INFO ||--o{ USERS : "is assigned to operator"

    USERS {
        ObjectId _id PK
        string nic UK "prosumer primary key, unique sparse"
        string username UK
        string email UK
        string passwordHash "PBKDF2-SHA256"
        string passwordSalt
        string fullName
        string phone
        string address
        string role "Backoffice|GridOperator|Prosumer"
        string status "Pending|Active|Deactivated"
        bool deactivationRequested
        date deactivationRequestedAt
        string assignedStationId FK
        date lastLoginAt
        date createdAt
        date updatedAt
    }

    SOLAR_STATION_INFO {
        ObjectId _id PK
        string stationCode UK "STN-001"
        string name
        string type "Solar|Hybrid|Battery"
        string address
        string city
        object location "latitude, longitude"
        double capacityKwh
        int totalBatterySlots
        int availableBatterySlots
        object operatingHours
        array weeklySchedule "7 x DaySchedule"
        string contactNumber
        string status "Active|Inactive"
        date createdAt
        date updatedAt
    }

    ENERGY_BOOKING_SLOTS {
        ObjectId _id PK
        string stationId FK
        string stationCode "denormalised"
        string slotCode "unique per station+date"
        date slotDate
        string startTime "HH:mm"
        string endTime "HH:mm"
        date startUtc "rules compare on this"
        date endUtc
        string slotType "Charging|DropOff"
        double energyCapacityKwh
        int batterySlotNumber
        int maxBookings
        int bookedCount
        string status "Available|Full|Blocked"
    }

    ENERGY_RESERVATION {
        ObjectId _id PK
        string reservationNo UK "RSV-20260904-0001"
        string prosumerNic FK
        string prosumerName "denormalised"
        string stationId FK
        string stationCode "denormalised"
        string slotId FK
        date reservationStartUtc "R1,R2,R3 measured here"
        date reservationEndUtc
        string slotType
        double energyKwh
        double deliveredKwh
        string status "Pending|Approved|Completed|Cancelled|Rejected"
        string qrToken UK "unique sparse, cleared on cancel/complete"
        string qrPayload
        date qrIssuedAt
        string approvedBy
        date approvedAt
        string completedBy
        date completedAt
        string cancelReason
        date createdAt
    }
```

---

## 6. Reservation life cycle (state diagram)

```mermaid
stateDiagram-v2
    [*] --> Pending : prosumer creates booking<br/>(R1: within 7 days)

    Pending --> Approved : staff approves<br/>QR token issued
    Pending --> Rejected : staff rejects with reason<br/>slot capacity returned
    Pending --> Cancelled : cancelled<br/>(R3: ≥12h notice)
    Pending --> Pending : updated<br/>(R2: ≥12h notice)

    Approved --> Completed : operator scans QR (R9)<br/>records delivered kWh<br/>token cleared
    Approved --> Cancelled : cancelled<br/>(R3: ≥12h notice)<br/>token cleared
    Approved --> Approved : updated (R2)<br/>QR re-issued so the old<br/>code cannot be replayed

    Completed --> [*]
    Cancelled --> [*]
    Rejected --> [*]

    note right of Approved
        The API computes canModify and
        canCancel per record and sends
        them to the clients. Neither
        client evaluates the 12-hour
        rule itself.
    end note
```

---

## 7. QR verification sequence

The critical security property: the server trusts **only the token** and re-reads every
other field from MongoDB. Nothing inside the scanned payload is believed.

```mermaid
sequenceDiagram
    autonumber
    actor P as Prosumer (Android)
    actor O as Grid Operator (Android)
    participant API as Web API (FAT service)
    participant DB as MongoDB

    Note over API: Booking already Approved
    P->>API: GET /reservations/{id}/qr
    API->>DB: find reservation
    DB-->>API: reservation (status Approved)
    API->>API: render PNG from qrPayload (QRCoder)
    API-->>P: qrToken + qrPayload + base64 PNG
    Note over P: Phone only DISPLAYS the image

    P->>O: shows QR code on screen
    O->>O: ZXing decodes the image
    O->>API: POST /reservations/verify-qr { qrToken }

    API->>API: accept bare token OR whole JSON payload
    API->>DB: find by qrToken
    alt token not found
        DB-->>API: none
        API-->>O: 400 QR_INVALID
    else status is not Approved
        API-->>O: 400 QR_NOT_APPROVED
    else outside the ±2h scan window
        API-->>O: 400 QR_OUT_OF_WINDOW
    else valid
        DB-->>API: reservation
        API-->>O: 200 reservation details<br/>(re-read from the database,<br/>never from the scanned payload)
    end

    O->>API: POST /reservations/complete<br/>{ qrToken, deliveredKwh, notes }
    API->>API: re-run the SAME R9 validation
    API->>DB: status = Completed, clear qrToken
    Note over DB: token cleared so the same<br/>code cannot be scanned twice
    API-->>O: 200 "Energy transfer finalised"
```

---

## 8. Booking creation sequence — showing where each rule fires

```mermaid
sequenceDiagram
    autonumber
    actor P as Prosumer
    participant C as Client (web or Android)
    participant CT as ReservationsController
    participant SV as ReservationService
    participant DB as MongoDB

    P->>C: selects node, date, slot, kWh
    C->>CT: POST /api/v1/reservations
    Note over C: client sends NO prosumerNic;<br/>the API forces the caller's own (R11)
    CT->>SV: CreateAsync(request, caller)

    SV->>DB: load prosumer by NIC
    alt account not Active
        SV-->>CT: 403 ACCOUNT_PENDING / ACCOUNT_DEACTIVATED
    end

    SV->>DB: load slot + station
    SV->>SV: R13 — already booked this slot?
    alt duplicate
        SV-->>CT: 409 DUPLICATE_RESERVATION
    end

    SV->>SV: R1 — start in future and ≤ 7 days?
    alt outside window
        SV-->>CT: 400 RES_WINDOW_7_DAYS
    end

    SV->>SV: R12 — node open that weekday?
    alt closed
        SV-->>CT: 400 STATION_CLOSED
    end

    SV->>SV: R8 — slot Available and has capacity?
    alt unavailable
        SV-->>CT: 409 SLOT_UNAVAILABLE
    end

    SV->>DB: insert reservation (status Pending)
    SV->>DB: atomic $inc bookedCount +1
    SV-->>CT: 201 ReservationDto<br/>(canModify / canCancel computed here)
    CT-->>C: 201 + envelope
    C->>P: summary screen
```
