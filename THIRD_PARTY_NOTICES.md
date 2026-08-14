# Third-party notices

PingYi is licensed under MIT. Standard offline release packages additionally contain:

- PaddlePaddle PP-OCRv5 mobile detection and recognition ONNX models, licensed under Apache-2.0. Source: https://huggingface.co/PaddlePaddle
- Argos Translate runtime components, licensed under MIT. Source: https://github.com/argosopentech/argos-translate
- Chinese-English and English-Chinese Argos model packages derived from OPUS-MT. The model package README identifies the original model license as CC BY 4.0 and credits Jörg Tiedemann and Santhosh Thottingal, “OPUS-MT — Building open translation services for the World,” EAMT 2020.
- CTranslate2, licensed under MIT. Source: https://github.com/OpenNMT/CTranslate2
- CTranslate2's x64 wheels statically link Intel oneMKL and oneDNN 3.1.1. Intel oneMKL is redistributed under the Intel Simplified Software License with its complete third-party notices; oneDNN is Apache-2.0 with its upstream third-party-programs file.
- SentencePiece and PaddleOCR, licensed under Apache-2.0; ONNX Runtime, Avalonia, SkiaSharp and their transitive runtime components under their respective licenses.

Depending on the target platform, the CTranslate2 engine bundles an OpenMP
runtime. GNU libgomp is GPLv3 with the GCC Runtime Library Exception 3.1;
LLVM OpenMP is Apache-2.0 with the LLVM exception; and the Intel OpenMP binary
in the official Windows wheel is redistributed under the Intel Developer Tools
End User License Agreement. Release license manifests distinguish these
runtimes and include the applicable full terms only when each binary is present.

PingYi release builds intentionally exclude NVIDIA CUDA, cuDNN and related
proprietary GPU runtime binaries. The release dependency audit fails the build
if these files, an NVIDIA Python package, or a Torch runtime are detected.

PingYi Complete release packages additionally contain pinned official llama.cpp
CPU and Vulkan runtime binaries, licensed under MIT. Source and exact release:
https://github.com/ggml-org/llama.cpp/releases/tag/b10227. These binaries do not
include CUDA, cuDNN, ROCm, or model weights.

The pinned llama.cpp packages may also include the LLVM OpenMP runtime required
by the server binaries. LLVM OpenMP is distributed under Apache-2.0 with the LLVM
exception. Complete release artifacts include both full texts in `licenses/`.
The llama.cpp payload does not duplicate GNU libgomp; Ubuntu packages declare
the system `libgomp1` runtime dependency instead.

At the user's request, PingYi Complete can download the following model files
from pinned ModelScope revisions. They are not bundled in PingYi releases:

- Qwen3.5-2B GGUF conversions by Unsloth, based on Qwen3.5-2B, Apache-2.0.
  Source: https://modelscope.cn/models/unsloth/Qwen3.5-2B-GGUF
- Gemma 4 E2B GGUF conversion published by ggml-org, Apache-2.0 as reported by
  the model repository. Source: https://modelscope.cn/models/ggml-org/gemma-4-E2B-it-GGUF

The download catalog pins each repository revision, file size, and SHA-256 hash.
Users remain responsible for reviewing model terms and acceptable-use
requirements applicable to their jurisdiction and use case.

PingYi can optionally call Google Cloud Vision API and Google Cloud Translation
Basic v2 with an API key supplied from the user's own Google Cloud project.
Google services and credentials are not bundled with PingYi. Their use remains
subject to Google's terms, project configuration, quotas, and billing. Source:
https://cloud.google.com/vision and https://cloud.google.com/translate

Every release archive and installer includes a `licenses/` directory. It contains
the complete license texts and upstream third-party notices collected from the
exact NuGet and Python packages used by that build, the self-contained .NET and
Python runtimes, bundled fonts and offline models, and llama.cpp in the Complete
edition, including its LLVM OpenMP dependency. `licenses/manifest.json` maps
each bundled component to its files, and the release audit fails if a required
text is absent or truncated.

The upstream projects and authors retain all rights granted by their respective
licenses. This summary does not replace the complete texts distributed in
`licenses/`.
