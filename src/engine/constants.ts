// Encoding constants and defaults for the encoder UI and arg templates.

export const DEFAULT_LIMIT = 19;
export const DEFAULT_BITRATE = 5000;
export const DEFAULT_Q = 25;
export const DEFAULT_VIDEO_CODEC = 'x264';
export const DEFAULT_X264_Q = 23;
export const MIN_X264_Q = 0;
export const MAX_X264_Q = 51;
export const MIN_Q = 0;
export const MAX_Q = 63;
export const DEFAULT_AUDIO_CODEC = 'aac';
export const DEFAULT_OPUS_BITRATE = 128;
export const MIN_OPUS_BITRATE = 6;
export const MAX_OPUS_BITRATE = 510;
export const DEFAULT_VORBIS_Q = 4;
export const MIN_VORBIS_Q = -1;
export const MAX_VORBIS_Q = 10;
export const DEFAULT_AAC_BITRATE = 128;
export const MIN_AAC_BITRATE = 128;
export const MAX_AAC_BITRATE = 768;
export const PASS1_COEFF = 0.3;

export const SMALL_HD_BITRATE = 2000;
export const BIG_HD_BITRATE = 5000;
export const SMALL_FHD_BITRATE = 4000;
export const BIG_FHD_BITRATE = 10000;
export const SMALL_OTHER_BITRATE = 1000;
export const BIG_OTHER_BITRATE = 20000;

export const BITRATE_RECOMMENDATION_TEXT =
  'Recommended bitrates: 2000÷5000 (HD), 4000÷10000 (FHD), 1000÷20000 (other)';

// Vorbis quality → Kbps, used to back out the video bitrate in limit mode.
export const VORBIS_Q_TO_BITRATE: Record<number, number> = {
  '-1': 45,
  0: 64,
  1: 80,
  2: 96,
  3: 112,
  4: 128,
  5: 160,
  6: 192,
  7: 224,
  8: 256,
  9: 320,
  10: 500,
};

export const SHORT_FRAGMENT_SECONDS = 6;

export const FILE_EXTENSIONS = [
  'mkv',
  'webm',
  'ogg',
  'ogv',
  'ogm',
  'mp4',
  'mov',
  'm4v',
  'flv',
  'f4v',
  'ts',
  'tp',
  'm2ts',
  'vob',
  'mpg',
  'avi',
  'wmv',
  'asf',
  '3gp',
  'y4m',
];

export const SUBTITLE_EXTENSIONS = [
  'ass',
  'ssa',
  'srt',
  'webvtt',
  'vtt',
  'sub',
];

export type VideoCodec = 'av1' | 'vp9' | 'vp8' | 'x264';
export type AudioCodec = 'opus' | 'vorbis' | 'aac' | 'copy';
export type MTMode = 'row-mt' | 'tile-col' | 'no-mt';

export const X264_PRESETS = [
  'ultrafast',
  'superfast',
  'veryfast',
  'faster',
  'fast',
  'medium',
  'slow',
  'slower',
  'veryslow',
  'placebo',
] as const;

export function outputContainer(vcodec: VideoCodec): 'mp4' | 'webm' {
  return vcodec === 'x264' ? 'mp4' : 'webm';
}
