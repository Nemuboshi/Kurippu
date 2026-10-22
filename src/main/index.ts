import { join } from 'node:path';
import { app, BrowserWindow, dialog, globalShortcut, Menu } from 'electron';
import { debugEnabled, noHardwareAcceleration } from './binaries';
import { registerIpc } from './ipc';
import { createMpvMain } from './mpv-service';
import { killAll } from './process';
import { cleanupTempFiles } from './tempfiles';

const DEBUG = debugEnabled();
const hasInstanceLock = app.requestSingleInstanceLock();
let mainWindow: BrowserWindow | null = null;
let hasSource = false;
let quitting = false;
let shutdownPromise: Promise<void> | null = null;
const mpv = hasInstanceLock ? createMpvMain() : null;

if (!hasInstanceLock) {
  app.quit();
} else {
  app.on('second-instance', () => {
    const win = mainWindow;
    if (!win || win.isDestroyed()) return;
    if (win.isMinimized()) win.restore();
    win.focus();
  });
}

if (noHardwareAcceleration()) app.disableHardwareAcceleration();

function createWindow(): void {
  const win = new BrowserWindow({
    width: 960,
    height: 960,
    minWidth: 640,
    minHeight: 640,
    autoHideMenuBar: true,
    webPreferences: {
      preload: join(import.meta.dirname, '../preload/index.cjs'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
    },
  });

  if (process.env.ELECTRON_RENDERER_URL)
    win.loadURL(process.env.ELECTRON_RENDERER_URL);
  else win.loadFile(join(import.meta.dirname, '../renderer/index.html'));
  mpv?.attachWindow(win);
  win.webContents.on('context-menu', (_event, params) => {
    if (!params.isEditable) return;
    Menu.buildFromTemplate([
      { role: 'undo' },
      { role: 'redo' },
      { type: 'separator' },
      { role: 'cut' },
      { role: 'copy' },
      { role: 'paste' },
      { type: 'separator' },
      { role: 'selectAll' },
    ]).popup({ window: win });
  });
  mainWindow = win;
  win.on('close', (event) => {
    if (quitting || !hasSource) return;
    const answer = dialog.showMessageBoxSync(win, {
      type: 'warning',
      buttons: ['Cancel', 'Quit'],
      defaultId: 0,
      cancelId: 0,
      message: 'Discard source and quit?',
    });
    if (answer === 0) event.preventDefault();
    else quitting = true;
  });
  win.on('closed', () => {
    mainWindow = null;
  });
}

if (hasInstanceLock) {
  app.whenReady().then(() => {
    registerIpc(
      () => mainWindow,
      (value: boolean) => {
        hasSource = value;
      },
    );
    if (DEBUG) {
      globalShortcut.register('F12', () =>
        BrowserWindow.getFocusedWindow()?.webContents.toggleDevTools(),
      );
      globalShortcut.register('CommandOrControl+R', () =>
        BrowserWindow.getFocusedWindow()?.webContents.reload(),
      );
    }
    createWindow();
    app.on('activate', () => {
      if (BrowserWindow.getAllWindows().length === 0) createWindow();
    });
  });
}

app.on('before-quit', (event) => {
  if (shutdownPromise) {
    event.preventDefault();
    return;
  }
  event.preventDefault();
  quitting = true;
  shutdownPromise = (async () => {
    await mpv?.dispose();
    killAll();
    await cleanupTempFiles();
  })()
    .catch((error: unknown) => {
      console.error('Shutdown cleanup failed', error);
    })
    .finally(() => app.exit(0));
});
app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') app.quit();
});
