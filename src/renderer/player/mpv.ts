import type { MediaInfo } from '../../engine/options';
import type { MpvVideoState } from '../../shared/mpv-types';
import type { PlayerBackend } from './backend';
import type { MpvVideoElement } from './mpv-video';
import { probeMpvSession } from './probe';

export function createPlayerBackend(): PlayerBackend {
  return new MpvBackend();
}

class MpvBackend implements PlayerBackend {
  private element: MpvVideoElement | null = null;
  private time = 0;
  private duration = 0;
  private playing = false;
  private volume = 100;
  private listeners = new Set<(seconds: number) => void>();

  attach(el: HTMLElement): void {
    if (this.element) return;
    const video = document.createElement('mpv-video');
    video.setAttribute('render-mode', 'shared-texture');
    video.addEventListener('mpv-state', ((
      event: CustomEvent<MpvVideoState>,
    ) => {
      this.time = event.detail.time;
      this.duration = event.detail.duration;
      this.playing = event.detail.status === 'Playing';
      for (const listener of this.listeners) listener(this.time);
    }) as EventListener);
    video.style.cssText = 'position:absolute;inset:0;width:100%;height:100%';
    el.prepend(video);
    this.element = video;
  }

  detach(): void {
    this.element?.remove();
    this.element = null;
  }

  play(): void {
    void this.element?.play();
  }
  pause(): void {
    void this.element?.pause();
  }
  toggle(): void {
    this.playing ? this.pause() : this.play();
  }
  seek(seconds: number): void {
    void this.element?.seek(seconds);
  }
  frameStep(direction: 1 | -1): void {
    this.pause();
    this.seek(this.time + direction / 24);
  }
  setVolume(volume: number): void {
    this.volume = volume;
    void this.element?.setVolume(volume);
  }
  setMuted(muted: boolean): void {
    void this.element?.setVolume(muted ? 0 : this.volume);
  }
  setDeinterlace(on: boolean): void {
    void this.element?.setDeinterlace(on);
  }
  onTime(cb: (seconds: number) => void): () => void {
    this.listeners.add(cb);
    return () => this.listeners.delete(cb);
  }
  getTime(): number {
    return this.time;
  }
  getDuration(): number {
    return this.duration;
  }
  isPlaying(): boolean {
    return this.playing;
  }

  async probe(path: string): Promise<MediaInfo> {
    if (!this.element) throw new Error('Player is not attached');
    const element = this.element;
    return probeMpvSession(path, {
      open: (source) => element.open(source),
      getProperty: (name) => element.getProperty(name),
      onEvent: (callback) => {
        const listener = ((
          event: CustomEvent<{ type: string; error?: string }>,
        ) => callback(event.detail)) as EventListener;
        element.addEventListener('mpv-event', listener);
        return () => element.removeEventListener('mpv-event', listener);
      },
    });
  }

  destroy(): void {
    void this.element?.destroy();
    this.detach();
    this.listeners.clear();
  }
}
