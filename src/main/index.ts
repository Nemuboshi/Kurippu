import { join } from 'node:path';
import { app, BrowserWindow, dialog, globalShortcut, Menu } from 'electron';
import { debugEnabled } from './binaries';
import { registerIpc } from './ipc';
import { createMpvMain } from './mpv-service';

const DEBUG = debugEnabled();
let mainWindow: BrowserWindow | null = null;
let hasSource = false;
let quitting = false;
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

  if (process.env.ELECTRON_RENDERER_URL)
    win.loadURL(process.env.ELECTRON_RENDERER_URL);
  else win.loadFile(join(import.meta.dirname, '../renderer/index.html'));
  mpv.attachWindow(win);
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

app.on('before-quit', () => {
  quitting = true;
  void mpv.dispose();
});
app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') app.quit();
});
