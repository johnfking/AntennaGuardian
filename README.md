# AntennaGuardian

**A quiet, always-visible antenna safety interlock for Flex radios.**

[![Build and release](https://github.com/johnfking/AntennaGuardian/actions/workflows/build-release.yml/badge.svg)](https://github.com/johnfking/AntennaGuardian/actions/workflows/build-release.yml)

AntennaGuardian is a compact Windows sidecar that enforces an explicit
antenna-by-band allow matrix through the Flex Ethernet interlock. Because the
decision is enforced at the radio, it applies whether PTT originates in
SmartSDR, SmartSDR CAT, WSJT-X, or another client.

<p align="center">
  <img src="docs/images/overlay-protected.png" alt="AntennaGuardian protected overlay" width="606">
</p>

## At a glance

| Offline | Protected |
| --- | --- |
| ![Offline overlay](docs/images/overlay-offline.png) | ![Protected overlay](docs/images/overlay-protected.png) |
| **TX blocked** | **Transmitting** |
| ![Blocked transmit overlay](docs/images/overlay-blocked.png) | ![Transmitting overlay](docs/images/overlay-transmitting.png) |

The overlay stays out of the way, remains movable, can sit above other station
software, and uses color only where it carries operational meaning:

- **Gray:** protection is offline.
- **Green:** the current antenna and band are allowed.
- **Red:** transmit is blocked or a fault requires attention.
- **Amber:** connection or registration is in progress.

## Policy control

![AntennaGuardian policy settings](docs/images/settings-policy.png)

The policy window provides a dense two-column matrix for ANT1 and ANT2 across
the Flex native amateur bands from 160m through 6m. There is no 2-meter policy
because the target radio does not support 2 meters.

Window size, overlay position, opacity, always-on-top mode, and click-through
mode are remembered between sessions.

## Safety model

AntennaGuardian is deliberately fail closed:

- Protection is disabled by default on a new installation.
- The default matrix allows no antenna and band combinations.
- Unknown frequency, unknown antenna, out-of-band frequency, and unconfigured
  combinations are denied.
- Connecting registers a dynamic `ANT` interlock and immediately asserts
  `not_ready`.
- `ready` is sent only after an allowed PTT request with a known interlock.
- Unkey, policy changes, and newly forbidden radio context reassert
  `not_ready`.
- An out-of-policy `TRANSMITTING` report becomes a visible fault and reasserts
  `not_ready`.
- Disconnect and cleanup are idempotent, so duplicate shutdown paths cannot
  terminate the application.

Software is an additional guard, not a substitute for the radio's hardware
protection, a correct station configuration, or responsible RF operation.

## Install

1. Open the [latest GitHub Release](https://github.com/johnfking/AntennaGuardian/releases/latest).
2. Download `AntennaGuardian.exe`.
3. Run the executable and open **Settings** from the overlay or tray icon.
4. Enter the Flex radio address and configure the antenna/band matrix.
5. Use **ENABLE PROTECTION** when the policy is ready.

The executable is self-contained for Windows x64; a separate .NET installation
is not required. Releases are not currently code-signed, so Windows may display
a SmartScreen warning.

## How it works

The application is split into three focused modules:

- `AntennaGuardian.Core` contains the band catalog, explicit allow policy, and
  deterministic guardian state machine.
- `AntennaGuardian.Flex` contains the SmartSDR TCP protocol parser, radio
  adapter, interlock lifecycle, and reconnecting runtime.
- `AntennaGuardian.App` contains the WPF overlay, tray controls, policy editor,
  activity view, and persisted desktop settings.

The controller is the only module that can emit `SetInterlockReady`. Protocol
input is translated into domain events before it reaches that safety boundary.

## Verified behavior

The original bench spike demonstrated that software-originated PTT produced
`PTT_REQUESTED`, the dynamic antenna interlock withheld ready, the radio
reported that the interlock was preventing transmission, and no
`TRANSMITTING` state followed. The interlock was then removed successfully.

See [BENCH_RESULT.md](BENCH_RESULT.md) for the test configuration and evidence
summary. The original one-purpose Python spike remains in
[`antennaguardian_spike.py`](antennaguardian_spike.py).

## Build locally

Prerequisite: .NET 10 SDK on Windows.

```powershell
git clone https://github.com/johnfking/AntennaGuardian.git
cd AntennaGuardian
dotnet restore .\AntennaGuardian.sln
dotnet test .\AntennaGuardian.sln -c Release --no-restore
dotnet publish .\src\AntennaGuardian.App\AntennaGuardian.App.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -o .\dist
```

## Automated releases

GitHub Actions builds and tests every push and pull request. Every `v*` tag
publishes the self-contained Windows executable as both a workflow artifact and
a GitHub Release asset.

```powershell
git tag v0.1.2
git push origin v0.1.2
```

## Protocol references

- [SmartSDR TCP/IP API](https://github.com/flexradio/smartsdr-api-docs/wiki/SmartSDR-TCPIP-API)
- [SmartSDR Ethernet interlock](https://github.com/flexradio/smartsdr-api-docs/wiki/TCPIP-interlock)
- [SmartSDR status subscriptions](https://github.com/flexradio/smartsdr-api-docs/wiki/TCPIP-sub)
