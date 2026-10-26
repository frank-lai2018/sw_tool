const { test } = require('node:test');
const assert = require('node:assert/strict');

test('paginates large tables without losing rows, repeats headers, and resolves references', async () => {
  const { buildDocument } = await import('../src/document-model.mjs');
  const rows = Array.from({ length: 1500 }, (_, n) => `| ROW-${n}-END | [連結][ref] |`);
  const model = buildDocument('# 相同標題\n\n| 編號 | 連結 |\n|---|---|\n' + rows.join('\n') + '\n\n# 相同標題\n\n[ref]: https://example.com\n');
  assert.ok(model.pages.length > 2);
  const html = model.pages.map(page => page.html).join('');
  for (let n = 0; n < 1500; n++) assert.equal(html.split(`ROW-${n}-END`).length - 1, 1);
  assert.equal((html.match(/href="https:\/\/example.com"/g) || []).length, 1500);
  assert.equal((html.match(/<tbody>/g) || []).length, 15);
  assert.equal(new Set(model.headings.map(h => h.id)).size, 2);
  assert.ok(model.headings[1].page > 0);
  assert.ok(model.pages.every(page => (page.html.match(/<td>/g) || []).length <= 900));
});
test('source pages preserve every character and UTF-16 pairs', async () => {
  const { splitSource } = await import('../src/document-model.mjs');
  const text = ('中文😀\n'.repeat(10000)) + '結尾';
  const parts = splitSource(text, 501);
  assert.equal(parts.join(''), text);
  assert.ok(parts.every(part => part.length <= 502));
  assert.ok(parts.every(part => !/[\uD800-\uDBFF]$/.test(part)));
});
test('oversized code or HTML blocks have bounded pages without executing markup', async () => {
  const { buildDocument } = await import('../src/document-model.mjs');
  const html = '<div>' + '<b>x</b>'.repeat(25000) + '</div>';
  const model = buildDocument(html);
  assert.ok(model.pages.length > 1);
  assert.ok(model.pages.every(page => !page.html.includes('<b>')));
  assert.ok(model.pages.every(page => (page.html.match(/<pre>/g) || []).length <= 2));
});
