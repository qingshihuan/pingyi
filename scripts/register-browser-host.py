"""Register PingYi native messaging for the current user. Never handles user content."""
import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import re
import sys

NAME = "com.pingyi.browser"
ROOT = Path(__file__).resolve().parents[1]


def extension_id():
    manifest = json.loads((ROOT / "browser-extension/manifest.json").read_text(encoding="utf-8"))
    digest = hashlib.sha256(base64.b64decode(manifest["key"])).hexdigest()[:32]
    return "".join(chr(ord("a") + int(n, 16)) for n in digest)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--host", type=Path, help="Full path to published PingYi.BrowserHost executable")
    parser.add_argument("--extension-id", default=extension_id())
    parser.add_argument("--unregister", action="store_true")
    args = parser.parse_args()
    if not re.fullmatch("[a-p]{32}", args.extension_id):
        parser.error("Invalid extension ID")
    if not args.unregister and (not args.host or not args.host.is_file()):
        parser.error("--host must point to an existing published executable")
    if sys.platform == "win32":
        import winreg
        folder = Path(os.environ["LOCALAPPDATA"]) / "PingYiBrowser"
        folder.mkdir(parents=True, exist_ok=True)
        destinations = [folder / (NAME + ".json")]
        for browser in (r"Google\Chrome", r"Microsoft\Edge", "Chromium"):
            key = rf"Software\{browser}\NativeMessagingHosts\{NAME}"
            if args.unregister:
                try:
                    winreg.DeleteKey(winreg.HKEY_CURRENT_USER, key)
                except FileNotFoundError:
                    pass
            else:
                with winreg.CreateKey(winreg.HKEY_CURRENT_USER, key) as handle:
                    winreg.SetValueEx(handle, "", 0, winreg.REG_SZ, str(destinations[0]))
    elif sys.platform.startswith("linux"):
        config = Path(os.environ.get("XDG_CONFIG_HOME", Path.home() / ".config"))
        destinations = [config / browser / "NativeMessagingHosts" / (NAME + ".json")
                        for browser in ("google-chrome", "chromium", "microsoft-edge")]
    else:
        parser.error("Supported platforms: Windows and Linux")
    for destination in destinations:
        if args.unregister:
            destination.unlink(missing_ok=True)
        else:
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_text(json.dumps({
                "name": NAME, "description": "Screen Insight desktop translation bridge",
                "path": str(args.host.resolve()), "type": "stdio",
                "allowed_origins": [f"chrome-extension://{args.extension_id}/"]
            }, indent=2), encoding="utf-8")
            if sys.platform.startswith("linux"):
                destination.chmod(0o600)
    print("Unregistered." if args.unregister else f"Registered for extension {args.extension_id}")


if __name__ == "__main__":
    main()
