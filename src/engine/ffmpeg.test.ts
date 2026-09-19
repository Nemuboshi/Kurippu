import { describe, expect, it } from 'vitest';
import { concatListLine, escapeArg, escapeFilterPath, fileArg } from './escape';
import {
  getConcatArgs,
  getCropDetectArgs,
  getEncodeArgs,
  getInterlaceArgs,
  getPreviewArgs,
  getRawArgs,
  getTestArgs,
  parseArgs,
  parseCropDetectOutput,
  parseInterlaceOutput,
  type RawArgsInput,
} from './ffmpeg';

const base: RawArgsInput = {
  inpath: '/tmp/in.mkv',
  start: null,
  end: null,
  duration: 60,
  hasAudio: true,
  vtrackn: 0,
  atrackn: 0,
  vcodec: 'vp9',
  acodec: 'opus',
  threads: 8,
  vb: 2466,
  quality: 25,
  preset: 'veryslow',
  modeMT: 'row-mt',
  deinterlace: false,
  crop: { w: null, h: null, x: null, y: null },
  scalew: null,
  scaleh: null,
  finalWidth: 1920,
  finalHeight: 1080,
  fixSAR: false,
  anamorph: false,
  burnSubs: false,
  subpath: null,
  strackn: null,
  speed: null,
  amplify: null,
  fadeIn: null,
  fadeOut: null,
  atrackChannels: 2,
  ab: 128,
};

describe('getRawArgs', () => {
  it('orders input, streams, video, audio, output', () => {
    const raw = getRawArgs(base);
    const args = parseArgs(raw);
    const iIdx = args.indexOf('-i');
    const mapIdx = args.indexOf('-map');
    const cvIdx = args.indexOf('-c:v');
    const fIdx = args.lastIndexOf('-f');
    expect(iIdx).toBeLessThan(mapIdx);
    expect(mapIdx).toBeLessThan(cvIdx);
    expect(cvIdx).toBeLessThan(fIdx);
    expect(args[args.indexOf('-b:v') + 1]).toBe('2466k');
    expect(args[fIdx + 1]).toBe('webm');
  });

  it('acceptance: CRF + VP9 + quality 0 adds -lossless 1', () => {
    const args = parseArgs(getRawArgs({ ...base, quality: 0 }));
    expect(args).toContain('-lossless');
  });

  it('acceptance: VP8 + quality < 4 adds -qmin 0', () => {
    const args = parseArgs(getRawArgs({ ...base, vcodec: 'vp8', quality: 3 }));
    const i = args.indexOf('-qmin');
    expect(args[i + 1]).toBe('0');
  });

  it('acceptance: AV1 row-mt flag set', () => {
    const args = parseArgs(getRawArgs({ ...base, vcodec: 'av1' }));
    expect(args).toContain('libaom-av1');
    expect(args[args.indexOf('-cpu-used') + 1]).toBe('4');
    expect(args).toContain('-tile-columns');
    expect(args).toContain('-tile-rows');
    expect(args).toContain('-row-mt');
    expect(args[args.indexOf('-strict') + 1]).toBe('experimental');
  });

  it('av1 no-mt uses -cpu-used 0 and no tiles', () => {
    const args = parseArgs(
      getRawArgs({ ...base, vcodec: 'av1', modeMT: 'no-mt' }),
    );
    expect(args[args.indexOf('-cpu-used') + 1]).toBe('0');
    expect(args).not.toContain('-tile-columns');
  });

  it('x264 picks mp4 and skips vpx-only flags', () => {
    const args = parseArgs(
      getRawArgs({ ...base, vcodec: 'x264', acodec: 'aac' }),
    );
    expect(args[args.lastIndexOf('-f') + 1]).toBe('mp4');
    expect(args).not.toContain('-auto-alt-ref');
  });

  it('acceptance: burn subs + start=10 wraps with setpts', () => {
    const raw = getRawArgs({
      ...base,
      start: 10,
      burnSubs: true,
      subpath: '/tmp/sub.srt',
    });
    const vf = raw.match(/-vf "([^"]*)"/)?.[1] ?? '';
    expect(vf).toContain('setpts=PTS+10/TB');
    expect(vf).toContain('subtitles=');
    expect(vf).toContain('setpts=PTS-STARTPTS');
    expect(vf.indexOf('setpts=PTS+10/TB')).toBeLessThan(
      vf.indexOf('subtitles='),
    );
    expect(vf.indexOf('subtitles=')).toBeLessThan(
      vf.indexOf('setpts=PTS-STARTPTS'),
    );
  });

  it('embedded subs use source path with si index', () => {
    const raw = getRawArgs({ ...base, burnSubs: true, strackn: 1 });
    expect(raw).toContain('si=1');
  });

  it('deinterlace is the first filter', () => {
    const raw = getRawArgs({
      ...base,
      deinterlace: true,
      crop: { w: 100, h: 100, x: 0, y: 0 },
    });
    const vf = raw.match(/-vf "([^"]*)"/)?.[1] ?? '';
    expect(vf.startsWith('yadif,')).toBe(true);
  });

  it('acompressor comes before fades', () => {
    const args = parseArgs(
      getRawArgs({ ...base, amplify: 4, fadeIn: 1, fadeOut: 2 }),
    );
    const af = args[args.indexOf('-af') + 1] ?? '';
    expect(af.indexOf('acompressor')).toBeLessThan(af.indexOf('afade=t=in'));
    expect(af.indexOf('afade=t=in')).toBeLessThan(af.indexOf('afade=t=out'));
    expect(af).toContain('st=58.000');
  });

  it('copy audio emits -c:a copy and no filters', () => {
    const args = parseArgs(getRawArgs({ ...base, acodec: 'copy', fadeIn: 1 }));
    expect(args).toContain('-c:a');
    expect(args[args.indexOf('-c:a') + 1]).toBe('copy');
    expect(args).not.toContain('-af');
  });

  it('no audio drops the audio map', () => {
    const args = parseArgs(getRawArgs({ ...base, hasAudio: false }));
    expect(args.filter((a) => a === '-map')).toHaveLength(1);
  });

  it('copy with start emits the timestamp reset -ss 0 after -i', () => {
    const args = parseArgs(getRawArgs({ ...base, start: 5, acodec: 'copy' }));
    const iIdx = args.indexOf('-i');
    expect(args[iIdx + 2]).toBe('-ss');
    expect(args[iIdx + 3]).toBe('0');
  });

  it('null vb writes -b:v 0', () => {
    const args = parseArgs(getRawArgs({ ...base, vb: null }));
    expect(args[args.indexOf('-b:v') + 1]).toBe('0');
  });
});

describe('parseArgs', () => {
  it('round-trips escaped values', () => {
    const raw = getRawArgs(base);
    const args = parseArgs(raw);
    expect(args[args.indexOf('-i') + 1]).toBe('file:/tmp/in.mkv');
  });
  it('throws on unmatched quotes', () => {
    expect(() => parseArgs('-i "unclosed')).toThrow('Unmatched quote');
  });
  it('handles single quotes', () => {
    expect(parseArgs("-a 'b c'")).toEqual(['-a', 'b c']);
  });
});

describe('getEncodeArgs', () => {
  const input = {
    rawArgs: getRawArgs(base),
    title: 't',
    outpath: '/tmp/out.webm',
    passlog: '/tmp/x-0.log',
  };
  it('acceptance: pass1 ends with -f null - and strips the passlog suffix', () => {
    const args = getEncodeArgs(input, 1);
    expect(args.slice(-3)).toEqual(['-f', 'null', '-']);
    expect(args[args.indexOf('-passlogfile') + 1]).toBe('/tmp/x');
    expect(args[args.indexOf('-pass') + 1]).toBe('1');
    expect(args).toContain('-an');
  });
  it('pass2 adds metadata and output', () => {
    const args = getEncodeArgs(input, 2);
    expect(args[args.indexOf('-pass') + 1]).toBe('2');
    expect(args[args.indexOf('-metadata') + 1]).toBe('title=t');
    expect(args.at(-1)).toBe('file:/tmp/out.webm');
  });
  it('single pass has no -pass', () => {
    const args = getEncodeArgs(input, 0);
    expect(args).not.toContain('-pass');
    expect(args.at(-1)).toBe('file:/tmp/out.webm');
  });
});

describe('getTestArgs', () => {
  it('forces x264 crf 18 ultrafast matroska and strips speed flags', () => {
    const args = getTestArgs(getRawArgs(base), '/tmp/test.mkv');
    expect(args[args.indexOf('-c:v') + 1]).toBe('libx264');
    expect(args[args.indexOf('-crf') + 1]).toBe('18');
    expect(args[args.indexOf('-preset') + 1]).toBe('ultrafast');
    expect(args).not.toContain('-speed');
    expect(args).not.toContain('-b:v');
    expect(args.slice(-2)).toEqual(['matroska', 'file:/tmp/test.mkv']);
  });
  it('appends even-width scale to an existing -vf', () => {
    const raw = getRawArgs({ ...base, fixSAR: true });
    const args = getTestArgs(raw, '/tmp/test.mkv');
    const vf = args[args.indexOf('-vf') + 1] ?? '';
    expect(vf).toContain('scale=floor((iw+1)/2)*2:-2');
  });
});

describe('getPreviewArgs', () => {
  it('acceptance: vf chain matches the spec template', () => {
    const args = getPreviewArgs({
      inpath: '/tmp/in.mkv',
      time: 3,
      vcodec: 'libvpx-vp9',
      outfmt: 'webm',
      outpath: '/tmp/prev.webm',
      width: 1920,
      height: 1080,
      sar: 1,
      fps: '30000/1001',
    });
    const vf = args[args.indexOf('-vf') + 1] ?? '';
    expect(vf).toBe(
      'setpts=PTS-STARTPTS,scale=iw:iw/dar,scale=1921:1081:force_original_aspect_ratio=decrease[main];' +
        'color=color=#DDDDDD:size=1920x1080[bg];[bg][main]overlay=(W-w)/2:(H-h)/2',
    );
    expect(args[args.indexOf('-frames:v') + 1]).toBe('1');
  });
  it('sar > 1 widens the preview canvas', () => {
    const args = getPreviewArgs({
      inpath: '/i',
      time: null,
      vcodec: 'libvpx',
      outfmt: 'webm',
      outpath: '/o',
      width: 720,
      height: 576,
      sar: 2,
      fps: '25',
    });
    const vf = args[args.indexOf('-vf') + 1] ?? '';
    expect(vf).toContain('size=1440x576');
  });
});

describe('getConcatArgs', () => {
  it('acceptance: -itsoffset is ceil(1/fps, 3)', () => {
    const args = getConcatArgs({
      listpath: '/l.txt',
      mainpath: '/m.webm',
      hasAudio: true,
      fps: 29.97,
      outfmt: 'webm',
      outpath: '/o.webm',
    });
    expect(args[args.indexOf('-itsoffset') + 1]).toBe('0.034');
    expect(args).toContain('-map');
    expect(args[args.indexOf('-c') + 1]).toBe('copy');
  });
  it('no audio drops the audio map', () => {
    const args = getConcatArgs({
      listpath: '/l.txt',
      mainpath: '/m.webm',
      hasAudio: false,
      fps: 25,
      outfmt: 'webm',
      outpath: '/o.webm',
    });
    expect(args.filter((a) => a === '-map')).toHaveLength(1);
  });
});

describe('detection commands', () => {
  it('interlace args use idet over 10 frames', () => {
    const args = getInterlaceArgs('/tmp/in.mkv', 5, 0);
    expect(args.join(' ')).toContain(
      'idet,metadata=print:lavfi.idet.multiple.tff:file=-',
    );
    expect(args[args.indexOf('-frames:v') + 1]).toBe('10');
    expect(args[args.indexOf('-ss') + 1]).toBe('10');
  });
  it('parseInterlaceOutput: tff > 3 is interlaced', () => {
    expect(parseInterlaceOutput('lavfi.idet.multiple.tff=4')).toBe(true);
    expect(parseInterlaceOutput('lavfi.idet.multiple.tff=2')).toBe(false);
    expect(parseInterlaceOutput('')).toBe(false);
  });
  it('cropdetect parses the last values', () => {
    const args = getCropDetectArgs('/tmp/in.mkv', 0, 0);
    expect(args.join(' ')).toContain('cropdetect=round=2');
    const area = parseCropDetectOutput(
      'lavfi.cropdetect.w=1918 lavfi.cropdetect.h=1078 lavfi.cropdetect.x=0 lavfi.cropdetect.y=0',
    );
    expect(area).toEqual({ w: 1918, h: 1078, x: 0, y: 0 });
    expect(parseCropDetectOutput('nothing')).toBeNull();
  });
});

describe('escaping', () => {
  it('filter path escapes backslash, quote, colon', () => {
    expect(escapeFilterPath('C:\\a\\b.srt')).toBe("'C\\:\\\\a\\\\b.srt'");
  });
  it('escapeArg wraps in double quotes', () => {
    expect(escapeArg('a "b" $c')).toBe('"a \\"b\\" \\$c"');
  });
  it('concat list line escapes single quotes', () => {
    expect(concatListLine("it's.webm")).toBe("file 'it'\\''s.webm'");
  });
  it('fileArg prefixes file:', () => {
    expect(fileArg('/x.mkv')).toBe('file:/x.mkv');
  });
});
