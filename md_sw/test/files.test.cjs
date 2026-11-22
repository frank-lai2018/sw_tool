const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const { isDocument, decodeDocument, readDocument, saveDocument, readLocalImage } = require('../src/files.cjs');

test('accepts document formats, rejects executable and invalid paths', () => {
  assert.equal(isDocument('繁體中文.MD'), true);
  assert.equal(isDocument('readme.markdown'), true);
  assert.equal(isDocument('program.exe'), false);
  assert.equal(isDocument(null), false);
});
test('decodes UTF-8, UTF-16 BOM and Big5 Chinese documents', () => {
  assert.equal(decodeDocument(Buffer.from('中文測試')), '中文測試');
  assert.equal(decodeDocument(Buffer.concat([Buffer.from([255, 254]), Buffer.from('中文', 'utf16le')])), '中文');
  assert.equal(decodeDocument(Buffer.from([0xfe, 0xff, 0x4e, 0x2d, 0x65, 0x87])), '中文');
  assert.equal(decodeDocument(Buffer.from([0xa4, 0xa4, 0xa4, 0xe5])), '中文');
});
test('reads a document and confines image access to its own folder', async t => {
  const temp = await fs.mkdtemp(path.join(os.tmpdir(), 'md-reader-'));
  t.after(() => fs.rm(temp, { recursive: true, force: true }));
  await fs.mkdir(path.join(temp, 'docs'));
  const doc = path.join(temp, 'docs', '中文.md');
  await fs.writeFile(doc, '# 中文\n\n內容');
  await fs.writeFile(path.join(temp, 'docs', 'sample.png'), Buffer.from([137, 80, 78, 71]));
  await fs.writeFile(path.join(temp, 'private.png'), 'private');
  await fs.writeFile(path.join(temp, 'docs', 'vector.svg'), '<svg/>');
  assert.equal((await readDocument(doc)).text, '# 中文\n\n內容');
  assert.match(await readLocalImage(doc, 'sample.png'), /^data:image\/png;base64,/);
  assert.equal(await readLocalImage(doc, '../private.png'), null);
  assert.equal(await readLocalImage(doc, 'vector.svg'), null);
  assert.equal(await readLocalImage(doc, 'https://example.com/image.png'), null);
  assert.equal(await readLocalImage(doc, '//server/share.png'), null);
  await assert.rejects(readDocument(path.join(temp, 'private.png')), /Markdown/);
});
test('accepts documents above the old 20 MiB limit and rejects above 100 MiB', async t => {
  const temp = await fs.mkdtemp(path.join(os.tmpdir(), 'md-reader-size-'));
  t.after(() => fs.rm(temp, { recursive: true, force: true }));
  const file = path.join(temp, 'large.md');
  const handle = await fs.open(file, 'w');
  await handle.truncate(20 * 1024 * 1024 + 1); await handle.close();
  const document = await readDocument(file);
  assert.equal(document.size, 20 * 1024 * 1024 + 1);
  await saveDocument(document, '# 可儲存');
  assert.equal((await readDocument(file)).text, '# 可儲存');
  await fs.truncate(file, 100 * 1024 * 1024 + 1);
  await assert.rejects(readDocument(file), /100 MiB/);
});
test('saves UTF-16 documents with their byte order and line endings intact', async t => {
  const temp = await fs.mkdtemp(path.join(os.tmpdir(), 'md-reader-save-'));
  t.after(() => fs.rm(temp, { recursive: true, force: true }));
  const file = path.join(temp, 'note.md');
  await fs.writeFile(file, Buffer.from('\uFEFF# 中文\r\n\r\n原文', 'utf16le').swap16());
  const document = await readDocument(file);
  await saveDocument(document, '# 編輯\n\n新內容');
  assert.deepEqual(await fs.readFile(file), Buffer.from('\uFEFF# 編輯\r\n\r\n新內容', 'utf16le').swap16());
  await assert.rejects(saveDocument(document, '舊草稿'), /磁碟上的檔案已變更/);
  assert.deepEqual(await fs.readdir(temp), ['note.md']);
});
