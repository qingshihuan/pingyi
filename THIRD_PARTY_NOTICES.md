# Third-party notices

PingYi is licensed under MIT. Standard offline release packages additionally contain:

- PaddlePaddle PP-OCRv5 mobile detection and recognition ONNX models, licensed under Apache-2.0. Source: https://huggingface.co/PaddlePaddle
- Argos Translate runtime components, licensed under MIT. Source: https://github.com/argosopentech/argos-translate
- Chinese-English and English-Chinese Argos model packages derived from OPUS-MT. The model package README identifies the original model license as CC BY 4.0 and credits Jörg Tiedemann and Santhosh Thottingal, “OPUS-MT — Building open translation services for the World,” EAMT 2020.
- CTranslate2, licensed under MIT. Source: https://github.com/OpenNMT/CTranslate2
- SentencePiece and PaddleOCR, licensed under Apache-2.0; ONNX Runtime, Avalonia, SkiaSharp and their transitive runtime components under their respective licenses.

PingYi release builds intentionally exclude NVIDIA CUDA, cuDNN and related
proprietary GPU runtime binaries. The release dependency audit fails the build
if these files, an NVIDIA Python package, or a Torch runtime are detected.

PingYi Complete release packages additionally contain pinned official llama.cpp
CPU and Vulkan runtime binaries, licensed under MIT. Source and exact release:
https://github.com/ggml-org/llama.cpp/releases/tag/b10227. These binaries do not
include CUDA, cuDNN, ROCm, or model weights.

At the user's request, PingYi Complete can download the following model files
from pinned ModelScope revisions. They are not bundled in PingYi releases:

- Qwen3.5-2B GGUF conversions by Unsloth, based on Qwen3.5-2B, Apache-2.0.
  Source: https://modelscope.cn/models/unsloth/Qwen3.5-2B-GGUF
- Gemma 4 E2B GGUF conversion published by ggml-org, Apache-2.0 as reported by
  the model repository. Source: https://modelscope.cn/models/ggml-org/gemma-4-E2B-it-GGUF

The download catalog pins each repository revision, file size, and SHA-256 hash.
Users remain responsible for reviewing model terms and acceptable-use
requirements applicable to their jurisdiction and use case.

The upstream projects and authors retain all rights granted by their respective licenses. This notice does not replace the full upstream license texts.
