# Screen Insight · Remotion source

Two independent Chinese compositions for desktop v0.7.0: `ProductIntro` (60 seconds) and `FirstRunGuide` (110 seconds), 1920×1080 at 30 fps. Visuals use actual Avalonia UI previews, original motion graphics, burned-in captions and an original synthesized instrumental score. There is no voiceover or third-party video footage.

## Reproduce

Install Node.js 22+, .NET 10, Python 3 and FFmpeg. Install a Chinese sans-serif font such as Noto Sans CJK SC on the rendering machine; do not copy font binaries into this repository. The desktop executable is not needed for rendering once the PNG previews are available.

```bash
# Optional: regenerate genuine UI previews, with no live model calls.
PINGYI_VIDEO_ASSETS="$PWD/media/remotion/public" \
  dotnet test tests/PingYi.App.Tests/PingYi.App.Tests.csproj -c Release \
  --filter FullyQualifiedName~VideoPreviewTests
cp src/PingYi.App/Assets/screen-insight-icon-512.png media/remotion/public/icon.png
python media/remotion/tools/assets.py prepare
cd media/remotion
npm ci
npm run typecheck
npm run studio
```

The preview server prints the Studio URL. Both compositions can be selected on its timeline. For output, from this directory:

```bash
npm run render:promo
npm run render:setup
npx remotion still src/index.tsx ProductIntro ../../docs/videos/intro-poster.jpg --frame=90
npx remotion still src/index.tsx FirstRunGuide ../../docs/videos/setup-poster.jpg --frame=90
python tools/assets.py publish
```

`src/storyboard.json` owns timing, captions and content. `src/index.tsx` owns scene composition, highlights, cursor motion and visual identity. `tools/assets.py` generates the original score, sidecar captions and validated media manifest; its publication phase updates the two README video sections idempotently. A valid encoded file, audio track, dimensions, duration and frame count are checked before a render is published.

The `Remotion documentation videos` workflow can regenerate artifacts. Its only automatic write target is the dedicated `docs/remotion-product-videos` branch, never main or a release tag. After review, merge the documentation PR. Manual renders on other branches only upload Actions artifacts.

## Product truthfulness

The initial-setup screen is a real control rendered with `hasRuntime=true`, matching the Complete package state; no download is performed for filming. Main-window labels contain explicit synthetic presentation data. The example translation, mountain graphic and reference prompt illustrate the workflow and are not claimed as measured model output. QR decoding is not attributed to the language model. Real cloud usage retains its existing consent boundaries.

Remotion's own license applies to the rendering dependencies. The project-authored source and media use the repository license. See ../../docs/videos/SOURCES.md. No fonts, node_modules, private screenshots, weights or desktop release packages are stored here.
