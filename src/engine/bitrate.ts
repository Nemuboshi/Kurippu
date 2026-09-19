// Video bitrate derivation for limit mode and encode progress estimation
// from ffmpeg frame= output lines.

import type { AudioCodec } from './constants';
import { VORBIS_Q_TO_BITRATE } from './constants';

export interface BitrateInput {
  modeLimit: boolean;
  modeCRF: boolean;
  limit: number;
  hasAudio: boolean;
  acodec: AudioCodec;
  ab: number | null;
  duration: number;
}

// Fallback audio bitrate for acodec=copy when mpv reports no stream bitrate.
export const COPY_BITRATE_FALLBACK = 320;

// Effective audio bitrate in Kbps. For copy, pass the mpv demux-bitrate when
// known; otherwise the caller leaves ab null and this returns the fallback.
export function effectiveAudioBitrate(input: BitrateInput): number {
  if (!input.hasAudio) return 0;
  if (input.acodec === 'vorbis') {
    const q = input.ab ?? 4;
    return VORBIS_Q_TO_BITRATE[q] ?? 128;
  }
  if (input.acodec === 'copy') return input.ab ?? COPY_BITRATE_FALLBACK;
  return input.ab ?? 0;
}

// Returns the video bitrate in Kbps, an integer >= 1.
export function getVideoBitrate(input: BitrateInput): number {
  if (!input.modeLimit || input.modeCRF) return input.limit;
  const ab = effectiveAudioBitrate(input);
  const vb = Math.floor((input.limit * 8 * 1024) / input.duration - ab);
  return Math.max(1, vb);
}

export interface ProgressState {
  fps: number;
  totalFrames: number;
  progress: number;
  lastFrame: number;
  pass: 0 | 1 | 2;
}

// Parses an ffmpeg-style frame rate "30000/1001" into a float.
export function parseFrameRate(s: string): number {
  const m = /^(\d+)\/(\d+)$/.exec(s.trim());
  if (m) {
    const den = Number.parseInt(m[2] ?? '0', 10);
    if (den !== 0) return Number.parseInt(m[1] ?? '0', 10) / den;
  }
  const v = Number.parseFloat(s);
  return Number.isFinite(v) && v > 0 ? v : 0;
}

export function initProgress(
  avgFrameRate: string,
  duration: number,
): ProgressState {
  const fps = parseFrameRate(avgFrameRate);
  return {
    fps,
    totalFrames: Math.ceil(duration * fps),
    progress: 0,
    lastFrame: 0,
    pass: 0,
  };
}

const FRAME_RE = /^frame=\s*(\d+)\b/;

// Feeds one ffmpeg output line into the progress state. Progress is capped at
// 99; the caller sets 100 only on completion.
export function feedProgressLine(state: ProgressState, line: string): number {
  const m = FRAME_RE.exec(line);
  if (!m || state.totalFrames <= 0) return state.progress;
  const frame = Number.parseInt(m[1] ?? '0', 10);
  let delta = ((frame - state.lastFrame) / state.totalFrames) * 100;
  if (state.pass === 1) delta *= 0.3;
  if (state.pass === 2) delta *= 0.7;
  state.lastFrame = frame;
  state.progress = Math.min(state.progress + delta, 99);
  return state.progress;
}
