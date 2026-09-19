// Encode orchestration: spawns ffmpeg through the preload bridge, feeds
// the progress bar from its output, and appends result stats to the log.

import { feedProgressLine, initProgress } from '../../engine/bitrate';
import { getEncodeArgs, getTestArgs } from '../../engine/ffmpeg';
import { validateOutputDir, validateOutputFile } from '../../engine/output';
import { showTime } from '../../engine/time';

export interface EncodeRequest {
  kind: 'test' | 'normal';
  rawArgs: string;
  title: string;
  outpath: string;
  twoPass: boolean;
  container: 'mp4' | 'webm';
  duration: number;
  avgFrameRate: string;
}

export interface EncodeEvents {
  log: (line: string) => void;
  progress: (percent: number) => void;
  finished: (outpath: string) => void;
}

// True while an encode runs. Exported as an object with a $state field so
// the panel buttons can react to it.
export const encoding: { current: boolean } = $state({ current: false });

let procId: number | null = null;
let offLog: (() => void) | null = null;
let offStart: (() => void) | null = null;
let savedTitle = '';

function setTitle(outpath: string, percent: number | null): void {
  const base = outpath.split(/[\\/]/).at(-1) ?? outpath;
  document.title =
    percent == null ? savedTitle : `[${percent.toFixed(1)}%] ${base}`;
}

function stopPipe(): void {
  offLog?.();
  offLog = null;
  offStart?.();
  offStart = null;
  procId = null;
}

function describeExit(code: number | null, signal: string | null): string {
  if (signal) return `killed by ${signal}`;
  return `ffmpeg exited with ${code ?? 'unknown'}`;
}

interface RunResult {
  id: number;
  code: number | null;
  signal: string | null;
}

async function runPass(
  bin: string,
  args: string[],
  events: EncodeEvents,
): Promise<RunResult> {
  events.log(`$ ffmpeg ${args.join(' ')}`);
  return window.kurippu.runProcess(bin, args);
}

export async function cancelEncode(): Promise<void> {
  if (procId != null) await window.kurippu.killProcess(procId);
}

export async function openOutput(path: string): Promise<void> {
  await window.kurippu.openPath(path);
}

export async function openFolder(path: string): Promise<void> {
  await window.kurippu.showItemInFolder(path);
}

async function statFile(
  path: string,
): Promise<{ size: number; mtimeMs: number } | null> {
  const info = await window.kurippu.statPath(path);
  return info.exists ? info : null;
}

async function logStats(
  outpath: string,
  duration: number,
  elapsedMs: number,
  events: EncodeEvents,
): Promise<void> {
  const st = await statFile(outpath);
  events.log(`Output: ${outpath}`);
  events.log(`Duration: ${showTime(duration)}`);
  if (st) {
    const mbits = (st.size * 8) / 1e6;
    const mib = st.size / 2 ** 20;
    events.log(
      `Bitrate: ${duration > 0 ? (mbits / duration).toFixed(3) : '?'} Mbps, ` +
        `size: ${mib.toFixed(1)} MiB`,
    );
  }
  events.log(`Elapsed: ${Math.round(elapsedMs / 1000)} s`);
}

export async function startEncode(
  req: EncodeRequest,
  events: EncodeEvents,
): Promise<void> {
  if (encoding.current) {
    await cancelEncode();
    return;
  }
  const dirMatch = /^(.*)[\\/][^\\/]+$/.exec(req.outpath);
  if (req.kind === 'normal') {
    const [dir, file] = await Promise.all([
      window.kurippu.statPath(dirMatch?.[1] ?? ''),
      window.kurippu.statPath(req.outpath),
    ]);
    if (
      validateOutputDir(dirMatch?.[1] ?? '', dir) != null ||
      validateOutputFile(file) != null
    ) {
      events.log('Fix invalid path.');
      return;
    }
  }

  const bin = await window.kurippu.resolveFfmpeg();
  if (!bin) {
    events.log('ffmpeg not found.');
    return;
  }

  encoding.current = true;
  savedTitle = document.title;
  const prog = initProgress(req.avgFrameRate, req.duration);
  events.progress(0);
  const t0 = Date.now();
  offStart = window.kurippu.onProcessStart((id) => {
    procId = id;
  });
  offLog = window.kurippu.onProcessLog((_id, text) => {
    for (const line of text.split(/[\r\n]+/)) {
      if (!line) continue;
      events.log(line);
      const p = feedProgressLine(prog, line);
      events.progress(p);
      setTitle(req.outpath, p);
    }
  });

  try {
    if (req.kind === 'test') {
      const tmppath = await window.kurippu.makeTempPath('.mkv');
      await window.kurippu.registerTempFile(tmppath);
      const r = await runPass(bin, getTestArgs(req.rawArgs, tmppath), events);
      if (r.code === 0) {
        events.log(`Test encode done: ${tmppath}`);
        await openOutput(tmppath);
      } else {
        events.log(describeExit(r.code, r.signal));
      }
    } else if (req.twoPass) {
      // The main process owns temp paths and cleanup.
      const passlog = await window.kurippu.makeTempPath('.log');
      const input = {
        rawArgs: req.rawArgs,
        title: req.title,
        outpath: req.outpath,
        passlog,
      };
      prog.pass = 1;
      const r1 = await runPass(bin, getEncodeArgs(input, 1), events);
      if (r1.code !== 0) {
        events.log(describeExit(r1.code, r1.signal));
        return;
      }
      prog.pass = 2;
      prog.lastFrame = 0;
      const r2 = await runPass(bin, getEncodeArgs(input, 2), events);
      if (r2.code !== 0) {
        events.log(describeExit(r2.code, r2.signal));
        return;
      }
      events.progress(100);
      await logStats(req.outpath, req.duration, Date.now() - t0, events);
      events.finished(req.outpath);
    } else {
      const r = await runPass(
        bin,
        getEncodeArgs(
          {
            rawArgs: req.rawArgs,
            title: req.title,
            outpath: req.outpath,
            passlog: '',
          },
          0,
        ),
        events,
      );
      if (r.code !== 0) {
        events.log(describeExit(r.code, r.signal));
        return;
      }
      events.progress(100);
      await logStats(req.outpath, req.duration, Date.now() - t0, events);
      events.finished(req.outpath);
    }
  } finally {
    stopPipe();
    encoding.current = false;
    document.title = savedTitle;
  }
}
