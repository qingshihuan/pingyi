import { test } from 'node:test';
import assert from 'node:assert/strict';
import { cropBounds, normalizePrefs, languageCode } from '../shared.js';

test('crop uses actual screenshot scale, clips edges, and rejects invalid selections', () => {
  assert.deepEqual(cropBounds({ x: 10, y: 20, width: 100, height: 50, viewportWidth: 800, viewportHeight: 600 }, 1600, 1200), { x: 20, y: 40, width: 200, height: 100 });
  assert.deepEqual(cropBounds({ x: 790, y: 590, width: 50, height: 50, viewportWidth: 800, viewportHeight: 600 }, 800, 600), { x: 790, y: 590, width: 10, height: 10 });
  for (const rect of [null, {}, { x: NaN, y: 0, width: 50, height: 50, viewportWidth: 800, viewportHeight: 600 }]) assert.throws(() => cropBounds(rect, 800, 600));
});
test('preferences only persist allowed fields, never content or credentials', () => {
  const p = normalizePrefs({ text: 'private', image: 'private', apiKey: 'private', mode: 'unsafe', edition: 'other', hover: true });
  assert.equal(p.mode, 'bilingual'); assert.equal(p.edition, 'standard'); assert.equal(p.hover, true);
  assert.deepEqual(Object.keys(p).sort(), ['edition', 'hover', 'mode', 'selection', 'sourceLanguage', 'targetLanguage'].sort());
});
test('browser locale codes preserve traditional Chinese and normalize regional variants', () => {
  assert.equal(languageCode('zh-TW'), 'zh-Hant'); assert.equal(languageCode('zh-CN'), 'zh');
  assert.equal(languageCode('en-US'), 'en'); assert.equal(languageCode('ja'), 'ja');
});
