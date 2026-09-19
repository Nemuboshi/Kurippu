import { type ChildProcess, spawn } from 'node:child_process';
import type { WebContents } from 'electron';

// Process service. Spawns ffmpeg/yt-dlp, streams stdout+stderr to the
// renderer over IPC, kills with SIGKILL.

export interface RunHandle {
  id: number;
  proc: ChildProcess;
}

let nextId = 1;
const running = new Map<number, ChildProcess>();

export interface RunResult {
  code: number | null;
  signal: string | null;
  stdout: string;
}

export function runProcess(
  sender: WebContents,
  bin: string,
  args: string[],
): { id: number; done: Promise<RunResult> } {
  const id = nextId++;
  const proc = spawn(bin, args, { stdio: ['ignore', 'pipe', 'pipe'] });
  running.set(id, proc);
  // The invocation only resolves when the process exits, so the id is
  // pushed separately; the renderer needs it to cancel a running process.
  if (!sender.isDestroyed()) sender.send('proc:start', id);
  let stdout = '';
  const onLog = (chunk: Buffer) => {
    const text = chunk.toString('utf8');
    if (!sender.isDestroyed()) sender.send('proc:log', id, text);
  };
  proc.stdout?.on('data', (chunk: Buffer) => {
    stdout += chunk.toString('utf8');
    onLog(chunk);
  });
  proc.stderr?.on('data', onLog);
  const done = new Promise<RunResult>((resolve) => {
    proc.once('close', (code: number | null, signal: NodeJS.Signals | null) => {
      running.delete(id);
      resolve({ code, signal, stdout });
    });
  });
  return { id, done };
}

// SIGKILL everywhere; we never wait for graceful exits.
export function killProcess(id: number): void {
  running.get(id)?.kill('SIGKILL');
}

export function killAll(): void {
  for (const proc of running.values()) proc.kill('SIGKILL');
}
