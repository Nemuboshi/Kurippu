import { contextBridge, ipcRenderer } from 'electron';

// Renderer-facing API. Everything goes through invoke/handle channels;
// the renderer never touches Node or Electron directly.
const api = {
  platform: process.platform,

  resolveFfmpeg: (): Promise<string | null> => ipcRenderer.invoke('bin:ffmpeg'),
  resolveYtDlp: (): Promise<string | null> => ipcRenderer.invoke('bin:ytdlp'),

  runProcess: (
    bin: string,
    args: string[],
  ): Promise<{
    id: number;
    code: number | null;
    signal: string | null;
    stdout: string;
  }> => ipcRenderer.invoke('proc:run', bin, args),
  killProcess: (id: number): Promise<void> =>
    ipcRenderer.invoke('proc:kill', id),
  onProcessLog: (cb: (id: number, text: string) => void): (() => void) => {
    const listener = (
      _e: Electron.IpcRendererEvent,
      id: number,
      text: string,
    ) => cb(id, text);
    ipcRenderer.on('proc:log', listener);
    return () => ipcRenderer.removeListener('proc:log', listener);
  },

  registerTempFile: (path: string): Promise<void> =>
    ipcRenderer.invoke('temp:register', path),
  unregisterTempFile: (path: string): Promise<void> =>
    ipcRenderer.invoke('temp:unregister', path),

  openVideoDialog: (): Promise<string | null> =>
    ipcRenderer.invoke('dialog:openVideo'),
  saveOutputDialog: (
    defaultPath: string,
    container: 'mp4' | 'webm',
  ): Promise<string | null> =>
    ipcRenderer.invoke('dialog:saveOutput', defaultPath, container),
  openFileDialog: (
    filters: { name: string; extensions: string[] }[],
  ): Promise<string | null> => ipcRenderer.invoke('dialog:openFile', filters),
  confirmDialog: (message: string): Promise<boolean> =>
    ipcRenderer.invoke('dialog:confirm', message),
};

contextBridge.exposeInMainWorld('kurippu', api);

export type KurippuApi = typeof api;
