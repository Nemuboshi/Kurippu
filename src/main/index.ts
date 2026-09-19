import { join } from 'node:path';
import { app, BrowserWindow, globalShortcut } from 'electron';
import { createMpvMain } from 'electron-mpv-video/main';
import { registerIpc } from './ipc';

const DEBUG = Boolean(process.env.BORAM_DEBUG);

let mainWindow: BrowserWindow | null = null;
const mpv = createMpvMain();

function createWindow(): void {
  const win = new BrowserWindow({
    width: 960,
    height: 960,
    minWidth: 640,
    minHeight: 640,
    autoHideMenuBar: true,
    webPreferences: {
      preload: join(import.meta.dirname, '../preload/index.js'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: false,
    },
  });

  if (process.env.ELECTRON_RENDERER_URL) {
    win.loadURL(process.env.ELECTRON_RENDERER_URL);
  } else {
    win.loadFile(join(import.meta.dirname, '../renderer/index.html'));
  }
  mpv.attachWindow(win);
  mainWindow = win;
  win.on('closed', () => {
    mainWindow = null;
  });
}

app.whenReady().then(() => {
  registerIpc(() => mainWindow);
  if (DEBUG) {
    globalShortcut.register('F12', () => {
      BrowserWindow.getFocusedWindow()?.webContents.toggleDevTools();
    });
    globalShortcut.register('CommandOrControl+R', () => {
      BrowserWindow.getFocusedWindow()?.webContents.reload();
    });
  }
  createWindow();
  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow();
  });
});

app.on('before-quit', () => {
  void mpv.dispose();
});

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') app.quit();
});
