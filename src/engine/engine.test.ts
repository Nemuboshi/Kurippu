import { describe, expect, it } from 'vitest';
import {
  effectiveAudioBitrate,
  feedProgressLine,
  getVideoBitrate,
  initProgress,
  parseFrameRate,
} from './bitrate';
import { VORBIS_Q_TO_BITRATE } from './constants';
import { getFinalHeight, getFinalWidth, parseAR, round2 } from './dimensions';
import { ceilFixed, parseTime, showTime } from './time';
import type { FieldError } from './validators';
import { validateAmplify, validateCropW, validateCropX } from './validators';

describe('parseTime', () => {
  it('parses plain seconds', () => {
    expect(parseTime('10')).toBe(10);
    expect(parseTime('10.5')).toBe(10.5);
  });
  it('parses m:s', () => {
    expect(parseTime('1:30')).toBe(90);
  });
  it('parses h:m:s', () => {
    expect(parseTime('1:02:03.500')).toBe(3723.5);
  });
  it('rejects invalid input', () => {
    expect(() => parseTime('abc')).toThrow('Invalid time');
    expect(() => parseTime('1:2:3:4')).toThrow('Invalid time');
  });
});

describe('showTime', () => {
  it('omits hours when zero', () => {
    expect(showTime(90)).toBe('01:30.000');
  });
  it('includes hours when nonzero', () => {
    expect(showTime(3723.5)).toBe('01:02:03.500');
  });
  it('floors milliseconds by default and ceils on request', () => {
    expect(showTime(1.0009)).toBe('00:01.000');
    expect(showTime(1.0001, { ceil: true })).toBe('00:01.001');
  });
});

describe('ceilFixed', () => {
  it('rounds up to the given decimals', () => {
    expect(ceilFixed(60.001, 3)).toBe('60.001');
    expect(ceilFixed(1 / 29.97, 3)).toBe('0.034');
  });
});

describe('parseAR', () => {
  it('parses num:den', () => {
    expect(parseAR('4:3')).toBeCloseTo(4 / 3);
  });
  it('maps invalid and 0:1 to 1', () => {
    expect(parseAR('0:1')).toBe(1);
    expect(parseAR('junk')).toBe(1);
  });
});

describe('round2', () => {
  it('rounds to even, ties upward', () => {
    expect(round2(3)).toBe(4);
    expect(round2(4)).toBe(4);
    expect(round2(5)).toBe(6);
  });
});

describe('final dimensions', () => {
  const base = {
    scalew: null,
    scaleh: null,
    cropw: null,
    croph: null,
    fixSAR: false,
    width: 1920,
    height: 1080,
    sar: 1,
  };
  it('scalew wins', () => {
    expect(getFinalWidth({ ...base, scalew: 640 })).toBe(640);
  });
  it('scaleh alone derives width from dar', () => {
    expect(getFinalWidth({ ...base, scaleh: 720 })).toBe(
      round2(720 * (16 / 9)),
    );
  });
  it('cropw passes through without fixSAR', () => {
    expect(getFinalWidth({ ...base, cropw: 1000 })).toBe(1000);
  });
  it('fixSAR expands cropw when sar > 1', () => {
    expect(getFinalWidth({ ...base, cropw: 1000, sar: 2, fixSAR: true })).toBe(
      2000,
    );
  });
  it('height mirrors width', () => {
    expect(getFinalHeight({ ...base, scalew: 1280 })).toBe(
      round2(1280 / (16 / 9)),
    );
    expect(getFinalHeight({ ...base, croph: 500 })).toBe(500);
  });
  it('anamorphic sar<1 shrinks crop height with fixSAR', () => {
    expect(
      getFinalHeight({ ...base, croph: 500, sar: 0.5, fixSAR: true }),
    ).toBe(1000);
  });
});

describe('getVideoBitrate', () => {
  it('acceptance: limit=19MB, 60s, aac 128k → floor(19*8192/60−128) = 2466k', () => {
    const vb = getVideoBitrate({
      modeLimit: true,
      modeCRF: false,
      limit: 19,
      hasAudio: true,
      acodec: 'aac',
      ab: 128,
      duration: 60,
    });
    expect(vb).toBe(Math.floor((19 * 8192) / 60 - 128));
    expect(vb).toBe(2466);
  });
  it('crf mode returns limit untouched', () => {
    expect(
      getVideoBitrate({
        modeLimit: true,
        modeCRF: true,
        limit: 19,
        hasAudio: false,
        acodec: 'aac',
        ab: null,
        duration: 60,
      }),
    ).toBe(19);
  });
  it('non-limit mode returns bitrate directly', () => {
    expect(
      getVideoBitrate({
        modeLimit: false,
        modeCRF: false,
        limit: 5000,
        hasAudio: false,
        acodec: 'aac',
        ab: null,
        duration: 60,
      }),
    ).toBe(5000);
  });
  it('clamps to 1', () => {
    expect(
      getVideoBitrate({
        modeLimit: true,
        modeCRF: false,
        limit: 0.001,
        hasAudio: true,
        acodec: 'aac',
        ab: 768,
        duration: 600,
      }),
    ).toBe(1);
  });
  it('vorbis uses the quality table', () => {
    expect(
      effectiveAudioBitrate({
        modeLimit: true,
        modeCRF: false,
        limit: 19,
        hasAudio: true,
        acodec: 'vorbis',
        ab: 4,
        duration: 60,
      }),
    ).toBe(VORBIS_Q_TO_BITRATE[4]);
  });
  it('copy falls back to 320', () => {
    expect(
      effectiveAudioBitrate({
        modeLimit: true,
        modeCRF: false,
        limit: 19,
        hasAudio: true,
        acodec: 'copy',
        ab: null,
        duration: 60,
      }),
    ).toBe(320);
  });
});

describe('parseFrameRate', () => {
  it('parses fraction and float', () => {
    expect(parseFrameRate('30000/1001')).toBeCloseTo(29.97, 2);
    expect(parseFrameRate('25')).toBe(25);
  });
});

describe('feedProgressLine', () => {
  it('accumulates and caps at 99', () => {
    const st = initProgress('25', 4); // 100 frames total
    feedProgressLine(st, 'frame=   50 fps=25');
    expect(st.progress).toBe(50);
    feedProgressLine(st, 'frame=  200 fps=25');
    expect(st.progress).toBe(99);
  });
  it('pass1 weights by 0.3', () => {
    const st = initProgress('25', 4);
    st.pass = 1;
    feedProgressLine(st, 'frame=  100 fps=25');
    expect(st.progress).toBeCloseTo(30);
  });
});

describe('field validators', () => {
  it('collects errors and maps empty to null', () => {
    const errors: FieldError[] = [];
    expect(validateCropW(errors, '')).toBeNull();
    expect(validateCropW(errors, 'abc')).toBeNull();
    expect(validateCropW(errors, '0')).toBeNull();
    expect(validateCropW(errors, '640')).toBe(640);
    expect(errors).toHaveLength(2);
    expect(validateCropX(errors, '-1')).toBeNull();
    expect(validateAmplify(errors, '65')).toBeNull();
    expect(validateAmplify(errors, '64')).toBe(64);
  });
});
