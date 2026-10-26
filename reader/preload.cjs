const { contextBridge, ipcRenderer, webUtils } = require('electron')

contextBridge.exposeInMainWorld('api', {
    openDialog: () => ipcRenderer.invoke('dialog:open'),
    readFile: filePath => ipcRenderer.invoke('file:read', filePath),
    fileExists: filePath => ipcRenderer.invoke('file:exists', filePath),
    showInFolder: filePath => ipcRenderer.invoke('file:show', filePath),
    loadStore: () => ipcRenderer.invoke('store:load'),
    saveStore: data => ipcRenderer.invoke('store:save', data),
    pendingFiles: () => ipcRenderer.invoke('app:pending-files'),
    toggleFullscreen: () => ipcRenderer.invoke('window:toggle-fullscreen'),
    setFullscreen: value => ipcRenderer.invoke('window:set-fullscreen', value),
    pathForFile: file => webUtils.getPathForFile(file),
    onOpenFiles: callback => ipcRenderer.on('open-files', (_, files) => callback(files)),
    onFullscreen: callback => ipcRenderer.on('fullscreen', (_, value) => callback(value)),
})
