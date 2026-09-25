import { constants } from 'node:fs';
import { access, mkdtemp, stat, unlink, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { isAbsolute, join } from 'node:path';
import {
  app,
  type BrowserWindow,
  dialog,
  type IpcMainInvokeEvent,
  ipcMain,
  shell,
} from 'electron';
import { FILE_EXTENSIONS } from '../engine/constants';
import { debugValue, resolveFfmpeg, resolveYtDlp } from './binaries';
import { killProcess, runProcess } from './process';
import {
  isRegisteredTempFile,
  isTempPath,
  registerTempFile,
} from './tempfiles';

type ProcessTool = 'ffmpeg' | 'yt-dlp';

const EMPTY_STAT = {
  exists: false,
  isDir: false,
  writable: false,
  size: 0,
  mtimeMs: 0,
};

function assertTrustedSender(
  event: IpcMainInvokeEvent,
  getWindow: () => BrowserWindow | null,
): BrowserWindow {
  const win = getWindow();
  if (!win || win.isDestroyed() || event.sender !== win.webContents) {
    throw new Error('IPC request came from an untrusted renderer');
  }
  return win;
}

function stringValue(value: unknown, name: string, maxLength = 4096): string {
  if (typeof value !== 'string' || value.length > maxLength) {
    throw new Error(`Invalid ${name}`);
  }
  return value;
}

function absolutePath(value: unknown, name: string): string {
  const path = stringValue(value, name);
  if (!path || path.includes('\0') || !isAbsolute(path)) {
    throw new Error(`Invalid ${name}`);
  }
  return path;
}

function processArguments(value: unknown): string[] {
  if (!Array.isArray(value) || value.length > 4096) {
    throw new Error('Invalid process arguments');
  }
  return value.map((argument) => stringValue(argument, 'process argument'));
}

function resolveProcessTool(value: unknown): {
  tool: ProcessTool;
  path: string;
} {
  if (value === 'ffmpeg') {
    const path = resolveFfmpeg();
    if (!path) throw new Error('ffmpeg was not found');
    return { tool: value, path };
  }
  if (value === 'yt-dlp') {
    const path = resolveYtDlp();
    if (!path) throw new Error('yt-dlp was not found');
    return { tool: value, path };
  }
  throw new Error('Unsupported process tool');
}

function filtersValue(
  value: unknown,
): { name: string; extensions: string[] }[] {
  if (!Array.isArray(value) || value.length > 16) {
    throw new Error('Invalid dialog filters');
  }
  return value.map((filter) => {
    if (!filter || typeof filter !== 'object') {
      throw new Error('Invalid dialog filter');
    }
    const item = filter as { name?: unknown; extensions?: unknown };
    if (
      typeof item.name !== 'string' ||
      !Array.isArray(item.extensions) ||
      item.extensions.length > 32 ||
      item.extensions.some(
        (extension) => typeof extension !== 'string' || extension.length > 32,
      )
    ) {
      throw new Error('Invalid dialog filter');
    }
    return {
      name: item.name,
      extensions: item.extensions as string[],
    };
  });
}

// Wires every IPC channel exposed by the preload bridge.
export function registerIpc(
  getWindow: () => BrowserWindow | null,
  setHasSource: (value: boolean) => void,
): void {
  ipcMain.handle('app:setHasSource', (event, value: unknown) => {
    assertTrustedSender(event, getWindow);
    if (typeof value !== 'boolean') throw new Error('Invalid source state');
    setHasSource(value);
  });
  ipcMain.handle('app:desktopPath', (event) => {
    assertTrustedSender(event, getWindow);
    return app.getPath('desktop');
  });
  ipcMain.handle('app:debugSettings', (event) => {
    assertTrustedSender(event, getWindow);
    return {
      tab: debugValue('TAB') ?? null,
      format: debugValue('FORMAT') ?? null,
    };
  });
  ipcMain.handle('bin:ffmpeg', (event) => {
    assertTrustedSender(event, getWindow);
    return resolveFfmpeg();
  });
  ipcMain.handle('bin:ytdlp', (event) => {
    assertTrustedSender(event, getWindow);
    return resolveYtDlp();
  });

  ipcMain.handle('proc:run', (event, tool: unknown, args: unknown) => {
    assertTrustedSender(event, getWindow);
    const processTool = resolveProcessTool(tool);
    const { id, done } = runProcess(
      event.sender,
      processTool.path,
      processArguments(args),
    );
    return done.then((result) => ({ id, ...result }));
  });
  ipcMain.handle('proc:kill', (event, id: unknown) => {
    assertTrustedSender(event, getWindow);
    if (typeof id !== 'number' || !Number.isSafeInteger(id) || id < 1) {
      throw new Error('Invalid process id');
    }
    killProcess(event.sender, id);
  });

  ipcMain.handle('temp:makePath', async (event, ext: unknown) => {
    assertTrustedSender(event, getWindow);
    const suffix = stringValue(ext, 'temporary file extension', 16);
    if (!/^(?:\.[a-z0-9]+|-[a-z0-9]+(?:\.[a-z0-9]+)?)$/i.test(suffix)) {
      throw new Error('Invalid temporary file extension');
    }
    const directory = await mkdtemp(join(tmpdir(), 'kurippu-'));
    const path = join(directory, `download${suffix}`);
    registerTempFile(path);
    return path;
  });

  ipcMain.handle(
    'fs:writeText',
    async (event, file: unknown, text: unknown) => {
      assertTrustedSender(event, getWindow);
      const path = absolutePath(file, 'temporary file path');
      const value = stringValue(text, 'text', 1024 * 1024);
      if (!isRegisteredTempFile(path)) {
        throw new Error('Text can only be written to an application temp file');
      }
      await writeFile(path, value, 'utf8');
    },
  );
  ipcMain.handle('fs:deleteFiles', async (event, paths: unknown) => {
    assertTrustedSender(event, getWindow);
    if (!Array.isArray(paths) || paths.length > 64) {
      throw new Error('Invalid temporary file list');
    }
    const safePaths = paths.map((file) => absolutePath(file, 'temporary path'));
    if (safePaths.some((file) => !isTempPath(file))) {
      throw new Error(
        'Files outside the application temp directory cannot be deleted',
      );
    }
    await Promise.allSettled(safePaths.map((file) => unlink(file)));
  });
  ipcMain.handle('fs:stat', async (event, file: unknown) => {
    assertTrustedSender(event, getWindow);
    const path = stringValue(file, 'path');
    if (path.includes('\0')) throw new Error('Invalid path');
    if (!path) return EMPTY_STAT;
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
      return EMPTY_STAT;
    }
  });
  ipcMain.handle('shell:openPath', async (event, file: unknown) => {
    assertTrustedSender(event, getWindow);
    const path = absolutePath(file, 'path');
    const error = await shell.openPath(path);
    if (error) throw new Error(error);
  });
  ipcMain.handle('shell:showItemInFolder', (event, file: unknown) => {
    assertTrustedSender(event, getWindow);
    shell.showItemInFolder(absolutePath(file, 'path'));
  });

  ipcMain.handle('dialog:openVideo', async (event) => {
    const win = assertTrustedSender(event, getWindow);
    const result = await dialog.showOpenDialog(win, {
      properties: ['openFile'],
      filters: [
        { name: 'Videos', extensions: FILE_EXTENSIONS },
        { name: 'All files', extensions: ['*'] },
      ],
    });
    return result.canceled ? null : (result.filePaths[0] ?? null);
  });

  ipcMain.handle(
    'dialog:saveOutput',
    async (event, defaultPath: unknown, container: unknown) => {
      const win = assertTrustedSender(event, getWindow);
      if (container !== 'mp4' && container !== 'webm') {
        throw new Error('Invalid output container');
      }
      const result = await dialog.showSaveDialog(win, {
        defaultPath: stringValue(defaultPath, 'default output path'),
        filters: [{ name: container.toUpperCase(), extensions: [container] }],
      });
      return result.canceled ? null : result.filePath;
    },
  );

  ipcMain.handle('dialog:openFile', async (event, value: unknown) => {
    const win = assertTrustedSender(event, getWindow);
    const result = await dialog.showOpenDialog(win, {
      properties: ['openFile'],
      filters: filtersValue(value),
    });
    return result.canceled ? null : (result.filePaths[0] ?? null);
  });

  ipcMain.handle('dialog:confirm', async (event, message: unknown) => {
    const win = assertTrustedSender(event, getWindow);
    const result = await dialog.showMessageBox(win, {
      type: 'warning',
      buttons: ['Cancel', 'OK'],
      defaultId: 0,
      cancelId: 0,
      message: stringValue(message, 'confirmation message', 4096),
    });
    return result.response === 1;
  });
}
