import { defaults, normalizePrefs, languageCode, cropBounds, errors } from "./shared.js";

const hostName = "com.pingyi.browser";
let activeRequests = 0;
async function prefs() { return normalizePrefs((await chrome.storage.local.get("preferences")).preferences); }
function native(request) {
  return new Promise((resolve, reject) => {
    const port = chrome.runtime.connectNative(hostName);
    let finished = false;
    const finish = (value, error) => {
      if (finished) return;
      finished = true; clearTimeout(timer); port.disconnect();
      error ? reject(new Error(error)) : resolve(value);
    };
    const timer = setTimeout(() => finish(null, "request_timeout"), request.operation === "status" ? 7000 : 295000);
    port.onMessage.addListener(result => finish(result));
    port.onDisconnect.addListener(() => { void chrome.runtime.lastError; finish(null, "native_unavailable"); });
    port.postMessage(request);
  });
}
async function status(p) {
  const response = await native({ operation: "status", edition: p.edition });
  if (!response.ok) throw new Error(response.error);
  return response.status;
}
async function activeTab() {
  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  if (!tab?.id || !/^https?:\/\//.test(tab.url || "")) throw new Error("restricted_page");
  return tab;
}
async function tabAction(action, tab = null, extra = {}) {
  tab ||= await activeTab();
  try { await chrome.tabs.sendMessage(tab.id, { action: "ping" }); }
  catch { await chrome.scripting.executeScript({ target: { tabId: tab.id }, files: ["content.js"] }); }
  return chrome.tabs.sendMessage(tab.id, { action, ...extra });
}
async function translate(message, sender, p) {
  if (activeRequests >= 2) throw new Error("busy");
  activeRequests++;
  try {
    const s = await status(p);
    if (message.routeKey !== s.routeKey) throw new Error("settings_changed");
    let image;
    if (message.type === "region") {
      if (!sender.tab || sender.frameId !== 0) throw new Error("restricted_page");
      const [tab] = await chrome.tabs.query({ active: true, windowId: sender.tab.windowId });
      if (tab?.id !== sender.tab.id) throw new Error("tab_changed");
      const capture = await chrome.tabs.captureVisibleTab(sender.tab.windowId, { format: "png" });
      const [after] = await chrome.tabs.query({ active: true, windowId: sender.tab.windowId });
      if (after?.id !== sender.tab.id) throw new Error("tab_changed");
      // Crop in memory before sending anything to 截屏释义, including cloud OCR.
      const bytes = Uint8Array.from(atob(capture.split(",")[1]), c => c.charCodeAt(0));
      const bitmap = await createImageBitmap(new Blob([bytes], { type: "image/png" }));
      try {
        const r = cropBounds(message.rect, bitmap.width, bitmap.height);
        const canvas = new OffscreenCanvas(r.width, r.height);
        canvas.getContext("2d").drawImage(bitmap, r.x, r.y, r.width, r.height, 0, 0, r.width, r.height);
        const png = new Uint8Array(await (await canvas.convertToBlob({ type: "image/png" })).arrayBuffer());
        if (png.length > 8 * 1024 * 1024) throw new Error("invalid_image");
        let binary = "";
        for (let i = 0; i < png.length; i += 32768) binary += String.fromCharCode(...png.subarray(i, i + 32768));
        image = btoa(binary);
      } finally { bitmap.close(); }
      await chrome.tabs.sendMessage(sender.tab.id, { action: "region-captured", captureId: message.captureId });
    }
    let detectedLanguage = "auto";
    if (p.sourceLanguage === "auto" && typeof message.text === "string") {
      const detected = await chrome.i18n.detectLanguage(message.text);
      if (detected.isReliable && detected.languages[0]?.percentage >= 60)
        detectedLanguage = languageCode(detected.languages[0].language);
    }
    return await native({ operation: image ? "ocr" : "translate", edition: p.edition,
      text: image ? undefined : message.text, image, sourceLanguage: p.sourceLanguage,
      targetLanguage: p.targetLanguage, detectedLanguage, routeKey: s.routeKey,
      allowRemoteText: message.allowRemoteText === true, allowRemoteImage: message.allowRemoteImage === true });
  } finally { activeRequests--; }
}
chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (sender.id !== chrome.runtime.id) return false;
  const isPopup = sender.url === chrome.runtime.getURL("popup.html");
  const isPage = sender.tab && sender.frameId === 0 && /^https?:\/\//.test(sender.url || "");
  if (!isPopup && !isPage) return false;
  (async () => {
    const p = await prefs();
    switch (message.type) {
      case "status": return { ok: true, status: await status(p), preferences: p };
      case "preferences": return { ok: true, preferences: p };
      case "save":
        if (!isPopup) throw new Error("restricted_page");
        await chrome.storage.local.set({ preferences: normalizePrefs(message.preferences) });
        return { ok: true };
      case "action":
        if (!isPopup || !["page", "restore", "region", "selection"].includes(message.action)) throw new Error("restricted_page");
        await tabAction(message.action); return { ok: true };
      case "translate":
        if (typeof message.text !== "string" || !message.text.trim() || message.text.length > 12000) throw new Error("invalid_text");
        return translate(message, sender, p);
      case "region": return translate(message, sender, p);
      default: throw new Error("unsupported_operation");
    }
  })().then(result => sendResponse(result), error => sendResponse({ ok: false,
    error: errors[error.message] ? error.message : "restricted_page", message: errors[error.message] || errors.restricted_page }));
  return true;
});
chrome.runtime.onInstalled.addListener(async () => {
  const existing = await chrome.storage.local.get("preferences");
  if (!existing.preferences) await chrome.storage.local.set({ preferences: defaults });
  await chrome.contextMenus.removeAll();
  chrome.contextMenus.create({ id: "pingyi-selection", title: "使用 截屏释义 翻译选中文字", contexts: ["selection"], documentUrlPatterns: ["http://*/*", "https://*/*"] });
});
chrome.contextMenus.onClicked.addListener((info, tab) => {
  if (info.menuItemId === "pingyi-selection" && tab) tabAction("selection", tab, { text: info.selectionText }).catch(() => {});
});
chrome.commands.onCommand.addListener(command => {
  tabAction(command === "translate-region" ? "region" : "page").catch(() => {
    chrome.action.setBadgeText({ text: "!" });
    chrome.action.setTitle({ title: "此页无法翻译，请打开 截屏释义 插件面板查看。" });
  });
});
