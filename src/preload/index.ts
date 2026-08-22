import { contextBridge, ipcRenderer } from 'electron';
import type { PixelFitApi } from '../shared/ipc';

const api: PixelFitApi = {
  library: {
    stats: () => ipcRenderer.invoke('library:stats'),
    listAssets: () => ipcRenderer.invoke('library:listAssets'),
    updateAsset: (id, patch) => ipcRenderer.invoke('library:updateAsset', id, patch),
    deleteAsset: (id) => ipcRenderer.invoke('library:deleteAsset', id),
    importFiles: () => ipcRenderer.invoke('library:importFiles'),
    importPack: () => ipcRenderer.invoke('library:importPack'),
    resetLibrary: () => ipcRenderer.invoke('library:reset'),
    backup: () => ipcRenderer.invoke('library:backup'),
    revealRoot: () => ipcRenderer.invoke('library:revealRoot'),
  },
  looks: {
    list: () => ipcRenderer.invoke('looks:list'),
    save: (look, coverDataUrl) => ipcRenderer.invoke('looks:save', look, coverDataUrl),
    delete: (id) => ipcRenderer.invoke('looks:delete', id),
  },
  base: {
    get: (body) => ipcRenderer.invoke('base:get', body),
    importPhoto: (body) => ipcRenderer.invoke('base:importPhoto', body),
    reset: (body) => ipcRenderer.invoke('base:reset', body),
  },
  pipeline: {
    status: () => ipcRenderer.invoke('pipeline:status'),
    importPhotos: () => ipcRenderer.invoke('pipeline:importPhotos'),
  },
  link: {
    import: (url) => ipcRenderer.invoke('link:import', url),
  },
  tryOn: {
    status: () => ipcRenderer.invoke('tryon:status'),
    settings: () => ipcRenderer.invoke('tryon:settings'),
    saveSettings: (update) => ipcRenderer.invoke('tryon:saveSettings', update),
    generate: (request) => ipcRenderer.invoke('tryon:generate', request),
    cancel: (clientRequestId) => ipcRenderer.send('tryon:cancel', clientRequestId),
  },
  rules: {
    occlusion: () => ipcRenderer.invoke('rules:occlusion'),
  },
  update: {
    state: () => ipcRenderer.invoke('update:state'),
    check: () => ipcRenderer.invoke('update:check'),
    download: () => ipcRenderer.invoke('update:download'),
    install: () => ipcRenderer.send('update:install'),
    openReleasePage: () => ipcRenderer.send('update:openReleasePage'),
    setCheckOnLaunch: (enabled) => ipcRenderer.invoke('update:setCheckOnLaunch', enabled),
    onState: (listener) => {
      const handler = (_event: unknown, state: Parameters<typeof listener>[0]) => listener(state);
      ipcRenderer.on('update:state', handler);
      return () => { ipcRenderer.off('update:state', handler); };
    },
  },
  modelPack: {
    state: () => ipcRenderer.invoke('modelPack:state'),
    download: () => ipcRenderer.invoke('modelPack:download'),
    cancel: () => ipcRenderer.invoke('modelPack:cancel'),
    redownload: () => ipcRenderer.invoke('modelPack:redownload'),
    onState: (listener) => {
      const handler = (_event: unknown, state: Parameters<typeof listener>[0]) => listener(state);
      ipcRenderer.on('modelPack:state', handler);
      return () => { ipcRenderer.off('modelPack:state', handler); };
    },
  },
  app: {
    version: () => ipcRenderer.invoke('app:version'),
  },
  exportPng: (req) => ipcRenderer.invoke('export:png', req),
  window: {
    minimize: () => ipcRenderer.send('window:minimize'),
    toggleMaximize: () => ipcRenderer.send('window:toggleMaximize'),
    close: () => ipcRenderer.send('window:close'),
    isMaximized: () => ipcRenderer.invoke('window:isMaximized'),
  },
};

contextBridge.exposeInMainWorld('pixelfit', api);
