# PingYi configuration architecture (schema v15)

PingYi separates model services, the model catalog, task routing and runtime configuration. The legacy single-endpoint configuration from v0.8.4 and earlier is migrated into a compatibility service profile at load time. Existing model downloads and API keys are preserved.

## Configuration center

The new Configuration center supports:

- Multiple service profiles with independent names, types, endpoints, protocols, default model IDs, enabled states and declared capabilities.
- Presets for OpenAI, DeepSeek, SiliconFlow, OpenRouter, Alibaba Cloud Model Studio (DashScope) and Zhipu, plus manually configured local or compatible endpoints.
- Per-service `credentialRef` values stored in the OS secure store. Configuration files contain references, never key values. The migrated legacy profile points to the existing secret entry so upgrades do not lose credentials.
- Service tests query model-list endpoints and cache returned model IDs in a central catalog. Capabilities are declared by the user/service configuration; PingYi does not infer vision support from model names.
- Independent routes for OCR, translation, image description and prompt reconstruction. QR decoding is always local via ZXing.
- Explicit local fallback toggles for OCR (PaddleOCR) and translation (Argos). When disabled, failures are reported rather than silently switching services.

## Protocols and compatibility

- OpenAI profiles default to Responses API and can switch to Chat Completions.
- DeepSeek, SiliconFlow, OpenRouter, DashScope, Zhipu and local services default to Chat Completions.
- Local services must use loopback endpoints; remote services must use HTTPS.
- Responses vision requests use `input_image` and set `store:false`.
- Existing Vulkan/CUDA/ROCm/CPU backends and model installation remain available. Runtime configuration still uses the existing runtime page and compatibility fields.

## First-run setup

The first-run wizard now offers:
- Download and configure a local Basic model;
- Use Lightweight mode;
- Connect an existing local service (continue setup in Configuration center);
- Configure a third-party API Hybrid mode: local PaddleOCR for OCR and the selected cloud model for translation.

Cloud setup requires a user-supplied model ID and API Key. The key is stored in the OS secure store. Remote translation requires per-request confirmation by default.

## Data transfer and privacy

The Privacy & security page provides an enabled-by-default “Ask before every remote model request” setting. Remote translation sends recognized text; remote OCR, image description and prompt reconstruction send the selected screenshot. Providers may log, retain or bill for requests; review their policies. QR decoding always stays local.

## Schema v15 migration

- Legacy `CustomTranslationEndpoint`, model ID and protocol migrate to a `legacy-custom` service profile.
- The old API Key is not copied into JSON; the legacy profile's credential reference points to the existing OS secure-store entry.
- Legacy OCR and translation provider choices migrate to independent task routes.
- Legacy GPU backend, device, model package and mirror settings are mirrored into the new runtime profile while the original fields remain for existing runtime components.
- Tasks without a dedicated legacy service route to the compatibility profile; QR is migrated to `builtin:qr`.

## Scope note

This phase implements the configuration schema, migration, independent service profiles and credentials, model catalog cache, task routing, explicit OCR/translation fallbacks, configuration center, privacy confirmation and cloud first-run setup. GPU installer/device selection still uses the existing runtime page. Browser bridge and a few legacy settings entry points have not yet been fully migrated to the new routing model and need a separate follow-up and regression pass.
