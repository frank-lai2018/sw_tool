const path = require('node:path');
const fs = require('node:fs/promises');
const { TextDecoder } = require('node:util');

const EXTENSIONS = new Set(['.md', '.markdown', '.mdown', '.mkd', '.txt']);
const MAX_DOCUMENT_BYTES = 20 * 1024 * 1024;
function isDocument(file) { return typeof file === 'string' && EXTENSIONS.has(path.extname(file).toLowerCase()); }
function decodeDocument(bytes) {
  if (bytes[0] === 0xff && bytes[1] === 0xfe) return new TextDecoder('utf-16le').decode(bytes);
  if (bytes[0] === 0xfe && bytes[1] === 0xff) return new TextDecoder('utf-16be').decode(bytes);
  try { return new TextDecoder('utf-8', { fatal: true }).decode(bytes); }
  catch { return new TextDecoder('big5').decode(bytes); }
}
async function readDocument(file) {
  if (!isDocument(file)) throw new Error('請選擇 Markdown 或文字檔案（.md、.markdown、.txt）。');
  const real = await fs.realpath(file);
  const stat = await fs.stat(real);
  if (!stat.isFile()) throw new Error('這不是可讀取的檔案。');
  if (stat.size > MAX_DOCUMENT_BYTES) throw new Error('檔案超過 20 MB，請選擇較小的文件。');
  return { path: real, name: path.basename(real), text: decodeDocument(await fs.readFile(real)), size: stat.size };
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
module.exports = { isDocument, decodeDocument, readDocument, readLocalImage };
