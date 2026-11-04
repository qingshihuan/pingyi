# Contributing to PingYi

Thanks for helping improve PingYi.

## Before you start

- Search existing issues before opening a new one.
- Keep changes focused and explain their user impact.
- Do not include screenshots, OCR text, API keys, tokens, certificates, or other private data in issues or logs.

## Development

Requirements:

- .NET 10 SDK
- Python 3.13 for repository and packaging checks

Build and test:

```powershell
dotnet restore PingYi.slnx
dotnet build PingYi.slnx
dotnet test PingYi.slnx
python -m unittest discover -s scripts -p "test_*.py"
python scripts/audit-secrets.py
python scripts/audit-secrets.py --ref HEAD
```

See `README.md` for multimodal model setup and release packaging commands.

## Pull requests

- Describe what changed and why.
- Include the checks you ran.
- Add or update tests for behavior changes.
- Update documentation and third-party notices when applicable.

By contributing, you agree that your contribution is licensed under the MIT License.
