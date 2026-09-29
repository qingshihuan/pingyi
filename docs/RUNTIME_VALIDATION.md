# Runtime change validation

## Integration evidence

The hardware-aware runtime and inference changes were integrated in commit `cb26d5c73e699771029e05a4c834d80a14cab780`, after both platform jobs and their upstream CPU-package smoke tests passed:

https://github.com/qingshihuan/pingyi/actions/runs/36517731684

This run uses Bash with fail-fast behavior on Windows and Linux; a failed native test command cannot be hidden by a later successful command. The previous integration attempt's Windows job used a multi-command PowerShell step and masked two .NET failures. That earlier green job is **not acceptance evidence**. The two failures exposed missing `InvalidDataException` handling in corrupt-file/source rejection; those branches now fail over without accepting invalid bytes. The original assertions were retained.

Earlier compiler feedback also caught two span-based comparisons crossing an `await`; comparisons were corrected without removing digest checks.

The successful integration includes the complete .NET solution, 29 Python engine tests, 35 script tests, and real official CPU-runtime package checks on Windows and Ubuntu. Those checks download the pinned upstream package, validate its length and SHA-256, safely extract it, run `--version`, commit installation metadata, verify lookup, and repeat installation from the verified cache. They use no model weights and no user content.

The exact integrated source was exported as a tracked-source artifact for comparison. One-use source-export and integration workflows/scripts were removed from the resulting tree; they are not production launch or download components.

## Reproduction

Ordinary tests remain offline:

```sh
dotnet test PingYi.slnx -c Release
python -m unittest discover -s engine_host -p 'test_*.py'
python -m unittest discover -s scripts -p 'test_*.py'
```

The optional **Verified runtime package smoke** workflow explicitly exercises the upstream CPU package. Its test is gated by `PINGYI_RUNTIME_PACKAGE_SMOKE=1`; ordinary test execution does not download runtimes. The workflow contains read-only permissions and no third-party relays.

The normal repository CI additionally runs the isolated Xvfb/Openbox screenshot, portal and quit-flow regressions. Final PR readiness must refer to the actual final head CI, not only an earlier integrated tree.

## Boundaries

Hardware-policy and multi-GPU selection tests use authored device inventories. The UI snapshots label the synthetic GPUs. Successful CPU archive installation is not NVIDIA CUDA/AMD ROCm execution certification. Real GPU throughput, multi-GPU identity after hardware changes, physical VRAM recovery and mainland network availability are not claimed. Optional relays are transport candidates, not guaranteed reachable mirrors or hash authorities.

No installer, software release version, model weights, production dependencies or existing Release assets are changed by this PR. Runtime packages downloaded later retain their own upstream licenses; normal application use remains offline after setup.
