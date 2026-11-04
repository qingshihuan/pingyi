#!/usr/bin/env python3
"""Check the index, a commit, or an unpacked release without printing secret values."""
from __future__ import annotations
import argparse
from pathlib import Path, PurePosixPath
import re
import subprocess

PATTERNS = {
    "private key": re.compile(rb"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----"),
    "provider token": re.compile(rb"\b(?:sk-(?:proj-|ant-)?[A-Za-z0-9_-]{24,}|gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,}|AIza[A-Za-z0-9_-]{30,}|AKIA[A-Z0-9]{16})\b"),
    "literal credential": re.compile(rb"(?i)(?:api[_-]?key|access[_-]?token|client[_-]?secret|authorization)[\"']?\s*[:=]\s*[\"'](?:Bearer\s+)?[A-Za-z0-9_+/.=-]{32,}[\"']"),
}
TEXT_SUFFIXES = {".cs", ".axaml", ".xml", ".json", ".jsonl", ".md", ".txt", ".yml", ".yaml", ".py", ".ps1", ".sh", ".cmd", ".cjs", ".js", ".ts", ".tsx", ".html", ".srt", ".toml", ".config", ".props", ".csproj"}

def private_path(name: str) -> bool:
    path = PurePosixPath(name.replace("\\", "/").lower())
    return (path.suffix in {".pfx", ".p12", ".pem", ".key", ".snk", ".cer", ".crt"}
            or path.name == "settings.json" or path.name.startswith("settings.json.")
            or ".pre-v" in path.name and path.name.endswith(".bak")
            or path.name == ".env" or path.name.startswith(".env.") and not path.name.endswith((".example", ".template"))
            or any(p in {"secrets", "pingyi-refactor-backups", "pingyicomplete", "pingyi-complete"} for p in path.parts))

def inspect(name: str, data: bytes) -> list[str]:
    issues = [f"{name}: private local configuration/backup"] if private_path(name) else []
    # Binary files are checked by filename. Never decode or log private contents.
    if b"\0" in data[:8192] or (Path(name).suffix.lower() not in TEXT_SUFFIXES and not name.endswith(".env")):
        return issues
    for label, pattern in PATTERNS.items():
        for match in pattern.finditer(data):
            line = data[:match.start()].count(b"\n") + 1
            issues.append(f"{name}:{line}: possible {label}")
    return issues

def git_entries(ref: str | None):
    if ref:
        raw = subprocess.check_output(["git", "ls-tree", "-rz", ref])
        entries = [(row.split(b"\t", 1)[1].decode("utf-8"), row.split(b"\t", 1)[0].split()[2]) for row in raw.split(b"\0") if row]
    else:
        raw = subprocess.check_output(["git", "ls-files", "--stage", "-z"])
        entries = []
        for row in raw.split(b"\0"):
            if not row: continue
            meta, name = row.split(b"\t", 1)
            mode, oid, stage = meta.split()
            if stage != b"0": raise RuntimeError("Resolve merge conflicts before auditing")
            entries.append((name.decode("utf-8"), oid))
    # One process keeps full-tree/index checks fast and checks actual staged bytes.
    batch = subprocess.run(["git", "cat-file", "--batch"], input=b"\n".join(oid for _, oid in entries)+b"\n", stdout=subprocess.PIPE, check=True).stdout
    offset = 0
    for name, _ in entries:
        end = batch.index(b"\n", offset)
        size = int(batch[offset:end].split()[2]); offset = end + 1
        yield name, batch[offset:offset+size]
        offset += size + 1

def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    group = parser.add_mutually_exclusive_group()
    group.add_argument("--ref", help="Audit every file in this commit/tree")
    group.add_argument("--directory", type=Path, help="Audit an unpacked release")
    args = parser.parse_args()
    if args.directory:
        root = args.directory.resolve(strict=True)
        entries = ((str(p.relative_to(root)), p.read_bytes()) for p in root.rglob("*") if p.is_file())
    else:
        entries = git_entries(args.ref)
    issues = []; count = 0
    for name, data in entries:
        count += 1; issues.extend(inspect(name, data))
    for issue in issues: print(issue)
    print(f"Secret audit: {count} files, {len(issues)} findings (values never printed).")
    return 1 if issues else 0

if __name__ == "__main__":
    raise SystemExit(main())
