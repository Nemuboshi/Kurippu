import type { WebContents } from 'electron';
import { describe, expect, it } from 'vitest';
import { killProcess, runProcess } from './process';

describe('process service', () => {
  it('kills a running child process', async () => {
    let startedId: number | null = null;
    const sender = {
      id: 101,
      isDestroyed: () => false,
      send: (_channel: string, id: number) => {
        startedId = id;
      },
    } as unknown as WebContents;
    const handle = runProcess(sender, process.execPath, [
      '-e',
      'setTimeout(() => {}, 60000)',
    ]);

    expect(startedId).toBe(handle.id);
    killProcess(sender, handle.id);
    const result = await handle.done;

    expect(result.code === 0 && result.signal == null).toBe(false);
  }, 10000);
});
