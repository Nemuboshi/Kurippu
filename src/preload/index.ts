import { contextBridge } from 'electron';

// Placeholder bridge. Real IPC surface lands with the main-process services task.
const api = {
  platform: process.platform,
};

contextBridge.exposeInMainWorld('kurippu', api);

export type KurippuApi = typeof api;
