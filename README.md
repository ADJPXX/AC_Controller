# AC Controller

AC Controller is a Windows system-tray application for controlling Gree air conditioners and compatible devices that expose the local Gree/EWPE Smart protocol. It discovers devices on the local IPv4 network and communicates directly with the selected unit over UDP; a Gree account or cloud service is not required.

The production application is a compact tray utility. A separate WPF prototype in `prototype/` explores a larger interface using simulated data and does not control real appliances.

## Features

- Discover compatible units with IPv4 broadcast and, when available, a direct scan of the last known address.
- Select one unit when several are found and remember it by MAC address.
- Pair with the selected unit, obtain its local session key, and reuse the saved session on later launches when it still works.
- Show connection state, power, operating mode, set temperature, fan speed, and vane direction in the tray menu and tooltip.
- Turn the unit on or off; select cooling or heating; set 16–30 °C; select fan speeds 1–5 or Turbo; and choose a vane position or continuous swing.
- Poll status periodically and reconnect automatically after a communication failure. The tray menu also provides retry and device-selection actions.

The main application's menu text is currently in Portuguese.

## Requirements

- Windows with the .NET 10 SDK to build and run from source.
- A Gree unit, or a compatible unit and firmware that respond to the local Gree/EWPE protocol used here.
- The PC and appliance on the same reachable IPv4 local network. UDP traffic to and from port `7000` must be allowed.

Discovery uses IPv4 broadcast on active network interfaces. Routers, guest Wi-Fi, VLANs, and wireless client-isolation settings commonly block broadcasts or local device-to-device traffic. Internet access is not needed for device discovery or control.

## First run

1. Connect the PC and air conditioner to the same local network.
2. Start AC Controller. It scans the local IPv4 networks and tries the saved device address, if one exists.
3. If one compatible unit is found, the app selects it. If several are found and there is no saved selection, choose one in the selection window.
4. The app pairs with the selected unit and saves its MAC address, current IP address, and protected session information.
5. Open the AC Controller icon in the Windows notification area to see status and issue commands.

The app identifies a saved unit by MAC address, so a DHCP address change does not normally require manual configuration: discovery refreshes the current address. Use **Select another device** in the tray menu to change the selected unit.

## Controls

The tray menu exposes power, cooling and heating modes, set temperatures from 16 °C through 30 °C, five fan-speed levels plus Turbo, and vane positions plus continuous swing. The protocol service can encode mode values 0–4, but the current tray menu only offers cooling and heating. Device firmware may ignore or interpret some settings differently.

The app controls one selected appliance at a time. It refreshes status about every three seconds while connected. A response timeout is five seconds; failed connections retry with an increasing delay, starting at four seconds and capped at forty seconds.

## How the application works

1. **Discovery** (`GreeDiscoveryService`) sends a scan packet to the IPv4 broadcast address for each eligible network interface, collects responses, decrypts the returned device metadata, normalizes MAC addresses, and removes duplicates. If a last known IPv4 address is available and broadcast did not find that address, the app also sends a direct scan there.
2. **Selection and session reuse** (`App`) selects the saved MAC when possible, opens a selection window if needed, then tries the protected cached session for that MAC. If the cached session cannot retrieve status, the app negotiates a new session.
3. **Pairing** (`GreeBindingService`) refreshes the device scan and sends a bind request. It tries the encryption version reported by discovery, then the other supported version if necessary.
4. **Commands and status** (`GreeService`) serializes protocol payloads, encrypts them with the device session key, exchanges packets through `GreeNetworkService`, validates responses, and maps status fields to `GreeStatus`.
5. **Tray and reconnect loop** (`App`) updates the Windows notification-area menu, polls status, and schedules retries. The WPF device-selection window is created only when the user must choose a unit.

The protocol implementation supports the Gree variants represented in the code as AES-ECB and AES-GCM. Discovery distinguishes the response format by the presence of a GCM authentication tag. These details are implementation compatibility mechanisms, not a general-purpose secure transport.

## Configuration and local data

The app stores per-user data under `%LOCALAPPDATA%\AC_Controller`:

| File | Contents |
| --- | --- |
| `settings.json` | Non-secret settings, including the selected device MAC address, last known IP address, and `StartWithWindows` flag. |
| `session.dat` | Device session information, including its local key, protected with Windows DPAPI for the current Windows user. |
| `ac-controller.log` | Best-effort local log entries for startup, connection, and command errors. |

The startup service can register the app in the current user's Windows `Run` key when `StartWithWindows` is enabled. The production tray menu currently has no setting to toggle that option; it defaults to off unless enabled in saved or migrated settings.

A legacy `AC_Controller.json` beside the executable is read only when `settings.json` does not yet exist. The app copies supported non-secret settings into the per-user settings file and attempts to replace the legacy file with a copy that omits unrecognized fields such as an old plaintext key.

## Network and security notes

- Keep the appliance and PC on a trusted local network. Do not expose or port-forward UDP port `7000` to the public internet.
- The local protocol uses reverse-engineered, shared bootstrap keys for discovery and pairing. These are not unique credentials for an individual installation.
- The implementation supports both legacy AES-ECB packets and AES-GCM packets. ECB does not authenticate packets. GCM uses the fixed IV and associated data required by the device protocol; reusing a fixed nonce is not a pattern to adopt in new cryptographic protocols.
- DPAPI protects the saved session file for the current Windows user. It does not secure traffic on the local network or protect against code already running as that user.

## Build and run

From the repository root in PowerShell:

```powershell
dotnet restore .\AC_Controller.sln
dotnet build .\AC_Controller.sln -c Release
dotnet run --project .\AC_Controller\AC_Controller.csproj
```

The solution contains the production application. Its project targets `net10.0-windows`, uses WPF, and references `Hardcodet.NotifyIcon.Wpf` for the notification-area icon.

To publish a self-contained, single-file Windows x64 executable:

```powershell
dotnet publish .\AC_Controller\AC_Controller.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:PublishTrimmed=false `
  -o .\publish
```

The GitHub Actions workflow publishes this Windows x64 executable when changes are pushed to `master`, then creates a GitHub Release with the generated `AC_Controller.exe`.

## Visual prototype

The separate `prototype/` project is a UI exploration. Its device list, temperatures, connection state, schedules, and history are simulated in memory. Its schedule, history, device, and settings screens are not wired to the production services, and the prototype sends no commands to real appliances. Run it separately:

```powershell
dotnet run --project .\prototype\ACControllerPrototype.csproj
```

## Current scope

- Only one real appliance is controlled at a time.
- The production app does not currently implement schedules, persistent command history, or the prototype's settings, theme, and notification flows.
- The production tray menu offers only cooling and heating even though the protocol service can send other mode values.
- Compatibility depends on the appliance's Wi-Fi module and firmware. The app requires protocol fields expected by its status parser and does not claim compatibility with every Gree-branded model.
- Device discovery is IPv4 broadcast based; it does not scan IPv6 networks or route broadcasts across subnets.

## Troubleshooting

- **No device is found:** Confirm the appliance is connected to Wi-Fi, the PC is on the same local network, and the router does not isolate wireless clients. Check that the Windows firewall allows local UDP traffic on port `7000`.
- **The device is on another subnet:** IPv4 broadcasts generally do not cross routers or VLANs. Put the PC and unit on a shared subnet or configure the network so they can communicate directly.
- **A saved device is not selected:** Use **Select another device** to scan and choose a unit. The app remembers the choice by MAC address.
- **Connection or pairing fails:** Use **Try again** in the tray menu, check local reachability and firewall rules, then consult `%LOCALAPPDATA%\AC_Controller\ac-controller.log`. Some compatible-looking models use firmware that does not support the protocol flow implemented here.

## Repository layout

```text
AC_Controller.sln
AC_Controller/
  App.xaml(.cs)                 Tray lifecycle, UI, polling, and reconnect loop
  DeviceSelectionWindow.cs      Device chooser
  Models/                       Configuration, device, session, status, and crypto models
  Services/                     Discovery, binding, protocol, crypto, persistence, startup
  Resources/accontroller.ico    Application icon
prototype/                      Standalone simulated WPF UI prototype
.github/workflows/release.yml   Windows x64 publish and GitHub Release workflow
```
