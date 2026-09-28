"""Build the unpackable extension archive without models, settings, or test data."""
import json
from pathlib import Path
import zipfile

root = Path(__file__).resolve().parents[1]
source = root / "browser-extension"
version = json.loads((source / "manifest.json").read_text(encoding="utf-8"))["version"]
output = root / "artifacts" / f"ScreenInsight-Browser-{version}.zip"
output.parent.mkdir(exist_ok=True)
files = ["manifest.json", "background.js", "shared.js", "content.js", "popup.html",
         "popup.css", "theme.css", "popup.js", "help.html", "help.css", "icon.png", "icon-16.png", "icon-32.png", "icon-48.png", "icon-128.png", "README.md"]
with zipfile.ZipFile(output, "w", zipfile.ZIP_DEFLATED) as archive:
    for name in files:
        archive.write(source / name, "ScreenInsight-Browser/" + name)
    archive.write(root / "LICENSE", "ScreenInsight-Browser/LICENSE")
print(output)
