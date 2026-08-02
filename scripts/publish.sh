#!/usr/bin/env bash
set -euo pipefail

version="${1:-0.1.0}"
project_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
publish_dir="$project_root/artifacts/publish/linux-x64"
engine_dir="$project_root/artifacts/engine-host/linux-x64/pingyi-engine"
engine_python="$project_root/.venv-engine/bin/python"
deb_root="$project_root/artifacts/deb-root"

if [[ ! -x "$engine_dir/pingyi-engine" ]]; then
  "$project_root/scripts/build-engine.sh"
fi

rm -rf -- "$publish_dir"
dotnet publish "$project_root/src/PingYi.App/PingYi.App.csproj" \
  -c Release -r linux-x64 --self-contained true -o "$publish_dir" \
  -p:Version="$version" \
  -p:PublishSingleFile=false \
  -p:PublishTrimmed=true \
  -p:TrimMode=partial \
  -p:PublishReadyToRun=false \
  -p:DebugType=None \
  -p:DebugSymbols=false

rm -rf -- "$publish_dir/engine-host"
mkdir -p "$publish_dir/engine-host"
cp -a "$engine_dir/." "$publish_dir/engine-host/"
find "$publish_dir" -type f -name '*.pdb' -delete
bash "$project_root/scripts/prepare-offline-models.sh" "${PINGYI_OFFLINE_MODEL_SOURCE:-}" "$publish_dir/offline-models"
cp "$project_root/LICENSE" "$project_root/THIRD_PARTY_NOTICES.md" "$publish_dir/"
"$engine_python" "$project_root/scripts/audit-release-dependencies.py" "$publish_dir"

tar -C "$publish_dir" -czf "$project_root/artifacts/PingYi-$version-linux-x64.tar.gz" .

rm -rf -- "$deb_root"
mkdir -p "$deb_root/DEBIAN" "$deb_root/opt/pingyi" \
  "$deb_root/usr/bin" "$deb_root/usr/share/applications" "$deb_root/usr/share/doc/pingyi" \
  "$deb_root/usr/share/icons/hicolor/512x512/apps"
cp -a "$publish_dir/." "$deb_root/opt/pingyi/"
cp "$project_root/packaging/linux/pingyi.desktop" "$deb_root/usr/share/applications/pingyi.desktop"
cp "$project_root/src/PingYi.App/Assets/pingyi-v2-icon-512.png" \
  "$deb_root/usr/share/icons/hicolor/512x512/apps/pingyi.png"
cp "$project_root/LICENSE" "$deb_root/usr/share/doc/pingyi/copyright"
ln -s /opt/pingyi/PingYi.App "$deb_root/usr/bin/pingyi"
sed "s/@VERSION@/$version/g" "$project_root/packaging/linux/control" > "$deb_root/DEBIAN/control"
chmod 0755 "$deb_root/opt/pingyi/PingYi.App" "$deb_root/opt/pingyi/engine-host/pingyi-engine"

dpkg-deb --root-owner-group --build "$deb_root" "$project_root/artifacts/PingYi-$version-linux-x64.deb"
echo "Release files are under $project_root/artifacts"
