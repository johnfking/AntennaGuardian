# Privacy Policy

AntennaGuardian does not collect telemetry, analytics, crash reports, or usage
data. It does not transmit personal information to the author or to any cloud
service.

## Network activity

When protection is enabled, AntennaGuardian connects to the Flex radio address
configured by the operator. The connection uses the SmartSDR TCP/IP API on TCP
port 4992 to observe transmit state and manage the antenna interlock.

Installed editions can also request public release metadata from GitHub to
check for a newer AntennaGuardian version. Automatic checks can be disabled in
Settings. An update package is downloaded only after the operator selects the
download action. Portable editions do not perform update checks. These GitHub
requests are not used by AntennaGuardian to collect telemetry or identify the
operator, although GitHub may process ordinary connection information under
its own privacy policy.

## Local data

AntennaGuardian stores the following settings in
`%LOCALAPPDATA%\AntennaGuardian\settings.json`:

- The configured radio address.
- The antenna-by-band policy matrix.
- Protection and overlay preferences.
- The automatic update-check preference.
- Remembered window dimensions and position.

The activity view is held in memory and is not uploaded or persisted by the
application. AntennaGuardian does not store radio credentials.

## Third-party services

GitHub hosts the source code, release metadata, and update downloads. Installed
editions communicate with GitHub as described above. Release binaries may be
submitted to SignPath.io for code signing under the project's published
[code signing policy](CODE_SIGNING_POLICY.md). These services operate under
their own privacy policies. The running application does not communicate with
SignPath.

## Contact

Questions about this policy can be opened in the project's
[GitHub issue tracker](https://github.com/johnfking/AntennaGuardian/issues).
