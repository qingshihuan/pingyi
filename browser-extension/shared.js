export const defaults = Object.freeze({ sourceLanguage: "auto", targetLanguage: "zh", mode: "bilingual", hover: false, selection: true, edition: "standard" });
export function normalizePrefs(value = {}) {
  value = value && typeof value === "object" ? value : {};
  return {
    sourceLanguage: typeof value.sourceLanguage === "string" ? value.sourceLanguage : "auto",
    targetLanguage: typeof value.targetLanguage === "string" ? value.targetLanguage : "zh",
    mode: value.mode === "translation" ? "translation" : "bilingual",
    hover: value.hover === true, selection: value.selection !== false,
    edition: value.edition === "complete" ? "complete" : "standard"
  };
}
export function languageCode(code) {
  if (/^zh[-_](TW|HK|Hant)/i.test(code || "")) return "zh-Hant";
  return (code || "auto").split(/[-_]/)[0];
}
export function cropBounds(rect, width, height) {
  const values = [rect?.x, rect?.y, rect?.width, rect?.height, rect?.viewportWidth, rect?.viewportHeight];
  if (!values.every(Number.isFinite) || rect.width < 4 || rect.height < 4 || rect.viewportWidth <= 0 || rect.viewportHeight <= 0)
    throw new Error("invalid_region");
  const sx = width / rect.viewportWidth, sy = height / rect.viewportHeight;
  const x = Math.max(0, Math.floor(rect.x * sx)), y = Math.max(0, Math.floor(rect.y * sy));
  const right = Math.min(width, Math.ceil((rect.x + rect.width) * sx));
  const bottom = Math.min(height, Math.ceil((rect.y + rect.height) * sy));
  if (right <= x || bottom <= y || (right - x) * (bottom - y) > 16000000) throw new Error("invalid_region");
  return { x, y, width: right - x, height: bottom - y };
}
export const errors = {
  desktop_unavailable: "未连接到 截屏释义。请启动新版桌面端，并确认标准版 / 完全版选择正确。",
  native_unavailable: "尚未安装连接组件。请查看插件目录中的安装说明，注册 PingYi.BrowserHost。",
  request_timeout: "处理超时。请在 截屏释义 中检查模型是否就绪后重试。",
  provider_failed: "所选服务处理失败。请在 截屏释义 中检查模型、语言支持或 API 配置。",
  translation_unavailable: "翻译服务尚未就绪。请在 截屏释义 设置中检查模型和凭据。",
  ocr_unavailable: "OCR 尚未就绪。请在 截屏释义 中准备 OCR 模型或配置识别服务。",
  remote_text_consent_required: "此服务会发送文字到远程端，请先允许本次连接使用远程翻译。",
  remote_image_consent_required: "此 OCR 服务会上传所选图片，需要单独确认。",
  settings_changed: "截屏释义 的服务配置已变化，请重新连接后重试。",
  invalid_text: "请选择 1–12,000 字的文字。", text_too_long: "文字太长，请缩小选择范围。",
  no_text: "所选区域中未识别到文字。", invalid_image: "图片无效或过大，请缩小圈选区域。",
  invalid_region: "圈选范围无效，请在当前可见网页中重新框选。",
  busy: "已有翻译正在处理，请稍后重试。", tab_changed: "当前标签页已变化，请重新圈选。",
  restricted_page: "此页面不允许扩展读取。请在普通 HTTP / HTTPS 网页中使用。"
};
