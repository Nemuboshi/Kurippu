import type { MediaInfo } from '../../engine/options';
import type { PlayerBackend } from './backend';

// Backend seam: the factory returns MockBackend today. Swap createPlayerBackend
// for the libmpv addon implementation when native/mpv lands; nothing else in
// the player UI should change.
export function createPlayerBackend(): PlayerBackend {
  return new MockBackend();
}

const MOCK_DURATION = 60;

// Timer-driven fake playback so the UI can be exercised without libmpv.
class MockBackend implements PlayerBackend {
  private time = 0;
  private playing = false;
  private timer: ReturnType<typeof setInterval> | null = null;
  private listeners = new Set<(seconds: number) => void>();

  attach(_el: HTMLElement): void {}
  detach(): void {}

  play(): void {
    if (this.playing) return;
    if (this.time >= MOCK_DURATION) this.time = 0;
    this.playing = true;
    this.timer = setInterval(() => {
      this.time = Math.min(MOCK_DURATION, this.time + 0.1);
      if (this.time >= MOCK_DURATION) this.pause();
      this.emit();
    }, 100);
  }

  pause(): void {
    this.playing = false;
    if (this.timer !== null) {
      clearInterval(this.timer);
      this.timer = null;
    }
  }

  toggle(): void {
    if (this.playing) this.pause();
    else this.play();
  }

  seek(seconds: number): void {
    this.time = Math.max(0, Math.min(MOCK_DURATION, seconds));
    this.emit();
  }

  frameStep(direction: 1 | -1): void {
    this.pause();
    // Steps at a fixed 24 fps frame length.
    this.seek(this.time + direction / 24);
  }

  setVolume(_volume: number): void {}
  setMuted(_muted: boolean): void {}

  onTime(cb: (seconds: number) => void): () => void {
    this.listeners.add(cb);
    return () => this.listeners.delete(cb);
  }

  getTime(): number {
    return this.time;
  }

  getDuration(): number {
    return MOCK_DURATION;
  }

  isPlaying(): boolean {
    return this.playing;
  }

  probe(path: string): Promise<MediaInfo> {
    return Promise.resolve({
      path,
      duration: MOCK_DURATION,
      width: 1920,
      height: 1080,
      sar: 1,
      vtracks: [{ codec: 'h264', width: 1920, height: 1080, sar: 1, fps: 24 }],
      atracks: [{ codec: 'aac', channels: 2 }],
      stracks: [],
    });
  }

  destroy(): void {
    this.pause();
    this.listeners.clear();
  }

  private emit(): void {
    for (const cb of this.listeners) cb(this.time);
  }
}
