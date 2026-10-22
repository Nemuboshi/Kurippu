// Playback backend contract. The real implementation talks to the libmpv
// N-API addon; the renderer only ever sees this interface.

import type { MediaInfo } from '../../engine/options';

export type PlayerTrackSelection = {
  videoId: number;
  audioId: number | null;
  subtitleId: number | null;
};

export interface PlayerBackend {
  // Attaches rendering to the container. The mpv addon draws via HWND/wid.
  attach(el: HTMLElement): void;
  detach(): void;
  play(): void;
  pause(): void;
  toggle(): void;
  seek(seconds: number): void;
  // direction: +1 forward, -1 backward.
  frameStep(direction: 1 | -1): void;
  // volume: 0-100.
  setVolume(volume: number): void;
  setMuted(muted: boolean): void;
  setTracks(selection: PlayerTrackSelection): void;
  // Toggles yadif deinterlacing on the libmpv video chain.
  setDeinterlace(on: boolean): void;
  // Registers a time observer. Returns an unsubscribe function.
  onTime(cb: (seconds: number) => void): () => void;
  getTime(): number;
  getDuration(): number;
  isPlaying(): boolean;
  // The probe reports the same track list the encoder consumes.
  probe(path: string): Promise<MediaInfo>;
  destroy(): void;
}
