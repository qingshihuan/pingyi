import { defaults, errors } from "./shared.js";
const $ = id => document.getElementById(id);
let preferences = { ...defaults }, connectedStatus;
function feedback(text) { $("feedback").textContent = text; }
async function send(message) {
  const response = await chrome.runtime.sendMessage(message);
  if (!response?.ok) throw new Error(errors[response?.error] || response?.message || "连接失败，请重试。");
  return response;
}
function render() {
  for (const key of ["sourceLanguage", "targetLanguage", "edition"]) {
    if (![...$(key).options].some(o => o.value === preferences[key])) $(key).add(new Option(preferences[key], preferences[key]));
    $(key).value = preferences[key];
  }
  for (const key of ["hover", "selection"]) $(key).checked = preferences[key];
  document.querySelector(`input[name=mode][value=${preferences.mode}]`).checked = true;
}
async function connect() {
  connectedStatus = null; $("connection").textContent = "连接中"; $("connection").classList.remove("online");
  try {
    const r = await send({ type: "status" }); connectedStatus = r.status; preferences = r.preferences;
    for (const key of ["sourceLanguage", "targetLanguage"]) {
      const select = $(key); select.replaceChildren();
      select.add(new Option(key === "sourceLanguage" ? "自动检测" : "自动中英互译", key === "sourceLanguage" ? "auto" : "auto-opposite"));
      for (const l of r.status.languages) select.add(new Option(l.displayName, l.code));
    }
    render(); $("connection").textContent = "已连接"; $("connection").classList.add("online");
    $("providerName").textContent = r.status.translationProvider;
    $("privacy").textContent = r.status.remoteText ? "远程服务 · 发送前需要允许文字上传" : "本机处理 · 文字翻译无需上传";
    feedback("");
  } catch (error) {
    $("connection").textContent = "未连接"; $("providerName").textContent = "启动本机 截屏释义 后连接"; feedback(error.message);
  }
}
async function save() {
  for (const key of ["sourceLanguage", "targetLanguage", "edition"]) preferences[key] = $(key).value;
  for (const key of ["hover", "selection"]) preferences[key] = $(key).checked;
  preferences.mode = document.querySelector("input[name=mode]:checked").value;
  await send({ type: "save", preferences });
}
for (const control of document.querySelectorAll("select,input[type=radio],input[type=checkbox]")) control.addEventListener("change", async () => {
  try { await save(); if (control.id === "edition") await connect(); } catch (e) { feedback(e.message); }
});
for (const action of ["page", "restore", "region"]) $(action).onclick = async () => {
  try { await send({ type: "action", action }); window.close(); } catch (e) { feedback(e.message); }
};
$("reconnect").onclick = connect;
$("textToggle").onclick = () => { $("textPanel").hidden = !$("textPanel").hidden; if (!$("textPanel").hidden) $("inputText").focus(); };
$("translateText").onclick = async () => {
  const button = $("translateText"); button.disabled = true; feedback("正在翻译…");
  try {
    const { status } = await send({ type: "status" });
    if (status.remoteText && !confirm(`将这段文字交给「${status.translationProvider}」远程翻译？`)) { feedback("已取消"); return; }
    const result = await send({ type: "translate", text: $("inputText").value, routeKey: status.routeKey, allowRemoteText: status.remoteText });
    $("textResult").textContent = preferences.mode === "bilingual" ? `${result.original}\n\n${result.translation}` : result.translation;
    feedback("翻译完成");
  } catch (e) { feedback(e.message); } finally { button.disabled = false; }
};
(async () => { const r = await send({ type: "preferences" }); preferences = r.preferences; render(); await connect(); })().catch(e => feedback(e.message));
