// Pure player math: letterbox mapping, crop rect conversion, loop boundary.

export interface CropRect {
  w: number;
  h: number;
  x: number;
  y: number;
}

export interface VideoRect {
  x: number;
  y: number;
  width: number;
  height: number;
  scalef: number;
}

// Display size after SAR correction. sar>1 widens the display width;
// sar<1 shrinks the display height.
export function sarDisplay(
  width: number,
  height: number,
  sar: number,
): { dw: number; dh: number } {
  if (sar > 1) return { dw: width * sar, dh: height };
  return { dw: width, dh: height / sar };
}

// Centered letterbox fit of the SAR-corrected video into the container.
// scalef converts container pixels to video pixels.
export function letterbox(
  cw: number,
  ch: number,
  dw: number,
  dh: number,
  vwidth: number,
): VideoRect {
  const scale = Math.min(cw / dw, ch / dh);
  const width = dw * scale;
  const height = dh * scale;
  return {
    x: (cw - width) / 2,
    y: (ch - height) / 2,
    width,
    height,
    scalef: vwidth / dw,
  };
}

// Converts a crop rect from video pixels to container pixels (with inset).
// Returns null when the video does not fit in the container.
export function videoToContainer(
  rect: CropRect,
  vr: VideoRect,
  inset: number,
): { left: number; top: number; width: number; height: number } | null {
  if (vr.scalef <= 0) return null;
  return {
    left: vr.x + rect.x / vr.scalef + inset,
    top: vr.y + rect.y / vr.scalef + inset,
    width: rect.w / vr.scalef - inset * 2,
    height: rect.h / vr.scalef - inset * 2,
  };
}

// Converts a crop rect from container pixels back to video pixels.
// Clamps into the video area; returns null when the rect is degenerate.
export function containerToVideo(
  rect: { left: number; top: number; width: number; height: number },
  vr: VideoRect,
  vwidth: number,
  vheight: number,
): CropRect | null {
  const x = Math.round((rect.left - vr.x) * vr.scalef);
  const y = Math.round((rect.top - vr.y) * vr.scalef);
  const w = Math.round(rect.width * vr.scalef);
  const h = Math.round(rect.height * vr.scalef);
  const cx = Math.max(0, Math.min(vwidth - 1, x));
  const cy = Math.max(0, Math.min(vheight - 1, y));
  const cw = Math.max(1, Math.min(vwidth - cx, w));
  const ch = Math.max(1, Math.min(vheight - cy, h));
  if (cw <= 0 || ch <= 0) return null;
  return { w: cw, h: ch, x: cx, y: cy };
}

// Loop boundary check: playing past mend wraps back to mstart.
// mstart/mend null = unbounded side; guard against an empty interval.
export function loopCheck(
  time: number,
  mstart: number | null,
  mend: number | null,
): number | null {
  if (mend == null) return null;
  if (mstart != null && mend <= mstart) return null;
  if (time <= mend) return null;
  return mstart ?? 0;
}
