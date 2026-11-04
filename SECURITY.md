# Security policy

## Supported versions

Until the first stable release, security fixes are applied to the latest published version.

## Reporting a vulnerability

Please do not publish credential exposure, arbitrary code execution, update-channel, or privacy vulnerabilities in a public issue.

Use GitHub's private vulnerability reporting feature for this repository. Include the affected version, reproduction steps, expected impact, and any suggested mitigation. Do not include real screenshots, recognized text, translated text, API keys, or other personal data.

General bugs that do not expose sensitive information can use the public bug report template.

## Local keys and repository checks

Retain the existing platform-specific local key store. Migration must never overwrite an existing destination key. Configuration, secrets and migration backups remain outside source control; ignore rules also cover `.env` and signing keys. Never paste real keys into issues, documentation or release examples.

Run `python scripts/audit-secrets.py` on the staged tree and `python scripts/audit-secrets.py --ref HEAD` on the commit before pushing. Packaging audits unpacked output too. Reports contain paths and line numbers only. Review the diff as well: pattern matching cannot detect every possible secret. A secret scan alone does not approve a release.
