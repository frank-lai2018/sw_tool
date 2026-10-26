import { marked, Renderer } from '../node_modules/marked/lib/marked.esm.js';

export const LARGE_DOCUMENT_CHARACTERS = 256 * 1024;
const PAGE_BUDGET = 90000;
const BLOCK_CHARACTERS = 32000;

export function splitSource(text, limit = 48000) {
  const pages = [];
  for (let start = 0; start < text.length;) {
    let end = Math.min(start + limit, text.length);
    if (end < text.length) {
      const newline = text.lastIndexOf('\n', end);
      if (newline > start + limit / 2) end = newline + 1;
      // Do not split a UTF-16 surrogate pair.
      if (/[\uD800-\uDBFF]/.test(text[end - 1])) end--;
    }
    pages.push(text.slice(start, end)); start = end;
  }
  return pages.length ? pages : [''];
}
function decodeEntities(text) {
  const named = { amp: '&', lt: '<', gt: '>', quot: '"', apos: "'", nbsp: ' ' };
  return text.replace(/&(#x[\da-f]+|#\d+|amp|lt|gt|quot|apos|nbsp);/gi, (whole, entity) => {
    if (entity[0] !== '#') return named[entity.toLowerCase()] ?? whole;
    const number = entity[1].toLowerCase() === 'x' ? parseInt(entity.slice(2), 16) : parseInt(entity.slice(1), 10);
    return number > 0 && number <= 0x10ffff ? String.fromCodePoint(number) : '\uFFFD';
  });
}
export function plainText(html) {
  return decodeEntities(html.replace(/<!--[^]*?-->/g, '').replace(/<(script|style|textarea)\b[^>]*>[^]*?<\/\1\s*>/gi, '').replace(/<[^>]*>/g, ''));
}
export function buildDocument(text) {
  let cjk = 0, latin = 0, inWord = false;
  const cjkCharacter = /[\p{Script=Han}\p{Script=Hiragana}\p{Script=Katakana}]/u;
  const wordCharacter = /[\p{L}\p{N}]/u;
  for (const character of text) {
    if (cjkCharacter.test(character)) { cjk++; inWord = false; }
    else if (wordCharacter.test(character)) { if (!inWord) latin++; inWord = true; }
    else inWord = false;
  }
  const tokens = marked.lexer(text, { gfm: true });
  const groups = []; let group = [], cost = 0;
  const flush = () => { if (group.length) groups.push(group); group = []; cost = 0; };
  function add(token, weight) {
    if (cost && cost + weight > PAGE_BUDGET) flush();
    group.push(token); cost += weight;
    if (cost >= PAGE_BUDGET) flush();
  }
  for (const token of tokens) {
    if (token.type === 'table') {
      // Repeat headers on continuation pages. Bound both cell count and text size.
      let rows = [], weight = token.header.reduce((n, cell) => n + cell.text.length + 100, 0);
      const headerWeight = weight;
      for (const row of token.rows) {
        const rowWeight = row.reduce((n, cell) => n + cell.text.length + 100, 0);
        if (rows.length && (weight + rowWeight > PAGE_BUDGET || rows.length >= 100)) {
          add({ ...token, rows }, weight); rows = []; weight = headerWeight;
        }
        rows.push(row); weight += rowWeight;
      }
      add({ ...token, rows }, weight);
    } else if ((token.raw?.length || 0) > BLOCK_CHARACTERS && token.type !== 'space') {
      if (token.type === 'code') {
        for (const part of splitSource(token.text, BLOCK_CHARACTERS)) add({ ...token, text: part }, part.length + 10000);
      } else {
        // A single huge HTML/list/paragraph block is shown as text, not an unbounded DOM.
        for (const part of splitSource(token.raw, BLOCK_CHARACTERS)) add({ type: 'code', text: part, lang: 'text' }, part.length + 10000);
      }
    } else add(token, (token.raw?.length || 0) + 200);
  }
  flush();
  const headings = [], ids = new Set();
  const pages = [];
  let pageIndex = 0;
  const renderer = new Renderer();
  renderer.heading = function(token) {
    const inline = this.parser.parseInline(token.tokens);
    const title = plainText(inline);
    let slug = title.toLowerCase().trim().replace(/[^\p{L}\p{N}\s_-]/gu, '').replace(/\s/g, '-') || `section-${headings.length}`;
    const base = slug; let suffix = 1; while (ids.has(slug)) slug = `${base}-${suffix++}`;
    ids.add(slug);
    const index = headings.length;
    headings.push({ title, slug, id: `md-heading-${slug}`, depth: token.depth, page: pageIndex, index });
    return `<h${token.depth} data-reader-heading="${index}">${inline}</h${token.depth}>\n`;
  };
  for (const pageTokens of groups) {
    pageTokens.links = tokens.links;
    const html = marked.parser(pageTokens, { gfm: true, renderer });
    pages.push({ html, searchText: plainText(html) }); pageIndex++;
  }
  return { pages: pages.length ? pages : [{ html: '', searchText: '' }], headings, cjk, latin };
}
