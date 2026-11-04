# 后端下载来源与版本核对

核对日期：2026-11-04（北京时间）。这是当日检查结果，后续官方版本可能继续更新。

## 在线下载

程序通过 [llama.cpp 官方最新发布接口](https://api.github.com/repos/ggml-org/llama.cpp/releases/latest) 获取最新稳定版，并读取发布说明指向的官方二进制构建。此次核对为 [v0.9.0](https://github.com/ggml-org/llama.cpp/releases/tag/v0.9.0)，对应 [b12110](https://github.com/ggml-org/llama.cpp/releases/tag/b12110)。

| 后端 | Windows x64 主运行包 | Ubuntu x64 主运行包 |
| --- | --- | --- |
| CPU | [b12110 CPU](https://github.com/ggml-org/llama.cpp/releases/download/b12110/llama-b12110-bin-win-cpu-x64.zip) | [b12110 CPU](https://github.com/ggml-org/llama.cpp/releases/download/b12110/llama-b12110-bin-ubuntu-x64.tar.gz) |
| Vulkan | [b12110 Vulkan](https://github.com/ggml-org/llama.cpp/releases/download/b12110/llama-b12110-bin-win-vulkan-x64.zip) | [b12110 Vulkan](https://github.com/ggml-org/llama.cpp/releases/download/b12110/llama-b12110-bin-ubuntu-vulkan-x64.tar.gz) |
| CUDA 12 | [b12110 CUDA 12.4](https://github.com/ggml-org/llama.cpp/releases/download/b12110/llama-b12110-bin-win-cuda-12.4-x64.zip) | [b12110 CUDA 12.8](https://github.com/ggml-org/llama.cpp/releases/download/b12110/llama-b12110-bin-ubuntu-cuda-12.8-x64.tar.gz) |
| CUDA 13 | [b12110 CUDA 13.4](https://github.com/ggml-org/llama.cpp/releases/download/b12110/llama-b12110-bin-win-cuda-13.4-x64.zip) | [b12110 CUDA 13.4](https://github.com/ggml-org/llama.cpp/releases/download/b12110/llama-b12110-bin-ubuntu-cuda-13.4-x64.tar.gz) |
| ROCm | [b12110 ROCm 10.0](https://github.com/ggml-org/llama.cpp/releases/download/b12110/llama-b12110-bin-win-rocm-10.0-x64.zip) | [b12110 ROCm 10.0](https://github.com/ggml-org/llama.cpp/releases/download/b12110/llama-b12110-bin-ubuntu-rocm-10.0-x64.tar.gz) |

CUDA 还需同一发布下匹配平台和 CUDA 小版本的 `cudart` 文件。程序会同时选取并检查依赖，不能只下载主运行包：

- [Windows CUDA 12.4 依赖](https://github.com/ggml-org/llama.cpp/releases/download/b12110/cudart-llama-bin-win-cuda-12.4-x64.zip)
- [Windows CUDA 13.4 依赖](https://github.com/ggml-org/llama.cpp/releases/download/b12110/cudart-llama-bin-win-cuda-13.4-x64.zip)
- [Ubuntu CUDA 12.8 依赖](https://github.com/ggml-org/llama.cpp/releases/download/b12110/cudart-llama-b12110-bin-ubuntu-cuda-12.8-x64.tar.gz)
- [Ubuntu CUDA 13.4 依赖](https://github.com/ggml-org/llama.cpp/releases/download/b12110/cudart-llama-b12110-bin-ubuntu-cuda-13.4-x64.tar.gz)

以上 14 个下载链接均返回 HTTP 200，文件长度与官方资产信息一致；Ubuntu CUDA 13 首次网络请求失败，重试通过。使用官方响应执行程序的版本解析，10 种平台/后端组合均解析到 b12110，并验证官方 URL、文件大小与 SHA-256 元数据。此次没有下载和运行全部 CUDA/ROCm 文件，链接检查不等于本机驱动兼容性验证。

## 内置版本与备用清单

安装包内固定的 CPU/Vulkan 及 `RuntimeCatalog.Pinned` 备用清单仍为 **b11146**（对应当时 v0.5.0）。这些属于固定恢复组件，不应称为最新版本。在线查询失败时界面会明确提示使用内置可信清单。

“一键检测并自动配置”优先复用已安装后端，缺失时才准备下载；它不强制替换已工作的运行包。“下载安装／更新后端”会查询上面的最新稳定版本。第三方备用源只传输公开文件，校验值仍取自官方或固定可信清单，不从备用源获取新的信任信息。
