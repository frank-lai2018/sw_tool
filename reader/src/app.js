import { makeBook } from './foliate/view.js'

const api = window.api
const $ = selector => document.querySelector(selector)
const $$ = selector => Array.from(document.querySelectorAll(selector))

const SUPPORTED = ['epub', 'pdf', 'azw3', 'azw', 'mobi', 'prc', 'fb2', 'fbz', 'cbz']

const THEMES = {
    light: { fg: '#1f1f1f', link: '#2f6fd6' },
    sepia: { fg: '#5b4636', link: '#a0662b' },
    green: { fg: '#1f2e22', link: '#2e7d4a' },
    dark: { fg: '#d6d3cd', link: '#7aa7ff', dark: true },
    black: { fg: '#bdbab4', link: '#7aa7ff', dark: true },
}

// PDF 與漫畫等固定版面無法改字色，改用濾鏡模擬主題
const FIXED_FILTERS = {
    light: 'none',
    sepia: 'sepia(.35) brightness(.97)',
    green: 'sepia(.2) hue-rotate(60deg) brightness(.96)',
    dark: 'invert(.88) hue-rotate(180deg)',
    black: 'invert(1) hue-rotate(180deg)',
}

const FONT_STACKS = {
    serif: '"Noto Serif TC", "Source Han Serif TC", "PMingLiU", "MingLiU", "Times New Roman", serif',
    sans: '"Microsoft JhengHei", "Noto Sans TC", "Source Han Sans TC", "Segoe UI", sans-serif',
    kai: '"DFKai-SB", "BiauKai", "KaiTi", "STKaiti", serif',
}

const DEFAULT_SETTINGS = {
    theme: 'light',
    fontSize: 19,
    lineHeight: 1.7,
    fontFamily: '',
    flow: 'paginated',
    columns: 2,
    pageWidth: 720,
    gap: 6,
    justify: true,
    zoom: 'fit-page',
    sort: 'lastReadAt',
    pdfSpread: false,
}

let store = { books: [], settings: { ...DEFAULT_SETTINGS } }
const missingPaths = new Set()

const debounce = (fn, ms) => {
    let timer
    const debounced = (...args) => {
        clearTimeout(timer)
        timer = setTimeout(() => fn(...args), ms)
    }
    debounced.flush = (...args) => {
        clearTimeout(timer)
        return fn(...args)
    }
    return debounced
}

const saveStore = debounce(() => api.saveStore(store).catch(e => console.error(e)), 600)

/* ============ 共用工具 ============ */

let toastTimer
const toast = (message, ms = 3200) => {
    const el = $('#toast')
    el.textContent = message
    el.hidden = false
    clearTimeout(toastTimer)
    toastTimer = setTimeout(() => el.hidden = true, ms)
}

const extOf = path => path.split('.').pop().toLowerCase()
const basename = path => path.split(/[\\/]/).pop()
const stem = path => basename(path).replace(/\.[^.]+$/, '')
const isSupported = path => SUPPORTED.includes(extOf(path))
const percent = x => x > 0 && x < 0.01 ? '<1%' : `${Math.round((x ?? 0) * 100)}%`

const formatLanguageMap = x => {
    if (!x) return ''
    if (typeof x === 'string') return x
    const keys = Object.keys(x)
    return x[keys.find(k => k.startsWith('zh')) ?? keys[0]]
}
const formatOneContributor = c => typeof c === 'string' ? c : formatLanguageMap(c?.name)
const formatContributor = c => Array.isArray(c)
    ? c.map(formatOneContributor).filter(Boolean).join('、')
    : formatOneContributor(c)

const hashString = str => {
    let h = 0
    for (const ch of str) h = (h * 31 + ch.codePointAt(0)) | 0
    return Math.abs(h)
}

const icon = name => {
    const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg')
    svg.classList.add('icon')
    const use = document.createElementNS('http://www.w3.org/2000/svg', 'use')
    use.setAttribute('href', `#i-${name}`)
    svg.append(use)
    return svg
}

const el = (tag, props = {}, ...children) => {
    const node = document.createElement(tag)
    Object.assign(node, props)
    node.append(...children)
    return node
}

/* ============ 檔案讀取與 DRM 檢查 ============ */

class ReaderError extends Error {}

const loadFile = async path => {
    try {
        const { name, data } = await api.readFile(path)
        return new File([data], name.toLowerCase())
    } catch (e) {
        if (/ENOENT/.test(e.message)) {
            missingPaths.add(path)
            throw new ReaderError('找不到檔案，可能已被移動或刪除')
        }
        throw e
    }
}

const FONT_OBFUSCATION = [
    'http://www.idpf.org/2008/embedding',
    'http://ns.adobe.com/pdf/enc#RC',
]

const checkDRM = async file => {
    const head = new Uint8Array(await file.slice(0, 82).arrayBuffer())
    const magic = String.fromCharCode(...head.slice(60, 68))
    if (magic === 'BOOKMOBI') {
        const view = new DataView(head.buffer)
        const record0 = view.getUint32(78)
        const header = new DataView(await file.slice(record0, record0 + 16).arrayBuffer())
        if (header.getUint16(12) !== 0)
            throw new ReaderError('此 Kindle 檔案受 DRM 保護，無法開啟')
    } else if (head[0] === 0x50 && head[1] === 0x4b) {
        const { configure, ZipReader, BlobReader, TextWriter } = await import('./foliate/vendor/zip.js')
        configure({ useWebWorkers: false })
        const reader = new ZipReader(new BlobReader(file))
        const entries = await reader.getEntries()
        const names = new Set(entries.map(e => e.filename))
        const encryption = entries.find(e => e.filename === 'META-INF/encryption.xml')
        let drm = names.has('META-INF/rights.xml')
        if (!drm && encryption) {
            const xml = await encryption.getData(new TextWriter())
            const algorithms = Array.from(xml.matchAll(/Algorithm\s*=\s*"([^"]+)"/g), m => m[1])
                .filter(a => !/xmldsig|sha1|sha256/i.test(a))
            drm = algorithms.some(a => !FONT_OBFUSCATION.includes(a))
        }
        await reader.close()
        if (drm) throw new ReaderError('此 EPUB 受 DRM 保護，無法開啟')
    }
}

const describeError = e => {
    if (e instanceof ReaderError) return e.message
    if (e?.name === 'PasswordException') return '此 PDF 受密碼保護，無法開啟'
    if (e?.name === 'UnsupportedTypeError' || /not supported/i.test(e?.message)) return '不支援的檔案格式'
    if (e?.name === 'InvalidPDFException') return 'PDF 檔案已損毀或格式錯誤'
    return `無法開啟檔案：${e?.message ?? e}`
}

/* ============ 書架 ============ */

const makeCover = async book => {
    let url
    try {
        const blob = await book.getCover?.()
        if (!blob) return null
        url = URL.createObjectURL(blob)
        const img = new Image()
        img.src = url
        await img.decode()
        const width = 300
        const height = Math.round(img.naturalHeight * width / img.naturalWidth) || 450
        const canvas = el('canvas', { width, height })
        canvas.getContext('2d').drawImage(img, 0, 0, width, height)
        return canvas.toDataURL('image/jpeg', 0.85)
    } catch (e) {
        console.warn('cover failed', e)
        return null
    } finally {
        if (url) URL.revokeObjectURL(url)
    }
}

const findEntry = path => store.books.find(b => b.id === path.toLowerCase())

const upsertEntry = (path, book) => {
    let entry = findEntry(path)
    if (!entry) {
        entry = {
            id: path.toLowerCase(), path,
            addedAt: Date.now(), lastReadAt: 0,
            progress: 0, cfi: null, bookmarks: [],
        }
        store.books.push(entry)
    }
    entry.path = path
    entry.title = formatLanguageMap(book.metadata?.title)?.trim() || stem(path)
    entry.author = formatContributor(book.metadata?.author)?.trim() || ''
    entry.format = extOf(path).toUpperCase()
    missingPaths.delete(path)
    saveStore()
    return entry
}

const ensureCover = async (entry, book) => {
    if (entry.cover !== undefined) return
    entry.cover = await makeCover(book)
    saveStore()
    if (current === entry) $('#side-cover').src = entry.cover ?? ''
    renderLibrary()
}

const sorters = {
    lastReadAt: (a, b) => (b.lastReadAt || b.addedAt) - (a.lastReadAt || a.addedAt),
    addedAt: (a, b) => b.addedAt - a.addedAt,
    title: (a, b) => a.title.localeCompare(b.title, 'zh-Hant'),
    author: (a, b) => (a.author || '￿').localeCompare(b.author || '￿', 'zh-Hant'),
}

const renderCover = entry => {
    const cover = el('div', { className: 'cover' })
    cover.append(el('span', { className: 'badge', textContent: entry.format }))
    if (entry.cover) {
        cover.append(el('img', { src: entry.cover, alt: '', loading: 'lazy' }))
    } else {
        const hue = hashString(entry.title) % 360
        const ph = el('div', { className: 'placeholder-cover', textContent: entry.title })
        ph.style.background = `linear-gradient(160deg, hsl(${hue} 45% 45%), hsl(${(hue + 40) % 360} 50% 28%))`
        cover.append(ph)
    }
    if (entry.progress > 0) {
        const bar = el('div')
        bar.style.width = percent(entry.progress)
        cover.append(el('div', { className: 'meter' }, bar))
    }
    return cover
}

const renderLibrary = () => {
    const query = $('#lib-filter').value.trim().toLowerCase()
    const books = store.books
        .filter(b => !query || b.title.toLowerCase().includes(query) || b.author.toLowerCase().includes(query))
        .sort(sorters[store.settings.sort] ?? sorters.lastReadAt)

    $('#empty-state').hidden = store.books.length > 0
    const shelf = $('#shelf')
    shelf.replaceChildren(...books.map(entry => {
        const card = el('div', { className: 'book-card', title: entry.path })
        if (missingPaths.has(entry.path)) card.classList.add('missing')
        card.append(
            renderCover(entry),
            el('div', { className: 'title', textContent: entry.title }),
            el('div', { className: 'author', textContent: entry.author || '未知作者' }),
            el('div', { className: 'pct', textContent: missingPaths.has(entry.path)
                ? '找不到檔案' : entry.progress > 0 ? `已讀 ${percent(entry.progress)}` : '未讀' }),
        )
        card.addEventListener('click', () => openBook(entry.path))
        card.addEventListener('contextmenu', e => showContextMenu(e, [
            ['開啟', () => openBook(entry.path)],
            ['在檔案總管中顯示', () => api.showInFolder(entry.path)],
            ['重設閱讀進度', () => {
                Object.assign(entry, { progress: 0, cfi: null })
                saveStore()
                renderLibrary()
            }],
            ['從書架移除', () => removeEntry(entry), 'danger'],
        ]))
        return card
    }))
    if (store.books.length && !books.length)
        shelf.append(el('div', { className: 'panel-empty', textContent: '找不到符合的書' }))
}

const removeEntry = entry => {
    store.books = store.books.filter(b => b !== entry)
    saveStore()
    renderLibrary()
    toast(`已從書架移除《${entry.title}》（檔案本身不會刪除）`)
}

const checkMissingFiles = async () => {
    await Promise.all(store.books.map(async b => {
        if (!await api.fileExists(b.path)) missingPaths.add(b.path)
    }))
    if (missingPaths.size) renderLibrary()
}

const importBook = async path => {
    const file = await loadFile(path)
    await checkDRM(file)
    const book = await makeBook(file)
    try {
        const entry = upsertEntry(path, book)
        await ensureCover(entry, book)
    } finally {
        book.destroy?.()
    }
}

const handleFiles = async paths => {
    paths = paths.filter(isSupported)
    if (!paths.length) return toast('不支援的檔案格式，請選擇 EPUB、PDF、AZW3、AZW 或 MOBI')
    if (paths.length === 1) return openBook(paths[0])
    let added = 0
    const failed = []
    for (const [i, path] of paths.entries()) {
        toast(`正在加入書架… ${i + 1} / ${paths.length}`, 60000)
        try {
            await importBook(path)
            added++
        } catch (e) {
            console.error(e)
            failed.push(`${basename(path)}：${describeError(e)}`)
        }
    }
    renderLibrary()
    toast(failed.length
        ? `已加入 ${added} 本，${failed.length} 本失敗：${failed.join('；')}`
        : `已加入 ${added} 本書`, failed.length ? 8000 : 3200)
}

const openDialog = async () => handleFiles(await api.openDialog())

/* ============ 右鍵選單 / 彈出面板 ============ */

const showContextMenu = (event, items) => {
    event.preventDefault()
    const menu = $('#context-menu')
    menu.replaceChildren(...items.map(([label, action, cls]) => {
        const button = el('button', { textContent: label, className: cls ?? '' })
        button.addEventListener('click', () => {
            menu.hidden = true
            action()
        })
        return button
    }))
    menu.hidden = false
    const { innerWidth: w, innerHeight: h } = window
    const rect = menu.getBoundingClientRect()
    menu.style.left = `${Math.min(event.clientX, w - rect.width - 8)}px`
    menu.style.top = `${Math.min(event.clientY, h - rect.height - 8)}px`
}

const closePopovers = () => {
    $('#context-menu').hidden = true
    $('#settings-panel').hidden = true
    $('#settings-btn').classList.remove('active')
}

document.addEventListener('mousedown', e => {
    if (!e.target.closest('.popover, #settings-btn')) closePopovers()
})

/* ============ 閱讀器 ============ */

let view = null
let current = null
let isFixed = false
let opening = false
let tocLinks = new Map()
let currentTOCHref = null
let sliderDragging = false
let searchToken = 0

const readerView = $('#reader-view')

const getCSS = s => {
    const t = THEMES[s.theme] ?? THEMES.light
    return `
    @namespace epub "http://www.idpf.org/2007/ops";
    html {
        color-scheme: ${t.dark ? 'dark' : 'light'};
        color: ${t.fg} !important;
        background: transparent !important;
        font-size: ${s.fontSize}px !important;
    }
    body {
        background: transparent !important;
        color: inherit !important;
    }
    ${t.dark ? `
    body *:not(img):not(svg):not(image) {
        color: inherit !important;
        background-color: transparent !important;
    }
    a:any-link, a:any-link * { color: ${t.link} !important; }
    img { filter: brightness(.85); }
    ` : ''}
    ${s.fontFamily ? `
    body, body *:not(code):not(pre):not(kbd):not(samp) {
        font-family: ${FONT_STACKS[s.fontFamily]} !important;
    }` : ''}
    p, li, blockquote, dd {
        line-height: ${s.lineHeight} !important;
        text-align: ${s.justify ? 'justify' : 'start'};
        hyphens: auto;
        -webkit-hyphens: auto;
        widows: 2;
    }
    [align="left"] { text-align: left; }
    [align="right"] { text-align: right; }
    [align="center"] { text-align: center; }
    [align="justify"] { text-align: justify; }
    pre { white-space: pre-wrap !important; }
    aside[epub|type~="endnote"],
    aside[epub|type~="footnote"],
    aside[epub|type~="note"],
    aside[epub|type~="rearnote"] {
        display: none;
    }`
}

const applySettings = () => {
    const s = store.settings
    document.body.dataset.theme = s.theme
    syncSettingsUI()
    if (!view?.renderer) return
    const r = view.renderer
    if (isFixed) {
        r.setAttribute('zoom', s.zoom)
        view.style.setProperty('--fixed-filter', FIXED_FILTERS[s.theme] ?? 'none')
    } else {
        r.setAttribute('flow', s.flow)
        r.setAttribute('max-column-count', s.columns)
        r.setAttribute('max-inline-size', `${s.pageWidth}px`)
        r.setAttribute('gap', `${s.gap}%`)
        r.setStyles?.(getCSS(s))
    }
}

const updateSetting = (key, value) => {
    store.settings[key] = value
    applySettings()
    saveStore()
}

const syncSettingsUI = () => {
    const s = store.settings
    $$('#theme-swatches button').forEach(b => b.classList.toggle('active', b.dataset.theme === s.theme))
    $('#lib-theme').replaceChildren(icon(THEMES[s.theme]?.dark ? 'sun' : 'moon'))
    $('#font-size').value = s.fontSize
    $('#font-size-val').textContent = `${s.fontSize}px`
    $('#line-height').value = s.lineHeight
    $('#line-height-val').textContent = s.lineHeight.toFixed(1)
    $('#font-family').value = s.fontFamily
    $('#page-width').value = s.pageWidth
    $('#page-width-val').textContent = `${s.pageWidth}px`
    $('#gap').value = s.gap
    $('#gap-val').textContent = `${s.gap}%`
    $('#justify').checked = s.justify
    $('#zoom').value = String(s.zoom)
    $('#pdf-spread').checked = !!s.pdfSpread
    $$('#flow-seg button').forEach(b => b.classList.toggle('active', b.dataset.value === s.flow))
    $$('#columns-seg button').forEach(b => b.classList.toggle('active', b.dataset.value === String(s.columns)))
    $('#lib-sort').value = s.sort
}

const changeFontSize = delta => {
    if (isFixed) {
        const steps = ['0.5', '0.75', '1', '1.25', '1.5', '2', '3']
        const zoom = store.settings.zoom
        const idx = steps.indexOf(String(zoom))
        const from = idx === -1 ? steps.indexOf('1') : idx
        const next = steps[Math.max(0, Math.min(steps.length - 1, from + delta))]
        updateSetting('zoom', next)
        toast(`縮放 ${percent(parseFloat(next))}`, 1200)
    } else {
        const size = Math.max(12, Math.min(40, store.settings.fontSize + delta))
        updateSetting('fontSize', size)
    }
}

const showReader = () => {
    $('#library-view').hidden = true
    readerView.hidden = false
}

const showLibrary = () => {
    closeReader()
    readerView.hidden = true
    $('#library-view').hidden = false
    document.title = '電子書閱讀器'
    renderLibrary()
}

const saveProgress = () => {
    const loc = view?.lastLocation
    if (!current || !loc) return
    current.cfi = loc.cfi
    current.progress = isFixed && loc.section
        ? (loc.section.current + 1) / loc.section.total
        : loc.fraction ?? current.progress
    current.lastReadAt = Date.now()
    saveStore()
}

const closeReader = () => {
    if (!view) return
    saveProgress()
    saveStore.flush()
    searchToken++
    try {
        view.close()
        view.book?.destroy?.()
    } catch (e) {
        console.warn(e)
    }
    view.remove()
    view = null
    current = null
    tocLinks = new Map()
    currentTOCHref = null
    $('#toc').replaceChildren()
    $('#search-results').replaceChildren()
    $('#search-status').textContent = ''
    $('#search-input').value = ''
    $('#chapter-label').textContent = ''
    $('#progress-label').textContent = ''
    $('#side-cover').removeAttribute('src')
}

const openBook = async path => {
    if (opening) return
    opening = true
    closePopovers()
    closeReader()
    showReader()
    $('#loading').hidden = false
    $('#book-title').textContent = basename(path)
    try {
        const file = await loadFile(path)
        await checkDRM(file)
        const book = await makeBook(file)
        // PDF 預設單頁顯示；EPUB 固定版面維持出版社設定
        if (book.rendition?.layout === 'pre-paginated' && extOf(path) === 'pdf' && !store.settings.pdfSpread)
            book.rendition.spread = 'none'
        book.transformTarget?.addEventListener('data', ({ detail }) => {
            detail.data = Promise.resolve(detail.data).catch(e => {
                console.error(new Error(`Failed to load ${detail.name}`, { cause: e }))
                return ''
            })
        })

        const entry = upsertEntry(path, book)
        current = entry
        view = document.createElement('foliate-view')
        $('#book-container').append(view)
        await view.open(book)
        isFixed = view.isFixedLayout
        readerView.classList.toggle('fixed', isFixed)
        view.addEventListener('load', onDocLoad)
        view.addEventListener('relocate', onRelocate)
        applySettings()

        document.title = `${entry.title} - 電子書閱讀器`
        $('#book-title').textContent = entry.title
        $('#side-title').textContent = entry.title
        $('#side-author').textContent = entry.author
        if (entry.cover) $('#side-cover').src = entry.cover
        renderTOC(book.toc)
        renderBookmarks()
        ensureCover(entry, book)

        try {
            await view.init({ lastLocation: entry.cfi })
        } catch (e) {
            console.warn('could not restore position', e)
            await view.next()
        }
        entry.lastReadAt = Date.now()
        saveStore()
        view.focus()
    } catch (e) {
        console.error(e)
        showLibrary()
        toast(describeError(e), 6000)
    } finally {
        $('#loading').hidden = true
        opening = false
    }
}

const onRelocate = ({ detail }) => {
    if (!current) return
    const { fraction, tocItem, pageItem, section, location } = detail
    if (isFixed && section) {
        if (!sliderDragging) $('#progress').value = section.total > 1 ? section.current / (section.total - 1) : 0
        $('#progress-label').textContent = `第 ${section.current + 1} / ${section.total} 頁`
    } else {
        if (!sliderDragging) $('#progress').value = fraction ?? 0
        const page = pageItem?.label ? ` · 第 ${pageItem.label} 頁`
            : location ? ` · ${location.current + 1} / ${location.total}` : ''
        $('#progress-label').textContent = `${percent(fraction)}${page}`
    }
    $('#chapter-label').textContent = tocItem?.label?.trim() ?? ''
    setCurrentTOC(tocItem?.href)
    updateBookmarkButton()
    saveProgress()
}

/* ---- 翻頁：鍵盤、滑鼠滾輪、點擊左右兩側 ---- */

let wheelAccum = 0
let wheelLock = 0
const onWheel = e => {
    if (!view) return
    if (e.ctrlKey) {
        e.preventDefault()
        if (Date.now() < wheelLock) return
        wheelLock = Date.now() + 120
        changeFontSize(e.deltaY < 0 ? 1 : -1)
        return
    }
    const turnsPages = isFixed ? store.settings.zoom === 'fit-page' : store.settings.flow === 'paginated'
    if (!turnsPages) return
    if (Date.now() < wheelLock) return
    wheelAccum += Math.abs(e.deltaY) > Math.abs(e.deltaX) ? e.deltaY : e.deltaX
    if (Math.abs(wheelAccum) < 50) return
    wheelLock = Date.now() + 280
    if (wheelAccum > 0) view.next()
    else view.prev()
    wheelAccum = 0
}

const handlePageClick = x => {
    const rect = $('#book-container').getBoundingClientRect()
    const rel = (x - rect.left) / rect.width
    if (rel < 0.25) view?.goLeft()
    else if (rel > 0.75) view?.goRight()
    else if (readerView.classList.contains('immersive')) readerView.classList.toggle('show-bars')
}

const onDocLoad = ({ detail: { doc } }) => {
    doc.addEventListener('keydown', onKeydown)
    doc.addEventListener('wheel', onWheel, { passive: false })
    doc.addEventListener('mousedown', closePopovers)
    doc.addEventListener('click', e => {
        if (e.defaultPrevented || e.button !== 0) return
        if (doc.getSelection()?.toString()) return
        if (e.target.closest?.('a, button, input, select, textarea, video, audio')) return
        // iframe 內的座標無法直接對應視窗，改用螢幕座標換算
        const border = (window.outerWidth - window.innerWidth) / 2
        handlePageClick(e.screenX - window.screenX - border)
    })
    doc.addEventListener('dragover', e => e.preventDefault())
    doc.addEventListener('drop', onDrop)
}

$('#book-container').addEventListener('wheel', onWheel, { passive: false })
$('#book-container').addEventListener('click', e => {
    if (e.target.tagName === 'FOLIATE-VIEW') handlePageClick(e.clientX)
})

const isTyping = target => target?.matches?.('input:not([type="range"]):not([type="checkbox"]), textarea, select, [contenteditable="true"]')

const onKeydown = e => {
    const key = e.key
    const ctrl = e.ctrlKey || e.metaKey
    if (key === 'F11') {
        e.preventDefault()
        api.toggleFullscreen()
        return
    }
    if (ctrl && key.toLowerCase() === 'o') {
        e.preventDefault()
        openDialog()
        return
    }
    if (readerView.hidden) return

    if (key === 'Escape') {
        e.preventDefault()
        if (!$('#settings-panel').hidden || !$('#context-menu').hidden) closePopovers()
        else if (document.fullscreenElement || readerView.classList.contains('immersive')) api.setFullscreen(false)
        else if ($('#sidebar').classList.contains('open')) toggleSidebar(false)
        else showLibrary()
        return
    }
    if (ctrl && key.toLowerCase() === 'f') {
        e.preventDefault()
        openSidebarTab('search')
        $('#search-input').focus()
        $('#search-input').select()
        return
    }
    if (ctrl && key.toLowerCase() === 'd') {
        e.preventDefault()
        toggleBookmark()
        return
    }
    if (ctrl && (key === '=' || key === '+')) return e.preventDefault(), changeFontSize(1)
    if (ctrl && key === '-') return e.preventDefault(), changeFontSize(-1)
    if (ctrl && key === '0') {
        e.preventDefault()
        if (isFixed) updateSetting('zoom', 'fit-page')
        else updateSetting('fontSize', DEFAULT_SETTINGS.fontSize)
        return
    }
    if (isTyping(e.target) || ctrl || e.altKey || !view) return

    switch (key) {
        case 'ArrowLeft': view.goLeft(); break
        case 'ArrowRight': view.goRight(); break
        case 'ArrowUp': case 'PageUp': view.prev(); break
        case 'ArrowDown': case 'PageDown': view.next(); break
        case ' ': e.shiftKey ? view.prev() : view.next(); break
        case 'Home': isFixed ? view.goTo(0) : view.goToFraction(0); break
        case 'End': isFixed ? view.goTo(view.book.sections.length - 1) : view.goToFraction(1); break
        case 't': case 'T': toggleSidebar(); break
        default: return
    }
    e.preventDefault()
}
document.addEventListener('keydown', onKeydown)

/* ---- 進度列 ---- */

const progress = $('#progress')
progress.addEventListener('pointerdown', () => sliderDragging = true)
progress.addEventListener('input', () => {
    sliderDragging = true
    const v = parseFloat(progress.value)
    const total = view?.book?.sections.length ?? 0
    $('#progress-label').textContent = isFixed
        ? `第 ${Math.round(v * (total - 1)) + 1} / ${total} 頁`
        : percent(v)
})
progress.addEventListener('change', async () => {
    const v = parseFloat(progress.value)
    try {
        if (isFixed) await view?.goTo(Math.round(v * (view.book.sections.length - 1)))
        else await view?.goToFraction(v)
    } finally {
        sliderDragging = false
    }
})

$('#prev-btn').addEventListener('click', () => view?.goLeft())
$('#next-btn').addEventListener('click', () => view?.goRight())
$('#back-btn').addEventListener('click', showLibrary)
$('#fullscreen-btn').addEventListener('click', () => api.toggleFullscreen())

/* ---- 全螢幕沉浸模式 ---- */

api.onFullscreen(value => {
    readerView.classList.toggle('immersive', value)
    readerView.classList.remove('show-bars')
})
$$('.hover-zone').forEach(zone => zone.addEventListener('mouseenter', () =>
    readerView.classList.add('show-bars')))
$$('.reader-bar, .reader-foot').forEach(bar => bar.addEventListener('mouseleave', e => {
    if (!$('#settings-panel').hidden || e.relatedTarget?.closest?.('.reader-bar, .reader-foot, .popover')) return
    readerView.classList.remove('show-bars')
}))

/* ---- 側邊欄：目錄 / 書籤 / 搜尋 ---- */

const toggleSidebar = force => {
    const open = $('#sidebar').classList.toggle('open', force)
    $('#toc-btn').classList.toggle('active', open)
    if (open && currentTOCHref) tocLinks.get(currentTOCHref)?.a.scrollIntoView({ block: 'center' })
}

const openSidebarTab = tab => {
    toggleSidebar(true)
    $$('.tabs button').forEach(b => b.classList.toggle('active', b.dataset.tab === tab))
    $$('.tab-panel').forEach(p => p.hidden = p.id !== `panel-${tab}`)
}

$('#toc-btn').addEventListener('click', () => {
    const sidebar = $('#sidebar')
    if (sidebar.classList.contains('open')) toggleSidebar(false)
    else openSidebarTab($('.tabs button.active')?.dataset.tab ?? 'toc')
})
$('#search-btn').addEventListener('click', () => {
    openSidebarTab('search')
    $('#search-input').focus()
})
$$('.tabs button').forEach(b => b.addEventListener('click', () => openSidebarTab(b.dataset.tab)))

const renderTOC = toc => {
    const container = $('#toc')
    tocLinks = new Map()
    if (!toc?.length) {
        container.replaceChildren(el('div', { className: 'panel-empty', textContent: '此書沒有目錄' }))
        return
    }
    const build = (items, depth) => {
        const ul = el('ul')
        for (const item of items) {
            const li = el('li')
            const row = el('div', { className: 'toc-row' })
            row.style.paddingLeft = `${depth * 14 + 4}px`
            const hasChildren = item.subitems?.length > 0
            const toggle = el(hasChildren ? 'button' : 'span', { className: 'toggle' })
            if (hasChildren) {
                toggle.append(icon('right'))
                toggle.addEventListener('click', () => li.classList.toggle('expanded'))
            }
            const label = item.label?.trim() || '（無標題）'
            const link = item.href
                ? el('a', { href: '#', textContent: label, title: label })
                : el('span', { className: 'label', textContent: label })
            if (item.href) {
                link.addEventListener('click', e => {
                    e.preventDefault()
                    view?.goTo(item.href)
                })
                if (!tocLinks.has(item.href)) tocLinks.set(item.href, { a: link, li })
            }
            row.append(toggle, link)
            li.append(row)
            if (hasChildren) li.append(build(item.subitems, depth + 1))
            ul.append(li)
        }
        return ul
    }
    container.replaceChildren(build(toc, 0))
}

const setCurrentTOC = href => {
    if (href === currentTOCHref) return
    tocLinks.get(currentTOCHref)?.a.classList.remove('current')
    currentTOCHref = href
    const item = tocLinks.get(href)
    if (!item) return
    item.a.classList.add('current')
    for (let li = item.li.parentElement?.closest('li'); li; li = li.parentElement?.closest('li'))
        li.classList.add('expanded')
    if ($('#sidebar').classList.contains('open') && !$('#panel-toc').hidden)
        item.a.scrollIntoView({ block: 'nearest' })
}

/* ---- 書籤 ---- */

const isBookmarkOnPage = (bookmark, loc) => {
    if (!loc) return false
    if (bookmark.cfi === loc.cfi) return true
    if (isFixed) return bookmark.section === loc.section?.current
    const range = loc.range
    if (!range || bookmark.section !== loc.section?.current) return false
    try {
        const { anchor } = view.resolveNavigation(bookmark.cfi)
        const doc = range.startContainer.ownerDocument ?? range.startContainer
        const target = anchor(doc)
        const node = target instanceof Range ? target.startContainer : target
        const offset = target instanceof Range ? target.startOffset : 0
        return range.comparePoint(node, offset) === 0
    } catch {
        return false
    }
}

const updateBookmarkButton = () => {
    const marked = current?.bookmarks.some(b => isBookmarkOnPage(b, view?.lastLocation))
    $('#bookmark-btn').classList.toggle('active', !!marked)
}

const toggleBookmark = () => {
    const loc = view?.lastLocation
    if (!current || !loc) return
    const existing = current.bookmarks.filter(b => isBookmarkOnPage(b, loc))
    if (existing.length) {
        current.bookmarks = current.bookmarks.filter(b => !existing.includes(b))
        toast('已移除書籤', 1500)
    } else {
        const excerpt = (loc.range?.toString() ?? '').replace(/\s+/g, ' ').trim().slice(0, 80)
        current.bookmarks.push({
            cfi: loc.cfi,
            section: loc.section?.current,
            fraction: isFixed && loc.section ? loc.section.current / loc.section.total : loc.fraction,
            label: loc.tocItem?.label?.trim() || current.title,
            page: isFixed && loc.section ? loc.section.current + 1 : null,
            excerpt,
            createdAt: Date.now(),
        })
        current.bookmarks.sort((a, b) => a.fraction - b.fraction)
        toast('已加入書籤', 1500)
    }
    saveStore()
    renderBookmarks()
    updateBookmarkButton()
}

const renderBookmarks = () => {
    const list = $('#bookmark-list')
    const bookmarks = current?.bookmarks ?? []
    if (!bookmarks.length) {
        list.replaceChildren(el('li', { className: 'panel-empty', textContent: '尚無書籤，按 Ctrl+D 或上方書籤按鈕新增' }))
        return
    }
    const dateFormat = new Intl.DateTimeFormat('zh-TW', { dateStyle: 'short', timeStyle: 'short' })
    list.replaceChildren(...bookmarks.map(b => {
        const remove = el('button', { className: 'icon-btn', title: '刪除書籤' }, icon('trash'))
        remove.addEventListener('click', e => {
            e.stopPropagation()
            current.bookmarks = current.bookmarks.filter(x => x !== b)
            saveStore()
            renderBookmarks()
            updateBookmarkButton()
        })
        const where = b.page ? `第 ${b.page} 頁` : percent(b.fraction)
        const li = el('li', {},
            el('div', { className: 'bm-text' },
                el('div', { className: 'bm-label', textContent: b.label }),
                b.excerpt ? el('div', { className: 'bm-meta', textContent: b.excerpt }) : '',
                el('div', { className: 'bm-meta', textContent: `${where} · ${dateFormat.format(b.createdAt)}` })),
            remove)
        li.addEventListener('click', () => view?.goTo(b.cfi))
        return li
    }))
}

$('#bookmark-btn').addEventListener('click', toggleBookmark)

/* ---- 全文搜尋 ---- */

// foliate 的搜尋只支援可重排的書，PDF 改用 pdf.js 的文字內容搜尋
async function* searchPDF(pdf, query) {
    const needle = query.toLocaleLowerCase()
    for (let i = 1; i <= pdf.numPages; i++) {
        const page = await pdf.getPage(i)
        const { items } = await page.getTextContent()
        const text = items.map(item => item.str + (item.hasEOL ? ' ' : '')).join('')
        const lower = text.toLocaleLowerCase()
        const subitems = []
        for (let pos = lower.indexOf(needle); pos !== -1 && subitems.length < 100; pos = lower.indexOf(needle, pos + needle.length)) {
            subitems.push({
                target: i - 1,
                excerpt: {
                    pre: text.slice(Math.max(0, pos - 30), pos),
                    match: text.slice(pos, pos + needle.length),
                    post: text.slice(pos + needle.length, pos + needle.length + 30),
                },
            })
        }
        yield { progress: i / pdf.numPages }
        if (subitems.length) yield { label: `第 ${i} 頁`, subitems }
    }
    yield 'done'
}

const runSearch = async query => {
    const token = ++searchToken
    const results = $('#search-results')
    const status = $('#search-status')
    results.replaceChildren()
    view.clearSearch()
    if (!query) {
        status.textContent = ''
        return
    }
    let count = 0
    status.textContent = '搜尋中…'
    const iter = view.book.pdf ? searchPDF(view.book.pdf, query) : view.search({ query })
    try {
        for await (const result of iter) {
            if (token !== searchToken) break
            if (result === 'done') {
                status.textContent = count ? `共 ${count} 筆結果` : '找不到符合的內容'
            } else if (result.progress != null) {
                status.textContent = `搜尋中… ${percent(result.progress)}（已找到 ${count} 筆）`
            } else if (result.subitems) {
                count += result.subitems.length
                results.append(el('div', { className: 'search-group', textContent: result.label || '　' }))
                for (const { cfi, target, excerpt } of result.subitems) {
                    const hit = el('a', { className: 'search-hit' },
                        excerpt.pre, el('mark', { textContent: excerpt.match }), excerpt.post)
                    hit.addEventListener('click', () => view?.goTo(cfi ?? target))
                    results.append(hit)
                }
            }
        }
    } catch (e) {
        console.error(e)
        if (token === searchToken) status.textContent = `搜尋失敗：${e.message}`
    }
}

$('#search-form').addEventListener('submit', e => {
    e.preventDefault()
    if (view) runSearch($('#search-input').value.trim())
})
$('#search-input').addEventListener('input', e => {
    if (!e.target.value && view) {
        searchToken++
        view.clearSearch()
        $('#search-results').replaceChildren()
        $('#search-status').textContent = ''
    }
})

/* ---- 閱讀設定面板 ---- */

$('#settings-btn').addEventListener('click', () => {
    const panel = $('#settings-panel')
    panel.hidden = !panel.hidden
    $('#settings-btn').classList.toggle('active', !panel.hidden)
})
$$('#theme-swatches button').forEach(b =>
    b.addEventListener('click', () => updateSetting('theme', b.dataset.theme)))
$('#font-size').addEventListener('input', e => updateSetting('fontSize', parseInt(e.target.value)))
$$('[data-step="font-size"]').forEach(b =>
    b.addEventListener('click', () => changeFontSize(parseInt(b.dataset.delta))))
$('#line-height').addEventListener('input', e => updateSetting('lineHeight', parseFloat(e.target.value)))
$('#font-family').addEventListener('change', e => updateSetting('fontFamily', e.target.value))
$('#page-width').addEventListener('input', e => updateSetting('pageWidth', parseInt(e.target.value)))
$('#gap').addEventListener('input', e => updateSetting('gap', parseInt(e.target.value)))
$('#justify').addEventListener('change', e => updateSetting('justify', e.target.checked))
$('#zoom').addEventListener('change', e => updateSetting('zoom', e.target.value))
$('#pdf-spread').addEventListener('change', e => {
    updateSetting('pdfSpread', e.target.checked)
    // 頁面排列在開書時決定，需重新開啟（會回到目前位置）
    if (current && extOf(current.path) === 'pdf') openBook(current.path)
})
$$('#flow-seg button').forEach(b =>
    b.addEventListener('click', () => updateSetting('flow', b.dataset.value)))
$$('#columns-seg button').forEach(b =>
    b.addEventListener('click', () => updateSetting('columns', parseInt(b.dataset.value))))

/* ============ 書架工具列與拖放 ============ */

$('#open-btn').addEventListener('click', openDialog)
$('#lib-filter').addEventListener('input', renderLibrary)
$('#lib-sort').addEventListener('change', e => {
    store.settings.sort = e.target.value
    saveStore()
    renderLibrary()
})
$('#lib-theme').addEventListener('click', () =>
    updateSetting('theme', THEMES[store.settings.theme]?.dark ? 'light' : 'dark'))

const onDrop = e => {
    e.preventDefault()
    $('#drop-overlay').hidden = true
    const paths = Array.from(e.dataTransfer?.files ?? [], f => api.pathForFile(f)).filter(Boolean)
    if (paths.length) handleFiles(paths)
}
let dragDepth = 0
window.addEventListener('dragenter', e => {
    if (!e.dataTransfer?.types.includes('Files')) return
    dragDepth++
    $('#drop-overlay').hidden = false
})
window.addEventListener('dragleave', () => {
    if (--dragDepth <= 0) {
        dragDepth = 0
        $('#drop-overlay').hidden = true
    }
})
window.addEventListener('dragover', e => e.preventDefault())
window.addEventListener('drop', e => {
    dragDepth = 0
    onDrop(e)
})

window.addEventListener('beforeunload', () => {
    saveProgress()
    saveStore.flush()
})

api.onOpenFiles(paths => handleFiles(paths))

/* ============ 啟動 ============ */

const init = async () => {
    const saved = await api.loadStore()
    if (saved) {
        store = {
            books: (saved.books ?? []).map(b => ({ bookmarks: [], ...b })),
            settings: { ...DEFAULT_SETTINGS, ...saved.settings },
        }
    }
    applySettings()
    renderLibrary()
    checkMissingFiles()
    const pending = await api.pendingFiles()
    if (pending.length) handleFiles(pending)
}

init()
