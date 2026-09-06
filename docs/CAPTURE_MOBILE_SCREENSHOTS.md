# Capturing the Android screenshots

The report needs a screenshot of every mobile screen, plus one of the **main opening
screen** (the submission instructions ask for this specifically — use the prosumer home
screen). This note records the exact commands so the capture is repeatable.

---

## 1. Point the app at a reachable API

`mobile/local.properties` is not committed, so create it if it is missing:

```properties
sdk.dir=C\:\\Android\\Sdk
MAPS_API_KEY=<your Google Maps Android API key>
API_BASE_URL_DEBUG=http\://10.0.2.2\:5218/api/v1/
```

`10.0.2.2` is the emulator's alias for the host machine's `localhost`. On a physical
phone use the laptop's LAN address instead (for example
`http://192.168.1.5:5218/api/v1/`) and put both devices on the same Wi-Fi.

Start the API first and confirm it answers:

```bash
curl http://localhost:5218/api/v1/health
```

## 2. Start an emulator

```bash
C:\Android\Sdk\cmdline-tools\latest\bin\sdkmanager "system-images;android-35;google_apis;x86_64"
C:\Android\Sdk\cmdline-tools\latest\bin\avdmanager create avd -n SmartSolar -k "system-images;android-35;google_apis;x86_64" -d pixel_6
C:\Android\Sdk\emulator\emulator -avd SmartSolar
```

> Use a **google_apis** image, not a plain AOSP one. Google Play services are required
> for the Maps screen to render tiles.

## 3. Install and run

```bash
cd mobile
./gradlew installDebug
adb shell am start -n lk.sliit.smartsolar/.activities.SplashActivity
```

## 4. Capture each screen

```bash
adb exec-out screencap -p > docs/screenshots/mobile/01-splash.png
```

Or capture the lot in one pass with this helper — run it, then press Enter after
navigating to each screen:

```bash
mkdir -p docs/screenshots/mobile
for name in 01-splash 02-login 03-register 04-prosumer-home 05-new-booking-station \
            06-new-booking-slots 07-booking-summary 08-my-bookings-current \
            09-my-bookings-history 10-booking-detail-qr 11-edit-booking 12-stations-map \
            13-station-detail 14-profile 15-operator-home 16-scanner 17-verify-result \
            18-operator-bookings; do
  read -p "Navigate to $name, then press Enter..."
  adb exec-out screencap -p > "docs/screenshots/mobile/$name.png"
  echo "  captured $name"
done
```

## 5. Screens to capture

| # | Screen | How to reach it |
|---|---|---|
| 01 | Splash | Launch the app |
| 02 | Login | Automatic when no session is stored |
| 03 | Register | "Create an account" on the login screen |
| 04 | **Prosumer home — the main opening screen** | Sign in as `200112301234` / `Prosumer@123` |
| 05 | New booking, node selected | Home → New booking |
| 06 | New booking, slots listed | Pick a node and a date |
| 07 | Booking summary | Submit the booking |
| 08 | My bookings — Current tab | Home → My bookings |
| 09 | My bookings — History tab | Switch tabs |
| 10 | Booking detail with QR code | Open an **Approved** booking |
| 11 | Edit booking | "Change booking" on a booking more than 12 h away |
| 12 | Stations map (Google Maps) | Home → Nearby nodes |
| 13 | Station detail | Tap a marker's info window |
| 14 | Profile | Home → Profile |
| 15 | Operator home | Sign out, sign in as `operator1` / `Operator@123` |
| 16 | QR scanner | Operator home → Scan QR code |
| 17 | Verify result | Scan an approved booking's QR, or use "Enter code manually" |
| 18 | Operator bookings | Operator home → Node bookings |

## 6. Demonstrating the QR flow without a camera

The emulator's virtual camera cannot easily show a real QR code. `ScannerActivity` has an
**"Enter code manually"** button for exactly this reason. To get a live token:

```bash
TOKEN=$(curl -s -X POST http://localhost:5218/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"identifier":"admin","password":"Admin@123"}' | jq -r .data.token)

curl -s "http://localhost:5218/api/v1/reservations?status=Approved&pageSize=1" \
  -H "Authorization: Bearer $TOKEN" | jq -r '.data.items[0].id'

curl -s "http://localhost:5218/api/v1/reservations/<id>/qr" \
  -H "Authorization: Bearer $TOKEN" | jq -r .data.qrToken
```

Paste that token into the manual-entry dialog.

> **Note on rule R9.** The server only accepts a scan within two hours either side of the
> slot. A booking further out is refused with `QR_OUT_OF_WINDOW` — that is the rule
> working, not a bug. For the demonstration, either approve a booking whose slot is
> starting shortly, or capture the `QR_OUT_OF_WINDOW` dialog as evidence that R9 is
> enforced.

## 7. Recording the demonstration video

```bash
adb shell screenrecord --time-limit 180 /sdcard/demo.mp4
adb pull /sdcard/demo.mp4 docs/demo-mobile.mp4
```

The video must be no longer than five minutes and its link goes in the README.
