#!/usr/bin/env python3
"""Collect complete license texts for components shipped in a release directory."""

from __future__ import annotations

import argparse
from importlib import metadata
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ElementTree


PROJECT_ROOT = Path(__file__).resolve().parent.parent
CURATED_ROOT = PROJECT_ROOT / "packaging" / "licenses"
LICENSE_NAME = re.compile(
    r"^(license|licence|copying|notice|third[-_ ]?party[-_ ]?(notices?|programs?)|ofl)",
    re.IGNORECASE,
)
CURATED_FILES = {
    "MIT": "MIT.txt",
    "Apache-2.0": "Apache-2.0.txt",
    "CC-BY-4.0": "CC-BY-4.0.txt",
    "OFL-1.1": "OFL-1.1.txt",
    "LLVM-exception": "LLVM-exception.txt",
    "GPL-3.0": "GPL-3.0.txt",
    "GCC-Runtime-Library-Exception-3.1": "GCC-Runtime-Library-Exception-3.1.txt",
}


def safe_name(value: str) -> str:
    return re.sub(r"[^A-Za-z0-9._-]+", "-", value).strip("-.") or "component"


def reset_directory(path: Path, parent: Path) -> None:
    resolved = path.resolve()
    parent_resolved = parent.resolve()
    if parent_resolved not in resolved.parents or resolved == parent_resolved:
        raise RuntimeError(f"Unsafe license output directory: {resolved}")
    if resolved.exists():
        shutil.rmtree(resolved)
    resolved.mkdir(parents=True)


def is_license_file(path: Path) -> bool:
    return path.is_file() and bool(LICENSE_NAME.match(path.name))


def copy_file(source: Path, destination: Path) -> str:
    if not source.is_file() or source.stat().st_size < 100:
        raise RuntimeError(f"License file is missing or unexpectedly short: {source}")
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, destination)
    return destination.name


def copy_curated(license_id: str, destination: Path) -> str:
    try:
        source = CURATED_ROOT / CURATED_FILES[license_id]
    except KeyError as error:
        raise RuntimeError(f"No curated text for license expression: {license_id}") from error
    return copy_file(source, destination / source.name)


def nuspec_license(package_root: Path) -> tuple[str | None, str | None]:
    nuspecs = list(package_root.glob("*.nuspec"))
    if len(nuspecs) != 1:
        return None, None
    root = ElementTree.parse(nuspecs[0]).getroot()
    element = next((item for item in root.iter() if item.tag.endswith("license")), None)
    if element is None:
        return None, None
    return element.attrib.get("type"), (element.text or "").strip()


def dotnet_package_root() -> Path:
    configured = os.environ.get("NUGET_PACKAGES")
    return Path(configured).expanduser() if configured else Path.home() / ".nuget" / "packages"


def collect_dotnet(publish_dir: Path, output: Path) -> list[dict[str, object]]:
    deps_files = sorted(publish_dir.glob("*.deps.json"))
    if not deps_files:
        raise RuntimeError(f"No .deps.json found under {publish_dir}")
    deps = json.loads(deps_files[0].read_text(encoding="utf-8-sig"))
    components: list[dict[str, object]] = []
    package_cache = dotnet_package_root()
    for identity, details in sorted(deps.get("libraries", {}).items()):
        if details.get("type") != "package":
            continue
        name, version = identity.rsplit("/", 1)
        package_root = package_cache / name.lower() / version
        if not package_root.is_dir():
            raise RuntimeError(f"NuGet package is not restored: {identity} at {package_root}")
        component_dir = output / "dotnet" / f"{safe_name(name)}-{safe_name(version)}"
        files: list[str] = []
        candidates = sorted(
            (path for path in package_root.rglob("*") if is_license_file(path)),
            key=lambda path: str(path).lower(),
        )
        for index, source in enumerate(candidates):
            relative = source.relative_to(package_root)
            destination_name = "__".join(relative.parts)
            if index and (component_dir / destination_name).exists():
                destination_name = f"{index}-{destination_name}"
            files.append(copy_file(source, component_dir / destination_name))

        license_type, license_value = nuspec_license(package_root)
        if license_type == "file" and license_value and Path(license_value).name not in files:
            files.append(copy_file(package_root / license_value, component_dir / Path(license_value).name))
        if license_type == "expression" and license_value:
            expressions = [part for part in re.split(r"\s+(?:AND|OR|WITH)\s+", license_value) if part]
            for expression in expressions:
                curated_name = CURATED_FILES.get(expression)
                if curated_name not in files:
                    files.append(copy_curated(expression, component_dir))
        if not files:
            raise RuntimeError(f"No distributable license text found for NuGet package {identity}")
        components.append(
            {
                "name": name,
                "version": version,
                "kind": "NuGet package",
                "licenseFiles": [str((component_dir / file).relative_to(output)) for file in files],
            }
        )
    return components


def find_dotnet_root() -> Path:
    candidates = [
        os.environ.get("DOTNET_ROOT"),
        os.environ.get("DOTNET_ROOT_X64"),
    ]
    executable = shutil.which("dotnet")
    if executable:
        candidates.append(str(Path(executable).resolve().parent))
    for candidate in candidates:
        if not candidate:
            continue
        root = Path(candidate)
        if (root / "LICENSE.txt").is_file() and (root / "ThirdPartyNotices.txt").is_file():
            return root
    raise RuntimeError("Could not locate .NET runtime LICENSE.txt and ThirdPartyNotices.txt")


def collect_dotnet_runtime(publish_dir: Path, output: Path) -> dict[str, object]:
    deps_file = next(iter(sorted(publish_dir.glob("*.deps.json"))))
    deps = json.loads(deps_file.read_text(encoding="utf-8-sig"))
    identity = next(
        (
            name
            for name, details in deps.get("libraries", {}).items()
            if details.get("type") == "runtimepack" and "Microsoft.NETCore.App.Runtime" in name
        ),
        ".NET runtime/unknown",
    )
    package_identity, version = identity.rsplit("/", 1)
    component_dir = output / "dotnet-runtime"
    package_name = package_identity.removeprefix("runtimepack.")
    package_root = dotnet_package_root() / package_name.lower() / version
    package_license = next(iter(sorted(package_root.glob("LICENSE*"))), None)
    package_notices = next(iter(sorted(package_root.glob("THIRD-PARTY-NOTICES*"))), None)
    if package_license and package_notices:
        files = [
            copy_file(package_license, component_dir / "LICENSE.txt"),
            copy_file(package_notices, component_dir / "ThirdPartyNotices.txt"),
        ]
    else:
        root = find_dotnet_root()
        files = [
            copy_file(root / "LICENSE.txt", component_dir / "LICENSE.txt"),
            copy_file(root / "ThirdPartyNotices.txt", component_dir / "ThirdPartyNotices.txt"),
        ]
    return {
        "name": ".NET runtime",
        "version": version,
        "kind": "self-contained runtime",
        "licenseFiles": [str((component_dir / file).relative_to(output)) for file in files],
    }


def distribution_license_files(distribution: metadata.Distribution) -> list[Path]:
    result: list[Path] = []
    for entry in distribution.files or ():
        source = Path(distribution.locate_file(entry))
        if is_license_file(source):
            result.append(source)
    return sorted(set(result), key=lambda path: str(path).lower())


def collect_python(output: Path) -> list[dict[str, object]]:
    components: list[dict[str, object]] = []
    python_license = Path(sys.base_prefix) / "LICENSE.txt"
    if not python_license.is_file():
        python_license = Path(sys.base_prefix) / "LICENSE"
    if not python_license.is_file():
        python_license = CURATED_ROOT / "Python-3.13.txt"
    python_dir = output / "python" / f"Python-{sys.version_info.major}.{sys.version_info.minor}"
    copied = copy_file(python_license, python_dir / python_license.name)
    components.append(
        {
            "name": "Python",
            "version": f"{sys.version_info.major}.{sys.version_info.minor}.{sys.version_info.micro}",
            "kind": "embedded runtime",
            "licenseFiles": [str((python_dir / copied).relative_to(output))],
        }
    )

    fallbacks = {
        "argostranslate": ["MIT"],
        "ctranslate2": ["MIT"],
        "sentencepiece": ["Apache-2.0"],
        "pyinstaller": [],
    }
    for package_name, extra_licenses in fallbacks.items():
        try:
            distribution = metadata.distribution(package_name)
        except metadata.PackageNotFoundError as error:
            raise RuntimeError(f"Python build dependency is missing: {package_name}") from error
        component_dir = output / "python" / (
            f"{safe_name(distribution.metadata.get('Name') or package_name)}-{safe_name(distribution.version)}"
        )
        files: list[str] = []
        for source in distribution_license_files(distribution):
            files.append(copy_file(source, component_dir / source.name))
        for license_id in extra_licenses:
            curated_name = CURATED_FILES[license_id]
            if curated_name not in files:
                files.append(copy_curated(license_id, component_dir))
        if not files:
            raise RuntimeError(f"No distributable license text found for Python package {package_name}")
        components.append(
            {
                "name": distribution.metadata.get("Name") or package_name,
                "version": distribution.version,
                "kind": "embedded Python package/build runtime",
                "licenseFiles": [str((component_dir / file).relative_to(output)) for file in files],
            }
        )
    return components


def collect_distribution_license_component(
    package_name: str,
    component_name: str,
    output: Path,
) -> dict[str, object]:
    try:
        distribution = metadata.distribution(package_name)
    except metadata.PackageNotFoundError as error:
        raise RuntimeError(
            f"{component_name} is bundled, but the {package_name} license package is missing"
        ) from error
    sources = distribution_license_files(distribution)
    if not sources:
        raise RuntimeError(f"{package_name} does not contain distributable license texts")
    component_dir = output / "native" / safe_name(component_name)
    files: list[str] = []
    for index, source in enumerate(sources):
        destination_name = source.name
        if (component_dir / destination_name).exists():
            destination_name = f"{index}-{destination_name}"
        files.append(copy_file(source, component_dir / destination_name))
    return {
        "name": component_name,
        "version": distribution.version,
        "kind": "statically linked/native runtime dependency",
        "licenseFiles": [
            str((component_dir / file).relative_to(output)) for file in files
        ],
    }


def collect_native_runtimes(publish_dir: Path, output: Path) -> list[dict[str, object]]:
    names = {
        path.name.lower()
        for path in publish_dir.rglob("*")
        if path.is_file()
    }
    components: list[dict[str, object]] = []
    if any(name.startswith("libgomp") for name in names):
        component_dir = output / "native" / "GNU-OpenMP-runtime"
        files = [
            copy_curated("GPL-3.0", component_dir),
            copy_curated("GCC-Runtime-Library-Exception-3.1", component_dir),
        ]
        components.append(
            {
                "name": "GNU OpenMP runtime",
                "version": "bundled by CTranslate2",
                "kind": "native runtime dependency",
                "licenseFiles": [
                    str((component_dir / file).relative_to(output)) for file in files
                ],
            }
        )
    if any(name.startswith("libomp") for name in names):
        component_dir = output / "native" / "LLVM-OpenMP-runtime"
        files = [
            copy_curated("Apache-2.0", component_dir),
            copy_curated("LLVM-exception", component_dir),
        ]
        components.append(
            {
                "name": "LLVM OpenMP runtime",
                "version": "bundled native runtime",
                "kind": "native runtime dependency",
                "licenseFiles": [
                    str((component_dir / file).relative_to(output)) for file in files
                ],
            }
        )
    if any(name.startswith("libiomp") for name in names):
        components.append(
            collect_distribution_license_component(
                "intel-openmp",
                "Intel OpenMP runtime",
                output,
            )
        )
    if any(name.startswith(("ctranslate2", "libctranslate2")) for name in names):
        components.append(
            collect_distribution_license_component(
                "onemkl-license",
                "Intel oneMKL runtime",
                output,
            )
        )
        component_dir = output / "native" / "oneDNN"
        files = [
            copy_file(
                CURATED_ROOT / "oneDNN-3.1.1-LICENSE.txt",
                component_dir / "LICENSE.txt",
            ),
            copy_file(
                CURATED_ROOT / "oneDNN-3.1.1-THIRD-PARTY-PROGRAMS.txt",
                component_dir / "THIRD-PARTY-PROGRAMS.txt",
            ),
        ]
        components.append(
            {
                "name": "oneDNN",
                "version": "3.1.1 (statically linked by CTranslate2)",
                "kind": "statically linked native dependency",
                "licenseFiles": [
                    str((component_dir / file).relative_to(output)) for file in files
                ],
            }
        )
    return components


def collect_models(output: Path, complete: bool) -> list[dict[str, object]]:
    components: list[dict[str, object]] = []
    model_licenses = (
        ("PaddleOCR models", "PP-OCRv5 mobile", "Apache-2.0"),
        ("Argos OPUS-MT model packages", "zh-en and en-zh", "CC-BY-4.0"),
        ("Inter font", "bundled by Avalonia.Fonts.Inter", "OFL-1.1"),
    )
    for name, version, license_id in model_licenses:
        component_dir = output / "models-and-assets" / safe_name(name)
        copied = copy_curated(license_id, component_dir)
        components.append(
            {
                "name": name,
                "version": version,
                "kind": "offline model/font asset",
                "licenseFiles": [str((component_dir / copied).relative_to(output))],
            }
        )
    if complete:
        component_dir = output / "native" / "llama.cpp"
        copied = copy_curated("MIT", component_dir)
        components.append(
            {
                "name": "llama.cpp",
                "version": "b10227",
                "kind": "CPU/Vulkan runtime",
                "licenseFiles": [str((component_dir / copied).relative_to(output))],
            }
        )
    return components


def write_summary(output: Path, components: list[dict[str, object]]) -> None:
    lines = [
        "PingYi bundled third-party licenses",
        "====================================",
        "",
        "Each component below is accompanied by its full license text and, when supplied",
        "by the upstream package, its third-party notice file.",
        "",
    ]
    for component in sorted(components, key=lambda item: str(item["name"]).lower()):
        lines.append(f"- {component['name']} {component['version']} ({component['kind']})")
        for path in component["licenseFiles"]:
            lines.append(f"  - {path}")
    (output / "README.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("target", type=Path, help="Published application directory")
    args = parser.parse_args()
    publish_dir = args.target.resolve()
    if not publish_dir.is_dir():
        parser.error(f"Published application directory does not exist: {publish_dir}")
    output = publish_dir / "licenses"
    reset_directory(output, publish_dir)
    complete = (publish_dir / "pingyi-complete.edition").is_file()
    components = collect_dotnet(publish_dir, output)
    components.append(collect_dotnet_runtime(publish_dir, output))
    components.extend(collect_python(output))
    components.extend(collect_native_runtimes(publish_dir, output))
    components.extend(collect_models(output, complete))
    manifest = {
        "schemaVersion": 1,
        "edition": "Complete" if complete else "Standard",
        "components": components,
    }
    (output / "manifest.json").write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    write_summary(output, components)
    print(f"Collected {len(components)} license entries under {output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
