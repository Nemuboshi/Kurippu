<script lang="ts">
import type { MediaInfo } from '../engine/options';
import { showTime } from '../engine/time';
import EncodePanel from './encode/EncodePanel.svelte';
import { encoding } from './encode/runner.svelte';
import AudioFx from './encoder/AudioFx.svelte';
import Codecs from './encoder/Codecs.svelte';
import SubTabs from './encoder/SubTabs.svelte';
import { EncoderStore } from './encoder/store.svelte';
import VideoFx from './encoder/VideoFx.svelte';
import type { CropRect } from './player/math';
import Player from './player/Player.svelte';
import type { Source } from './state';

interface Props {
  source: Source;
  onChangeSource: () => void;
}
const { source, onChangeSource }: Props = $props();
const store = new EncoderStore();
let currentTime = $state<number | null>(null);
let desktopDir = $state('');
$effect(() => {
  void window.kurippu.desktopPath().then((p) => {
    desktopDir = p;
  });
});
function onProbe(info: MediaInfo): void {
  store.setMedia(info);
}
function onCrop(crop: CropRect | null): void {
  if (!crop) {
    store.clearCrop();
    return;
  }
  store.cropw = String(crop.w);
  store.croph = String(crop.h);
  store.cropx = String(crop.x);
  store.cropy = String(crop.y);
}
const crop = $derived.by<CropRect | null>(() => {
  const values = [store.cropw, store.croph, store.cropx, store.cropy].map(
    Number,
  );
  return values.every(Number.isInteger) && values[0]! > 0 && values[1]! > 0
    ? { w: values[0]!, h: values[1]!, x: values[2]!, y: values[3]! }
    : null;
});

// Spec 02 §5: the tab area sits below the player, starts at 450px tall,
// and the divider above it is draggable. Encoding locks the layout.
const INITIAL_TABS_HEIGHT = 450;
const MIN_TABS_HEIGHT = 150;
const MIN_PLAYER_HEIGHT = 120;
let tabsHeight = $state(INITIAL_TABS_HEIGHT);
let encoderEl: HTMLDivElement | undefined = $state();
let dragging = false;

function startDrag(event: PointerEvent): void {
  if (encoding.current) return;
  dragging = true;
  event.preventDefault();
  window.addEventListener('pointermove', onDrag);
  window.addEventListener('pointerup', endDrag, { once: true });
}

function onDrag(event: PointerEvent): void {
  if (!dragging || !encoderEl) return;
  // Dragging the divider down shrinks the tab area: the pointer's distance
  // from the container bottom is the new tab height.
  const remaining = encoderEl.getBoundingClientRect().bottom - event.clientY;
  const max = encoderEl.clientHeight - MIN_PLAYER_HEIGHT;
  tabsHeight = Math.max(MIN_TABS_HEIGHT, Math.min(remaining, max));
}

function endDrag(): void {
  dragging = false;
  window.removeEventListener('pointermove', onDrag);
}

// Keep the listener from outliving the component if a drag never ended.
$effect(() => {
  return () => window.removeEventListener('pointermove', onDrag);
});
</script>

<div class="encoder" bind:this={encoderEl}>
  <button class="change" disabled={encoding.current} onclick={onChangeSource}>open another source</button>
  <div class="player-pane"><Player src={source.path} {crop} disabled={encoding.current} {onCrop} onProbe={onProbe} onTime={(t) => { currentTime = t; }} onMarkStart={(t) => store.markStart(t)} onMarkEnd={(t) => store.markEnd(t)} /></div>
  <div class="divider" class:dragging role="separator" aria-orientation="horizontal" aria-label="Resize player and panels" onpointerdown={startDrag}></div>
  <div class="tabs-pane" style="height:{tabsHeight}px"><SubTabs {store} disabled={encoding.current}>
    {#snippet info()}<dl class="info">
      <dt>path</dt><dd><button class="path" onclick={() => void window.kurippu.openPath(source.path)} oncontextmenu={(e) => { e.preventDefault(); void window.kurippu.showItemInFolder(source.path); }}>{source.path}</button></dd>
      <dt>title</dt><dd>{store.media?.title ?? source.title ?? '—'}</dd>
      <dt>duration</dt><dd>{store.media ? showTime(store.media.duration) : '—'}</dd>
      <dt>bitrate</dt><dd>{store.media?.bitrate ? `${Math.round(store.media.bitrate / 1000)} Kbps` : '—'}</dd>
      <dt>size</dt><dd>{store.media?.fileSize ? `${(store.media.fileSize / 2 ** 20).toFixed(2)} MiB` : '—'}</dd>
      <dt>format</dt><dd>{store.media?.format ?? '—'}</dd>
      {#each store.media?.vtracks ?? [] as t, i}<dt>video #{i}</dt><dd>{t.codec}, {t.width}x{t.height}, SAR {t.sar ?? 1}, {t.fps ?? '?'} fps</dd>{/each}
      {#each store.media?.atracks ?? [] as t, i}<dt>audio #{i}</dt><dd>{t.codec}, {t.channels ?? '?'} channels, {t.sampleRate ?? '?'} Hz, {t.lang ?? 'unknown'}</dd>{/each}
      {#each store.media?.stracks ?? [] as t, i}<dt>subtitle #{i}</dt><dd>{t.codec}, {t.lang ?? 'unknown'}</dd>{/each}
    </dl>{/snippet}
    {#snippet video()}<VideoFx {store} />{/snippet}
    {#snippet audio()}<AudioFx {store} />{/snippet}
    {#snippet codecs()}<Codecs {store} />{/snippet}
    {#snippet encode()}<EncodePanel {source} rawArgs={store.rawArgs} valid={store.derived?.allValid ?? false} twoPass={store.mode2Pass} container={store.derived?.container ?? 'mp4'} duration={store.derived?.duration ?? 0} avgFrameRate={String(store.media?.vtracks[0]?.fps ?? 25)} defaultDir={desktopDir} {currentTime} media={store.media} />{/snippet}
  </SubTabs></div>
</div>
<style>
.encoder{display:flex;flex-direction:column;height:100%;min-height:0;position:relative}.change{position:absolute;z-index:4;top:5px;left:5px}.player-pane{flex:1;min-height:0;min-width:0}.divider{flex:none;height:6px;cursor:ns-resize;border-top:1px solid #ddd;background:#f4f4f4}.divider:hover,.divider.dragging{background:#e0e0e0}.divider.dragging{background:#ffd54f}.tabs-pane{flex:none;min-height:0;overflow:auto;border-top:1px solid #ddd}.info{display:grid;grid-template-columns:max-content 1fr;gap:4px 10px;margin:0;padding:10px;font-size:13px}.info dt{color:#777}.info dd{margin:0;word-break:break-all}.path{all:unset;color:#1565c0;cursor:pointer}
</style>
