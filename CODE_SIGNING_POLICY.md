# Code Signing Policy

Free code signing provided by SignPath.io, certificate by SignPath Foundation.

This policy covers official Windows release binaries published from the
[AntennaGuardian repository](https://github.com/johnfking/AntennaGuardian).
Until SignPath enrollment and workflow integration are complete, releases are
unsigned and are identified as such on the download page.

## Roles

- **Committer and reviewer:** [John / W3JFK (`@johnfking`)](https://github.com/johnfking)
- **Signing approver:** [John / W3JFK (`@johnfking`)](https://github.com/johnfking)

The maintainer uses multi-factor authentication for source-control and signing
access. Signing credentials are not stored in the repository or release
artifacts.

## Release process

Official signed releases must follow this process:

1. Source and build instructions are committed to the public repository.
2. A version tag triggers the GitHub-hosted Windows build workflow.
3. The workflow restores dependencies, runs the complete automated test suite,
   and publishes the self-contained Windows executable.
4. The unsigned workflow artifact is submitted to SignPath with GitHub origin
   verification.
5. The signing approver manually reviews and approves the signing request.
6. The workflow verifies the returned Authenticode signature before creating
   the GitHub Release.
7. The signed artifact is published without further modification.

Only artifacts built from this repository by the configured GitHub Actions
workflow are eligible for signing. Local builds and pull-request artifacts are
not official releases and must not be signed with the project certificate.

## Incident response

Reports of malware, unauthorized signing, compromised maintainer access, or a
release that does not correspond to its tagged source will be investigated
promptly. Affected releases will be withdrawn, SignPath will be notified, and
certificate revocation will be requested when warranted.

Report suspected problems through
[private vulnerability reporting](https://github.com/johnfking/AntennaGuardian/security/advisories/new).
Do not include credentials, private radio addresses, or other unnecessary
sensitive data.
