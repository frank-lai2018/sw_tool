import { marked } from '../node_modules/marked/lib/marked.esm.js';
import { LARGE_DOCUMENT_CHARACTERS, splitSource } from './document-model.mjs';

const $ = id => document.getElementById(id);
const api = window.reader;
let current = null;
const editor = $('editor');
let savedText = null;
let editing = false;
let dirty = false;
let saving = false;
let prefs = { theme: 'light', fontSize: 17, pageWidth: 860, sidebarWidth: 248, recent: [] };
let showSource = false;
let renderVersion = 0;
let headingObserver;
let toastTimer;
let searchTimer;
let searchRanges = [];
let searchIndex = -1;
let searchTruncated = false;
let largeModel = null;
let sourcePages = null;
let readingPage = 0;
let sourcePage = 0;
let parserTask = null;
let paintVersion = 0;
let searchCounts = [];
let searchTotal = 0;
let searchRun = 0;

const welcome = `# 每一份筆記，都有好看的樣子

這是 **MD Reader**，一個輕巧、專注的 Markdown 閱讀空間。從工作筆記到專案文件，把注意力留給內容。

> 不需要登入，也不需要網路。你的文件，就留在你的電腦裡。

## 從這裡開始

按左側的 **開啟文件**，或直接將檔案拖曳到視窗中。你也可以使用 \`Ctrl + O\`，快速打開下一份筆記。

- **章節導覽** — 左側自動整理標題，點一下就能抵達。
- **舒服閱讀** — 拖動上方「頁寬」滑桿調整頁面寬度，點「滿版」填滿閱讀區，也能調整字體或切換深色主題。
- **快速搜尋** — 按 \`Ctrl + F\`，找到你想看的文字。
- **多視窗對照** — 點左側「新視窗」或按 \`Ctrl + N\`，同時閱讀不同文件。

## 讓內容清楚呈現

### 程式碼，也能一目了然

支援常用程式語言的語法高亮，點選右上角即可複製程式碼。

\`\`\`javascript
const reading = {
  focus: true,
  theme: "light",
  message: "Hello, Markdown!"
};

console.log(reading.message);
\`\`\`

### 表格與待辦清單

| 你想做的事 | 快捷鍵 |
| :--- | :--- |
| 開啟 Markdown 文件 | Ctrl + O |
| 搜尋文字 | Ctrl + F |
| 重新載入已修改的文件 | Ctrl + R |
| 列印或儲存為 PDF | Ctrl + P |
| 切換深淺色 | Ctrl + Shift + L |

- [x] 打開閱讀器
- [x] 找到舒適的閱讀方式
- [ ] 開始閱讀你的第一份文件

## 你的閱讀節奏

支援 Markdown 標題、清單、引言、表格、連結與本機圖片。相對路徑圖片需放在文件所在資料夾或其子資料夾；網路圖片會顯示提示，保持閱讀離線。

切換上方的 **原始碼** 按鈕，即可查看 Markdown 文字。開啟外部連結時，將使用你的預設瀏覽器。

---

慢慢讀，讓想法有空間。
`;

function notice(message) { $('toast').textContent = message; $('toast').hidden = false; clearTimeout(toastTimer); toastTimer = setTimeout(() => { $('toast').hidden = true; }, 4200); }
function updateEditorControls() {
  $('edit-button').disabled = !current?.path;
  $('edit-button').setAttribute('aria-expanded', String(editing));
  $('edit-button').querySelector('span').textContent = editing ? '預覽' : '編輯';
  $('save-button').hidden = !editing && !dirty;
  $('save-button').disabled = !dirty || saving;
  $('save-button').textContent = saving ? '儲存中…' : '儲存';
  $('draft-state').hidden = !dirty;
  $('source-button').disabled = editing;
  editor.readOnly = saving;
}
function updateDirty() {
  const changed = editor.value !== savedText;
  if (changed === dirty) return;
  dirty = changed;
  api.setDirty(current.id, dirty);
  updateEditorControls();
}
function showEditor(show) {
  editing = show;
  document.body.classList.toggle('editing', editing);
  $('editor-area').hidden = !editing;
  $('scroll-area').hidden = editing;
  updateEditorControls();
}
function loadDocument(doc) {
  $('open-error').hidden = true;
  editor.value = '';
  savedText = null;
  dirty = false;
  current = doc;
  showEditor(false);
  return renderDocument(doc);
}
function showOpenError(error) {
  $('open-error-path').textContent = error.path;
  $('open-error-message').textContent = error.message;
  $('open-error').hidden = false;
  if (!current?.path) {
    $('welcome-banner').hidden = true;
    $('content').replaceChildren(); $('toc').replaceChildren();
    $('filename').textContent = error.name;
    $('filename').title = error.path;
    $('document-location').textContent = error.path;
    $('document-location').title = error.path;
    $('document-state').textContent = '開啟失敗';
    $('word-count').textContent = ''; $('reading-time').textContent = '';
  }
}
async function previewDraft() {
  showEditor(false);
  await renderDocument({ ...current, text: editor.value });
}
async function toggleEditor() {
  if (!current?.path) return;
  if (editing) {
    const render = previewDraft();
    $('scroll-area').focus();
    await render;
  } else {
    if (savedText === null) {
      editor.value = current.text;
      savedText = editor.value;
    }
    if (!$('search-bar').hidden) closeSearch();
    showEditor(true);
    $('document-state').textContent = '編輯模式';
    editor.focus();
  }
}
async function saveDraft() {
  if (!dirty || saving) return;
  const { id } = current;
  const text = editor.value;
  saving = true;
  updateEditorControls();
  try {
    if (await api.save(id, text) && current.id === id) {
      savedText = text;
      updateDirty();
      notice('已儲存文件。');
    }
  } catch { notice('無法儲存文件，請再試一次。'); }
  finally { saving = false; updateEditorControls(); }
}
function applyPreferences(settings) {
  prefs = { ...prefs, ...settings };
  document.body.classList.toggle('dark', prefs.theme === 'dark');
  applySidebarWidth();
  document.documentElement.style.setProperty('--font-size', `${prefs.fontSize}px`);
  document.documentElement.style.setProperty('--page-width', prefs.pageWidth === 0 ? '100%' : `${prefs.pageWidth}px`);
  if (prefs.pageWidth !== 0) $('page-width').value = prefs.pageWidth;
  $('page-width-value').textContent = prefs.pageWidth === 0 ? '滿版' : `${prefs.pageWidth} px`;
  $('full-width').setAttribute('aria-pressed', String(prefs.pageWidth === 0));
  $('font-size').textContent = prefs.fontSize;
  $('font-smaller').disabled = prefs.fontSize <= 13;
  $('font-larger').disabled = prefs.fontSize >= 24;
  $('theme-button').setAttribute('aria-label', prefs.theme === 'dark' ? '切換為淺色' : '切換為深色');
  $('recent').replaceChildren();
  if (!prefs.recent.length) $('recent').innerHTML = '<p class="empty-nav">開啟文件後，會在這裡留下紀錄。</p>';
  for (const file of prefs.recent) {
    const button = document.createElement('button');
    const name = document.createElement('strong');
    const location = document.createElement('small');
    name.textContent = file.split(/[\\/]/).pop(); location.textContent = file; button.title = file;
    button.append(name, location); button.onclick = () => api.openFile(file); $('recent').append(button);
  }
  requestAnimationFrame(updateProgress);
}
function changeTheme() { applyPreferences({ theme: prefs.theme === 'dark' ? 'light' : 'dark' }); api.preferences({ theme: prefs.theme }); }
function changeSize(delta) { applyPreferences({ fontSize: Math.max(13, Math.min(24, prefs.fontSize + delta)) }); api.preferences({ fontSize: prefs.fontSize }); }
function changeWidth(pageWidth) { applyPreferences({ pageWidth }); api.preferences({ pageWidth }); }
function sidebarLimits() { return { min: 200, max: Math.max(200, Math.min(600, window.innerWidth - 460)) }; }
function applySidebarWidth() {
  const { min, max } = sidebarLimits();
  const width = Math.max(min, Math.min(max, prefs.sidebarWidth));
  document.documentElement.style.setProperty('--sidebar-width', `${width}px`);
  $('sidebar-resizer').setAttribute('aria-valuemax', String(max));
  $('sidebar-resizer').setAttribute('aria-valuenow', String(width));
  $('sidebar-resizer').setAttribute('aria-valuetext', `${width} 像素`);
}
function changeSidebarWidth(width, save = false) {
  const { min, max } = sidebarLimits();
  prefs.sidebarWidth = Math.max(min, Math.min(max, Math.round(width)));
  applySidebarWidth();
  if (save) api.preferences({ sidebarWidth: prefs.sidebarWidth });
}
let sidebarDrag = null;
let sidebarResizeFrame = 0;
let pendingSidebarWidth = null;
function flushSidebarResize() {
  cancelAnimationFrame(sidebarResizeFrame); sidebarResizeFrame = 0;
  if (pendingSidebarWidth !== null) { changeSidebarWidth(pendingSidebarWidth); pendingSidebarWidth = null; }
}
function finishSidebarResize(event) {
  if (!sidebarDrag || (event?.pointerId !== undefined && event.pointerId !== sidebarDrag.id)) return;
  const id = sidebarDrag.id; sidebarDrag = null;
  flushSidebarResize();
  document.body.classList.remove('sidebar-resizing');
  if ($('sidebar-resizer').hasPointerCapture(id)) $('sidebar-resizer').releasePointerCapture(id);
  api.preferences({ sidebarWidth: prefs.sidebarWidth });
}
$('sidebar-resizer').onpointerdown = event => {
  if (event.button !== 0 || sidebarDrag) return;
  event.preventDefault();
  sidebarDrag = { id: event.pointerId, x: event.clientX, width: $('sidebar').getBoundingClientRect().width };
  $('sidebar-resizer').focus({ preventScroll: true });
  $('sidebar-resizer').setPointerCapture(event.pointerId);
  document.body.classList.add('sidebar-resizing');
};
$('sidebar-resizer').onpointermove = event => {
  if (!sidebarDrag || event.pointerId !== sidebarDrag.id) return;
  pendingSidebarWidth = sidebarDrag.width + event.clientX - sidebarDrag.x;
  if (!sidebarResizeFrame) sidebarResizeFrame = requestAnimationFrame(flushSidebarResize);
};
$('sidebar-resizer').onpointerup = finishSidebarResize;
$('sidebar-resizer').onpointercancel = finishSidebarResize;
$('sidebar-resizer').onlostpointercapture = finishSidebarResize;
$('sidebar-resizer').ondblclick = () => changeSidebarWidth(248, true);
$('sidebar-resizer').onkeydown = event => {
  const { min, max } = sidebarLimits();
  const width = $('sidebar').getBoundingClientRect().width;
  const step = event.shiftKey ? 40 : 16;
  const next = { ArrowLeft: width - step, ArrowRight: width + step, Home: min, End: max }[event.key];
  if (next === undefined) return;
  event.preventDefault(); changeSidebarWidth(next, true);
};
window.addEventListener('blur', () => finishSidebarResize());
window.addEventListener('resize', applySidebarWidth);
function updateProgress() {
  const scroll = $('scroll-area');
  const max = scroll.scrollHeight - scroll.clientHeight;
  const fraction = max > 0 ? scroll.scrollTop / max : 1;
  const pages = pagesForMode();
  const progress = Math.round(largeModel && pages ? (pageForMode() + fraction) / pages.length * 100 : fraction * 100);
  $('progress').textContent = `${progress}%`; $('progress-fill').style.width = `${progress}%`;
}
function buildToc() {
  headingObserver?.disconnect(); $('toc').replaceChildren();
  const headings = [...$('content').querySelectorAll('h1,h2,h3,h4,h5,h6')];
  headingObserver = new IntersectionObserver(entries => {
    const visible = entries.filter(entry => entry.isIntersecting).sort((a, b) => a.boundingClientRect.top - b.boundingClientRect.top);
    if (!visible.length) return;
    const id = visible[0].target.id;
    $('toc').querySelectorAll('a').forEach(a => a.classList.toggle('active', a.getAttribute('href') === `#${id}`));
  }, { root: $('scroll-area'), rootMargin: '0px 0px -65% 0px', threshold: 0 });
  const ids = new Set();
  headings.forEach((heading, i) => {
    // Generate unique, namespaced anchors and support common GitHub-style heading links.
    let slug = heading.textContent.toLowerCase().trim().replace(/[^\p{L}\p{N}\s_-]/gu, '').replace(/\s/g, '-');
    slug = slug || `section-${i}`;
    const base = slug; let n = 1; while (ids.has(slug)) slug = `${base}-${n++}`;
    ids.add(slug); heading.id = `md-heading-${slug}`; heading.dataset.slug = slug;
    const link = document.createElement('a'); link.href = `#${heading.id}`; link.textContent = heading.textContent; link.title = heading.textContent;
    link.className = `depth-${heading.tagName.slice(1)}${i === 0 ? ' active' : ''}`;
    link.onclick = async event => { event.preventDefault(); const id = heading.id; if (showSource) await toggleSource(); const target = [...$('content').querySelectorAll('[id]')].find(node => node.id === id); target?.scrollIntoView({ behavior: 'smooth', block: 'start' }); };
    $('toc').append(link); headingObserver.observe(heading);
  });
  if (!headings.length) $('toc').innerHTML = '<p class="empty-nav">這份文件沒有標題。<br>使用 # 標題即可建立目錄。</p>';
}
function parseLargeDocument(text) {
  if (parserTask) { parserTask.worker.terminate(); parserTask.reject(new Error('cancelled')); parserTask = null; }
  return new Promise((resolve, reject) => {
    const worker = new Worker(new URL('./document-worker.mjs', import.meta.url), { type: 'module' });
    parserTask = { worker, reject };
    const finish = () => { worker.terminate(); if (parserTask?.worker === worker) parserTask = null; };
    worker.onmessage = event => { finish(); if (event.data.error) reject(new Error(event.data.error)); else resolve(event.data.model); };
    worker.onerror = event => { finish(); reject(new Error(event.message || '文件解析失敗')); };
    worker.postMessage(text);
  });
}
function clearSearchHighlights() {
  searchRanges = []; searchIndex = -1;
  CSS.highlights.delete('search-results'); CSS.highlights.delete('search-current');
}
function pagesForMode() { return showSource ? sourcePages : largeModel?.pages; }
function pageForMode() { return showSource ? sourcePage : readingPage; }
function updatePagination() {
  const pages = pagesForMode(), total = pages?.length || 1, index = pageForMode();
  $('pagination').hidden = !largeModel;
  $('page-number').value = index + 1; $('page-number').max = total;
  $('page-label').textContent = `/ ${total} 頁`;
  $('page-prev').disabled = index === 0; $('page-next').disabled = index >= total - 1;
  $('print-button').title = largeModel ? '列印目前頁（Ctrl+P）' : '列印或儲存 PDF（Ctrl+P）';
  api.pagination({ page: index + 1, total });
}
function buildModelToc() {
  headingObserver?.disconnect(); $('toc').replaceChildren();
  for (const heading of largeModel.headings) {
    const link = document.createElement('a'); link.href = `#${heading.id}`;
    link.textContent = heading.title; link.title = `${heading.title}（第 ${heading.page + 1} 頁）`; link.className = `depth-${heading.depth}`;
    link.onclick = event => { event.preventDefault(); goToHeading(heading); };
    $('toc').append(link);
  }
  if (!largeModel.headings.length) $('toc').innerHTML = '<p class="empty-nav">這份文件沒有標題，可使用上方頁碼導覽。</p>';
}
function bindModelHeadings() {
  headingObserver?.disconnect();
  const assigned = new Set();
  $('content').querySelectorAll('[data-reader-heading]').forEach(node => {
    const heading = largeModel.headings[Number(node.getAttribute('data-reader-heading'))];
    node.removeAttribute('data-reader-heading');
    if (!heading || heading.page !== readingPage || assigned.has(heading.id) || node.tagName !== `H${heading.depth}`) return;
    node.id = heading.id; node.dataset.slug = heading.slug; assigned.add(heading.id);
  });
  const active = largeModel.headings.find(h => h.page === readingPage) || largeModel.headings.filter(h => h.page < readingPage).at(-1);
  $('toc').querySelectorAll('a').forEach(a => a.classList.toggle('active', a.getAttribute('href') === `#${active?.id}`));
}
async function goToHeading(heading) {
  if (showSource) { showSource = false; $('source-button').setAttribute('aria-pressed', 'false'); }
  readingPage = heading.page;
  await renderCurrentPage();
  $('content').querySelectorAll('[id]').forEach(node => { if (node.id === heading.id) node.scrollIntoView({ behavior: 'instant', block: 'start' }); });
}
async function renderDocument(documentData, demo = false) {
  const version = ++renderVersion; ++paintVersion; ++searchRun;
  if (parserTask) { parserTask.worker.terminate(); parserTask.reject(new Error('cancelled')); parserTask = null; }
  clearTimeout(searchTimer); clearSearchHighlights(); searchCounts = []; searchTotal = 0;
  headingObserver?.disconnect();
  largeModel = null; sourcePages = null; readingPage = 0; sourcePage = 0;
  current = documentData; showSource = false; $('source').hidden = true; $('source').textContent = ''; $('content').hidden = false; $('content').replaceChildren(); $('toc').replaceChildren();
  $('source-button').setAttribute('aria-pressed', 'false');
  $('welcome-banner').hidden = !demo; $('pagination').hidden = true;
  $('filename').textContent = documentData.name; $('filename').title = documentData.path || '範例文件';
  $('document-location').textContent = demo ? '歡迎來到你的閱讀空間' : documentData.path;
  $('document-location').title = documentData.path || '';
  if (!editing) $('document-state').textContent = '讀取中…';
  $('word-count').textContent = ''; $('reading-time').textContent = '';
  let cjk, latin;
  try {
    if (documentData.text.length > LARGE_DOCUMENT_CHARACTERS) {
      const model = await parseLargeDocument(documentData.text);
      if (version !== renderVersion) return;
      largeModel = model; ({ cjk, latin } = model); buildModelToc();
    } else {
      cjk = (documentData.text.match(/[\p{Script=Han}\p{Script=Hiragana}\p{Script=Katakana}]/gu) || []).length;
      latin = (documentData.text.replace(/[\p{Script=Han}\p{Script=Hiragana}\p{Script=Katakana}]/gu, ' ').match(/[\p{L}\p{N}]+/gu) || []).length;
    }
    $('word-count').textContent = `${(cjk + latin).toLocaleString()} 字 / 詞`;
    $('reading-time').textContent = `約 ${Math.max(1, Math.ceil(cjk / 400 + latin / 220))} 分鐘`;
    await renderCurrentPage();
    if (version === renderVersion && !editing && $('search-input').value) await runSearch();
  } catch (error) { if (version === renderVersion && !editing) { $('document-state').textContent = '讀取失敗'; notice(error.message); } }
}
async function renderCurrentPage({ search = true } = {}) {
  if (editing) return;
  const version = ++paintVersion;
  clearSearchHighlights();
  $('content').hidden = showSource; $('source').hidden = !showSource;
  $('source-button').setAttribute('aria-pressed', String(showSource));
  $('document-state').textContent = showSource ? 'Markdown 原始碼' : largeModel ? '分頁閱讀' : current.path ? '閱讀模式' : '範例文件';
  if (showSource) {
    if (largeModel) $('content').replaceChildren();
    $('source').textContent = sourcePages ? sourcePages[sourcePage] : current.text;
  } else {
    $('source').textContent = '';
    const html = largeModel ? largeModel.pages[readingPage].html : marked.parse(current.text, { gfm: true, breaks: false });
    await paintHtml(html, version);
    if (version !== paintVersion) return;
    if (largeModel) bindModelHeadings(); else buildToc();
  }
  updatePagination(); $('scroll-area').scrollTo({ top: 0, behavior: 'instant' }); requestAnimationFrame(updateProgress);
  if (search && $('search-input').value) collectPageRanges();
}
async function paintHtml(html, version) {
  const fragment = DOMPurify.sanitize(html, {
    RETURN_DOM_FRAGMENT: true,
    ADD_ATTR: ['data-reader-heading'],
    USE_PROFILES: { html: true },
    FORBID_TAGS: ['style', 'form', 'button', 'textarea', 'select', 'video', 'audio', 'source'],
    FORBID_ATTR: ['style', 'srcset', 'autofocus', 'contenteditable', 'name'],
    ALLOW_DATA_ATTR: false,
    ALLOW_ARIA_ATTR: false
  });
  // Keep the sanitized fragment: avoid serializing and parsing a second DOM tree.
  fragment.querySelectorAll('[id]').forEach(node => node.removeAttribute('id'));
  fragment.querySelectorAll('input').forEach(input => {
    if (input.type !== 'checkbox') input.remove();
    else { input.disabled = true; input.removeAttribute('form'); }
  });
  const imageSources = new Map();
  fragment.querySelectorAll('img').forEach(img => { imageSources.set(img, img.getAttribute('src')); img.removeAttribute('src'); img.removeAttribute('id'); });
  fragment.querySelectorAll('a').forEach(link => { link.removeAttribute('target'); link.removeAttribute('download'); });
  // Authored IDs are removed before insertion to keep the application controls unique.
  $('content').replaceChildren(fragment);
  if (!current.text.trim()) { const p = document.createElement('p'); p.textContent = '這是一份空白文件。'; p.className = 'empty-nav'; $('content').append(p); }
  $('content').querySelectorAll('pre code').forEach(code => {
    const language = [...code.classList].find(cls => cls.startsWith('language-'))?.slice(9) || 'text';
    try { if (window.hljs && code.textContent.length < 16000 && window.hljs.getLanguage(language)) window.hljs.highlightElement(code); } catch {}
    const label = document.createElement('span'); label.className = 'code-language'; label.textContent = language;
    const copy = document.createElement('button'); copy.className = 'copy-code'; copy.textContent = '複製'; copy.title = '複製程式碼';
    copy.onclick = async () => { try { await navigator.clipboard.writeText(code.textContent); copy.textContent = '已複製 ✓'; setTimeout(() => { copy.textContent = '複製'; }, 1800); } catch { notice('無法複製，請選取文字後按 Ctrl+C。'); } };
    code.parentElement.append(label, copy);
  });
  // Run in sequence: opening another file invalidates any outstanding image responses.
  for (const [img, source] of imageSources) {
    if (version !== paintVersion) return;
    img.loading = 'lazy'; img.decoding = 'async';
    let data = null;
    if (source && /^data:image\/(png|jpeg|gif|webp|avif|bmp);base64,/i.test(source)) data = source;
    else if (current.path && source && !/^https?:/i.test(source)) data = await api.image(source);
    if (version !== paintVersion) return;
    if (data) { img.src = data; img.onload = updateProgress; }
    else { const note = document.createElement('span'); note.className = 'image-note'; note.textContent = `圖片未載入：${img.alt || source || '未指定路徑'}${/^https?:/i.test(source || '') ? '（離線閱讀不載入網路圖片）' : ''}`; img.replaceWith(note); }
  }
}
async function setPage(index, { search = true } = {}) {
  const pages = pagesForMode();
  if (!pages) return;
  index = Math.max(0, Math.min(pages.length - 1, Math.trunc(Number(index) || 0)));
  if (showSource) sourcePage = index; else readingPage = index;
  await renderCurrentPage({ search });
}
async function toggleSource() {
  showSource = !showSource;
  if (showSource && largeModel && !sourcePages) sourcePages = splitSource(current.text);
  await renderCurrentPage({ search: false });
  if ($('search-input').value) await runSearch();
}
function highlightSearch(scroll = true) {
  CSS.highlights.delete('search-results'); CSS.highlights.delete('search-current');
  if (searchRanges.length) {
    CSS.highlights.set('search-results', new Highlight(...searchRanges));
    CSS.highlights.set('search-current', new Highlight(searchRanges[searchIndex]));
    if (scroll) {
      const bounds = searchRanges[searchIndex].getBoundingClientRect();
      const parent = $('scroll-area').getBoundingClientRect();
      $('scroll-area').scrollTo({ top: $('scroll-area').scrollTop + bounds.top - parent.top - parent.height / 3, behavior: 'instant' });
    }
  }
  $('search-result').textContent = $('search-input').value ? `${searchIndex < 0 ? 0 : searchCounts.slice(0, pageForMode()).reduce((a, b) => a + b, 0) + searchIndex + 1} / ${searchTotal}${searchTruncated ? '+' : ''}` : '';
}
function collectPageRanges() {
  clearSearchHighlights();
  const query = $('search-input').value;
  if (query) {
    const nodes = []; let text = '';
    const walker = document.createTreeWalker(showSource ? $('source') : $('content'), NodeFilter.SHOW_TEXT, {
      acceptNode: node => node.parentElement.closest('.copy-code,.code-language') ? NodeFilter.FILTER_REJECT : NodeFilter.FILTER_ACCEPT
    });
    while (walker.nextNode()) { const node = walker.currentNode; if (node.length) { nodes.push({ node, start: text.length, end: text.length + node.length }); text += node.textContent; } }
    const pattern = new RegExp(query.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'), 'giu');
    function locate(offset, isEnd) {
      let lo = 0, hi = nodes.length - 1;
      while (lo < hi) { const mid = (lo + hi) >> 1; if (nodes[mid].end < offset || (!isEnd && nodes[mid].end === offset)) lo = mid + 1; else hi = mid; }
      return nodes[lo];
    }
    for (const match of text.matchAll(pattern)) {
      if (searchRanges.length >= (largeModel ? searchCounts[pageForMode()] || 0 : 2000)) { if (!largeModel) searchTruncated = true; break; }
      const start = locate(match.index, false), end = locate(match.index + match[0].length, true);
      const range = document.createRange(); range.setStart(start.node, match.index - start.start); range.setEnd(end.node, match.index + match[0].length - end.start); searchRanges.push(range);
    }
    if (searchRanges.length) searchIndex = 0;
  }
  if (!largeModel) { searchTotal = searchRanges.length; searchCounts = []; }
  highlightSearch();
}
async function runSearch() {
  clearTimeout(searchTimer); const version = ++searchRun;
  clearSearchHighlights(); searchCounts = []; searchTotal = 0; searchTruncated = false;
  const query = $('search-input').value, pages = pagesForMode();
  if (query && largeModel && pages) {
    const pattern = new RegExp(query.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'), 'giu');
    for (const page of pages) {
      let count = 0;
      const text = showSource ? page : page.searchText;
      if (searchTotal >= 2000) { pattern.lastIndex = 0; if (pattern.test(text)) searchTruncated = true; }
      else for (const match of text.matchAll(pattern)) {
        if (searchTotal + count >= 2000) { searchTruncated = true; break; }
        count++;
      }
      searchCounts.push(count); searchTotal += count;
    }
    const first = searchCounts.findIndex(count => count > 0);
    if (first >= 0 && first !== pageForMode()) await setPage(first, { search: false });
    if (version !== searchRun) return;
  }
  collectPageRanges();
}
async function stepSearch(forward) {
  if (searchTimer) { searchTimer = null; await runSearch(); return; }
  if (searchRanges.length && (forward ? searchIndex < searchRanges.length - 1 : searchIndex > 0)) {
    searchIndex += forward ? 1 : -1; highlightSearch(); return;
  }
  if (largeModel && searchTotal) {
    const pages = pagesForMode(); let next = pageForMode();
    for (let n = 0; n < pages.length; n++) {
      next = (next + (forward ? 1 : -1) + pages.length) % pages.length;
      if (searchCounts[next]) {
        await setPage(next, { search: false }); collectPageRanges();
        searchIndex = forward ? 0 : searchRanges.length - 1; highlightSearch(); return;
      }
    }
  }
  if (searchRanges.length) { searchIndex = forward ? 0 : searchRanges.length - 1; highlightSearch(); }
}
async function showSearch() {
  if (editing) await previewDraft();
  if (editing) return;
  $('search-bar').hidden = false; $('search-input').focus(); $('search-input').select();
}
async function printDocument() {
  if (editing) await previewDraft();
  if (!editing) return api.print();
}
function closeSearch() { clearTimeout(searchTimer); $('search-bar').hidden = true; $('search-input').value = ''; runSearch(); $('scroll-area').focus(); }
function setTab(recent) {
  $('toc').hidden = recent; $('recent').hidden = !recent;
  for (const [id, selected] of [['toc-tab', !recent], ['recent-tab', recent]]) { $(id).classList.toggle('selected', selected); $(id).setAttribute('aria-selected', String(selected)); }
}
$('open-file').onclick = $('welcome-open').onclick = () => api.chooseFile();
$('new-window').onclick = () => api.newWindow();
$('theme-button').onclick = changeTheme;
$('font-smaller').onclick = () => changeSize(-1); $('font-larger').onclick = () => changeSize(1);
$('page-width').oninput = event => changeWidth(Number(event.target.value));
$('full-width').onclick = () => changeWidth(prefs.pageWidth === 0 ? Number($('page-width').value) : 0);
$('toggle-sidebar').onclick = () => document.body.classList.toggle('sidebar-hidden');
$('source-button').onclick = toggleSource;
$('edit-button').onclick = toggleEditor;
$('save-button').onclick = saveDraft;
editor.oninput = updateDirty;
$('page-prev').onclick = () => setPage(pageForMode() - 1);
$('page-next').onclick = () => setPage(pageForMode() + 1);
$('page-number').onchange = event => setPage(Number(event.target.value) - 1);
$('page-number').onkeydown = event => { if (event.key === 'Enter') { event.preventDefault(); setPage(Number(event.target.value) - 1); } };
$('reload').onclick = () => current.path ? api.reload() : notice('目前顯示範例文件，請先開啟你的 Markdown 檔案。');
$('print-button').onclick = printDocument;
$('toc-tab').onclick = () => setTab(false); $('recent-tab').onclick = () => setTab(true);
$('search-button').onclick = showSearch; $('close-search').onclick = closeSearch;
$('find-prev').onclick = () => stepSearch(false);
$('find-next').onclick = () => stepSearch(true);
$('search-input').oninput = () => { clearTimeout(searchTimer); searchTimer = setTimeout(() => { searchTimer = null; runSearch(); }, 180); };
$('search-input').onkeydown = event => { if (event.key === 'Enter') { event.preventDefault(); stepSearch(!event.shiftKey); } };
document.addEventListener('keydown', event => { if (event.key === 'Escape' && !$('search-bar').hidden) closeSearch(); });
$('content').onclick = event => {
  const link = event.target.closest('a'); if (!link) return;
  event.preventDefault(); const href = link.getAttribute('href'); if (!href) return;
  try {
    if (href.startsWith('#')) { const id = decodeURIComponent(href.slice(1)); if (largeModel) { const heading = largeModel.headings.find(h => h.id === id || h.slug === id); if (heading) { goToHeading(heading); return; } } const target = [...$('content').querySelectorAll('[id]')].find(node => node.id === id || node.dataset.slug === id); if (target) target.scrollIntoView({ behavior: 'smooth' }); }
    else api.openLink(href).catch(() => notice('無法開啟這個連結。'));
  } catch { notice('連結格式無法辨識。'); }
};
$('scroll-area').addEventListener('scroll', updateProgress, { passive: true });
new ResizeObserver(updateProgress).observe($('scroll-area'));
let dragDepth = 0;
document.addEventListener('dragenter', event => { event.preventDefault(); if (event.dataTransfer.types.includes('Files')) { dragDepth++; $('drop-overlay').hidden = false; } });
document.addEventListener('dragover', event => { event.preventDefault(); event.dataTransfer.dropEffect = 'copy'; });
document.addEventListener('dragleave', event => { event.preventDefault(); if (--dragDepth <= 0) { dragDepth = 0; $('drop-overlay').hidden = true; } });
document.addEventListener('drop', event => { event.preventDefault(); dragDepth = 0; $('drop-overlay').hidden = true; const file = event.dataTransfer.files[0]; if (file) api.openFile(api.droppedPath(file)); });
api.onDocument(loadDocument); api.onSettings(applyPreferences); api.onNotice(notice); api.onOpenError(showOpenError); api.onSearch(showSearch); api.onTheme(changeTheme);
api.onSave(saveDraft); api.onPrint(printDocument);
await renderDocument({ name: '歡迎使用.md', text: welcome, path: null }, true);
applyPreferences(await api.initialize());
