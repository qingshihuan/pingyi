#!/usr/bin/env bash
set -euo pipefail

version="${1:-0.1.0}"
edition="${2:-standard}"
if [[ "$edition" != "standard" && "$edition" != "complete" ]]; then
  echo "Edition must be standard or complete" >&2
  exit 2
fi
project_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
edition_suffix=""
archive_prefix="PingYi"
package_name="pingyi"
install_name="pingyi"
desktop_name="pingyi.desktop"
if [[ "$edition" == "complete" ]]; then
  edition_suffix="-complete"
  archive_prefix="PingYi-Complete"
  package_name="pingyi-complete"
  install_name="pingyi-complete"
  desktop_name="pingyi-complete.desktop"
fi
publish_dir="$project_root/artifacts/publish/linux-x64$edition_suffix"
engine_dir="$project_root/artifacts/engine-host/linux-x64/pingyi-engine"
engine_python="$project_root/.venv-engine/bin/python"
deb_root="$project_root/artifacts/deb-root$edition_suffix"

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
if [[ "$edition" == "complete" ]]; then
  runtime_source="${PINGYI_LLAMA_RUNTIME_SOURCE:-}"
  if [[ -z "$runtime_source" || ! -d "$runtime_source" ]]; then
    echo "Complete edition requires PINGYI_LLAMA_RUNTIME_SOURCE" >&2
    exit 1
  fi
  mkdir -p "$publish_dir/llama-runtime"
  cp -a "$runtime_source/." "$publish_dir/llama-runtime/"
  printf 'PingYi Complete\n' > "$publish_dir/pingyi-complete.edition"
fi
cp "$project_root/LICENSE" "$project_root/THIRD_PARTY_NOTICES.md" "$publish_dir/"
"$engine_python" "$project_root/scripts/audit-release-dependencies.py" "$publish_dir"

tar -C "$publish_dir" -czf "$project_root/artifacts/$archive_prefix-$version-linux-x64.tar.gz" .

rm -rf -- "$deb_root"
mkdir -p "$deb_root/DEBIAN" "$deb_root/opt/$install_name" \
  "$deb_root/usr/bin" "$deb_root/usr/share/applications" "$deb_root/usr/share/doc/$install_name" \
  "$deb_root/usr/share/icons/hicolor/512x512/apps"
cp -a "$publish_dir/." "$deb_root/opt/$install_name/"
if [[ "$edition" == "complete" ]]; then
  sed -e 's/^Name=屏译$/Name=屏译 完全版/' \
      -e 's/^Exec=pingyi$/Exec=pingyi-complete/' \
      -e 's/^Icon=pingyi$/Icon=pingyi-complete/' \
      "$project_root/packaging/linux/pingyi.desktop" > "$deb_root/usr/share/applications/$desktop_name"
else
  cp "$project_root/packaging/linux/pingyi.desktop" "$deb_root/usr/share/applications/$desktop_name"
fi
cp "$project_root/src/PingYi.App/Assets/pingyi-v2-icon-512.png" \
  "$deb_root/usr/share/icons/hicolor/512x512/apps/$install_name.png"
cp "$project_root/LICENSE" "$deb_root/usr/share/doc/$install_name/copyright"
ln -s "/opt/$install_name/PingYi.App" "$deb_root/usr/bin/$install_name"
sed -e "s/@VERSION@/$version/g" -e "s/@PACKAGE@/$package_name/g" \
  "$project_root/packaging/linux/control" > "$deb_root/DEBIAN/control"
chmod 0755 "$deb_root/opt/$install_name/PingYi.App" "$deb_root/opt/$install_name/engine-host/pingyi-engine"
if [[ "$edition" == "complete" ]]; then
  chmod 0755 "$deb_root/opt/$install_name/llama-runtime/vulkan/llama-server" \
    "$deb_root/opt/$install_name/llama-runtime/cpu/llama-server"
fi

dpkg-deb --root-owner-group --build "$deb_root" "$project_root/artifacts/$archive_prefix-$version-linux-x64.deb"
echo "Release files are under $project_root/artifacts"
