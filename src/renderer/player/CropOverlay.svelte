<script lang="ts">
import type { CropRect, VideoRect } from './math';
import { containerToVideo, videoToContainer } from './math';

interface Props {
  vr: VideoRect | null;
  vwidth: number;
  vheight: number;
  crop: CropRect | null;
  onCrop: (crop: CropRect | null) => void;
}
const { vr, vwidth, vheight, crop, onCrop }: Props = $props();

type Handle =
  | 'nw'
  | 'n'
  | 'ne'
  | 'e'
  | 'se'
  | 's'
  | 'sw'
  | 'w'
  | 'move'
  | 'draw';
const HANDLE_CURSORS: Record<Handle, string> = {
  nw: 'nwse-resize',
  se: 'nwse-resize',
  ne: 'nesw-resize',
  sw: 'nesw-resize',
  n: 'ns-resize',
  s: 'ns-resize',
  e: 'ew-resize',
  w: 'ew-resize',
  move: 'move',
  draw: 'crosshair',
};

const EDGE = 10;

// start* snapshot the pointer-down geometry; drag math uses deltas from it.
let startRect = $state<{
  left: number;
  top: number;
  width: number;
  height: number;
} | null>(null);
let startX = 0;
let startY = 0;
let handle = $state<Handle | null>(null);

const MIN_SIZE = 8;

function toCss(
  c: CropRect,
): { left: number; top: number; width: number; height: number } | null {
  if (!vr) return null;
  return videoToContainer(c, vr, 1);
}

// Rect committed during a live draw/resize. Kept local so small or
// out-of-bounds drags do not spam the parent with rejected crops.
let liveRect = $state<{
  left: number;
  top: number;
  width: number;
  height: number;
} | null>(null);

const shownRect = $derived(liveRect ?? (crop ? toCss(crop) : null));

function pickHandle(mx: number, my: number): Handle {
  const r = crop && vr ? toCss(crop) : null;
  if (r) {
    const onX = mx >= r.left - EDGE / 2 && mx <= r.left + r.width + EDGE / 2;
    const onY = my >= r.top - EDGE / 2 && my <= r.top + r.height + EDGE / 2;
    if (onX && onY) {
      const nearL = Math.abs(mx - r.left) <= EDGE;
      const nearR = Math.abs(mx - (r.left + r.width)) <= EDGE;
      const nearT = Math.abs(my - r.top) <= EDGE;
      const nearB = Math.abs(my - (r.top + r.height)) <= EDGE;
      if (nearL && nearT) return 'nw';
      if (nearR && nearT) return 'ne';
      if (nearL && nearB) return 'sw';
      if (nearR && nearB) return 'se';
      if (nearL) return 'w';
      if (nearR) return 'e';
      if (nearT) return 'n';
      if (nearB) return 's';
      return 'move';
    }
  }
  return 'draw';
}

function applyDelta(
  base: { left: number; top: number; width: number; height: number },
  h: Handle,
  dx: number,
  dy: number,
): { left: number; top: number; width: number; height: number } {
  let { left, top, width, height } = base;
  if (h === 'move') {
    left += dx;
    top += dy;
  } else {
    if (h.includes('w')) {
      left += dx;
      width -= dx;
    }
    if (h.includes('e')) width += dx;
    if (h.includes('n')) {
      top += dy;
      height -= dy;
    }
    if (h.includes('s')) height += dy;
    if (width < MIN_SIZE) width = MIN_SIZE;
    if (height < MIN_SIZE) height = MIN_SIZE;
  }
  return { left, top, width, height };
}

function emit(r: {
  left: number;
  top: number;
  width: number;
  height: number;
}): void {
  if (!vr) return;
  onCrop(containerToVideo(r, vr, vwidth, vheight));
}

function onPointerDown(e: PointerEvent): void {
  if (e.button !== 0 || !vr) return;
  e.preventDefault();
  const el = e.currentTarget as HTMLElement;
  const bounds = el.getBoundingClientRect();
  startX = e.clientX - bounds.left;
  startY = e.clientY - bounds.top;
  handle = pickHandle(startX, startY);
  if (handle === 'draw') {
    startRect = { left: startX, top: startY, width: 0, height: 0 };
  } else {
    startRect = crop ? toCss(crop) : null;
    if (!startRect) {
      handle = null;
      return;
    }
  }
  el.setPointerCapture(e.pointerId);
}

function onPointerMove(e: PointerEvent): void {
  if (handle === null || startRect === null || !vr) return;
  const bounds = (e.currentTarget as HTMLElement).getBoundingClientRect();
  const dx = e.clientX - bounds.left - startX;
  const dy = e.clientY - bounds.top - startY;
  const next =
    handle === 'draw'
      ? {
          left: Math.min(startX, startX + dx),
          top: Math.min(startY, startY + dy),
          width: Math.abs(dx),
          height: Math.abs(dy),
        }
      : applyDelta(startRect, handle, dx, dy);
  liveRect = next;
  emit(next);
}

function onPointerUp(e: PointerEvent): void {
  if (handle === null) return;
  // Drop degenerate draws so a plain click does not leave a 1px crop.
  if (
    liveRect &&
    liveRect.width < MIN_SIZE &&
    liveRect.height < MIN_SIZE &&
    handle === 'draw'
  ) {
    onCrop(null);
  }
  handle = null;
  startRect = null;
  liveRect = null;
  (e.currentTarget as HTMLElement).releasePointerCapture(e.pointerId);
}

function onContextMenu(e: MouseEvent): void {
  e.preventDefault();
  onCrop(null);
}

const cursor = $derived(handle !== null ? HANDLE_CURSORS[handle] : 'crosshair');
</script>

<!-- svelte-ignore a11y_no_static_element_interactions -->
<div
  class="overlay"
  style:cursor
  onpointerdown={onPointerDown}
  onpointermove={onPointerMove}
  onpointerup={onPointerUp}
  oncontextmenu={onContextMenu}
>
  {#if shownRect}
    <div
      class="rect"
      style:left="{shownRect.left}px"
      style:top="{shownRect.top}px"
      style:width="{shownRect.width}px"
      style:height="{shownRect.height}px"
    ></div>
  {/if}
</div>

<style>
  .overlay {
    position: absolute;
    inset: 0;
  }
  .rect {
    position: absolute;
    border: 3px solid orange;
    box-sizing: border-box;
    pointer-events: none;
  }
</style>
