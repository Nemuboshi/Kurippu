import type { YtFormat, YtInfo } from './state';

// Sorting and display rules for the yt-dlp format selection page.

export interface FormatChoice {
  id: string;
  label: string;
}

function fmtSize(bytes: number | undefined): string {
  if (!bytes) return '';
  return ` (${(bytes / 1024 / 1024).toFixed(2)}MiB)`;
}

function vcodecLabel(
  v: string | undefined,
  height: number | undefined,
): string {
  if (!v || v === 'none') return '';
  const base = v.replace(/\..*$/, '');
  // vp9.2 carries HDR; show it as "vp9 hdr".
  return v.startsWith('vp9.2') ? 'vp9 hdr' : base;
}

// Video formats, best first: height ↓, width ↓, fps ↓, HDR (vp9.2) first,
// then VP9 over AVC at high resolution, then file size ↓.
export function videoFormats(info: YtInfo): FormatChoice[] {
  const list = info.formats.filter((f) => f.vcodec && f.vcodec !== 'none');
  const hiRes = (f: YtFormat) =>
    (f.height ?? 0) > 1920 || ((f.height ?? 0) >= 1080 && (f.fps ?? 0) >= 60);
  const score = (f: YtFormat) => {
    const hdr = f.vcodec?.startsWith('vp9.2') ? 1 : 0;
    const vp9 = f.vcodec?.startsWith('vp9') ? 1 : 0;
    const preferVp9 = hiRes(f) ? vp9 : 0;
    return { hdr, preferVp9 };
  };
  list.sort((a, b) => {
    const s = (x: YtFormat) => score(x);
    return (
      (b.height ?? 0) - (a.height ?? 0) ||
      (b.width ?? 0) - (a.width ?? 0) ||
      (b.fps ?? 0) - (a.fps ?? 0) ||
      s(b).hdr - s(a).hdr ||
      s(b).preferVp9 - s(a).preferVp9 ||
      (b.filesize ?? b.filesize_approx ?? 0) -
        (a.filesize ?? a.filesize_approx ?? 0)
    );
  });
  return list.map((f) => ({
    id: f.format_id,
    label:
      `${vcodecLabel(f.vcodec, f.height)}${f.acodec && f.acodec !== 'none' ? `+${vcodecLabel(f.acodec, undefined)}` : ''} ` +
      `${f.width ?? '?'}x${f.height ?? '?'} ${f.fps ? `${Math.round(f.fps)}fps` : ''}${fmtSize(f.filesize ?? f.filesize_approx)}`.trim(),
  }));
}

// Audio-only formats by bitrate ↓ then size ↓, plus a trailing "none".
export function audioFormats(info: YtInfo): FormatChoice[] {
  const list = info.formats.filter(
    (f) => f.vcodec === 'none' && f.acodec && f.acodec !== 'none',
  );
  list.sort(
    (a, b) =>
      (b.abr ?? 0) - (a.abr ?? 0) ||
      (b.filesize ?? b.filesize_approx ?? 0) -
        (a.filesize ?? a.filesize_approx ?? 0),
  );
  const out = list.map((f) => ({
    id: f.format_id,
    label:
      `${f.acodec} ${f.abr ? `${Math.round(f.abr)}kbits` : ''}${fmtSize(f.filesize ?? f.filesize_approx)}`.trim(),
  }));
  out.push({ id: 'none', label: 'none' });
  return out;
}

const ENGLISH = new Set(['en', 'eng', 'en_US', 'en-US']);

// Subtitle language keys in dictionary order, English first by default
// selection (handled by the caller), plus a trailing "none".
export function subtitleFormats(info: YtInfo): FormatChoice[] {
  const subs = info.requested_subtitles ?? {};
  const keys = Object.keys(subs).sort();
  const out = keys.map((lang) => ({
    id: lang,
    label: `${subs[lang]?.ext ?? 'srt'} (${lang})`,
  }));
  out.push({ id: 'none', label: 'none' });
  return out;
}

export function defaultSubtitle(info: YtInfo): string {
  const subs = info.requested_subtitles ?? {};
  const en = Object.keys(subs).find((k) => ENGLISH.has(k));
  return en ?? 'none';
}

// Download command for the picked formats.
export function downloadArgs(
  url: string,
  vfid: string,
  afid: string,
  sid: string,
  sext: string,
  outpath: string,
  ffmpegPath: string | null,
): string[] {
  const format = afid === 'none' ? vfid : `${vfid}+${afid}`;
  const args = [
    '--no-part',
    '--no-playlist',
    '--format',
    format,
    '--merge-output-format',
    'mkv',
  ];
  if (process.platform === 'darwin' && ffmpegPath)
    args.push('--ffmpeg-location', ffmpegPath);
  if (sid !== 'none') {
    args.push(
      '--sub-langs',
      sid,
      '--write-subs',
      '--embed-subs',
      '--postprocessor-args',
      '-c:s ass -f matroska',
    );
  }
  args.push('--output', outpath, '--', url);
  return args;
}

export function resolveArgs(url: string): string[] {
  return ['--no-playlist', '--dump-json', '--write-subs', '--', url];
}

// Extracts a download percentage from one yt-dlp output chunk.
export function parseProgress(chunk: string): number | null {
  const lines = chunk.split(/[\r\n]+/).filter(Boolean);
  const last = lines.at(-1) ?? '';
  const m = /\s(\d+(\.\d+)?)%\s/.exec(last);
  return m ? Number.parseFloat(m[1] ?? '0') : null;
}

// Temp file names yt-dlp leaves behind when a download is aborted.
export function ytdlpLeftovers(
  outpath: string,
  vfid: string,
  afid: string,
  sid: string,
  sext: string,
): string[] {
  const base = outpath.replace(/\.mkv$/, '');
  const files = [`${base}.temp.mkv`, `${base}.mkv.f${vfid}`];
  if (afid !== 'none') files.push(`${base}.mkv.f${afid}`);
  if (sid !== 'none') files.push(`${base}.${sid}.${sext}`);
  return files;
}
