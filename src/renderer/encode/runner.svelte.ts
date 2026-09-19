// Encode orchestration and progress reporting.
import { feedProgressLine, initProgress } from '../../engine/bitrate';
import {
  getConcatArgs,
  getEncodeArgs,
  getPreviewArgs,
  getTestArgs,
} from '../../engine/ffmpeg';
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
  preview: { path: string; time: number | null } | null;
  sourcePath: string;
  videoCodec: string;
  width: number;
  height: number;
  sar: number;
  hasAudio: boolean;
}
export interface EncodeEvents {
  log: (line: string) => void;
  progress: (percent: number) => void;
  finished: (outpath: string) => void;
}
export const encoding: { current: boolean } = $state({ current: false });
let procId: number | null = null;
let offLog: (() => void) | null = null;
let offStart: (() => void) | null = null;
let savedTitle = '';
function setTitle(path: string, percent: number | null): void {
  const base = path.split(/[\\/]/).at(-1) ?? path;
  document.title =
    percent == null ? savedTitle : `[${percent.toFixed(1)}%] ${base}`;
}
function stopPipe(): void {
  offLog?.();
  offStart?.();
  offLog = null;
  offStart = null;
  procId = null;
}
function describeExit(code: number | null, signal: string | null): string {
  return signal
    ? `killed by ${signal}`
    : `ffmpeg exited with ${code ?? 'unknown'}`;
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
async function logStats(
  path: string,
  duration: number,
  elapsed: number,
  e: EncodeEvents,
): Promise<void> {
  const st = await window.kurippu.statPath(path);
  e.log(`Output: ${path}`);
  e.log(`Duration: ${showTime(duration)}`);
  if (st.exists) {
    const mbits = (st.size * 8) / 1e6;
    e.log(
      `Bitrate: ${duration > 0 ? (mbits / duration).toFixed(3) : '?'} Mbps, size: ${(st.size / 2 ** 20).toFixed(1)} MiB`,
    );
  }
  e.log(`Elapsed: ${Math.round(elapsed / 1000)} s`);
}
async function encodeMain(
  bin: string,
  req: EncodeRequest,
  path: string,
  passlog: string,
  e: EncodeEvents,
  prog: ReturnType<typeof initProgress>,
): Promise<boolean> {
  const input = {
    rawArgs: req.rawArgs,
    title: req.title,
    outpath: path,
    passlog,
  };
  if (req.twoPass) {
    prog.pass = 1;
    const a = await runPass(bin, getEncodeArgs(input, 1), e);
    if (a.code !== 0) {
      e.log(describeExit(a.code, a.signal));
      return false;
    }
    prog.pass = 2;
    prog.lastFrame = 0;
    const b = await runPass(bin, getEncodeArgs(input, 2), e);
    if (b.code !== 0) {
      e.log(describeExit(b.code, b.signal));
      return false;
    }
    return true;
  }
  const r = await runPass(bin, getEncodeArgs(input, 0), e);
  if (r.code !== 0) e.log(describeExit(r.code, r.signal));
  return r.code === 0;
}
export async function startEncode(
  req: EncodeRequest,
  events: EncodeEvents,
): Promise<void> {
  if (encoding.current) {
    await cancelEncode();
    return;
  }
  if (!req.rawArgs.trim()) {
    events.log('Fix invalid parameters.');
    return;
  }
  const dir = /^(.*)[\\/][^\\/]+$/.exec(req.outpath)?.[1] ?? '';
  if (req.kind === 'normal') {
    const [ds, fs] = await Promise.all([
      window.kurippu.statPath(dir),
      window.kurippu.statPath(req.outpath),
    ]);
    if (validateOutputDir(dir, ds) || validateOutputFile(fs)) {
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
  const started = Date.now();
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
      const tmp = await window.kurippu.makeTempPath('.mkv');
      const r = await runPass(bin, getTestArgs(req.rawArgs, tmp), events);
      if (r.code === 0) {
        events.log(`Test encode done: ${tmp}`);
        await openOutput(tmp);
      } else events.log(describeExit(r.code, r.signal));
      return;
    }
    let main = req.outpath;
    let previewPath = '';
    let listPath = '';
    if (req.preview) {
      main = await window.kurippu.makeTempPath(`.${req.container}`);
      previewPath = await window.kurippu.makeTempPath(`.${req.container}`);
      listPath = await window.kurippu.makeTempPath('.txt');
    }
    const passlog = await window.kurippu.makeTempPath('-0.log');
    if (!(await encodeMain(bin, req, main, passlog, events, prog))) return;
    if (req.preview) {
      const p = await runPass(
        bin,
        getPreviewArgs({
          inpath: req.preview.path,
          time: req.preview.time,
          vcodec: req.videoCodec,
          outfmt: req.container,
          outpath: previewPath,
          width: req.width,
          height: req.height,
          sar: req.sar,
          fps: req.avgFrameRate,
        }),
        events,
      );
      if (p.code !== 0) {
        events.log(describeExit(p.code, p.signal));
        return;
      }
      const quote = (s: string) => s.replace(/'/g, "'\\''");
      await window.kurippu.writeText(
        listPath,
        `file '${quote(previewPath)}'\n`,
      );
      const c = await runPass(
        bin,
        getConcatArgs({
          listpath: listPath,
          mainpath: main,
          hasAudio: req.hasAudio,
          fps: Number(req.avgFrameRate) || 25,
          outfmt: req.container,
          outpath: req.outpath,
        }),
        events,
      );
      if (c.code !== 0) {
        events.log(describeExit(c.code, c.signal));
        return;
      }
    }
    events.progress(100);
    await logStats(req.outpath, req.duration, Date.now() - started, events);
    events.finished(req.outpath);
  } finally {
    stopPipe();
    encoding.current = false;
    document.title = savedTitle;
  }
}
