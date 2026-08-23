# Privacy Policy

AntennaGuardian does not collect telemetry, analytics, crash reports, or usage
data. It does not transmit personal information to the author or to any cloud
service.

## Network activity

When protection is enabled, AntennaGuardian connects only to the Flex radio
address configured by the operator. The connection uses the SmartSDR TCP/IP
API on TCP port 4992 to observe transmit state and manage the antenna
interlock. No connection is made until the operator enables protection.

## Local data

AntennaGuardian stores the following settings in
`%LOCALAPPDATA%\AntennaGuardian\settings.json`:

- The configured radio address.
- The antenna-by-band policy matrix.
- Protection and overlay preferences.
- Remembered window dimensions and position.

The activity view is held in memory and is not uploaded or persisted by the
application. AntennaGuardian does not store radio credentials.

## Third-party services

GitHub hosts the source code and release downloads. Release binaries may be
submitted to SignPath.io for code signing under the project's published
[code signing policy](CODE_SIGNING_POLICY.md). These services operate under
their own privacy policies; the running application does not communicate with
either service.

## Contact

Questions about this policy can be opened in the project's
[GitHub issue tracker](https://github.com/johnfking/AntennaGuardian/issues).
