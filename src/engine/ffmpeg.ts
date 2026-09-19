import type { AudioCodec, MTMode, VideoCodec } from './constants';
import { escapeArg, escapeFilterPath, fileArg } from './escape';
import { ceilFixed } from './time';

// Everything getRawArgs needs, already validated and derived.
export interface RawArgsInput {
  inpath: string;
  start: number | null;
  end: number | null;
  duration: number; // fragment length after start/end are applied
  hasAudio: boolean;
  vtrackn: number;
  atrackn: number;
  vcodec: VideoCodec;
  acodec: AudioCodec;
  threads: number;
  vb: number | null; // Kbps; null means -b:v 0
  quality: number | null;
  preset: string;
  modeMT: MTMode;
  deinterlace: boolean;
  crop: {
    w: number | null;
    h: number | null;
    x: number | null;
    y: number | null;
  };
  scalew: number | null;
  scaleh: number | null;
  finalWidth: number;
  finalHeight: number;
  fixSAR: boolean;
  anamorph: boolean;
  burnSubs: boolean;
  subpath: string | null; // external subtitle file; null uses the source path
  strackn: number | null; // embedded subtitle stream index
  speed: number | null;
  amplify: number | null;
  fadeIn: number | null;
  fadeOut: number | null;
  atrackChannels: number;
  ab: number | null; // Kbps for opus/aac, quality for vorbis, null for copy
}

function tilesFor(threads: number): number {
  return Math.ceil(Math.log2(threads) / 2);
}

function videoCodecArgs(input: RawArgsInput): string[] {
  const args: string[] = [];
  const { vcodec, modeMT, threads } = input;
  if (vcodec === 'av1') {
    args.push(
      '-c:v',
      'libaom-av1',
      '-cpu-used',
      modeMT === 'no-mt' ? '0' : '4',
    );
    if (modeMT !== 'no-mt') {
      const tiles = tilesFor(threads);
      const cols = Math.min(
        tiles,
        Math.floor(Math.log2((input.finalWidth + 63) / 64)),
      );
      const rows = Math.min(
        tiles,
        Math.floor(Math.log2((input.finalHeight + 63) / 64)),
      );
      args.push('-tile-columns', String(cols), '-tile-rows', String(rows));
    }
    if (modeMT === 'row-mt') args.push('-row-mt', '1');
    args.push('-strict', 'experimental');
  } else if (vcodec === 'vp9') {
    args.push(
      '-c:v',
      'libvpx-vp9',
      '-speed',
      modeMT === 'no-mt' ? '0' : '1',
      '-tile-columns',
      modeMT === 'no-mt' ? '0' : '6',
    );
    if (modeMT === 'row-mt') args.push('-row-mt', '1');
    args.push('-frame-parallel', '0');
  } else if (vcodec === 'vp8') {
    args.push('-c:v', 'libvpx', '-speed', '0');
  } else {
    args.push('-c:v', 'libx264');
  }
  return args;
}

function videoFilters(input: RawArgsInput): string[] {
  const filters: string[] = [];
  const { crop } = input;
  if (input.deinterlace) filters.push('yadif');
  if (crop.w != null || crop.h != null || crop.x != null || crop.y != null) {
    filters.push(
      `crop=${crop.w ?? 'iw'}:${crop.h ?? 'ih'}:${crop.x ?? '(iw-ow)/2'}:${crop.y ?? '(ih-oh)/2'}`,
    );
  }
  if (input.fixSAR || input.scalew != null || input.scaleh != null) {
    filters.push(`scale=${input.finalWidth}:${input.finalHeight}`);
    if (input.fixSAR || !input.anamorph) filters.push('setsar=1');
  }
  if (input.burnSubs) {
    const subpath = input.subpath ?? input.inpath;
    let sub = `subtitles=${escapeFilterPath(fileArg(subpath))}`;
    if (input.strackn != null && input.subpath == null)
      sub += `:si=${input.strackn}`;
    if (input.start != null) {
      // The double setpts wrapper works around ffmpeg trac#2067 for burned subs
      // on a cut fragment.
      filters.push(`setpts=PTS+${input.start}/TB`, sub, 'setpts=PTS-STARTPTS');
    } else {
      filters.push(sub);
    }
  }
  if (input.speed != null) filters.push(`setpts=PTS*${input.speed}`);
  return filters;
}

function audioArgs(input: RawArgsInput): string[] {
  if (!input.hasAudio) return [];
  const args: string[] = [];
  const { acodec } = input;
  if (acodec === 'copy') {
    args.push('-c:a', 'copy');
    return args;
  }
  if (acodec === 'opus')
    args.push('-c:a', 'libopus', '-b:a', `${input.ab ?? 128}k`);
  else if (acodec === 'aac')
    args.push('-c:a', 'aac', '-b:a', `${input.ab ?? 128}k`);
  else args.push('-c:a', 'libvorbis', '-q:a', String(input.ab ?? 4));
  if (input.atrackChannels > 2) args.push('-ac', '2');
  const filters: string[] = [];
  // acompressor must come before fades so the fade curve is not re-lifted.
  if (input.amplify != null)
    filters.push(`acompressor=makeup=${input.amplify}`);
  if (input.fadeIn != null) filters.push(`afade=t=in:d=${input.fadeIn}`);
  if (input.fadeOut != null)
    filters.push(
      `afade=t=out:d=${input.fadeOut}:st=${(input.duration - input.fadeOut).toFixed(3)}`,
    );
  if (filters.length > 0) args.push('-af', filters.join(','));
  return args;
}

// Builds the raw ffmpeg argument list in fixed order. Returns one string with
// shell-escaped -i and -vf values, ready to show and edit in the UI.
export function getRawArgs(input: RawArgsInput): string {
  const args: (string | string[])[] = [];

  if (input.start != null) args.push('-ss', String(input.start));
  args.push('-i', escapeArg(fileArg(input.inpath)));
  if (input.start != null && input.hasAudio && input.acodec === 'copy')
    args.push('-ss', '0');
  if (input.end != null) args.push('-t', ceilFixed(input.duration, 3));

  args.push('-map', `0:v:${input.vtrackn}`);
  if (input.hasAudio) args.push('-map', `0:a:${input.atrackn}`);

  args.push('-threads', String(input.threads));
  args.push(videoCodecArgs(input));
  args.push('-b:v', input.vb ? `${input.vb}k` : '0');
  if (input.quality != null) {
    args.push('-crf', String(input.quality));
    if (input.vcodec === 'vp8' && input.quality < 4) args.push('-qmin', '0');
    if (input.vcodec === 'vp9' && input.quality === 0)
      args.push('-lossless', '1');
  }
  args.push('-preset', input.preset);
  if (input.vcodec !== 'x264')
    args.push('-auto-alt-ref', '1', '-lag-in-frames', '25', '-g', '128');
  args.push('-pix_fmt', 'yuv420p');

  const vf = videoFilters(input);
  if (vf.length > 0) args.push('-vf', escapeArg(vf.join(',')));

  args.push(audioArgs(input));

  args.push('-f', input.vcodec === 'x264' ? 'mp4' : 'webm');
  return args.flat().join(' ');
}

// Splits a raw args string back into an argv array. Unlike the legacy
// shell-quote behavior, unbalanced quotes throw instead of silently passing
// through.
export function parseArgs(raw: string): string[] {
  const args: string[] = [];
  let cur = '';
  let quote: '"' | "'" | null = null;
  let hasCur = false;
  for (let i = 0; i < raw.length; i++) {
    const ch = raw[i] as string;
    if (quote) {
      if (ch === quote) quote = null;
      else if (ch === '\\' && quote === '"' && i + 1 < raw.length) {
        cur += raw[++i] as string;
      } else cur += ch;
    } else if (ch === '"' || ch === "'") {
      quote = ch;
      hasCur = true;
    } else if (/\s/.test(ch)) {
      if (cur.length > 0 || hasCur) args.push(cur);
      cur = '';
      hasCur = false;
    } else {
      cur += ch;
    }
  }
  if (quote) throw new Error('Unmatched quote in raw args');
  if (cur.length > 0 || hasCur) args.push(cur);
  return args;
}

// --- Execution args -------------------------------------------------------

export interface EncodeExecInput {
  rawArgs: string;
  title: string;
  outpath: string;
  passlog: string; // includes the -0.log suffix; stripped for -passlogfile
}

export const COMMON_PREFIX = ['-hide_banner', '-nostdin', '-y'];

function passlogBase(passlog: string): string {
  return passlog.replace(/-0\.log$/, '');
}

// passn: 0 = single pass, 1 or 2 = that pass of a 2-pass encode.
export function getEncodeArgs(
  input: EncodeExecInput,
  passn: 0 | 1 | 2,
): string[] {
  let args = parseArgs(input.rawArgs);
  if (passn === 1) {
    args = fixOpt(args, { '-speed': '4' });
    args.push(
      '-an',
      '-pass',
      '1',
      '-passlogfile',
      passlogBase(input.passlog),
      '-f',
      'null',
      '-',
    );
  } else if (passn === 2) {
    args.push('-pass', '2', '-passlogfile', passlogBase(input.passlog));
    args.push('-metadata', `title=${input.title}`, fileArg(input.outpath));
  } else {
    args.push('-metadata', `title=${input.title}`, fileArg(input.outpath));
  }
  return [...COMMON_PREFIX, ...args];
}

// Options removed or overridden for test encodes.
const TEST_STRIP = new Set([
  '-b:v',
  '-speed',
  '-cpu-used',
  '-row-mt',
  '-tile-columns',
  '-frame-parallel',
  '-auto-alt-ref',
  '-lag-in-frames',
  '-strict',
  '-g',
]);

// fixOpt sets or replaces option values in an argv list.
function fixOpt(args: string[], opts: Record<string, string>): string[] {
  const out: string[] = [];
  const seen = new Set<string>();
  for (let i = 0; i < args.length; i++) {
    const a = args[i] as string;
    if (a in opts) {
      out.push(a, opts[a] as string);
      seen.add(a);
      i++; // skip old value
    } else {
      out.push(a);
    }
  }
  for (const [k, v] of Object.entries(opts)) {
    if (!seen.has(k)) out.push(k, v);
  }
  return out;
}

// Removes options (and their values) from an argv list.
function stripOpts(args: string[], opts: Set<string>): string[] {
  const out: string[] = [];
  for (let i = 0; i < args.length; i++) {
    const a = args[i] as string;
    if (opts.has(a)) {
      i++; // drop value too
      continue;
    }
    out.push(a);
  }
  return out;
}

// Test encode: fast x264 throwaway to a temp matroska file.
export function getTestArgs(rawArgs: string, tmppath: string): string[] {
  let args = parseArgs(rawArgs);
  args = fixOpt(args, { '-c:v': 'libx264' });
  // Replace any existing -crf before appending the test value.
  args = stripOpts(args, new Set(['-crf']));
  args.push('-crf', '18');
  args = fixOpt(args, { '-preset': 'ultrafast' });
  args = stripOpts(args, TEST_STRIP);
  // Keep even width for the test scale.
  const vfIdx = args.indexOf('-vf');
  const extra = 'scale=floor((iw+1)/2)*2:-2';
  if (vfIdx >= 0) {
    args[vfIdx + 1] = `${args[vfIdx + 1]},${extra}`;
  } else {
    args.push('-vf', extra);
  }
  // Drop a trailing -f from the raw args; matroska replaces it.
  const fIdx = args.lastIndexOf('-f');
  if (fIdx >= 0) args.splice(fIdx, 2);
  args.push('-f', 'matroska', fileArg(tmppath));
  return [...COMMON_PREFIX, ...args];
}

export interface PreviewInput {
  inpath: string;
  time: number | null;
  vcodec: string; // ffmpeg codec name, matches the encode
  outfmt: string;
  outpath: string;
  width: number;
  height: number;
  sar: number;
  fps: string; // string frame rate for -r
}

// One-frame preview image encoded with the same codec, then prepended to the
// output via concat.
export function getPreviewArgs(input: PreviewInput): string[] {
  const w = input.sar > 1 ? Math.round(input.width * input.sar) : input.width;
  const h = input.sar < 1 ? Math.round(input.height / input.sar) : input.height;
  const vf =
    `setpts=PTS-STARTPTS,scale=iw:iw/dar,scale=${w + 1}:${h + 1}:force_original_aspect_ratio=decrease[main];` +
    `color=color=#DDDDDD:size=${w}x${h}[bg];[bg][main]overlay=(W-w)/2:(H-h)/2`;
  const args: string[] = [];
  if (input.time != null) args.push('-ss', String(input.time));
  args.push(
    '-i',
    fileArg(input.inpath),
    '-c:v',
    input.vcodec,
    '-b:v',
    '0',
    '-crf',
    '30',
    '-vf',
    vf,
    '-r',
    input.fps,
    '-an',
    '-sn',
    '-dn',
    '-frames:v',
    '1',
    '-pix_fmt',
    'yuv420p',
    '-strict',
    'experimental',
    '-f',
    input.outfmt,
    fileArg(input.outpath),
  );
  return [...COMMON_PREFIX, ...args];
}

export interface ConcatInput {
  listpath: string;
  mainpath: string;
  hasAudio: boolean;
  fps: number;
  outfmt: string;
  outpath: string;
}

// Concatenates the preview frame and the encoded main file.
export function getConcatArgs(input: ConcatInput): string[] {
  const args = ['-f', 'concat', '-safe', '0', '-i', fileArg(input.listpath)];
  args.push(
    '-itsoffset',
    ceilFixed(1 / input.fps, 3),
    '-i',
    fileArg(input.mainpath),
  );
  args.push('-map', '0:v:0');
  if (input.hasAudio) args.push('-map', '1:a:0?');
  args.push(
    '-c',
    'copy',
    '-strict',
    'experimental',
    '-f',
    input.outfmt,
    fileArg(input.outpath),
  );
  return [...COMMON_PREFIX, ...args];
}

// --- Detection commands ----------------------------------------------------

// Interlace detection: idet over 10 frames starting 5s into the fragment.
export function getInterlaceArgs(
  inpath: string,
  start: number,
  vtrackn: number,
): string[] {
  return [
    '-v',
    'error',
    '-nostdin',
    '-y',
    '-ss',
    String(start + 5),
    '-i',
    fileArg(inpath),
    '-map',
    `0:v:${vtrackn}`,
    '-vf',
    'idet,metadata=print:lavfi.idet.multiple.tff:file=-',
    '-frames:v',
    '10',
    '-f',
    'null',
    '-',
  ];
}

// Parses idet output. Interlaced when tff > 3 in the last reported frame.
export function parseInterlaceOutput(output: string): boolean {
  const matches = [...output.matchAll(/lavfi\.idet\.multiple\.tff=(\d+)/g)];
  const last = matches.at(-1);
  return last != null && Number.parseInt(last[1] ?? '0', 10) > 3;
}

// Black-bar detection: cropdetect over 3 frames.
export function getCropDetectArgs(
  inpath: string,
  start: number,
  vtrackn: number,
): string[] {
  return [
    '-v',
    'error',
    '-nostdin',
    '-y',
    '-ss',
    String(start + 5),
    '-i',
    fileArg(inpath),
    '-map',
    `0:v:${vtrackn}`,
    '-vf',
    'cropdetect=round=2,metadata=print:file=-',
    '-frames:v',
    '3',
    '-f',
    'null',
    '-',
  ];
}

export interface CropArea {
  w: number;
  h: number;
  x: number;
  y: number;
}

// Parses cropdetect output. Returns null when no crop values were reported.
export function parseCropDetectOutput(output: string): CropArea | null {
  const grab = (key: string): number | null => {
    const m = new RegExp(`lavfi\\.cropdetect\\.${key}=(-?\\d+)`).exec(output);
    return m ? Number.parseInt(m[1] ?? '0', 10) : null;
  };
  const w = grab('w');
  const h = grab('h');
  const x = grab('x');
  const y = grab('y');
  if (w == null || h == null || x == null || y == null) return null;
  return { w, h, x, y };
}
