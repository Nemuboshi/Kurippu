import { describe, expect, it } from 'vitest';
import {
  containerToVideo,
  letterbox,
  loopCheck,
  sarDisplay,
  videoToContainer,
} from './math';

describe('sarDisplay', () => {
  it('keeps square pixels unchanged', () => {
    expect(sarDisplay(1920, 1080, 1)).toEqual({ dw: 1920, dh: 1080 });
  });
  it('widens display width when sar>1', () => {
    expect(sarDisplay(720, 576, 16 / 9 / (720 / 576))).toEqual({
      dw: 720 * (16 / 9 / 1.25),
      dh: 576,
    });
    expect(sarDisplay(720, 576, 2)).toEqual({ dw: 1440, dh: 576 });
  });
  it('shrinks display height when sar<1', () => {
    expect(sarDisplay(1920, 1080, 0.5)).toEqual({ dw: 1920, dh: 2160 });
  });
});

describe('letterbox', () => {
  it('fits width-limited video centered vertically', () => {
    const vr = letterbox(1000, 1000, 1920, 1080, 1920);
    expect(vr.width).toBeCloseTo(1000);
    expect(vr.height).toBeCloseTo(562.5);
    expect(vr.x).toBeCloseTo(0);
    expect(vr.y).toBeCloseTo(218.75);
    expect(vr.scalef).toBeCloseTo(1);
  });
  it('fits height-limited video centered horizontally', () => {
    const vr = letterbox(1000, 300, 1920, 1080, 1920);
    expect(vr.height).toBeCloseTo(300);
    expect(vr.width).toBeCloseTo(533.33, 1);
    expect(vr.x).toBeGreaterThan(0);
    expect(vr.scalef).toBeCloseTo(1);
  });
  it('scalef tracks the SAR display width, not the video width', () => {
    const vr = letterbox(1000, 1000, 1440, 576, 720);
    expect(vr.scalef).toBeCloseTo(0.5);
  });
});

describe('crop rect conversion', () => {
  it('round-trips a rect through container space', () => {
    const vr = letterbox(1000, 1000, 1920, 1080, 1920);
    const crop = { w: 640, h: 360, x: 100, y: 50 };
    const css = videoToContainer(crop, vr, 0);
    expect(css).not.toBeNull();
    if (!css) throw new Error('crop conversion returned no rectangle');
    const back = containerToVideo(css, vr, 1920, 1080);
    expect(back).toEqual(crop);
  });
  it('applies the inset on container conversion', () => {
    const vr = { x: 0, y: 0, width: 100, height: 100, scalef: 2 };
    const css = videoToContainer({ w: 20, h: 20, x: 10, y: 10 }, vr, 1);
    expect(css).toEqual({ left: 6, top: 6, width: 8, height: 8 });
  });
  it('clamps rects that spill outside the video', () => {
    const vr = { x: 0, y: 0, width: 960, height: 540, scalef: 2 };
    const back = containerToVideo(
      { left: -50, top: 0, width: 200, height: 100 },
      vr,
      1920,
      1080,
    );
    expect(back).toEqual({ w: 400, h: 200, x: 0, y: 0 });
  });
});

describe('loopCheck', () => {
  it('returns null inside the fragment', () => {
    expect(loopCheck(5, 2, 10)).toBeNull();
    expect(loopCheck(10, 2, 10)).toBeNull();
  });
  it('seeks back to mstart past mend', () => {
    expect(loopCheck(10.5, 2, 10)).toBe(2);
  });
  it('seeks to 0 when mstart is unset', () => {
    expect(loopCheck(10.5, null, 10)).toBe(0);
  });
  it('returns null without an end mark', () => {
    expect(loopCheck(999, 2, null)).toBeNull();
  });
  it('ignores an empty interval', () => {
    expect(loopCheck(10.5, 10, 10)).toBeNull();
    expect(loopCheck(10.5, 12, 10)).toBeNull();
  });
});
