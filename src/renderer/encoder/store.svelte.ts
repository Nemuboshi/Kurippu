// Encoder parameter state. All UI option strings live here as $state fields;
// deriveOptions (pure engine module) validates them and produces the ffmpeg
// input. The store applies the interlocks that rewrite fields (crf forces the
// limit input, an acodec switch clears ab) before the next derivation runs.

import { SUBTITLE_EXTENSIONS } from '../../engine/constants';
import { isAnamorphic } from '../../engine/dimensions';
import {
  getCropDetectArgs,
  getInterlaceArgs,
  getRawArgs,
  parseCropDetectOutput,
  parseInterlaceOutput,
} from '../../engine/ffmpeg';
import type { MediaInfo } from '../../engine/options';
import { deriveOptions } from '../../engine/options';
import { showTime } from '../../engine/time';

export type SubTab = 'info' | 'video' | 'audio' | 'codecs' | 'encode';

// Sentinel value for the subtitle select entry that means "external file".
export const EXTERNAL_TRACK = '-1';

const VIDEO_CODECS = ['av1', 'vp9', 'vp8', 'x264'] as const;

function mediaForVideoTrack(media: MediaInfo, value: string): MediaInfo {
  const parsed = Number(value);
  const vtrackn =
    Number.isSafeInteger(parsed) && parsed >= 0 && parsed < media.vtracks.length
      ? parsed
      : 0;
  const track = media.vtracks[vtrackn];
  return {
    ...media,
    width: track?.width ?? media.width,
    height: track?.height ?? media.height,
    sar: track?.sar ?? media.sar,
    vtrackn,
  };
}

export class EncoderStore {
  media = $state<MediaInfo | null>(null);
  sourcePath = $state('');
  activeTab = $state<SubTab>('info');

  // video fx
  vtrack = $state('0');
  deinterlace = $state(false);
  cropw = $state('');
  croph = $state('');
  cropx = $state('');
  cropy = $state('');
  scalew = $state('');
  scaleh = $state('');
  fixSAR = $state(false);
  burnSubs = $state(false);
  strack = $state('');
  subpath = $state('');

  // audio fx
  hasAudio = $state(true);
  atrack = $state('0');
  fadeIn = $state('');
  fadeOut = $state('');
  amplify = $state('');

  // codecs
  start = $state('');
  end = $state('');
  vcodec = $state('x264');
  acodec = $state('aac');
  limit = $state('0');
  quality = $state('');
  ab = $state('');
  mode2Pass = $state(false);
  modeLimit = $state(false);
  modeCRF = $state(true);
  modeMT = $state<'row-mt' | 'tile-col' | 'no-mt'>('row-mt');
  preset = $state('veryslow');

  // Raw args text. The user can edit it; their text wins until the next
  // valid parameter change regenerates it.
  rawArgs = $state('');

  // Detection runs; buttons stay disabled while one is in flight.
  detectingInterlace = $state(false);
  detectingCrop = $state(false);

  private lastAutoArgs = '';
  private threads = 8;

  derived = $derived(
    this.media
      ? deriveOptions(
          {
            cropw: this.cropw,
            croph: this.croph,
            cropx: this.cropx,
            cropy: this.cropy,
            scalew: this.scalew,
            scaleh: this.scaleh,
            deinterlace: this.deinterlace,
            fixSAR: this.fixSAR,
            burnSubs: this.burnSubs,
            strack: this.strack,
            subpath: this.subpath,
            hasAudio: this.hasAudio,
            atrack: this.atrack,
            fadeIn: this.fadeIn,
            fadeOut: this.fadeOut,
            amplify: this.amplify,
            start: this.start,
            end: this.end,
            vcodec: this.vcodec as (typeof VIDEO_CODECS)[number],
            acodec: this.acodec as 'opus' | 'vorbis' | 'aac' | 'copy',
            limit: this.limit,
            quality: this.quality,
            ab: this.ab,
            mode2Pass: this.mode2Pass,
            modeLimit: this.modeLimit,
            modeCRF: this.modeCRF,
            modeMT: this.modeMT,
            preset: this.preset,
          },
          mediaForVideoTrack(this.media, this.vtrack),
          this.threads,
        )
      : null,
  );

  constructor() {
    void window.kurippu.debugSettings().then(({ tab }) => {
      if (tab && ['info', 'video', 'audio', 'codecs', 'encode'].includes(tab))
        this.activeTab = tab as SubTab;
    });
    $effect(() => {
      // Regenerate raw args whenever a valid derivation produces new args.
      // Manual edits survive until the next param change.
      const d = this.derived;
      if (!d?.allValid || !d.values) return;
      const auto = getRawArgs(d.values);
      // Manual edits survive until this fires again: the user textarea is
      // not written back between regenerations.
      if (auto !== this.lastAutoArgs) {
        this.lastAutoArgs = auto;
        this.rawArgs = auto;
      }
    });
  }

  // Replaces the probed media info and resets track-dependent defaults.
  setMedia(media: MediaInfo, threads = 8): void {
    this.media = media;
    this.threads = threads;
    this.sourcePath = media.path;
    this.vtrack = '0';
    this.atrack = '0';
    this.strack = '';
    this.subpath = '';
    this.hasAudio = media.atracks.length > 0;
    this.fixSAR = isAnamorphic(media.sar);
    this.burnSubs = false;
  }

  // Video track switch resets fixSAR to that track's anamorphy.
  selectVTrack(i: number): void {
    this.vtrack = String(i);
    const t = this.media?.vtracks[i];
    if (t?.sar != null) this.fixSAR = isAnamorphic(t.sar);
  }

  // Audio codec switch clears ab so the new codec's default applies.
  selectACodec(codec: string): void {
    if (codec !== this.acodec) {
      this.acodec = codec;
      this.ab = '';
    }
  }

  // Mode interlocks (spec 03 §2): crf and limit are mutually exclusive, and
  // either toggle rewrites the limit input to "0" under crf or "" otherwise,
  // so a stale "0" never leaks into limit-mode bitrate math.
  setModeCRF(on: boolean): void {
    this.modeCRF = on;
    if (on) this.modeLimit = false;
    this.limit = this.modeCRF ? '0' : '';
  }
  setModeLimit(on: boolean): void {
    this.modeLimit = on;
    if (on) this.modeCRF = false;
    this.limit = this.modeCRF ? '0' : '';
    this.mode2Pass = on;
  }
  setMode2Pass(on: boolean): void {
    this.mode2Pass = on;
    // 2pass targets a size, so it pulls limit on; crf has no target size and
    // must stay exclusive with limit.
    if (on && !this.modeCRF) this.modeLimit = true;
  }

  // External subtitle pick auto-checks burn and switches the select to the
  // external entry.
  async pickExternalSubs(): Promise<void> {
    const path = await window.kurippu.openFileDialog([
      { name: 'Subtitles', extensions: [...SUBTITLE_EXTENSIONS] },
    ]);
    if (path) this.setExternalSubs(path);
  }

  setExternalSubs(path: string): void {
    this.subpath = path;
    this.strack = EXTERNAL_TRACK;
    this.burnSubs = true;
  }

  // Subtitle select change: the external entry keeps the current file, any
  // other choice drops it.
  selectStrack(v: string): void {
    this.strack = v;
    if (v !== EXTERNAL_TRACK) {
      this.subpath = '';
      if (v === '') this.burnSubs = false;
    }
  }

  clearCrop(): void {
    this.cropw = '';
    this.croph = '';
    this.cropx = '';
    this.cropy = '';
  }

  // Fragment duration readout click resets the fragment to the full length.
  resetFragment(): void {
    this.start = '';
    this.end = '';
  }

  // Player hooks: rewrite the start/end inputs from a player timestamp.
  // End at (near) the total duration stays empty, meaning "to the end".
  markStart(t: number): void {
    this.start = showTime(t);
  }
  markEnd(t: number): void {
    const dur = this.media?.duration ?? 0;
    this.end = t >= dur - 0.001 ? '' : showTime(t, { ceil: true });
  }

  // Auto-detects interlacing with ffmpeg idet over 10 frames.
  async detectInterlace(): Promise<void> {
    const media = this.media;
    const ffmpeg = await window.kurippu.resolveFfmpeg();
    if (!media || !ffmpeg || this.detectingInterlace) return;
    this.detectingInterlace = true;
    try {
      const start = this.derived?.start ?? 0;
      const args = getInterlaceArgs(
        media.path,
        start,
        Number.parseInt(this.vtrack, 10) || 0,
      );
      const { stdout } = await window.kurippu.runProcess('ffmpeg', args);
      if (parseInterlaceOutput(stdout)) this.deinterlace = true;
    } catch {
      // Detection is best-effort; the user can still toggle the checkbox.
    } finally {
      this.detectingInterlace = false;
    }
  }

  // Auto-detects black bars with ffmpeg cropdetect over 3 frames. A
  // full-size result clears the crop inputs instead of filling them.
  async detectCrop(): Promise<void> {
    const media = this.media;
    const ffmpeg = await window.kurippu.resolveFfmpeg();
    if (!media || !ffmpeg || this.detectingCrop) return;
    this.detectingCrop = true;
    try {
      const start = this.derived?.start ?? 0;
      const args = getCropDetectArgs(
        media.path,
        start,
        Number.parseInt(this.vtrack, 10) || 0,
      );
      const { stdout } = await window.kurippu.runProcess('ffmpeg', args);
      const area = parseCropDetectOutput(stdout);
      if (!area) return;
      if (area.w >= media.width && area.h >= media.height) {
        this.clearCrop();
      } else {
        this.cropw = String(area.w);
        this.croph = String(area.h);
        this.cropx = String(area.x);
        this.cropy = String(area.y);
      }
    } catch {
      // Best-effort; the crop inputs stay editable either way.
    } finally {
      this.detectingCrop = false;
    }
  }
}
