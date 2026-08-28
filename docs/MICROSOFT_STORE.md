# Microsoft Store Deployment

AntennaGuardian should be submitted as an MSIX-packaged desktop application,
not through the Store's EXE/MSI path.

## Why MSIX

- Microsoft signs the package after certification at no charge.
- Installation, updates, rollback, and removal are managed by Windows and the
  Microsoft Store.
- Users do not receive the unsigned-download SmartScreen warning associated
  with the GitHub installer.
- The EXE/MSI Store path would still require every executable in the installer
  to carry a trusted Authenticode signature.

The GitHub Velopack installer remains useful for direct distribution. The two
channels have separate update ownership: Velopack for GitHub installations and
Microsoft Store for MSIX installations.

## Partner Center prerequisite

1. Start the current free developer onboarding flow at
   [storedeveloper.microsoft.com](https://storedeveloper.microsoft.com/).
2. Use an Individual account unless publishing under a registered business
   entity.
3. In **Apps and games**, create a new **MSIX or PWA app** and reserve the name
   **AntennaGuardian**.
4. Open **Product management > Product identity** and record these exact values:
   - `Package/Identity/Name`
   - `Package/Identity/Publisher`
   - `Package/Properties/PublisherDisplayName`

Those values are assigned by Partner Center and must match the package
manifest exactly. They are the only blocker to creating the real Store package
project; placeholders should not be submitted.

Once they are available, build the unsigned Store package with:

```powershell
.\scripts\build-store-package.ps1 `
  -IdentityName 'VALUE FROM PACKAGE/IDENTITY/NAME' `
  -Publisher 'VALUE FROM PACKAGE/IDENTITY/PUBLISHER' `
  -PublisherDisplayName 'VALUE FROM PARTNER CENTER'
```

The script publishes the self-contained x64 WPF application, generates the
manifest and required tile assets, and writes the MSIX to `store-packages`.
It requires the Windows SDK so `MakeAppx.exe` is available.

For GitHub Actions, add the three Product identity values as repository
variables named `STORE_IDENTITY_NAME`, `STORE_PUBLISHER`, and
`STORE_PUBLISHER_DISPLAY_NAME`. The existing release workflow will then build
the MSIX as a private workflow artifact for Partner Center submission. It is
deliberately not attached to the public GitHub Release because an unsigned
Store package is not intended for direct installation.

## Package design

The package will target Windows Desktop x64 and contain the self-contained WPF
publish output. Its manifest must declare:

- `Windows.Desktop` as the target device family.
- `runFullTrust` for the WPF desktop process.
- `internetClient` for public web links shown by the application.
- `privateNetworkClientServer` for Flex UDP discovery and TCP control on the
  local network.
- A package version whose fourth component is `0`. Because Store versions
  cannot start with zero, the build maps application `0.3.0` to package
  `1.3.0.0`; this offset remains monotonic when the application reaches 1.x.

The Store build detects its package identity at runtime. It disables the
GitHub updater and displays **Updates are managed by Microsoft Store**.

## Submission material

Prepare the following before certification:

- The generated `.msixupload` or `.msix` package.
- App description and feature list based on the README.
- At least one clean screenshot; Microsoft recommends several.
- Store logo assets derived from the AntennaGuardian shield.
- Category and age-rating questionnaire.
- Public privacy-policy URL:
  `https://github.com/johnfking/AntennaGuardian/blob/main/PRIVACY.md`
- Certification notes explaining that local-network access is used only for
  Flex discovery on UDP 4992 and SmartSDR API/interlock control on TCP 4992.

Before submission, run the Windows App Certification Kit and test install,
launch, tray behavior, settings persistence, local-network discovery, clean
interlock removal, Store update ownership, and uninstall on a clean Windows
account.

## Official references

- [Open a developer account](https://learn.microsoft.com/windows/apps/publish/faq/open-developer-account)
- [Package a .NET WPF app with MSIX](https://learn.microsoft.com/windows/apps/desktop/modernize/dotnet/package-app)
- [View Partner Center product identity](https://learn.microsoft.com/windows/apps/publish/view-app-identity-details)
- [MSIX package requirements](https://learn.microsoft.com/windows/apps/publish/publish-your-app/msix/app-package-requirements)
- [Create an MSIX submission](https://learn.microsoft.com/windows/apps/publish/publish-your-app/msix/create-app-submission)
