// Derives validated encode parameters from raw UI option strings plus probed
// media info. Pure: no DOM, no Electron; the store feeds inputs and renders
// outputs. The UI interlocks (crf rewrites the limit input, an acodec switch
// clears ab) are applied by the store, so the strings arriving here are
// already post-interlock.

import { getVideoBitrate } from './bitrate';
import type { AudioCodec, MTMode, VideoCodec } from './constants';
import {
  BIG_FHD_BITRATE,
  BIG_HD_BITRATE,
  BIG_OTHER_BITRATE,
  BITRATE_RECOMMENDATION_TEXT,
  DEFAULT_AAC_BITRATE,
  DEFAULT_BITRATE,
  DEFAULT_LIMIT,
  DEFAULT_OPUS_BITRATE,
  DEFAULT_Q,
  DEFAULT_VORBIS_Q,
  DEFAULT_X264_Q,
  MAX_AAC_BITRATE,
  MAX_OPUS_BITRATE,
  MAX_Q,
  MAX_VORBIS_Q,
  MAX_X264_Q,
  MIN_AAC_BITRATE,
  MIN_OPUS_BITRATE,
  MIN_Q,
  MIN_VORBIS_Q,
  MIN_X264_Q,
  SHORT_FRAGMENT_SECONDS,
  SMALL_FHD_BITRATE,
  SMALL_HD_BITRATE,
  SMALL_OTHER_BITRATE,
} from './constants';
import { getFinalHeight, getFinalWidth, isAnamorphic } from './dimensions';
import type { RawArgsInput } from './ffmpeg';
import { parseTime } from './time';
import type { FieldError } from './validators';
import {
  emptyToNull,
  inRange,
  isFloat,
  isInt,
  validateAmplify,
  validateCropH,
  validateCropW,
  validateCropX,
  validateCropY,
  validateFadeIn,
  validateFadeOut,
  validateScaleH,
  validateScaleW,
} from './validators';

export interface OptionInputs {
  cropw: string;
  croph: string;
  cropx: string;
  cropy: string;
  scalew: string;
  scaleh: string;
  deinterlace: boolean;
  fixSAR: boolean;
  burnSubs: boolean;
  strack: string; // subtitle track index; '' = none
  subpath: string; // external subtitle file; '' = use embedded/source
  hasAudio: boolean;
  atrack: string; // audio track index
  fadeIn: string;
  fadeOut: string;
  amplify: string;
  start: string;
  end: string;
  vcodec: VideoCodec;
  acodec: AudioCodec;
  limit: string;
  quality: string;
  ab: string;
  mode2Pass: boolean;
  modeLimit: boolean;
  modeCRF: boolean;
  modeMT: MTMode;
  preset: string;
}

export interface TrackInfo {
  id?: number;
  codec: string;
  width?: number;
  height?: number;
  sar?: number;
  lang?: string;
  channels?: number;
  fps?: number;
  sampleRate?: number;
  bitrate?: number;
  profile?: string;
  selected?: boolean;
  title?: string;
}

// These codecs contain text that the subtitles filter can render. Unknown
// codecs are treated as image tracks and use the overlay filter instead.
const TEXT_SUBTITLE_CODECS = new Set([
  'ass',
  'eia_608',
  'eia_708',
  'jacosub',
  'microdvd',
  'mov_text',
  'mpl2',
  'pjs',
  'realtext',
  'sami',
  'scc',
  'ssa',
  'stl',
  'subrip',
  'subviewer',
  'subviewer1',
  'text',
  'tedcaptions',
  'ttml',
  'vplayer',
  'webvtt',
]);

export function isTextSubtitleCodec(codec: string): boolean {
  return TEXT_SUBTITLE_CODECS.has(codec.trim().toLowerCase());
}

export interface MediaInfo {
  path: string;
  duration: number;
  width: number;
  height: number;
  sar: number;
  vtracks: TrackInfo[];
  atracks: TrackInfo[];
  stracks: TrackInfo[];
  title?: string;
  bitrate?: number;
  fileSize?: number;
  format?: string;
  vtrackn?: number; // selected video track index; 0 when omitted
}

// Sub-tab ids; errors and warnings are grouped by tab so the tab header can
// light up red or orange.
export type OptionsTab = 'video' | 'audio' | 'codecs';

export interface OptionsResult {
  errors: Record<OptionsTab, FieldError[]>;
  warnings: Record<OptionsTab, string[]>;
  values: RawArgsInput | null; // null unless every field validated
  allValid: boolean;
  limitNum: number; // limit input as MB (limit mode) or Kbps (bitrate mode)
  vb: number | null; // resulting video bitrate in Kbps; null when CRF
  quality: number | null; // CRF value; null in non-CRF mode with empty input
  abNum: number | null; // Kbps for opus/aac, quality for vorbis, null for copy
  duration: number; // fragment length after start/end
  start: number | null;
  end: number | null;
  finalWidth: number;
  finalHeight: number;
  anamorph: boolean;
  container: 'mp4' | 'webm';
  atrackChannels: number;
  copyAvailable: boolean; // the selected audio track codec can be copied
}

function pushError(
  errors: Record<OptionsTab, FieldError[]>,
  tab: OptionsTab,
  list: FieldError[],
): void {
  for (const e of list) errors[tab].push(e);
}

export function deriveOptions(
  input: OptionInputs,
  media: MediaInfo,
  threads: number,
): OptionsResult {
  const errors: Record<OptionsTab, FieldError[]> = {
    video: [],
    audio: [],
    codecs: [],
  };
  const warnings: Record<OptionsTab, string[]> = {
    video: [],
    audio: [],
    codecs: [],
  };

  // --- Field validation ---------------------------------------------------
  const videoErrors: FieldError[] = [];
  const cropw = validateCropW(videoErrors, input.cropw);
  const croph = validateCropH(videoErrors, input.croph);
  const cropx = validateCropX(videoErrors, input.cropx);
  const cropy = validateCropY(videoErrors, input.cropy);
  const scalew = validateScaleW(videoErrors, input.scalew);
  const scaleh = validateScaleH(videoErrors, input.scaleh);
  pushError(errors, 'video', videoErrors);

  const audioErrors: FieldError[] = [];
  const fadeIn =
    input.acodec === 'copy' ? null : validateFadeIn(audioErrors, input.fadeIn);
  const fadeOut =
    input.acodec === 'copy'
      ? null
      : validateFadeOut(audioErrors, input.fadeOut);
  const amplify =
    input.acodec === 'copy'
      ? null
      : validateAmplify(audioErrors, input.amplify);
  pushError(errors, 'audio', audioErrors);

  const codecsErrors: FieldError[] = [];
  const total = media.duration;
  let start: number | null = null;
  let end: number | null = null;
  const startRaw = input.start.trim();
  const endRaw = input.end.trim();
  if (startRaw !== '') {
    try {
      start = parseTime(startRaw);
      if (!inRange(start, 0, total - 0.001)) {
        codecsErrors.push({ field: 'start', message: 'out of range' });
        start = null;
      }
    } catch {
      codecsErrors.push({ field: 'start', message: 'invalid time' });
    }
  }

  let graphicSubs = false;
  if (input.burnSubs && input.subpath.trim() === '') {
    const subtitleIndex = Number.parseInt(input.strack, 10);
    const subtitle = Number.isInteger(subtitleIndex)
      ? media.stracks[subtitleIndex]
      : undefined;
    if (!subtitle) {
      errors.video.push({
        field: 'strack',
        message: 'select an embedded subtitle track',
      });
    } else graphicSubs = !isTextSubtitleCodec(subtitle.codec);
  }
  if (endRaw !== '') {
    try {
      end = parseTime(endRaw);
      if (end > total + 0.0005) {
        codecsErrors.push({ field: 'end', message: 'out of range' });
        end = null;
      } else if (end != null && start != null && end - start <= 0) {
        codecsErrors.push({
          field: 'end',
          message: 'must be greater than start',
        });
        end = null;
      }
    } catch {
      codecsErrors.push({ field: 'end', message: 'invalid time' });
    }
  }
  // Empty end means the rest of the file; the fragment duration must stay > 0.
  const effectiveEnd = end ?? total;
  if (start != null && effectiveEnd - start <= 0) {
    codecsErrors.push({ field: 'end', message: 'must be greater than start' });
    end = null;
  }

  let limitNum: number;
  let limitValid = true;
  if (input.modeCRF) {
    // The crf interlock rewrites the limit input to "0"; honor anything else
    // as a stray edit and treat it as 0.
    limitNum = 0;
  } else if (input.modeLimit) {
    const s = emptyToNull(input.limit.trim());
    if (s === null) {
      limitNum = DEFAULT_LIMIT;
    } else if (!isFloat(s) || !inRange(Number.parseFloat(s), 0.001)) {
      codecsErrors.push({
        field: 'limit',
        message: 'must be a number >= 0.001',
      });
      limitNum = DEFAULT_LIMIT;
      limitValid = false;
    } else {
      limitNum = Number.parseFloat(s);
    }
  } else {
    const s = emptyToNull(input.limit.trim());
    if (s === null) {
      limitNum = DEFAULT_BITRATE;
    } else if (!isFloat(s) || !inRange(Number.parseFloat(s), 1)) {
      codecsErrors.push({ field: 'limit', message: 'must be a number >= 1' });
      limitNum = DEFAULT_BITRATE;
      limitValid = false;
    } else {
      limitNum = Number.parseFloat(s);
    }
  }

  let quality: number | null = null;
  {
    const s = emptyToNull(input.quality.trim());
    const min = input.vcodec === 'x264' ? MIN_X264_Q : MIN_Q;
    const max = input.vcodec === 'x264' ? MAX_X264_Q : MAX_Q;
    const def = input.vcodec === 'x264' ? DEFAULT_X264_Q : DEFAULT_Q;
    if (input.modeCRF) {
      if (s === null) {
        quality = def;
      } else if (!isInt(s) || !inRange(Number.parseInt(s, 10), min, max)) {
        codecsErrors.push({
          field: 'quality',
          message: `must be an integer between ${min} and ${max}`,
        });
        quality = def;
      } else {
        quality = Number.parseInt(s, 10);
      }
    } else if (s !== null) {
      if (!isInt(s) || !inRange(Number.parseInt(s, 10), min, max)) {
        codecsErrors.push({
          field: 'quality',
          message: `must be an integer between ${min} and ${max}`,
        });
      } else {
        quality = Number.parseInt(s, 10);
      }
    }
  }

  let abNum: number | null = null;
  {
    const s = emptyToNull(input.ab.trim());
    if (input.acodec === 'copy' || !input.hasAudio) {
      abNum = null;
    } else if (input.acodec === 'vorbis') {
      if (s === null) {
        abNum = DEFAULT_VORBIS_Q;
      } else if (
        !isInt(s) ||
        !inRange(Number.parseInt(s, 10), MIN_VORBIS_Q, MAX_VORBIS_Q)
      ) {
        codecsErrors.push({
          field: 'ab',
          message: `must be an integer between ${MIN_VORBIS_Q} and ${MAX_VORBIS_Q}`,
        });
      } else {
        abNum = Number.parseInt(s, 10);
      }
    } else if (input.acodec === 'opus') {
      if (s === null) {
        abNum = DEFAULT_OPUS_BITRATE;
      } else if (
        !isFloat(s) ||
        !inRange(Number.parseFloat(s), MIN_OPUS_BITRATE, MAX_OPUS_BITRATE)
      ) {
        codecsErrors.push({
          field: 'ab',
          message: `must be between ${MIN_OPUS_BITRATE} and ${MAX_OPUS_BITRATE}`,
        });
      } else {
        abNum = Number.parseFloat(s);
      }
    } else {
      // aac
      if (s === null) {
        abNum = DEFAULT_AAC_BITRATE;
      } else if (
        !isInt(s) ||
        !inRange(Number.parseInt(s, 10), MIN_AAC_BITRATE, MAX_AAC_BITRATE)
      ) {
        codecsErrors.push({
          field: 'ab',
          message: `must be an integer between ${MIN_AAC_BITRATE} and ${MAX_AAC_BITRATE}`,
        });
      } else {
        abNum = Number.parseInt(s, 10);
      }
    }
  }
  pushError(errors, 'codecs', codecsErrors);

  // --- Derived values -------------------------------------------------------
  const vtrackn = media.vtrackn ?? 0;
  const atrackn = input.atrack === '' ? 0 : Number.parseInt(input.atrack, 10);
  const atrackInfo = media.atracks[atrackn];
  const atrackChannels = atrackInfo?.channels ?? 2;
  const anamorph = isAnamorphic(media.sar);
  const copyAvailable = ['aac', 'vorbis', 'opus'].includes(
    atrackInfo?.codec ?? '',
  );

  const finalWidth = getFinalWidth({
    scalew,
    scaleh,
    cropw,
    croph,
    fixSAR: input.fixSAR,
    width: media.width,
    height: media.height,
    sar: media.sar,
  });
  const finalHeight = getFinalHeight({
    scalew,
    scaleh,
    cropw,
    croph,
    fixSAR: input.fixSAR,
    width: media.width,
    height: media.height,
    sar: media.sar,
  });

  const duration =
    (start != null || end != null) && effectiveEnd - (start ?? 0) > 0
      ? effectiveEnd - (start ?? 0)
      : total;

  const vb = input.modeCRF
    ? null
    : getVideoBitrate({
        modeLimit: input.modeLimit,
        modeCRF: input.modeCRF,
        limit: limitNum,
        hasAudio: input.hasAudio,
        acodec: input.acodec,
        ab: abNum,
        duration,
      });

  // --- Warnings ---------------------------------------------------------------
  if (duration < SHORT_FRAGMENT_SECONDS && !input.modeCRF) {
    warnings.codecs.push('Consider CRF mode for such short fragment');
  }
  if (!input.modeCRF && input.modeLimit && vb != null) {
    const size = Math.max(finalWidth, finalHeight);
    const tooSmall =
      (size > 1280 && vb < SMALL_FHD_BITRATE) ||
      (size === 1280 && vb < SMALL_HD_BITRATE) ||
      vb < SMALL_OTHER_BITRATE;
    const tooBig =
      (size <= 1280 && vb > BIG_HD_BITRATE) ||
      (size <= 1920 && vb > BIG_FHD_BITRATE) ||
      vb > BIG_OTHER_BITRATE;
    if (tooSmall) {
      warnings.codecs.push(
        `Video bitrate ${vb} Kbps looks too low for this resolution. ${BITRATE_RECOMMENDATION_TEXT}`,
      );
    } else if (tooBig) {
      warnings.codecs.push(
        `Video bitrate ${vb} Kbps looks too high for this resolution. ${BITRATE_RECOMMENDATION_TEXT}`,
      );
    }
  }
  if (anamorph && !input.fixSAR) {
    warnings.video.push(
      'Anamorphic source: aspect ratio will be wrong unless fix SAR is checked',
    );
  }

  const allValid =
    errors.video.length === 0 &&
    errors.audio.length === 0 &&
    errors.codecs.length === 0 &&
    limitValid;

  const values: RawArgsInput | null = allValid
    ? {
        inpath: media.path,
        start,
        end,
        duration,
        hasAudio: input.hasAudio,
        vtrackn,
        atrackn,
        vcodec: input.vcodec,
        acodec: input.acodec,
        threads,
        vb,
        quality,
        preset: input.preset,
        modeMT: input.modeMT,
        deinterlace: input.deinterlace,
        crop: { w: cropw, h: croph, x: cropx, y: cropy },
        scalew,
        scaleh,
        finalWidth,
        finalHeight,
        fixSAR: input.fixSAR,
        anamorph,
        burnSubs: input.burnSubs,
        graphicSubs,
        subpath: emptyToNull(input.subpath.trim()),
        strackn: input.strack === '' ? null : Number.parseInt(input.strack, 10),
        speed: null,
        amplify,
        fadeIn,
        fadeOut,
        atrackChannels,
        ab: abNum,
      }
    : null;

  return {
    errors,
    warnings,
    values,
    allValid,
    limitNum,
    vb,
    quality,
    abNum,
    duration,
    start,
    end,
    finalWidth,
    finalHeight,
    anamorph,
    container: input.vcodec === 'x264' ? 'mp4' : 'webm',
    atrackChannels,
    copyAvailable,
  };
}
