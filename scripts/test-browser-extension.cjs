// Real Chromium DOM/UI regression with synthetic providers; never uses private pages.
const { chromium } = require(process.env.PINGYI_PLAYWRIGHT_MODULE || 'playwright');
const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const assert = require('node:assert/strict');
const root = path.resolve(__dirname, '..');
const fixture = '<!doctype html><meta charset="utf-8"><body style="padding:60px;font:20px Arial"><h1>Translation test</h1><p id="paragraph">Hello <a id="link" href="#test">world</a>. This is a synthetic browser test.</p><p contenteditable="true" id="editable">Private editable input</p><pre id="code">Do not translate code</pre><input type="password" value="synthetic"><p id="second">The second paragraph is here.</p></body>';
const server = http.createServer((req, res) => {
  const file = path.join(root, 'browser-extension', path.basename(req.url.split('?')[0]));
  if (fs.existsSync(file) && fs.statSync(file).isFile()) {
    const ext = path.extname(file); res.setHeader('Content-Type', ({ '.js':'text/javascript','.css':'text/css','.html':'text/html','.png':'image/png' })[ext] || 'text/plain'); res.end(fs.readFileSync(file));
  } else { res.setHeader('Content-Type','text/html'); res.end(fixture); }
});
const mock = () => {
  const nativeAttach = Element.prototype.attachShadow;
  Element.prototype.attachShadow = function(options) { const value = nativeAttach.call(this, options); if (this.tagName === 'DIV') window.__pingyiRoot = value; return value; };
  window.testPrefs = { mode:'bilingual',sourceLanguage:'auto',targetLanguage:'zh',edition:'standard',hover:true,selection:true };
  window.calls = []; window.chromeListeners = []; window.storageListeners = [];
  window.route = { translationProvider:'本地 Argos',ocrProvider:'本地 PaddleOCR',remoteText:false,remoteImage:false,routeKey:'test-route',languages:[{code:'en',displayName:'英语'},{code:'zh',displayName:'简体中文'}] };
  window.chrome = { runtime: { id:'test-extension', onMessage:{addListener(fn){window.chromeListeners.push(fn);}}, async sendMessage(m) {
    if (m.type === 'preferences') return {ok:true,preferences:window.testPrefs};
    if (m.type === 'status') return {ok:true,preferences:window.testPrefs,status:window.route};
    if (m.type === 'save') { window.testPrefs=m.preferences; return {ok:true}; }
    if (m.type === 'translate') {
      window.calls.push(m); await new Promise(r=>setTimeout(r,window.delay || 20));
      return {ok:true,original:m.text,translation:'这是浏览器测试的译文。',sourceLanguage:'en',targetLanguage:'zh'};
    }
    if (m.type === 'region') { window.calls.push(m); window.chromeListeners.forEach(fn=>fn({action:'region-captured',captureId:m.captureId},{id:'test-extension'},()=>{})); return {ok:true,original:'Image text',translation:'图片中的文字',sourceLanguage:'en',targetLanguage:'zh'}; }
    return {ok:true};
  } }, storage:{onChanged:{addListener(fn){window.storageListeners.push(fn);}}} };
  window.action = action => window.chromeListeners.forEach(fn=>fn({action},{id:'test-extension'},()=>{}));
  window.changePrefs = patch => { Object.assign(window.testPrefs,patch); window.storageListeners.forEach(fn=>fn({preferences:{newValue:{...window.testPrefs}}},'local')); };
};
(async () => {
  await new Promise(resolve => server.listen(0,'127.0.0.1',resolve));
  const base = `http://127.0.0.1:${server.address().port}`;
  const browser = await chromium.launch({headless:true, ...(process.env.PINGYI_TEST_BROWSER ? {executablePath:process.env.PINGYI_TEST_BROWSER} : {})});
  try {
    const page = await browser.newPage({viewport:{width:1100,height:800}}); await page.addInitScript(mock);
    await page.goto(base); await page.addScriptTag({path:path.join(root,'browser-extension/content.js')});
    await page.evaluate(()=> { window.linkIdentity=document.getElementById('link'); window.action('page'); });
    await page.waitForFunction(()=>window.__pingyiRoot?.textContent.includes('翻译完成'));
    assert.equal(await page.locator('#paragraph > span[translate=no]').count(),1);
    assert.equal(await page.locator('#editable').textContent(),'Private editable input');
    assert.equal(await page.locator('#code').textContent(),'Do not translate code');
    assert.equal(await page.evaluate(()=>window.calls.some(c=>c.text.includes('Private'))),false);
    await page.evaluate(()=>window.action('restore'));
    assert.equal(await page.locator('#paragraph').textContent(),'Hello world. This is a synthetic browser test.');
    assert.equal(await page.evaluate(()=>window.linkIdentity === document.getElementById('link')),true);
    await page.evaluate(()=>{ window.changePrefs({mode:'translation'}); window.action('page'); });
    await page.waitForFunction(()=>window.__pingyiRoot?.textContent.includes('翻译完成'));
    assert.equal(await page.locator('#paragraph').textContent(),'这是浏览器测试的译文。');
    await page.evaluate(()=>{ document.querySelector('#second').firstChild.data='Site updated this content'; window.action('restore'); });
    assert.equal(await page.locator('#paragraph').textContent(),'Hello world. This is a synthetic browser test.');
    assert.equal(await page.locator('#second').textContent(),'Site updated this content');
    // Cancel while provider response is in flight: no late DOM changes, no next paragraph.
    await page.evaluate(()=>{window.calls=[];window.delay=150;window.action('page');});
    await page.waitForFunction(()=>window.calls.length===1); await page.evaluate(()=>window.action('restore'));
    await page.waitForTimeout(250); assert.equal(await page.evaluate(()=>window.calls.length),1);
    assert.equal(await page.locator('#paragraph').textContent(),'Hello world. This is a synthetic browser test.');
    // Real pointer and key events exercise deliberate hover.
    await page.evaluate(()=>{window.delay=20;window.calls=[];});
    await page.keyboard.down('Alt'); await page.locator('#paragraph').hover(); await page.waitForTimeout(850); await page.keyboard.up('Alt');
    assert.equal(await page.evaluate(()=>window.calls.length),1);
    assert.match(await page.evaluate(()=>window.__pingyiRoot.textContent),/这是浏览器测试的译文/);
    // Text selection floating action; no request until clicked.
    await page.evaluate(()=>{window.calls=[];const range=document.createRange();range.selectNodeContents(document.querySelector('#paragraph'));const selection=getSelection();selection.removeAllRanges();selection.addRange(range);});
    const bounds=await page.locator('#paragraph').boundingBox(); await page.mouse.move(bounds.x+10,bounds.y+10); await page.mouse.down();
    await page.evaluate(()=>{const range=document.createRange();range.selectNodeContents(document.querySelector('#paragraph'));getSelection().addRange(range);});
    await page.mouse.up();
    await page.waitForFunction(()=>!!window.__pingyiRoot.querySelector('.float'));
    assert.equal(await page.evaluate(()=>window.calls.length),0);
    await page.evaluate(()=>window.__pingyiRoot.querySelector('.float').click());
    await page.waitForFunction(()=>window.calls.length===1);
    // Region selection geometry and visible progress/result.
    await page.evaluate(()=>window.action('region')); await page.mouse.move(100,120); await page.mouse.down(); await page.mouse.move(500,300); await page.mouse.up();
    await page.waitForFunction(()=>window.__pingyiRoot.textContent.includes('图片中的文字'));
    assert.equal(await page.evaluate(()=>window.calls.find(c=>c.type==='region').rect.width),400);
    // Popup rendered using real styles, DOM, fonts, and controls.
    const popup=await browser.newPage({viewport:{width:380,height:700}}); await popup.addInitScript(mock); await popup.goto(base+'/popup.html');
    await popup.waitForFunction(()=>document.querySelector('#connection').textContent==='已连接');
    assert.equal(await popup.locator('body').evaluate(el=>el.scrollWidth <= 380),true);
    fs.mkdirSync(path.join(root,'artifacts/browser-extension'),{recursive:true});
    await popup.screenshot({path:path.join(root,'artifacts/browser-extension/popup.png'),fullPage:true,animations:'disabled'});
    await popup.emulateMedia({colorScheme:'dark'});
    await popup.screenshot({path:path.join(root,'artifacts/browser-extension/popup-dark.png'),fullPage:true,animations:'disabled'});
    await popup.emulateMedia({colorScheme:'light'});
    await popup.locator('#textToggle').click(); await popup.locator('#inputText').fill('Hello world'); await popup.locator('#translateText').click();
    await popup.waitForFunction(()=>document.querySelector('#textResult').textContent.includes('这是浏览器测试的译文'));
    await popup.locator('input[value=translation]').check();
    assert.equal(await popup.evaluate(()=>window.testPrefs.mode),'translation');
    console.log('PASS: bilingual, translation-only, safe restoration, editable/code exclusion, cancellation, Alt-hover, selection, region geometry, popup layout and text controls (synthetic provider).');
  } finally { await browser.close(); server.close(); }
})().catch(error=>{server.close();console.error(error);process.exitCode=1;});
