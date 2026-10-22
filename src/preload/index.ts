import { contextBridge, ipcRenderer } from 'electron';
import { exposeMpvApi } from './mpv-api';

// Renderer-facing API. Everything goes through invoke/handle channels;
// the renderer never touches Node or Electron directly.
const api = {
  platform: process.platform,
  setHasSource: (value: boolean): Promise<void> =>
    ipcRenderer.invoke('app:setHasSource', value),
  desktopPath: (): Promise<string> => ipcRenderer.invoke('app:desktopPath'),
  debugSettings: (): Promise<{ tab: string | null; format: string | null }> =>
    ipcRenderer.invoke('app:debugSettings'),

  resolveFfmpeg: (): Promise<string | null> => ipcRenderer.invoke('bin:ffmpeg'),
  resolveYtDlp: (): Promise<string | null> => ipcRenderer.invoke('bin:ytdlp'),
  runProcess: (
    tool: 'ffmpeg' | 'yt-dlp',
    args: string[],
  ): Promise<{
    id: number;
    code: number | null;
    signal: string | null;
    stdout: string;
  }> => ipcRenderer.invoke('proc:run', tool, args),
  killProcess: (id: number): Promise<void> =>
    ipcRenderer.invoke('proc:kill', id),
  onProcessStart: (cb: (id: number) => void): (() => void) => {
    const listener = (_e: Electron.IpcRendererEvent, id: number) => cb(id);
    ipcRenderer.on('proc:start', listener);
    return () => ipcRenderer.removeListener('proc:start', listener);
  },
  onProcessLog: (cb: (id: number, text: string) => void): (() => void) => {
    const listener = (
      _e: Electron.IpcRendererEvent,
      id: number,
      text: string,
    ) => cb(id, text);
    ipcRenderer.on('proc:log', listener);
    return () => ipcRenderer.removeListener('proc:log', listener);
  },

  makeTempPath: (ext: string): Promise<string> =>
    ipcRenderer.invoke('temp:makePath', ext),
  writeText: (path: string, text: string): Promise<void> =>
    ipcRenderer.invoke('fs:writeText', path, text),
  deleteFiles: (paths: string[]): Promise<void> =>
    ipcRenderer.invoke('fs:deleteFiles', paths),
  statPath: (
    path: string,
  ): Promise<{
    exists: boolean;
    isDir: boolean;
    writable: boolean;
    size: number;
    mtimeMs: number;
  }> => ipcRenderer.invoke('fs:stat', path),
  openPath: (path: string): Promise<void> =>
    ipcRenderer.invoke('shell:openPath', path),
  showItemInFolder: (path: string): Promise<void> =>
    ipcRenderer.invoke('shell:showItemInFolder', path),

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

exposeMpvApi();
contextBridge.exposeInMainWorld('kurippu', api);

export type KurippuApi = typeof api;
