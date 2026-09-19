<script lang="ts">
import type { MediaInfo } from '../../engine/options';
import { showTime } from '../../engine/time';
import type { PlayerBackend } from './backend';
import Controls from './Controls.svelte';
import CropOverlay from './CropOverlay.svelte';
import type { CropRect, VideoRect } from './math';
import { letterbox, loopCheck, sarDisplay } from './math';
import { createPlayerBackend } from './mock';
import SeekBar from './SeekBar.svelte';

interface Props {
  src: string | null;
  onCrop?: (crop: CropRect | null) => void;
  // Reports the probe result once the source is loaded.
  onProbe?: (info: MediaInfo) => void;
  // Reports the playback position, used by the encode tab preview field.
  onTime?: (seconds: number) => void;
}
const { src, onCrop, onProbe, onTime }: Props = $props();

const backend: PlayerBackend = createPlayerBackend();

let playing = $state(false);
let time = $state(0);
let duration = $state(0);
let volume = $state(100);
let muted = $state(false);
let mstart = $state<number | null>(null);
let mend = $state<number | null>(null);
let loop = $state(false);
let fullscreen = $state(false);
let crop = $state<CropRect | null>(null);
let probe = $state<MediaInfo | null>(null);
let containerEl = $state<HTMLElement | null>(null);
let videoW = $state(0);
let videoH = $state(0);
let controlsRef: ReturnType<typeof Controls> | null = null;

// Tracks container size so the letterbox rect and crop overlay stay correct.
$effect(() => {
  if (!containerEl) return;
  const ro = new ResizeObserver(() => {
    videoW = containerEl!.clientWidth;
    videoH = containerEl!.clientHeight;
    // Geometry changed, so container-pixel crop state is stale.
    crop = null;
    onCrop?.(null);
  });
  ro.observe(containerEl);
  return () => ro.disconnect();
});

// Loads the source and wires backend time into local state.
$effect(() => {
  let off: (() => void) | null = null;
  let cancelled = false;
  void (async () => {
    try {
      const info = await backend.probe(src ?? 'mock://blank');
      if (cancelled) return;
      probe = info;
      duration = info.duration;
      onProbe?.(info);
      off = backend.onTime((t) => {
        time = t;
        onTime?.(t);
        playing = backend.isPlaying();
        // Fragment loop: wrap back to mstart when playback passes mend.
        if (loop) {
          const back = loopCheck(t, mstart, mend);
          if (back !== null) backend.seek(back);
        }
      });
    } catch {
      probe = null;
    }
  })();
  return () => {
    cancelled = true;
    off?.();
  };
});

const vr = $derived.by<VideoRect | null>(() => {
  const v = probe?.vtracks[0];
  if (!v?.width || !v.height || videoW === 0 || videoH === 0) return null;
  const { dw, dh } = sarDisplay(v.width, v.height, v.sar ?? 1);
  return letterbox(videoW, videoH, dw, dh, v.width);
});

// The mock backend has no drawing surface, so it renders a gray canvas with
// the current time instead. The mpv addon will draw into this area directly.
const mockTime = $derived(showTime(time));

function togglePlay(): void {
  backend.toggle();
  playing = backend.isPlaying();
}

function seek(t: number): void {
  backend.seek(t);
  time = backend.getTime();
}

function markStart(): void {
  mstart = time;
  if (mend !== null && mend <= time) mend = null;
}

function markEnd(): void {
  mend = time;
  if (mstart !== null && mstart >= time) mstart = null;
}

function setVolume(v: number): void {
  volume = v;
  muted = v === 0 ? muted : false;
  backend.setVolume(v);
}

function toggleMute(): void {
  muted = !muted;
  backend.setMuted(muted);
}

function toggleFullscreen(): void {
  if (document.fullscreenElement) void document.exitFullscreen();
  else if (containerEl) void containerEl.requestFullscreen();
}

function onKeydown(e: KeyboardEvent): void {
  if (e.target instanceof HTMLInputElement) return;
  switch (e.key) {
    case ' ':
      e.preventDefault();
      togglePlay();
      break;
    case 'ArrowLeft':
      e.preventDefault();
      seek(time - (e.shiftKey ? 1 : 5));
      break;
    case 'ArrowRight':
      e.preventDefault();
      seek(time + (e.shiftKey ? 1 : 5));
      break;
    case 'ArrowUp':
      e.preventDefault();
      setVolume(Math.min(100, volume + 5));
      break;
    case 'ArrowDown':
      e.preventDefault();
      setVolume(Math.max(0, volume - 5));
      break;
    case '.':
      backend.frameStep(1);
      break;
    case ',':
      backend.frameStep(-1);
      break;
    case 'm':
      toggleMute();
      break;
    case 't':
      controlsRef?.focusTime();
      break;
    case 'f':
      toggleFullscreen();
      break;
    case 'Escape':
      if (document.fullscreenElement) void document.exitFullscreen();
      break;
  }
}

function onWheel(e: WheelEvent): void {
  e.preventDefault();
  backend.frameStep(e.deltaY < 0 ? 1 : -1);
}

function onFullscreenChange(): void {
  fullscreen = document.fullscreenElement !== null;
  // Fullscreen changes the geometry, so the crop overlay is stale.
  crop = null;
  onCrop?.(null);
}

$effect(() => {
  document.addEventListener('fullscreenchange', onFullscreenChange);
  return () =>
    document.removeEventListener('fullscreenchange', onFullscreenChange);
});

function onCropChange(c: CropRect | null): void {
  crop = c;
  onCrop?.(c);
}
</script>

<!-- svelte-ignore a11y_no_static_element_interactions -->
<div
  class="player"
  bind:this={containerEl}
  tabindex="0"
  role="application"
  aria-label="video player"
  onkeydown={onKeydown}
>
  <div class="video" onclick={togglePlay} onwheel={onWheel}>
    <div class="mock-surface">{mockTime}</div>
    {#if vr && probe?.vtracks[0]}
      {@const vtrack = probe.vtracks[0]}
      <CropOverlay
        {vr}
        vwidth={vtrack.width ?? 0}
        vheight={vtrack.height ?? 0}
        {crop}
        onCrop={onCropChange}
      />
    {/if}
  </div>
  <Controls
    bind:this={controlsRef}
    {playing}
    {time}
    {duration}
    {mstart}
    {mend}
    {loop}
    {muted}
    {volume}
    hasAudio={(probe?.atracks.length ?? 0) > 0}
    {fullscreen}
    onTogglePlay={togglePlay}
    onMarkStart={markStart}
    onMarkEnd={markEnd}
    onSeek={seek}
    onToggleLoop={() => (loop = !loop)}
    onToggleMute={toggleMute}
    onVolume={setVolume}
    onFullscreen={toggleFullscreen}
  />
  <SeekBar {duration} {time} {mstart} {mend} onSeek={seek} />
</div>

<style>
  .player {
    display: flex;
    flex-direction: column;
    height: 100%;
    outline: none;
    background: #000;
    font-family: Roboto, sans-serif;
  }
  .video {
    position: relative;
    flex: 1;
    min-height: 0;
    overflow: hidden;
  }
  .mock-surface {
    position: absolute;
    inset: 0;
    display: flex;
    align-items: center;
    justify-content: center;
    background: #9e9e9e;
    color: #fff;
    font-size: 48px;
    font-variant-numeric: tabular-nums;
    user-select: none;
  }
</style>
