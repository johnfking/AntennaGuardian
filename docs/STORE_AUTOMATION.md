# Microsoft Store submission automation

Verified against Microsoft documentation on 2026-09-05. This is a setup recommendation; it does not configure credentials or submit a release.

## Recommended approach

Keep the existing MSIX build described in [Microsoft Store deployment](MICROSOFT_STORE.md). For the current hotfix, upload its generated Store artifact through Partner Center. For subsequent releases, add a manually triggered GitHub Actions submission job consuming the tested MSIX artifact. This recommendation avoids making account provisioning a dependency of the hotfix.

Microsoft provides a Store Developer CLI and a GitHub Actions publishing workflow for MSIX. Its current documentation limits app updates through these tools to **free products**; verify AntennaGuardian's product pricing before adopting them. CLI interactive login requires Microsoft Entra credentials, rather than a personal Microsoft account. [CLI overview](https://learn.microsoft.com/en-us/windows/apps/publish/msstore-dev-cli/overview)

## One-time account setup

1. Associate a Microsoft Entra tenant with the existing Partner Center developer account.
2. Register a dedicated Entra application for release automation, then add it under Partner Center **Account settings > User management > Microsoft Entra applications**, with the documented **Manager** role.
3. Record tenant ID, application/client ID, seller ID, and the AntennaGuardian Store product ID. Generate a client secret and save its value immediately.
4. Store credentials in GitHub Actions secrets, using Microsoft's documented names: `AZURE_AD_TENANT_ID`, `AZURE_AD_APPLICATION_CLIENT_ID`, `AZURE_AD_APPLICATION_SECRET`, and `SELLER_ID`. Keep the Store product ID as configuration. These are separate from the existing package identity variables.

The published product satisfies Microsoft's prerequisite that the app already be live. Owner participation is needed for account association and any interactive sign-in. Do not paste secrets into chat or commit them. [Microsoft's GitHub Actions setup](https://learn.microsoft.com/en-us/windows/apps/publish/msstore-dev-cli/github-actions)

The API prerequisite documentation requires Global administrator permission for tenant setup and a completed first submission with age ratings. It authenticates with a client-credentials token for `https://manage.devcenter.microsoft.com`; tokens last 60 minutes. [Submission API prerequisites](https://learn.microsoft.com/en-us/windows/uwp/monetize/create-and-manage-submissions-using-windows-store-services)

## Submission sequence

Configure `msstore reconfigure` with the account identifiers and secret. Certificate credentials are also supported. For an existing package, use `msstore publish --inputFile <package.msix> --appId <product-id> --noCommit` to prepare a draft; then update release notes, commit with `msstore submission publish <product-id>`, and check with `msstore submission poll <product-id>`.

**Check for an existing draft first:** `msstore publish` replaces a pending draft with a copy of the last published submission, losing its staged metadata. Upload before applying metadata changes. `--noCommit` leaves a draft but does not prevent this replacement. [CLI commands and draft behavior](https://learn.microsoft.com/en-us/windows/apps/publish/msstore-dev-cli/commands)

A custom REST client can instead create a submission, update its JSON, upload a ZIP containing the package to the returned SAS URL, commit, and poll status. The creation response copies the last published submission. An accepted commit starts Store processing; it is not proof that customers can obtain the update. [Submission workflow](https://learn.microsoft.com/en-us/windows/uwp/monetize/manage-app-submissions)

## Constraints to check

- Keep changes to an API-created submission in the API. Editing it through Partner Center can prevent later API updates or commits.
- Mandatory app updates and Store-managed consumable add-ons are unsupported by the submission API. Pricing Version 2 has restrictions on updating Pricing and availability; other modules can still be updated.

[API limitations](https://learn.microsoft.com/en-us/windows/uwp/monetize/create-and-manage-submissions-using-windows-store-services)

Credential availability, product pricing, and pending-draft state have not been inspected in Partner Center. Once those are verified, the repository can automate upload and submission; Microsoft still performs certification before the release becomes available. [Publishing and certification](https://learn.microsoft.com/en-us/windows/apps/publish/msstore-dev-cli/github-actions)
