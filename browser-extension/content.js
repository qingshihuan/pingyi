(() => {
  if (globalThis.__pingyiInstalled) return;
  globalThis.__pingyiInstalled = true;
  let preferences = { mode: "bilingual", hover: false, selection: true };
  let panelVersion = 0, pageVersion = 0, pageRunning = false, hoverTimer, lastHover;
  let consentKey = "", regionCleanup, onRegionCaptured;
  const replacements = [], owned = new WeakSet();
  const excluded = "script,style,noscript,textarea,input,select,button,code,pre,svg,canvas,iframe,[contenteditable],[translate=no],.notranslate,[aria-hidden=true]";
  const blocks = "p,h1,h2,h3,h4,h5,h6,li,blockquote,figcaption,td,th,dt,dd,div,article,section";
  const host = document.createElement("div");
  owned.add(host);
  host.style.cssText = "all:initial!important;position:fixed!important;z-index:2147483647!important;left:0!important;top:0!important;";
  const root = host.attachShadow({ mode: "closed" });
  const style = document.createElement("style");
  style.textContent = `:host{all:initial;color-scheme:light dark;--py-bg:#FFFFFF;--py-subtle:#F0F1F4;--py-hover:#E8EFFA;--py-border:#E0E2E8;--py-text:#1D1D1F;--py-muted:#62646C;--py-brand:#0865D9;--py-action:#0765DB;--py-ring:#408AEE}
@media(prefers-color-scheme:dark){:host{--py-bg:#23252B;--py-subtle:#2D3038;--py-hover:#343D4E;--py-border:#3D404A;--py-text:#F2F2F7;--py-muted:#AFB2BE;--py-brand:#75ADFF}}
*{box-sizing:border-box}button{cursor:pointer;font:13px system-ui;color:var(--py-text);background:var(--py-subtle);border:1px solid var(--py-border);border-radius:9px;padding:7px 12px;min-height:34px}button:hover{background:var(--py-hover)}button:focus-visible{outline:2px solid var(--py-ring);outline-offset:2px}.card{position:fixed;width:min(380px,calc(100vw - 24px));max-height:min(480px,calc(100vh - 24px));overflow:auto;background:var(--py-bg);border:1px solid var(--py-border);border-radius:16px;box-shadow:0 16px 60px #0003;color:var(--py-text);padding:16px;font:14px/1.65 system-ui,'Microsoft YaHei',sans-serif;text-align:left;white-space:normal}.head{display:flex;justify-content:space-between;align-items:center;margin-bottom:10px;gap:12px}.title{font-size:12px;color:var(--py-brand);font-weight:600}.close{padding:2px 8px;font-size:19px;min-height:30px}.original{color:var(--py-muted);border-bottom:1px solid var(--py-border);padding-bottom:12px;margin-bottom:12px;font-size:13px}.body,.original{white-space:pre-wrap;overflow-wrap:anywhere}.actions{display:flex;gap:8px;margin-top:14px}.float{position:fixed;background:var(--py-action);color:#fff;border-color:var(--py-ring);box-shadow:0 4px 20px #0003;font-weight:600}.float:hover{background:#0756BB}.overlay{position:fixed;inset:0;cursor:crosshair;background:#0002;touch-action:none}.hint{position:absolute;top:18px;left:50%;transform:translateX(-50%);background:var(--py-bg);color:var(--py-text);padding:10px 20px;border:1px solid var(--py-action);border-radius:10px;font:14px system-ui;white-space:nowrap}.rect{position:absolute;border:2px solid var(--py-ring);background:#0765db19;box-shadow:0 0 0 9999px #0003;pointer-events:none}`;
  root.append(style);
  const mount = () => { if (!host.isConnected) document.documentElement.append(host); };
  function element(tag, className, text) {
    const node = document.createElement(tag); if (className) node.className = className;
    if (text !== undefined) node.textContent = text; return node;
  }
  function closePanel() { panelVersion++; root.querySelectorAll(".card,.float").forEach(n => n.remove()); }
  function position(node, x, y) {
    node.style.left = `${Math.max(12, Math.min(x, innerWidth - Math.min(380, innerWidth - 24) - 12))}px`;
    node.style.top = `${Math.max(12, Math.min(y + 12, innerHeight - 200))}px`;
    requestAnimationFrame(() => {
      const rect = node.getBoundingClientRect();
      if (rect.bottom > innerHeight - 12) node.style.top = `${Math.max(12, innerHeight - rect.height - 12)}px`;
    });
  }
  function card(title, text, x = innerWidth - 400, y = 12) {
    closePanel(); mount();
    const node = element("section", "card"); node.setAttribute("role", "dialog"); node.setAttribute("aria-label", title);
    const head = element("div", "head"), caption = element("span", "title", title), close = element("button", "close", "×");
    close.setAttribute("aria-label", "关闭翻译"); close.onclick = closePanel;
    head.append(caption, close); const body = element("div", "body", text); body.dir = "auto"; body.setAttribute("role", "status");
    node.append(head, body); root.append(node); position(node, x, y);
    return { node, body, caption, version: panelVersion };
  }
  async function send(message) {
    const r = await chrome.runtime.sendMessage(message);
    if (!r?.ok) throw new Error(r?.message || ({
      desktop_unavailable: "未连接到 截屏释义。请启动新版桌面端并选择正确版本。",
      provider_failed: "翻译失败，请在 截屏释义 中检查服务、模型与语言支持。",
      translation_unavailable: "翻译服务未就绪，请在 截屏释义 设置中检查模型或 API。",
      ocr_unavailable: "OCR 未就绪，请在 截屏释义 中准备识别模型。",
      settings_changed: "服务配置已变化，请重新翻译并确认。",
      no_text: "所选区域未识别到文字。", invalid_text: "请选择 1–12,000 字的文字。",
      invalid_image: "图片过大或无效，请缩小圈选范围。", invalid_language: "当前语言选择无效，请在插件面板重新选择。"
    }[r?.error] || "处理失败，请在插件面板检查连接后重试。"));
    return r;
  }
  async function authorize(image = false) {
    const r = await send({ type: "status" }); preferences = r.preferences;
    const s = r.status;
    if (s.remoteText && consentKey !== s.routeKey) {
      if (!confirm(`截屏释义 将把本次网页操作的文字发送到「${s.translationProvider}」翻译。是否允许？\n配置变化或页面刷新后会再次询问。`)) throw new Error("已取消远程翻译。");
      consentKey = s.routeKey;
    }
    if (image && s.remoteImage && !confirm(`本次圈选图片将发送到「${s.ocrProvider}」进行 OCR。是否允许上传这张图片？`))
      throw new Error("已取消图片上传。");
    return { routeKey: s.routeKey, allowRemoteText: s.remoteText, allowRemoteImage: image && s.remoteImage };
  }
  function addCopy(panel, text) {
    const actions = element("div", "actions"), copy = element("button", "", "复制译文");
    copy.onclick = async () => { try { await navigator.clipboard.writeText(text); copy.textContent = "已复制"; }
      catch { copy.textContent = "复制失败，请手动选择"; } };
    actions.append(copy); panel.node.append(actions);
  }
  async function showTranslation(text, x, y) {
    if (!text?.trim()) { card("截屏释义", "请先选中网页文字。", x, y); return; }
    const panel = card("截屏释义 · 正在翻译", "正在连接本机服务…", x, y);
    try {
      const authorization = await authorize();
      if (panel.version !== panelVersion) return;
      const r = await send({ type: "translate", text: text.trim(), ...authorization });
      if (panel.version !== panelVersion) return;
      panel.caption.textContent = `截屏释义 · ${r.sourceLanguage} → ${r.targetLanguage}`;
      if (preferences.mode === "bilingual") {
        const original = element("div", "original", r.original); original.dir = "auto"; panel.node.insertBefore(original, panel.body);
      }
      panel.body.textContent = r.translation; addCopy(panel, r.translation); position(panel.node, x, y);
    } catch (e) { if (panel.version === panelVersion) { panel.caption.textContent = "截屏释义 · 提示"; panel.body.textContent = e.message; } }
  }
  const visible = node => { const r = node.getBoundingClientRect(); return r.width > 0 && r.height > 0 && getComputedStyle(node).visibility !== "hidden"; };
  function textNodes(node) {
    const result = [], walker = document.createTreeWalker(node, NodeFilter.SHOW_TEXT, {
      acceptNode(n) {
        const p = n.parentElement;
        return p && !p.closest(excluded) && !owned.has(p) && visible(p) && n.data.trim()
          ? NodeFilter.FILTER_ACCEPT : NodeFilter.FILTER_REJECT;
      }
    });
    while (walker.nextNode()) result.push(walker.currentNode);
    return result;
  }
  function restore() {
    pageVersion++; pageRunning = false;
    for (const item of replacements) {
      item.addition?.remove();
      for (const entry of item.nodes) if (entry.node.isConnected && entry.node.data === entry.applied) entry.node.data = entry.original;
    }
    replacements.length = 0;
  }
  function candidates() {
    const found = [], used = new WeakSet();
    // Specific text containers first; then leaf containers for sites without semantic markup.
    for (const selector of ["p,h1,h2,h3,h4,h5,h6,blockquote,figcaption,td,th,dt,dd,li", "div,article,section"]) {
      for (const node of document.querySelectorAll(selector)) {
        if (node.closest(excluded) || !visible(node) || owned.has(node)) continue;
        const nodes = textNodes(node);
        if (!nodes.length || nodes.some(n => used.has(n))) continue;
        if (selector.startsWith("div") && node.querySelector(blocks)) continue;
        const text = nodes.map(n => n.data).join("").trim();
        if (text.length < 2 || !/\p{L}/u.test(text)) continue;
        nodes.forEach(n => used.add(n)); found.push({ node, nodes, text });
      }
    }
    return found.sort((a, b) => a.node.compareDocumentPosition(b.node) & Node.DOCUMENT_POSITION_FOLLOWING ? -1 : 1);
  }
  async function page() {
    if (pageRunning || replacements.length) { restore(); card("截屏释义", "已恢复原文。再次点击可翻译当前页面。"); return; }
    const generation = ++pageVersion; pageRunning = true;
    const panel = card("截屏释义 · 网页翻译", "正在准备…");
    const cancel = element("button", "", "停止并恢复原文"); cancel.onclick = () => { restore(); panel.body.textContent = "已恢复原文"; cancel.remove(); };
    panel.node.append(cancel);
    try {
      const auth = await authorize(); if (generation !== pageVersion) return;
      const items = candidates(), mode = preferences.mode;
      let done = 0, skipped = 0;
      // Bound each user operation; avoid unbounded paid API work on infinite feeds.
      for (const item of items.slice(0, 200)) {
        if (generation !== pageVersion) return;
        if (item.text.length > 12000) { skipped++; continue; }
        panel.body.textContent = `正在翻译 ${done + 1} / ${Math.min(items.length, 200)} 段…`;
        const originals = item.nodes.map(node => ({ node, original: node.data }));
        const r = await send({ type: "translate", text: item.text, ...auth });
        if (generation !== pageVersion) return;
        if (!item.node.isConnected || originals.some(n => !n.node.isConnected || n.node.data !== n.original)) { skipped++; continue; }
        if (r.sourceLanguage === r.targetLanguage) { done++; continue; }
        if (mode === "bilingual") {
          const addition = element("span", "", r.translation); addition.setAttribute("translate", "no"); addition.dir = "auto"; owned.add(addition);
          addition.style.cssText = "display:block;margin:8px 0;padding:8px 12px;border-left:3px solid #0765DB;background:rgba(7,101,219,.08);color:inherit;font-size:inherit;line-height:1.65;white-space:pre-wrap;";
          item.node.append(addition); replacements.push({ nodes: [], addition });
        } else {
          const nodes = originals.map((entry, index) => ({ ...entry, applied: index === 0 ? r.translation : "" }));
          for (const entry of nodes) entry.node.data = entry.applied;
          replacements.push({ nodes });
        }
        done++;
      }
      panel.caption.textContent = "截屏释义 · 翻译完成";
      panel.body.textContent = `已处理 ${done} 段${skipped ? `，跳过 ${skipped} 个超长或已变化段落` : ""}。${items.length > 200 ? "本次只处理前 200 段。" : ""}\n可随时恢复原文；动态加载的内容需再次翻译。`;
    } catch (e) { if (generation === pageVersion) panel.body.textContent = `${e.message}\n已完成的部分可用“恢复原文”撤销。`; }
    finally { if (generation === pageVersion) pageRunning = false; }
  }
  function region() {
    regionCleanup?.(); closePanel(); mount();
    const overlay = element("div", "overlay"), hint = element("div", "hint", "拖动框选文字或图片 · Esc 取消"), rect = element("div", "rect");
    rect.hidden = true; overlay.append(hint, rect); root.append(overlay);
    let start, bounds, ended = false, canceled = false, panel;
    const captureId = crypto.randomUUID();
    const prevent = e => e.preventDefault();
    const cleanup = () => { overlay.remove(); document.removeEventListener("keydown", escape, true); document.removeEventListener("wheel", prevent, true); regionCleanup = null; };
    const escape = e => { if (e.key === "Escape") { e.stopImmediatePropagation(); ended = true; canceled = true; cleanup(); } };
    regionCleanup = cleanup; document.addEventListener("keydown", escape, true); document.addEventListener("wheel", prevent, { capture: true, passive: false });
    overlay.onpointerdown = e => { if (e.button !== 0) return; e.preventDefault(); overlay.setPointerCapture(e.pointerId); start = { x: e.clientX, y: e.clientY }; };
    overlay.onpointermove = e => {
      if (!start || ended) return;
      const x = Math.max(0, Math.min(innerWidth, e.clientX)), y = Math.max(0, Math.min(innerHeight, e.clientY));
      bounds = { x: Math.min(start.x, x), y: Math.min(start.y, y), width: Math.abs(x - start.x), height: Math.abs(y - start.y), viewportWidth: innerWidth, viewportHeight: innerHeight };
      rect.hidden = false; Object.assign(rect.style, { left: `${bounds.x}px`, top: `${bounds.y}px`, width: `${bounds.width}px`, height: `${bounds.height}px` });
    };
    overlay.onpointercancel = () => { ended = true; cleanup(); };
    overlay.onpointerup = async () => {
      if (!start || ended) return; ended = true;
      if (!bounds || bounds.width < 4 || bounds.height < 4) { cleanup(); return; }
      try {
        const auth = await authorize(true);
        if (!overlay.isConnected) return;
        overlay.style.opacity = "0";
        await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
        onRegionCaptured = id => {
          if (id !== captureId || canceled) return;
          cleanup(); panel = card("截屏释义 · 圈选翻译", "截图已裁剪，正在识别与翻译…", bounds.x, bounds.y);
        };
        const r = await send({ type: "region", rect: bounds, captureId, ...auth }); cleanup();
        if (canceled || panel && panel.version !== panelVersion) return;
        panel = card(`截屏释义 · ${r.sourceLanguage} → ${r.targetLanguage}`, r.translation, bounds.x, bounds.y + bounds.height);
        if (preferences.mode === "bilingual") panel.node.insertBefore(element("div", "original", r.original), panel.body);
        addCopy(panel, r.translation);
      } catch (e) { cleanup(); if (!canceled && (!panel || panel.version === panelVersion)) card("截屏释义 · 圈选翻译", e.message, bounds.x, bounds.y); }
      finally { onRegionCaptured = null; }
    };
  }
  document.addEventListener("mouseup", e => {
    if (!e.isTrusted || regionCleanup || !preferences.selection || e.target === host || e.target.closest?.(excluded)) return;
    const selection = getSelection(), text = selection?.toString().trim();
    if (!text || text.length > 12000 || selection.isCollapsed) return;
    const range = selection.getRangeAt(0);
    if (range.startContainer.parentElement?.closest(excluded) || range.endContainer.parentElement?.closest(excluded)) return;
    closePanel(); mount(); const button = element("button", "float", "截屏释义"); button.title = "翻译选中文字";
    button.style.left = `${Math.min(e.clientX + 8, innerWidth - 70)}px`; button.style.top = `${Math.min(e.clientY + 10, innerHeight - 50)}px`;
    button.onmousedown = event => event.preventDefault(); button.onclick = () => showTranslation(text, e.clientX, e.clientY);
    root.append(button);
  });
  function scheduleHover(event) {
    clearTimeout(hoverTimer);
    if (!preferences.hover || !event.altKey || regionCleanup || pageRunning) return;
    const target = event.target?.closest?.(blocks);
    if (!target || target.closest(excluded)) return;
    const text = textNodes(target).map(n => n.data).join("").trim();
    if (!text || text.length > 12000) return;
    hoverTimer = setTimeout(() => showTranslation(text, event.clientX, event.clientY), 650);
  }
  document.addEventListener("mousemove", e => { if (e.isTrusted) { lastHover = e; scheduleHover(e); } }, { passive: true });
  document.addEventListener("keydown", e => {
    if (!e.isTrusted) return;
    if (e.key === "Escape") { closePanel(); clearTimeout(hoverTimer); }
    if (e.key === "Alt" && !e.repeat && lastHover) scheduleHover({ target: lastHover.target, clientX: lastHover.clientX, clientY: lastHover.clientY, altKey: true });
  });
  document.addEventListener("keyup", e => { if (e.key === "Alt") clearTimeout(hoverTimer); });
  window.addEventListener("blur", () => clearTimeout(hoverTimer));
  chrome.runtime.onMessage.addListener((message, sender, respond) => {
    if (sender.id !== chrome.runtime.id) return;
    switch (message.action) {
      case "ping": break;
      case "page": void page(); break;
      case "restore": restore(); closePanel(); break;
      case "region": region(); break;
      case "region-captured": onRegionCaptured?.(message.captureId); break;
      case "selection": void showTranslation(message.text || getSelection()?.toString(), innerWidth - 400, 12); break;
      default: return;
    }
    respond({ ok: true });
  });
  chrome.storage.onChanged.addListener((changes, area) => {
    if (area !== "local" || !changes.preferences) return;
    const next = changes.preferences.newValue;
    if (!next) return;
    if (next.mode !== preferences.mode || next.targetLanguage !== preferences.targetLanguage || next.sourceLanguage !== preferences.sourceLanguage || next.edition !== preferences.edition) { restore(); closePanel(); }
    if (next.edition !== preferences.edition) consentKey = "";
    preferences = next; clearTimeout(hoverTimer);
  });
  send({ type: "preferences" }).then(r => { preferences = r.preferences; }).catch(() => {});
})();
