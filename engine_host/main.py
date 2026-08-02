from __future__ import annotations

import contextlib
import hashlib
import importlib.util
import json
import os
import re
import shutil
import socket
import sys
import traceback
from pathlib import Path
from typing import Any

import runtime_minimal  # noqa: F401 - installs optional-dependency shims


MODEL_DIR = Path(
    os.environ.get(
        "PINGYI_MODEL_DIR",
        Path.home() / ".local" / "share" / "pingyi" / "models",
    )
)
_bundled_model_value = os.environ.get("PINGYI_BUNDLED_MODEL_DIR")
BUNDLED_MODEL_DIR = (
    Path(_bundled_model_value)
    if _bundled_model_value
    else MODEL_DIR / "__no_bundled_models__"
)
MODEL_DIR.mkdir(parents=True, exist_ok=True)


def translation_files_present(root: Path) -> bool:
    return (
        any(root.glob("argos/translate-zh_en-*/model/model.bin"))
        and any(root.glob("argos/translate-en_zh-*/model/model.bin"))
    )


ACTIVE_MODEL_DIR = (
    MODEL_DIR
    if translation_files_present(MODEL_DIR)
    else BUNDLED_MODEL_DIR
    if BUNDLED_MODEL_DIR.is_dir() and translation_files_present(BUNDLED_MODEL_DIR)
    else MODEL_DIR
)
os.environ.setdefault("ARGOS_PACKAGES_DIR", str(ACTIVE_MODEL_DIR / "argos"))
os.environ["ARGOS_CHUNK_TYPE"] = "MINISBD"
os.environ.setdefault("ARGOS_DEVICE_TYPE", "cpu")

_hash_cache: dict[Path, tuple[int, int, str]] = {}


@contextlib.contextmanager
def local_only_network_guard():
    """Fail closed if an inference dependency unexpectedly tries to connect."""
    original_connect = socket.socket.connect
    original_connect_ex = socket.socket.connect_ex
    original_create_connection = socket.create_connection

    def blocked_connect(*_: Any, **__: Any):
        raise RuntimeError("本地模式已阻止意外网络请求。")

    socket.socket.connect = blocked_connect
    socket.socket.connect_ex = blocked_connect
    socket.create_connection = blocked_connect
    try:
        yield
    finally:
        socket.socket.connect = original_connect
        socket.socket.connect_ex = original_connect_ex
        socket.create_connection = original_create_connection


def module_available(name: str) -> bool:
    return importlib.util.find_spec(name) is not None


def sha256_file(path: Path) -> str:
    stat = path.stat()
    cached = _hash_cache.get(path)
    if cached is not None and cached[0] == stat.st_size and cached[1] == stat.st_mtime_ns:
        return cached[2]
    digest = hashlib.sha256(path.read_bytes()).hexdigest()
    _hash_cache[path] = (stat.st_size, stat.st_mtime_ns, digest)
    return digest


def find_argos_model_file(root: Path, source_code: str, target_code: str) -> Path:
    candidates = sorted(
        (root / "argos").glob(f"translate-{source_code}_{target_code}-*/model/model.bin")
    )
    return candidates[-1] if candidates else root / "missing"


def verify_translation_manifest(root: Path) -> bool:
    manifest_path = root / "translation-models.json"
    try:
        payload = json.loads(manifest_path.read_text(encoding="utf-8"))
        expected = payload["sha256"]
        files = {
            "argos-installed-zh-en": find_argos_model_file(root, "zh", "en"),
            "argos-installed-en-zh": find_argos_model_file(root, "en", "zh"),
        }
        return all(
            path.is_file() and sha256_file(path) == expected[key]
            for key, path in files.items()
        )
    except (OSError, KeyError, TypeError, ValueError, json.JSONDecodeError):
        return False


def installed_argos_pairs() -> set[tuple[str, str]]:
    if not module_available("argostranslate"):
        return set()
    import argostranslate.translate

    pairs: set[tuple[str, str]] = set()
    languages = argostranslate.translate.get_installed_languages()
    for source in languages:
        for target in languages:
            if source.code == target.code:
                continue
            try:
                if source.get_translation(target) is not None:
                    pairs.add((source.code, target.code))
            except Exception:
                pass
    return pairs


class SimpleSentencizer:
    """Small offline splitter for screenshot text; avoids heavyweight NLP runtimes."""

    _boundary = re.compile(r"(?<=[。！？!?；;\.])(?:\s+|(?=\S))")
    _max_chars = 220

    def split_sentences(self, text: str) -> list[str]:
        pieces = [piece.strip() for piece in self._boundary.split(text) if piece.strip()]
        chunks: list[str] = []
        for piece in pieces or [text]:
            remaining = piece
            while len(remaining) > self._max_chars:
                cut = max(
                    remaining.rfind(" ", self._max_chars // 2, self._max_chars + 1),
                    remaining.rfind("，", self._max_chars // 2, self._max_chars + 1),
                    remaining.rfind(",", self._max_chars // 2, self._max_chars + 1),
                )
                if cut < self._max_chars // 2:
                    cut = self._max_chars
                chunks.append(remaining[: cut + 1].strip())
                remaining = remaining[cut + 1 :].strip()
            if remaining:
                chunks.append(remaining)
        return chunks or [text]


def use_simple_sentencizer(translation: Any) -> None:
    pending = [translation]
    visited: set[int] = set()
    while pending:
        current = pending.pop()
        if id(current) in visited:
            continue
        visited.add(id(current))
        if hasattr(current, "sentencizer"):
            current.sentencizer = SimpleSentencizer()
        for attribute in ("underlying", "t1", "t2"):
            nested = getattr(current, attribute, None)
            if nested is not None:
                pending.append(nested)


def configure_unicode_safe_sentencepiece() -> None:
    """Load tokenizer bytes directly so portable installs also work in Unicode paths."""
    import argostranslate.tokenizer
    import sentencepiece

    tokenizer_type = argostranslate.tokenizer.SentencePieceTokenizer
    if getattr(tokenizer_type, "_pingyi_unicode_safe", False):
        return

    def lazy_processor(instance: Any):
        if instance.processor is None:
            processor = sentencepiece.SentencePieceProcessor()
            processor.LoadFromSerializedProto(Path(instance.model_file).read_bytes())
            instance.processor = processor
        return instance.processor

    tokenizer_type.lazy_processor = lazy_processor
    tokenizer_type._pingyi_unicode_safe = True


def health(_: dict[str, Any]) -> dict[str, Any]:
    pairs = installed_argos_pairs()
    ready = (
        ("zh", "en") in pairs
        and ("en", "zh") in pairs
        and verify_translation_manifest(ACTIVE_MODEL_DIR)
    )
    return {
        "paddleocr": False,
        "ocrModelsReady": False,
        "argos": module_available("argostranslate"),
        "translationModelsReady": ready,
        "sentenceModelsReady": True,
        "modelDirectory": str(ACTIVE_MODEL_DIR),
        "usingBundledModels": ACTIVE_MODEL_DIR == BUNDLED_MODEL_DIR,
    }


def translate(params: dict[str, Any]) -> dict[str, Any]:
    if not module_available("argostranslate"):
        raise RuntimeError("未安装 Argos Translate 离线引擎。")
    if not verify_translation_manifest(ACTIVE_MODEL_DIR):
        raise RuntimeError("离线翻译模型缺失或校验失败，请重新安装标准离线版。")
    import argostranslate.translate
    configure_unicode_safe_sentencepiece()

    source_code = params["sourceLanguage"]
    target_code = params["targetLanguage"]
    languages = argostranslate.translate.get_installed_languages()
    source = next((language for language in languages if language.code == source_code), None)
    target = next((language for language in languages if language.code == target_code), None)
    if source is None or target is None:
        raise RuntimeError(f"离线翻译不支持 {source_code}→{target_code}。")
    translation = source.get_translation(target)
    if translation is None:
        raise RuntimeError(f"尚未安装 {source_code}→{target_code} 离线翻译模型。")
    use_simple_sentencizer(translation)
    with local_only_network_guard():
        result = translation.translate(params["text"])
    return {"text": result}


def install_translation_models(_: dict[str, Any]) -> dict[str, Any]:
    if verify_translation_manifest(ACTIVE_MODEL_DIR):
        source = "bundled" if ACTIVE_MODEL_DIR == BUNDLED_MODEL_DIR else "installed"
        return {"installed": ["zh-en", "en-zh"], "source": source}
    if not module_available("argostranslate"):
        raise RuntimeError("未安装 Argos Translate 离线引擎。")

    os.environ["ARGOS_PACKAGES_DIR"] = str(MODEL_DIR / "argos")
    import argostranslate.package

    argostranslate.package.update_package_index()
    available = argostranslate.package.get_available_packages()
    installed: list[str] = []
    checksums: dict[str, str] = {}
    for source_code, target_code in (("zh", "en"), ("en", "zh")):
        package = next(
            (
                item
                for item in available
                if item.from_code == source_code and item.to_code == target_code
            ),
            None,
        )
        if package is None:
            raise RuntimeError(f"找不到 {source_code}→{target_code} Argos 模型包。")
        download_path = package.download()
        argostranslate.package.install_from_path(download_path)
        installed_model = find_argos_model_file(MODEL_DIR, source_code, target_code)
        checksums[f"argos-installed-{source_code}-{target_code}"] = sha256_file(
            installed_model
        )
        installed.append(f"{source_code}-{target_code}")

    (MODEL_DIR / "translation-models.json").write_text(
        json.dumps({"schemaVersion": 1, "sha256": checksums}, ensure_ascii=False, indent=2),
        encoding="utf-8",
    )
    return {"installed": installed, "source": "download"}


def delete_models(params: dict[str, Any]) -> dict[str, Any]:
    scope = str(params.get("scope", "all"))
    if scope not in {"all", "translation"}:
        return {"deleted": []}
    shutil.rmtree(MODEL_DIR / "argos", ignore_errors=True)
    (MODEL_DIR / "translation-models.json").unlink(missing_ok=True)
    return {
        "deleted": ["translation"],
        "bundledFallbackAvailable": verify_translation_manifest(BUNDLED_MODEL_DIR)
        if BUNDLED_MODEL_DIR.is_dir()
        else False,
    }


METHODS = {
    "health": health,
    "translate": translate,
    "install_translation_models": install_translation_models,
    "delete_models": delete_models,
}


def write_response(payload: dict[str, Any]) -> None:
    sys.stdout.write(json.dumps(payload, ensure_ascii=False, separators=(",", ":")) + "\n")
    sys.stdout.flush()


def main() -> int:
    for line in sys.stdin:
        request_id = -1
        try:
            request = json.loads(line)
            request_id = int(request.get("id", -1))
            method = request.get("method")
            if method == "shutdown":
                write_response({"id": request_id, "result": {"ok": True}, "error": None})
                return 0
            if method not in METHODS:
                raise ValueError(f"未知方法：{method}")
            result = METHODS[method](request.get("params") or {})
            write_response({"id": request_id, "result": result, "error": None})
        except Exception as exc:
            traceback.print_exc(file=sys.stderr)
            write_response(
                {
                    "id": request_id,
                    "result": None,
                    "error": {"code": type(exc).__name__, "message": str(exc)},
                }
            )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
