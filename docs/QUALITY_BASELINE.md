# PingYi v0.1 quality baseline

This baseline was run on 2026-08-02 before the first public release. It is a
repeatable regression gate, not a claim that every real-world screenshot will
reach the same score.

## OCR

The test renders five deterministic Simplified Chinese and English desktop UI
scenes, runs the bundled PP-OCRv5 mobile ONNX models, normalizes punctuation and
whitespace, and calculates Levenshtein similarity.

| Scene | Result | Required |
| --- | ---: | ---: |
| Clean bilingual text | 100.0% | 95% |
| Compact settings UI | 100.0% | 92% |
| Dark result card | 100.0% | 92% |
| Long mixed technical text | 97.5% | 90% |
| Small dark terminal with colored text | 98.7% | 90% |

Run the same gate with a prepared offline model directory:

```powershell
.\scripts\run-quality-baseline.ps1 `
  -ModelDirectory .\artifacts\publish\win-x64-0.1.0\offline-models
```

The regression test lives in
`tests/PingYi.Core.Tests/OcrQualityBaselineTests.cs`. The recognition pipeline
uses a dynamic input width for long text lines and normalizes dark-background
text before inference.

## Translation

Three short UI strings were compared with the bundled Argos zh-en/en-zh model
and a local Gemma-class Q6 model exposed through llama.cpp's Chat Completions
endpoint. The local LLM preserved the meaning of all three samples. Argos was
usable on two samples but mistranslated “update” and omitted part of one
sentence.

Product policy therefore remains:

- Argos is the installation-time, no-network CPU fallback.
- A user-configured llama.cpp, Ollama, LM Studio, vLLM, or compatible local
  endpoint is preferred when higher translation quality is needed.
- Local LLM acceleration belongs to that external service. PingYi does not
  bundle vendor GPU drivers or runtimes.

## Release dependency gate

`scripts/audit-release-dependencies.py` rejects NVIDIA, CUDA, cuDNN and related
proprietary GPU runtime binaries, unexpected NVIDIA Python packages, and a
bundled Torch runtime. It runs after the standalone engine is built and again
before each release archive is created.

The standard package intentionally contains the CPU ONNX Runtime and a CPU-only
Argos path. GPU-enabled local LLM services remain optional external programs.
