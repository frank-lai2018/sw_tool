const { contextBridge, ipcRenderer, webUtils } = require('electron');
const subscribe = (event, callback) => { ipcRenderer.on(event, (_, data) => callback(data)); };
contextBridge.exposeInMainWorld('reader', {
  initialize: () => ipcRenderer.invoke('initialize'),
  chooseFile: () => ipcRenderer.invoke('choose-file'),
  newWindow: () => ipcRenderer.invoke('new-window'),
  openFile: file => ipcRenderer.invoke('open-file', file),
  droppedPath: file => webUtils.getPathForFile(file),
  reload: () => ipcRenderer.invoke('reload-document'),
  image: source => ipcRenderer.invoke('local-image', source),
  preferences: prefs => ipcRenderer.invoke('save-preferences', prefs),
  openLink: href => ipcRenderer.invoke('open-link', href),
  print: () => ipcRenderer.invoke('print'),
  pagination: state => ipcRenderer.invoke('pagination', state),
  onDocument: callback => subscribe('document', callback),
  onSettings: callback => subscribe('settings', callback),
  onNotice: callback => subscribe('notice', callback),
  onSearch: callback => subscribe('focus-search', callback),
  onTheme: callback => subscribe('toggle-theme', callback)
});
