# Demo video guide — how the system works, how to run it, what to say

The assignment asks for **a video of no more than 5 minutes explaining how the
application works**, with a YouTube or OneDrive link in the README.
Deadline: **30 September 2026, 11:59 PM**.

This guide has five parts:

1. [How the system works](#1-how-the-system-works) — read this first, so you can explain it in your own words
2. [Prepare (do this before recording)](#2-prepare-before-recording)
3. [Shot list with the words to say](#3-shot-list-and-narration)
4. [When something goes wrong](#4-when-something-goes-wrong)
5. [Cheat sheet](#5-cheat-sheet)

---

## 1. How the system works

### The three pieces

```
  Phone (Android app)          Browser (React web app)
  prosumers + operators        back-office officers + operators
          \                          /
           \   JSON over HTTP,      /
            \  JWT login token     /
             v                    v
        ┌──────────────────────────────┐
        │  C# Web API  (port 5218)     │   ← ALL the rules live here
        └──────────────┬───────────────┘
                       v
              MongoDB (SmartSolarMicrogridDb)
```

* **MongoDB** is the database. Four collections: `Users`, `SolarStationInfo`
  (microgrid nodes), `EnergyBookingSlots`, `EnergyReservation`.
* **The C# Web API** is the brain. It is called a *fat service* because every
  rule, every check and every calculation happens here.
* **The web app** and the **phone app** are just screens. They never touch the
  database and they never decide anything. They call the API and show what it says.

### The three kinds of user

| Role | Uses | What they do |
|---|---|---|
| **Prosumer** | Phone | A house owner with solar panels. Books energy slots to sell or store energy. |
| **Grid Operator** | Phone + web | Works at a microgrid node. Approves bookings and scans the QR code when the prosumer arrives. |
| **Backoffice** | Web | Administrator. Creates nodes and slots, activates new accounts, manages web users. |

### The life of one booking (this is the story of your video)

```
 Prosumer registers on phone ──► account is PENDING (cannot log in)
        │
 Backoffice activates it on the web ──► ACTIVE
        │
 Prosumer books a slot on the phone ──► reservation is PENDING
        │
 Staff approves it on the web ──► APPROVED, and the API issues a QR code
        │
 Prosumer arrives, operator scans the QR on the phone
        │  API checks: token exists? approved? inside the time window?
        ▼
 Operator enters the kWh delivered ──► COMPLETED
```

### The rules you can point at (each one is enforced only by the API)

| Rule | Plain English |
|---|---|
| R1 | You can only book a slot that starts in the future and within 7 days. |
| R2 / R3 | You can only **change or cancel** a booking if it starts **at least 12 hours from now**. |
| R4 | You cannot deactivate a node while it still has upcoming bookings. |
| R5 | A deactivated account can only be brought back by a Backoffice officer. |
| R6 | A self-registered prosumer starts **Pending** and cannot log in until activated. |
| R9 | A QR code only works if the booking is Approved and you scan within 2 hours before the start to 2 hours after the end. |

**The key sentence for the viva and the video:** *"The phone and the website contain
no business rules. For example the Cancel button is greyed out because the API sent
`canCancel: false`. The 12-hour maths happens on the server."*

---

## 2. Prepare (before recording)

### 2.1 Important finding: the database has no bookable slots

I checked MongoDB on this laptop. The newest slot in the database starts on
**17 Sep 2026**, and today is 29 Sep. The seeder only fills empty collections, so
nothing has been refreshed since. **Until there are slots in the future, the
phone's "New booking" screen has nothing to offer.**

You do not have to reset anything, because the shot list below **creates a new
node and generates fresh slots live**. That is also step 2 of the assignment's
own demo checklist, so it is part of the story anyway.

Optional: a clean reset. It removes leftover test data (there are four extra
Deactivated test accounts and eight cancelled bookings in there) and gives the
dashboard nice numbers: 5 pending, 6 approved and upcoming, 8 completed. **This
deletes everything in the database and re-seeds it on the next API start.**
Stop the API first, then run:

```bash
python -c "from pymongo import MongoClient; MongoClient().drop_database('SmartSolarMicrogridDb')"
```

Then start the API again (2.3) and it re-creates all demo data.

### 2.2 What must be installed

Already checked on this laptop: .NET 8 runtime, Node 22, MongoDB service running,
JDK 17, Android SDK, `adb`. Nothing to install.

### 2.3 Start everything (four terminals, in this order)

**Terminal 1 — MongoDB** (probably already running):

```bash
net start MongoDB
```

("The requested service has already been started" is fine.)

**Terminal 2 — the API. Leave it open, this is your backend shot:**

```bash
cd D:\Y4S2\EAD\SmartSolarMicrogridTradingSystem\backend
dotnet run --project SmartSolar.Api --urls "http://localhost:5218"
```

Wait for `Now listening on: http://localhost:5218`. Check it:

```bash
curl http://localhost:5218/api/v1/health
```

You should see `"database": "Connected"`. Also open <http://localhost:5218/swagger>
in the browser. That page lists every endpoint and is a good backend visual.

**Terminal 3 — the web app:**

```bash
cd D:\Y4S2\EAD\SmartSolarMicrogridTradingSystem\web
npm run dev
```

Open <http://localhost:5173>.

**Terminal 4 — the phone.** On the phone: Settings → About phone → tap *Build
number* 7 times → Settings → Developer options → turn on **USB debugging**.
Plug it into the laptop with a data cable and tap **Allow** on the phone.

```bash
adb devices
```

The phone must appear as `device` (not `unauthorized`, not an empty list). Then:

```bash
adb reverse tcp:5218 tcp:5218
```

**What this does:** it makes the phone's own `127.0.0.1:5218` point at the
laptop's port 5218, over the USB cable. The app is configured for
`http://127.0.0.1:5218/api/v1/`, so it reaches your API with no Wi-Fi and no
firewall setup. **Run it again every time the phone is unplugged or reboots.**

Install the app (this rebuilds it, so you get the latest code):

```bash
cd D:\Y4S2\EAD\SmartSolarMicrogridTradingSystem\mobile
.\gradlew.bat installDebug
```

The first build can take several minutes. Open **Smart Solar** on the phone. When
you see the login screen you are ready.

Then on the phone, grant permissions ahead of time so no dialog interrupts the
video: open the scanner once as an operator and tap **Allow** for the camera, and
open "Nearby nodes" once and tap **Allow** for location.

### 2.4 Known problems to decide about

* **The map screen will be blank on the phone.** `mobile/local.properties` still
  has the placeholder key `AIzaSyDUMMY_...`. Google Maps needs a real key (Google
  Cloud Console → enable *Maps SDK for Android*). Your own notes say the map is
  worth 5 marks. If you cannot get a key before recording: show the phone's
  "Nearby nodes" screen for two seconds (the list below the map still works and
  the API is still ranking by distance), and show the **web** map page instead. It
  uses OpenStreetMap and needs no key. Do not linger on the blank phone map.
* **IIS.** The README and the assignment say the API is hosted on IIS. The steps
  above run it with `dotnet run`. If your team has a working IIS site on port
  8080, show that in the first shot and change the phone/web URLs to match. If not,
  do not claim IIS in the video. Say "for the demo I'm running it locally".
* **Slot times use Sri Lankan local time.** The API converts `Asia/Colombo` wall
  time to UTC for storage and rule checks, so a slot shown as `17:00` starts at
  17:00 in Sri Lanka regardless of the phone or PC time-zone setting.

### 2.5 Recording setup (simplest way to get one clean video)

Mirror the phone onto the laptop, then record the laptop screen once.

```bash
winget install Genymobile.scrcpy
scrcpy
```

A window shows the phone's screen live (and the camera preview while scanning).
Put that window on the right and the browser on the left, then record the whole
screen with **Win + Alt + R** (Xbox Game Bar) or with OBS Studio. Speak while you
record, or record silently and add the voice-over afterwards. Silent + voice-over
is much easier if you are nervous.

Before you press record: close other apps, turn on Do Not Disturb, and set the
browser zoom to 125% so the text is readable on a small video.

### 2.6 Rehearse once with no recording

Run the whole shot list once. The first run will show you where you get stuck.
Things that use up unique data (the new node code, the new NIC and e-mail) must
be changed on the second run. See the cheat sheet.

---

## 3. Shot list and narration

Total target: **4 min 45 s.** Times are guides. The narration is in plain English;
change the words to sound like you.

**One thing to prepare in advance:** every step below marked ⏱ is slow to type on
camera. Type them during rehearsal and note the values, or record that step
separately and speed it up in editing.

### Shot 1 · Introduction and architecture (0:00 – 0:25)

**Show:** the README architecture diagram, or Terminal 2 with the API log.

> "This is our Smart Solar Microgrid Trading System. Prosumers, people with rooftop
> solar, trade energy with nearby microgrid nodes. There are three parts: a C#
> ASP.NET Core Web API backed by MongoDB, a React web app for back-office staff and
> operators, and a native Android app for prosumers and operators. All business
> rules live in the API. The web and the phone are only screens."

### Shot 2 · The backend is running (0:25 – 0:45)

**Show:** Terminal 2, then the browser at `http://localhost:5218/api/v1/health`,
then `http://localhost:5218/swagger` for a couple of seconds.

> "Here is the API running. The health endpoint reports the database as connected.
> Swagger lists every endpoint: authentication, users, prosumers, stations, slots
> and reservations. Every response uses the same JSON envelope with a success flag,
> a message and an error code."

### Shot 3 · Back office: create a node and publish slots (0:45 – 1:35) ⏱

**Show:** web app, `http://localhost:5173` → **Sign in** → tap the **Backoffice**
button under "Demo accounts" (fills `admin` / `Admin@123`) → **Sign in**.

> "I sign in to the web app as a Back office officer. The sidebar only shows what
> this role may use. The dashboard is live data from the API."

Click **Microgrid Nodes** → **New node**. Fill in (see cheat sheet) and save.

> "I'll add a new microgrid node with its GPS position, capacity and battery bays."

Click **Booking Slots** → pick **STN-007** in the node dropdown → **Generate
slots**. Set **From date** and **To date** to today, **Slot length (hours)** `2`,
**Start hour** and **End hour** from the rule below, then **Generate**.

> "Now I publish booking slots for this node. One call creates every window in the
> range. The API checks the request and inserts them in a single batch."

**How to choose the Start hour and End hour (Sri Lankan local time):**

1. Read the current Sri Lankan time.
2. Round **up** to the next whole hour.
3. Add 1. That is the **Start hour**.
4. **End hour** = Start hour + 4. (That gives two 2-hour slots.)

Example: Sri Lankan time is 14:05 → round up to 15:00 → +1 = **16**, so Start `16`,
End `20`. The first slot starts about 1 h 55 min from now.

Why: **the first slot must start 1 to 2 hours from now.** That keeps the booking
valid under R1 (it must be in the future), makes its QR code scannable straight
away under R9 (the scan window opens 2 h before the start), and keeps it inside the
12-hour notice period so shot 8 works. This rule always lands between 1 and 2 hours
ahead. Use today's Sri Lankan calendar date. Once you have generated the slots,
book within about 30 minutes.

### Shot 4 · Phone: register a prosumer (1:35 – 2:10) ⏱

**Show:** phone (or the scrcpy window). Login screen → **Create an account**.

> "On the phone, a new prosumer registers. Their NIC is their account identifier."

Fill the form (cheat sheet) → **Create account**. A dialog says the account will
be reviewed by a Back office officer. Close it, then try to sign in with the same
NIC and password.

> "The API created the account as Pending. Rule R6: they can't sign in until an
> officer activates them, and you can see the API refused the login."

### Shot 5 · Web: activate the account (2:10 – 2:30)

**Show:** web → **Pending Activations** → the new person's card → **Activate account**.

> "Back on the web, the officer sees the pending account in this queue and
> activates it. Only Back office can do this."

### Shot 6 · Phone: book an energy slot (2:30 – 3:10)

**Show:** phone → **Sign in** with the new NIC → **Prosumer home** → **New booking**.

> "The prosumer is now in. This is the home screen: their counts come straight
> from the API."

New booking: choose **STN-007** in the node picker, keep today's date, tap the first
slot, enter `12` kWh, tap **Request booking**. A summary screen appears.

> "They pick a node, a date and a slot, and request twelve kilowatt hours. The API
> checks the 7-day window, the node schedule and that the slot is free, then
> creates a Pending reservation."

Optional 5-second extra: **Home → Nearby nodes**.

> "Nearby nodes are ranked by distance. The Haversine calculation is done on the
> server, not on the phone."

### Shot 7 · Web: approve, and the QR code appears (3:10 – 3:35)

**Show:** web → **Reservations** → find the new booking (search the NIC) → **Approve**
→ **View**. The detail page shows a large QR code.

> "The officer approves the booking. At that moment the API issues a random QR
> token, saves it on the reservation and renders the QR image from it. The phone
> only displays and scans. The server is the only thing that can say whether a
> code is valid."

**Zoom the browser to 150% or more** on this page. You will scan this QR from the
phone later. Keep this tab open.

### Shot 8 · The 12-hour rule (3:35 – 3:55)

**Show:** phone → **My bookings** → open the booking. It shows Approved and a QR.
The **Change booking** and **Cancel booking** buttons are grey and a notice says
changes need 12 hours' notice.

> "This booking starts within twelve hours, so the Change and Cancel buttons are
> disabled. The phone doesn't calculate that. The API sends canModify and canCancel
> as false. Rule R3 is enforced on the server."

### Shot 9 · Rule R4: node cannot be deactivated (3:55 – 4:10)

**Show:** web → **Microgrid Nodes** → **STN-007** → **Deactivate** → confirm. A red
error toast appears.

> "Rule R4: a node can't be switched off while it has upcoming bookings. The API
> refuses with a conflict error."

**Do this before shot 10.** After the transfer completes the booking is no longer
"upcoming", and the API would allow the deactivation.

### Shot 10 · Operator scans the QR and finalises (4:10 – 4:45)

**Show:** phone → **Sign out** → sign in as `operator1` / `Operator@123` →
**Operator console** → **Scan QR code**. Point the **phone camera at the laptop
screen** showing the QR from shot 7. (If you use scrcpy, the camera view shows in
the mirror.)

> "At the node, the grid operator scans the prosumer's QR code with the phone
> camera. The phone sends only the token to the API. The API checks that it exists,
> that the booking is Approved and that we're inside the allowed time window."

(`operator1` is assigned to STN-001, so this booking at STN-007 will not appear in
their "Node bookings" list. The scan still works, because the API verifies the QR
token and does not restrict scanning to the operator's own node. Just don't open
that list in the video.)

The **Verify booking** screen appears. The *Energy delivered (kWh)* box is
pre-filled with the requested 12. Change it to `11.5` → **Finalise transfer**. A
summary says *Energy transfer finalised*.

> "The operator enters the energy actually delivered and finalises. The reservation
> becomes Completed and the QR code is retired, so it can't be used twice."

### Shot 11 · Close the loop and wrap up (4:45 – 5:00)

**Show:** web → refresh the reservation detail (status **Completed**, delivered
energy 11.5, audit trail) → **Dashboard**.

> "Back on the web, the same booking shows Completed with the delivered energy and a
> full audit trail. One API, one database, two clients, and every rule enforced on
> the server. Thank you."

**Stop at 5:00 at the latest.** The limit is 5 minutes, so trim a shot if you are
over.

### If you are over 5 minutes

Cut in this order: the Nearby nodes extra, shot 2's Swagger, then speed up the form
typing in shots 3 and 4 in the editor. Do **not** cut shots 7, 8, 9 or 10. They
show the rules and the QR flow, which is what the marker wants to see.

---

## 4. When something goes wrong

| What you see | Cause | Fix |
|---|---|---|
| `adb devices` shows nothing | No USB debugging or a charge-only cable | Re-enable USB debugging, use a data cable, tap Allow on the phone |
| `adb devices` shows `unauthorized` | You haven't tapped Allow on the phone | Unlock the phone, accept the dialog |
| App says "No internet connection" / cannot log in | The `adb reverse` tunnel is gone | Run `adb reverse tcp:5218 tcp:5218` again; check the API is running |
| Phone: New booking shows "no slots" | No slot exists for that node and date | Generate slots in shot 3; pick the node STN-007 and today |
| Web: generating slots says `0 slot(s) published, N skipped` | That node already has slots on that date (slot codes S1, S2… restart every day, so they collide) | Use the new node STN-007, or pick a date that has no slots for that node |
| Booking fails with `RES_WINDOW_7_DAYS` | Slot start is already in the past | Regenerate with a later start hour |
| Scan says `QR_OUT_OF_WINDOW` | Slot is more than 2 h away, or already ended | Regenerate so the slot starts within 2 hours (that error is rule R9 working; you can also show it on purpose) |
| Scanner does not read the QR | Screen too small or dim | Zoom the browser to 150–200%, full brightness, hold the phone steady 20 cm away |
| Scanner cannot use the camera | Permission denied | Phone Settings → Apps → Smart Solar → Permissions → Camera. Or use **Enter code manually** |
| Register says NIC already exists | You reused a NIC from a rehearsal | Use a fresh NIC and e-mail |
| `dotnet run` fails to bind port 5218 | An old API is still running | Close the other terminal, or find it: `netstat -ano | findstr 5218` |
| Web shows red "API offline" dot | API not running or wrong URL | Start Terminal 2; `web/.env` must say `http://localhost:5218/api/v1` |

**Manual QR fallback.** The scanner has an **Enter code manually** button. To get
a token without the camera, sign in to Swagger as an operator (`POST /auth/login`,
then **Authorize**) and call `GET /reservations/{id}/qr`. Paste `qrToken`.

---

## 5. Cheat sheet

**URLs**

| What | URL |
|---|---|
| Web app | <http://localhost:5173> |
| API health | <http://localhost:5218/api/v1/health> |
| Swagger | <http://localhost:5218/swagger> |

**Accounts** (created by the seeder)

| Login | Password | Role |
|---|---|---|
| `admin` | `Admin@123` | Backoffice |
| `operator1` | `Operator@123` | Grid operator (STN-001) |
| `200112301234` | `Prosumer@123` | Prosumer, active |
| `200234501122` | `Prosumer@123` | Prosumer, **Pending** (R6) |
| `199756403311` | `Prosumer@123` | Prosumer, **Deactivated** (R5) |

**New node (shot 3)**

| Field | Value |
|---|---|
| Node code | `STN-007` |
| Name | `Battaramulla Solar Hub` |
| Type | Solar |
| Address / City | `No. 10, Pannipitiya Road` / `Battaramulla` |
| Latitude / Longitude | `6.9000` / `79.9183` |
| Capacity (kWh) | `200` |
| Contact number | `0112413007` |
| Total / available bays | `10` / `8` |
| Opens / closes | `06:00` / `22:00` |

**New prosumer (shot 4)**. Change the NIC and e-mail on every rehearsal.

| Field | Value |
|---|---|
| NIC | `200498765432` (12 digits, or 9 digits + V) |
| Full name | `Kavindu Perera` |
| E-mail | `kavindu1@gmail.com` |
| Phone | `0771122334` |
| Address | `No. 22, Lake Road, Nugegoda` |
| Password | `Prosumer@123` (min. 6 characters) |

**Phone commands**

```bash
adb devices                            # is the phone connected?
adb reverse tcp:5218 tcp:5218          # re-run after any reconnect
adb shell am start -n lk.sliit.smartsolar/.activities.SplashActivity   # launch app
```

**If you want the phone recorded separately instead of scrcpy** (3-minute clips):

```bash
adb shell screenrecord --time-limit 180 /sdcard/demo1.mp4
adb pull /sdcard/demo1.mp4 D:\Y4S2\EAD\demo1.mp4
```

**After the video:** upload to YouTube (set to *Unlisted*) or OneDrive, then paste
the link into the README's "Video demonstration" section, replacing the
placeholder line.
