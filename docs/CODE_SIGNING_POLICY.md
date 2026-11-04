# Code signing policy

## Current status

Trusted signing is **not yet active**. The published v0.10.0 Windows application and installer are unsigned. SignPath Foundation is the proposed free open-source signing route; an application has not yet been submitted or approved. No certificate, signing organization or production signing policy has been provisioned.

Once approved and connected, the intended attribution is: Free code signing provided by [SignPath.io](https://signpath.io), certificate by [SignPath Foundation](https://signpath.org). This is a proposed arrangement, not a claim of existing sponsorship or approval. The Windows publisher would be SignPath Foundation, not the maintainer's personal name.

## Project and responsibility

- Project: 截屏释义 / Screen Insight (repository name: PingYi).
- Source: [qingshihuan/pingyi](https://github.com/qingshihuan/pingyi), licensed under [MIT](../LICENSE).
- Maintainer, committer and reviewer: [qingshihuan](https://github.com/qingshihuan).
- Proposed signing approver: the same repository owner, after configuring the SignPath account and confirming this role.
- Maintainer accounts must use multi-factor authentication on GitHub and SignPath before signing is enabled. This document does not claim that account setup or enrollment is already complete.
- Each signing request must be manually approved after checking its source commit, workflow run and release contents. Contributions from outside the maintainer group require review.

## Signing scope and build origin

Only this repository's own Windows application, installer and uninstaller are intended for signing. Third-party .NET, Avalonia, SkiaSharp and llama.cpp components retain their own upstream licensing and signing identity; they must not be re-signed under the project's SignPath subscription.

Release binaries must come from the public [release workflow](../.github/workflows/release.yml), tied to a specific source commit. Sign the application before packaging, sign the installer/uninstaller, verify trusted embedded signatures, expected publisher and timestamps, and compute checksums only after signing. SignPath integration must preserve and verify this build origin; uploading an arbitrary local binary is not an acceptable substitute.

The existing certificate-based release path now rejects missing signing configuration. `scripts/verify-windows-signatures.ps1` checks Windows trust, publisher identity, embedded signatures and timestamps. Actual SignPath service integration and signed-release validation remain pending approval; the release version marker will not be advanced until this is ready.

## Privacy and system changes

The application captures a user-selected screen region for recognition, translation, image description, prompt reconstruction or local QR decoding. Screenshots and results remain in memory; there is no persistent capture history. Built-in local inference uses the loopback service. Remote model requests go to the destination configured and authorized by the user; remembered authorization can be cleared in General settings. Browser-extension authorization is separate.

Model/backend downloads and update checks use the documented sources. API credentials stay in the existing local secret store and are excluded from source and release artifacts. See [privacy and configuration](../README.en.md#capture-and-privacy), [third-party APIs](THIRD_PARTY_API.md), [runtime downloads](RUNTIME_DOWNLOADS.md) and [third-party notices](../THIRD_PARTY_NOTICES.md). Selected API providers apply their own privacy and retention terms.

The installer creates the selected application files and shortcuts and provides an uninstaller. User configuration and downloaded models are retained for upgrade or rollback; the application does not automatically install GPU drivers or change operating-system security settings.

## Reporting concerns

Report release or signing concerns through [repository issues](https://github.com/qingshihuan/pingyi/issues). Do not post private keys, credentials, identity documents, screenshots containing personal information or private model input/output.

## Approval checklist

Before requesting production signing, confirm project eligibility with the Foundation, configure maintainer MFA and approver roles, connect verified GitHub build provenance, restrict artifact paths and product/version metadata, and review the service's [current conditions](https://signpath.org/terms.html). Approval and processing time are determined by the Foundation.
