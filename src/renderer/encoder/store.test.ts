import { beforeEach, describe, expect, it, vi } from 'vitest';
import { EncoderStore } from './store.svelte';

// The constructor pings debug settings; stub the preload surface so the store
// can be constructed outside a browser.
beforeEach(() => {
  vi.stubGlobal('window', {
    kurippu: { debugSettings: () => Promise.resolve({}) },
  });
});

describe('mode interlocks', () => {
  it('crf clears limit mode and writes "0" into the limit input', () => {
    const s = new EncoderStore();
    s.modeLimit = true;
    s.limit = '5000';
    s.setModeCRF(true);
    expect(s.modeCRF).toBe(true);
    expect(s.modeLimit).toBe(false);
    expect(s.limit).toBe('0');
  });

  it('limit clears crf and rewrites the limit input so a stale 0 cannot leak', () => {
    const s = new EncoderStore();
    s.setModeCRF(true);
    expect(s.limit).toBe('0');
    // Switching to limit mode must not keep the crf "0", or the derived
    // bitrate collapses to 1 Kbps.
    s.setModeLimit(true);
    expect(s.modeLimit).toBe(true);
    expect(s.modeCRF).toBe(false);
    expect(s.limit).toBe('');
  });

  it('keeps crf and limit exclusive when 2pass pulls limit on', () => {
    const s = new EncoderStore();
    s.setModeCRF(true);
    s.setMode2Pass(true);
    expect(s.mode2Pass).toBe(true);
    expect(s.modeCRF).toBe(true);
    expect(s.modeLimit).toBe(false);
  });

  it('unchecking limit also unchecks 2pass', () => {
    const s = new EncoderStore();
    s.setModeLimit(true);
    expect(s.mode2Pass).toBe(true);
    s.setModeLimit(false);
    expect(s.mode2Pass).toBe(false);
  });
});
