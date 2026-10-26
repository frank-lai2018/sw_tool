const { app, BrowserWindow, ipcMain, dialog, protocol, shell, Menu } = require('electron')
const path = require('node:path')
const fs = require('node:fs/promises')

const SRC_DIR = path.join(__dirname, 'src')
const BOOK_EXTENSIONS = ['epub', 'pdf', 'azw3', 'azw', 'mobi', 'prc', 'fb2', 'fbz', 'cbz']

const MIME_TYPES = {
    '.html': 'text/html; charset=utf-8',
    '.js': 'text/javascript; charset=utf-8',
    '.mjs': 'text/javascript; charset=utf-8',
    '.css': 'text/css; charset=utf-8',
    '.json': 'application/json',
    '.svg': 'image/svg+xml',
    '.png': 'image/png',
    '.ico': 'image/x-icon',
    '.bcmap': 'application/octet-stream',
    '.pfb': 'application/octet-stream',
    '.ttf': 'font/ttf',
}

// lets tests (or a portable setup) keep the library somewhere other than %APPDATA%
if (process.env.EBOOK_READER_USER_DATA) app.setPath('userData', path.resolve(process.env.EBOOK_READER_USER_DATA))

protocol.registerSchemesAsPrivileged([{
    scheme: 'app',
    privileges: { standard: true, secure: true, supportFetchAPI: true, corsEnabled: true, stream: true },
}])

const storePath = () => path.join(app.getPath('userData'), 'library.json')
const windowStatePath = () => path.join(app.getPath('userData'), 'window.json')

const isBookPath = p => typeof p === 'string'
    && BOOK_EXTENSIONS.includes(path.extname(p).slice(1).toLowerCase())

const booksFromArgv = argv => argv.slice(1)
    .filter(arg => !arg.startsWith('-') && isBookPath(arg))
    .map(arg => path.resolve(arg))

const readJSON = async (file, fallback) => {
    try {
        return JSON.parse(await fs.readFile(file, 'utf8'))
    } catch {
        return fallback
    }
}

// write to a temp file first so a crash mid-write never corrupts the library
const writeJSON = async (file, data) => {
    const tmp = file + '.tmp'
    await fs.writeFile(tmp, JSON.stringify(data))
    await fs.rename(tmp, file)
}

let mainWindow = null
let pendingFiles = booksFromArgv(process.argv)

const sendOpenFiles = files => {
    if (!files.length || !mainWindow) return
    mainWindow.webContents.send('open-files', files)
}

const createWindow = async () => {
    const state = await readJSON(windowStatePath(), { width: 1280, height: 860 })
    mainWindow = new BrowserWindow({
        width: state.width,
        height: state.height,
        x: state.x,
        y: state.y,
        minWidth: 640,
        minHeight: 480,
        show: false,
        backgroundColor: '#f7f5f0',
        title: '電子書閱讀器',
        icon: path.join(SRC_DIR, 'icon.png'),
        webPreferences: {
            preload: path.join(__dirname, 'preload.cjs'),
            contextIsolation: true,
            nodeIntegration: false,
            sandbox: true,
        },
    })
    if (state.maximized) mainWindow.maximize()
    mainWindow.once('ready-to-show', () => mainWindow.show())

    mainWindow.on('close', () => {
        const bounds = mainWindow.getNormalBounds()
        writeJSON(windowStatePath(), { ...bounds, maximized: mainWindow.isMaximized() })
            .catch(() => {})
    })
    mainWindow.on('closed', () => mainWindow = null)
    mainWindow.on('enter-full-screen', () => mainWindow.webContents.send('fullscreen', true))
    mainWindow.on('leave-full-screen', () => mainWindow.webContents.send('fullscreen', false))

    // links inside books open in the system browser; never navigate the app window
    mainWindow.webContents.setWindowOpenHandler(({ url }) => {
        if (/^(https?|mailto):/i.test(url)) shell.openExternal(url)
        return { action: 'deny' }
    })
    mainWindow.webContents.on('will-navigate', e => e.preventDefault())

    mainWindow.webContents.on('before-input-event', (_, input) => {
        if (input.type === 'keyDown' && input.key === 'F12' && !app.isPackaged)
            mainWindow.webContents.toggleDevTools()
    })

    await mainWindow.loadURL('app://reader/index.html')
}

const serveApp = async request => {
    const { pathname } = new URL(request.url)
    const file = path.normalize(path.join(SRC_DIR, decodeURIComponent(pathname)))
    if (!file.startsWith(SRC_DIR + path.sep)) return new Response('Forbidden', { status: 403 })
    try {
        const data = await fs.readFile(file)
        const type = MIME_TYPES[path.extname(file).toLowerCase()] ?? 'application/octet-stream'
        return new Response(data, { headers: { 'content-type': type, 'cache-control': 'no-cache' } })
    } catch {
        return new Response('Not Found', { status: 404 })
    }
}

const registerIPC = () => {
    ipcMain.handle('dialog:open', async () => {
        const { canceled, filePaths } = await dialog.showOpenDialog(mainWindow, {
            title: '開啟電子書',
            properties: ['openFile', 'multiSelections'],
            filters: [
                { name: '電子書', extensions: BOOK_EXTENSIONS },
                { name: '所有檔案', extensions: ['*'] },
            ],
        })
        return canceled ? [] : filePaths
    })

    ipcMain.handle('file:read', async (_, filePath) => {
        if (!isBookPath(filePath)) throw new Error('不支援的檔案格式')
        const data = await fs.readFile(filePath)
        return { name: path.basename(filePath), data }
    })

    ipcMain.handle('file:exists', async (_, filePath) => {
        try {
            return (await fs.stat(filePath)).isFile()
        } catch {
            return false
        }
    })

    ipcMain.handle('file:show', (_, filePath) => shell.showItemInFolder(filePath))

    ipcMain.handle('store:load', () => readJSON(storePath(), null))
    ipcMain.handle('store:save', (_, data) => writeJSON(storePath(), data))

    ipcMain.handle('app:pending-files', () => {
        const files = pendingFiles
        pendingFiles = []
        return files
    })

    ipcMain.handle('window:toggle-fullscreen', () => {
        mainWindow.setFullScreen(!mainWindow.isFullScreen())
    })
    ipcMain.handle('window:set-fullscreen', (_, value) => mainWindow.setFullScreen(!!value))
}

if (!app.requestSingleInstanceLock()) {
    app.quit()
} else {
    app.on('second-instance', (_, argv) => {
        if (!mainWindow) return
        if (mainWindow.isMinimized()) mainWindow.restore()
        mainWindow.focus()
        sendOpenFiles(booksFromArgv(argv))
    })

    app.whenReady().then(() => {
        Menu.setApplicationMenu(null)
        protocol.handle('app', serveApp)
        registerIPC()
        createWindow()
    })

    app.on('window-all-closed', () => app.quit())
}
