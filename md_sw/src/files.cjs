const path = require('node:path');
const fs = require('node:fs/promises');
const { TextDecoder } = require('node:util');
const { createHash, randomUUID } = require('node:crypto');

const EXTENSIONS = new Set(['.md', '.markdown', '.mdown', '.mkd', '.txt']);
const MAX_DOCUMENT_BYTES = 20 * 1024 * 1024;
function isDocument(file) { return typeof file === 'string' && EXTENSIONS.has(path.extname(file).toLowerCase()); }
function decodeWithEncoding(bytes) {
  if (bytes[0] === 0xff && bytes[1] === 0xfe) return { text: new TextDecoder('utf-16le').decode(bytes), encoding: 'utf-16le' };
  if (bytes[0] === 0xfe && bytes[1] === 0xff) return { text: new TextDecoder('utf-16be').decode(bytes), encoding: 'utf-16be' };
  try {
    const text = new TextDecoder('utf-8', { fatal: true }).decode(bytes);
    const encoding = bytes[0] === 0xef && bytes[1] === 0xbb && bytes[2] === 0xbf ? 'utf-8-bom' : 'utf-8';
    return { text, encoding };
  } catch { return { text: new TextDecoder('big5').decode(bytes), encoding: 'big5' }; }
}
function decodeDocument(bytes) { return decodeWithEncoding(bytes).text; }
function fingerprint(bytes) { return createHash('sha256').update(bytes).digest('hex'); }
async function readDocument(file) {
  if (!isDocument(file)) throw new Error('請選擇 Markdown 或文字檔案（.md、.markdown、.txt）。');
  const real = await fs.realpath(file);
  const stat = await fs.stat(real);
  if (!stat.isFile()) throw new Error('這不是可讀取的檔案。');
  if (stat.size > MAX_DOCUMENT_BYTES) throw new Error('檔案超過 20 MB，請選擇較小的文件。');
  const bytes = await fs.readFile(real);
  if (bytes.length > MAX_DOCUMENT_BYTES) throw new Error('檔案超過 20 MB，請選擇較小的文件。');
  const { text, encoding } = decodeWithEncoding(bytes);
  return {
    path: real, name: path.basename(real), text, size: bytes.length, encoding,
    newline: text.match(/\r\n|\r|\n/)?.[0] || '\n', fingerprint: fingerprint(bytes)
  };
}
function encodeDocument(text, encoding) {
  if (encoding === 'utf-16le' || encoding === 'utf-16be') {
    const bytes = Buffer.from('\uFEFF' + text, 'utf16le');
    return encoding === 'utf-16be' ? bytes.swap16() : bytes;
  }
  return Buffer.from(encoding === 'utf-8-bom' ? '\uFEFF' + text : text, 'utf8');
}
async function saveDocument(document, text) {
  const encoding = document.encoding === 'big5' ? 'utf-8' : document.encoding;
  const bytes = encodeDocument(text.replace(/\r\n|\r|\n/g, document.newline), encoding);
  if (bytes.length > MAX_DOCUMENT_BYTES) throw new Error('儲存後的檔案超過 20 MB，請縮短內容後再試。');
  const temp = path.join(path.dirname(document.path), `.md-reader-${randomUUID()}.tmp`);
  const stat = await fs.stat(document.path);
  if (!stat.isFile() || stat.size > MAX_DOCUMENT_BYTES) throw new Error('磁碟上的檔案已變更，請先保留目前的草稿。');
  // Write beside the original so a failed write cannot leave it partially overwritten.
  const handle = await fs.open(temp, 'wx', stat.mode);
  try {
    try { await handle.writeFile(bytes); await handle.sync(); }
    finally { await handle.close(); }
    if (fingerprint(await fs.readFile(document.path)) !== document.fingerprint) {
      throw new Error('磁碟上的檔案已變更，請先保留目前的草稿。');
    }
    await fs.rename(temp, document.path);
  } finally { await fs.unlink(temp).catch(() => {}); }
  return { encoding, fingerprint: fingerprint(bytes) };
}
const imageTypes = { '.png': 'image/png', '.jpg': 'image/jpeg', '.jpeg': 'image/jpeg', '.gif': 'image/gif', '.webp': 'image/webp', '.avif': 'image/avif', '.bmp': 'image/bmp' };
async function readLocalImage(documentPath, source) {
  if (!source || /^(?:[a-z][a-z\d+.-]*:|[\\/]{2})/i.test(source)) return null;
  const root = await fs.realpath(path.dirname(documentPath));
  const image = await fs.realpath(path.resolve(root, decodeURIComponent(source.split(/[?#]/)[0])));
  const relative = path.relative(root, image);
  if (relative.startsWith('..' + path.sep) || relative === '..' || path.isAbsolute(relative)) return null;
  const mime = imageTypes[path.extname(image).toLowerCase()];
  if (!mime || (await fs.stat(image)).size > 15 * 1024 * 1024) return null;
  return `data:${mime};base64,${(await fs.readFile(image)).toString('base64')}`;
}
module.exports = { isDocument, decodeDocument, readDocument, saveDocument, readLocalImage };
