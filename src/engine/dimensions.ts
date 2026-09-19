// Aspect-ratio math. Computes the final output dimensions from crop, scale,
// and SAR-fix inputs; ffmpeg needs even dimensions for yuv420p.

// Parses "num:den" into a ratio. Invalid input and "0:1" map to 1.
export function parseAR(s: string): number {
  const m = /^(\d+):(\d+)$/.exec(s.trim());
  if (!m) return 1;
  const num = Number.parseInt(m[1] ?? '0', 10);
  const den = Number.parseInt(m[2] ?? '0', 10);
  if (den === 0) return 1;
  const v = num / den;
  return v === 0 ? 1 : v;
}

export function darOf(width: number, height: number, sar: number): number {
  return (width / height) * sar;
}

export function isAnamorphic(sar: number): boolean {
  return sar !== 1;
}

// Rounds to the nearest even integer, ties upward: round2(n) = floor((n+1)/2)*2.
export function round2(n: number): number {
  return Math.floor((n + 1) / 2) * 2;
}

export interface DimensionInput {
  scalew: number | null;
  scaleh: number | null;
  cropw: number | null;
  croph: number | null;
  fixSAR: boolean;
  width: number;
  height: number;
  sar: number;
}

export function getFinalWidth(input: DimensionInput): number {
  const {
    scalew,
    scaleh,
    cropw,
    croph,
    fixSAR,
    width: w,
    height: h,
    sar,
  } = input;
  const dar = darOf(w, h, sar);
  if (scalew != null) return scalew;
  if (scaleh != null && (cropw != null || croph != null)) {
    let ar = (cropw ?? w) / (croph ?? h);
    if (fixSAR && sar > 1) ar *= sar;
    return round2(scaleh * ar);
  }
  if (scaleh != null) {
    return round2(scaleh * (fixSAR || sar === 1 ? dar : sar));
  }
  if (cropw != null) {
    return fixSAR && sar > 1 ? round2(cropw * sar) : cropw;
  }
  return fixSAR && dar < 1 ? round2(h * dar) : w;
}

export function getFinalHeight(input: DimensionInput): number {
  const {
    scalew,
    scaleh,
    cropw,
    croph,
    fixSAR,
    width: w,
    height: h,
    sar,
  } = input;
  const dar = darOf(w, h, sar);
  if (scaleh != null) return scaleh;
  if (scalew != null && (cropw != null || croph != null)) {
    let ar = (cropw ?? w) / (croph ?? h);
    if (fixSAR && sar < 1) ar /= sar;
    return round2(scalew / ar);
  }
  if (scalew != null) {
    return round2(scalew / (fixSAR || sar === 1 ? dar : sar));
  }
  if (croph != null) {
    return fixSAR && sar < 1 ? round2(croph / sar) : croph;
  }
  return fixSAR && dar > 1 ? round2(w / dar) : h;
}
