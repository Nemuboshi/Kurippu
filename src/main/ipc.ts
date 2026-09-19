import { constants } from 'node:fs';
import { access, mkdtemp, stat, unlink, writeFile } from 'node:fs/promises';
import { homedir, tmpdir } from 'node:os';
import { join } from 'node:path';
import { app, type BrowserWindow, dialog, ipcMain, shell } from 'electron';
import { FILE_EXTENSIONS } from '../engine/constants';
import { debugValue, resolveFfmpeg, resolveYtDlp } from './binaries';
import { killAll, killProcess, runProcess } from './process';
import {
  cleanupTempFiles,
  registerTempFile,
  unregisterTempFile,
} from './tempfiles';

// Wires every ipcMain.handle channel the renderer can call.
export function registerIpc(
  getWindow: () => BrowserWindow | null,
  setHasSource: (value: boolean) => void,
): void {
  ipcMain.handle('app:setHasSource', (_e, value: boolean) =>
    setHasSource(value),
  );
  ipcMain.handle('app:desktopPath', () => app.getPath('desktop'));
  ipcMain.handle('app:debugSettings', () => ({
    tab: debugValue('TAB') ?? null,
    format: debugValue('FORMAT') ?? null,
  }));
  ipcMain.handle('bin:ffmpeg', () => resolveFfmpeg());
  ipcMain.handle('bin:ytdlp', () => resolveYtDlp());

  ipcMain.handle('proc:run', (e, bin: string, args: string[]) => {
    const { id, done } = runProcess(e.sender, bin, args);
    return done.then((result) => ({ id, ...result }));
  });
  ipcMain.handle('proc:kill', (_e, id: number) => killProcess(id));

  ipcMain.handle('temp:register', (_e, path: string) => registerTempFile(path));
  ipcMain.handle('temp:unregister', (_e, path: string) =>
    unregisterTempFile(path),
  );

  ipcMain.handle('temp:makePath', async (_e, ext: string) => {
    const dir = await mkdtemp(join(tmpdir(), 'boram-'));
    const path = join(dir, `download${ext}`);
    registerTempFile(path);
    return path;
  });

  ipcMain.handle('fs:writeText', (_e, path: string, text: string) =>
    writeFile(path, text, 'utf8'),
  );
  ipcMain.handle('fs:deleteFiles', async (_e, paths: string[]) => {
    await Promise.allSettled(paths.map((p) => unlink(p)));
  });
  ipcMain.handle('fs:stat', async (_e, path: string) => {
    try {
      const info = await stat(path);
      let writable = true;
      try {
        await access(path, constants.W_OK);
      } catch {
        writable = false;
      }
      return {
        exists: true,
        isDir: info.isDirectory(),
        writable,
        size: info.size,
        mtimeMs: info.mtimeMs,
      };
    } catch {
      return {
        exists: false,
        isDir: false,
        writable: false,
        size: 0,
        mtimeMs: 0,
      };
    }
  });
  ipcMain.handle('shell:openPath', async (_e, path: string) => {
    const error = await shell.openPath(path);
    if (error) throw new Error(error);
  });
  ipcMain.handle('shell:showItemInFolder', (_e, path: string) =>
    shell.showItemInFolder(path),
  );

  ipcMain.handle('dialog:openVideo', async () => {
    const win = getWindow();
    if (!win) return null;
    const r = await dialog.showOpenDialog(win, {
      properties: ['openFile'],
      filters: [
        { name: 'Videos', extensions: FILE_EXTENSIONS },
        { name: 'All files', extensions: ['*'] },
      ],
    });
    return r.canceled ? null : (r.filePaths[0] ?? null);
  });

  ipcMain.handle(
    'dialog:saveOutput',
    async (_e, defaultPath: string, container: 'mp4' | 'webm') => {
      const win = getWindow();
      if (!win) return null;
      const r = await dialog.showSaveDialog(win, {
        defaultPath,
        filters: [{ name: container.toUpperCase(), extensions: [container] }],
      });
      return r.canceled ? null : r.filePath;
    },
  );

  ipcMain.handle(
    'dialog:openFile',
    async (_e, filters: { name: string; extensions: string[] }[]) => {
      const win = getWindow();
      if (!win) return null;
      const r = await dialog.showOpenDialog(win, {
        properties: ['openFile'],
        filters,
      });
      return r.canceled ? null : (r.filePaths[0] ?? null);
    },
  );

  ipcMain.handle('dialog:confirm', async (_e, message: string) => {
    const win = getWindow();
    if (!win) return false;
    const r = await dialog.showMessageBox(win, {
      type: 'warning',
      buttons: ['Cancel', 'OK'],
      defaultId: 0,
      cancelId: 0,
      message,
    });
    return r.response === 1;
  });

  app.on('will-quit', (e) => {
    killAll();
    e.preventDefault();
    void cleanupTempFiles().finally(() => app.exit(0));
  });
}
