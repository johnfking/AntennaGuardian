# AntennaGuardian

AntennaGuardian is a Windows sidecar for Flex radios. It applies an explicit
antenna-by-band allow matrix at the radio's Ethernet interlock, regardless of
whether PTT originates in SmartSDR, SmartSDR CAT, WSJT-X, or another client.

The UI is a compact, always-on-top WPF overlay with a tray icon and a separate
settings window. Protection is disabled by default and the default matrix
allows no combinations.

## Safety model

- The policy is fail closed: unknown frequency, unknown antenna, out-of-band
  frequency, and unconfigured combinations are denied.
- Connecting registers a dynamic `ANT` interlock and immediately asserts
  `not_ready`.
- `ready` is emitted only after an allowed PTT request with a known interlock.
- Unkey, policy changes, and newly forbidden radio context reassert
  `not_ready`.
- An out-of-policy `TRANSMITTING` report becomes a visible fault and reasserts
  `not_ready`.
- Only the native HF and 6-meter bands are included. There is no 2-meter band.

Software is an additional guard, not a replacement for the radio's hardware
protection or correct station configuration.

## Projects

- `AntennaGuardian.Core`: band catalog, policy engine, and deterministic state
  machine.
- `AntennaGuardian.Flex`: SmartSDR TCP protocol parser, radio adapter, and
  reconnecting runtime.
- `AntennaGuardian.App`: Windows WPF overlay, tray controls, settings, and
  activity view.
- `tests`: unit tests for safety policy, controller commands, and protocol
  parsing.

The original one-purpose Python bench tool remains in
`antennaguardian_spike.py`; its verified result is recorded in
`BENCH_RESULT.md`.

## Build and test offline

```powershell
cd AntennaGuardian
dotnet build .\AntennaGuardian.sln
dotnet test .\AntennaGuardian.sln --no-build
```

Open only the settings window for offline UI inspection:

```powershell
dotnet run --project .\src\AntennaGuardian.App -- --settings
```

Do not enable **Radio interlock** until a controlled live-radio validation is
authorized. Opening the normal app while protection remains disabled does not
construct or start the radio runtime.

## Releases

Every push and pull request is built and tested by GitHub Actions. Version tags
create a GitHub Release containing a self-contained Windows x64 executable:

```powershell
git tag v0.1.0
git push origin v0.1.0
```

## Live validation status

The Python spike proved that withholding `ready` blocks software-originated
PTT on the test Flex radio. The production C# adapter and allow path have not
yet been exercised against the radio. That validation is intentionally held
until the station operator authorizes it.

## Protocol references

- [SmartSDR TCP/IP API](https://github.com/flexradio/smartsdr-api-docs/wiki/SmartSDR-TCPIP-API)
- [SmartSDR Ethernet interlock](https://github.com/flexradio/smartsdr-api-docs/wiki/TCPIP-interlock)
- [SmartSDR status subscriptions](https://github.com/flexradio/smartsdr-api-docs/wiki/TCPIP-sub)
