import { describe, expect, it } from 'vitest';
import type { MediaInfo, OptionInputs } from './options';
import { deriveOptions } from './options';

const media: MediaInfo = {
  path: '/tmp/in.mkv',
  duration: 60,
  width: 1920,
  height: 1080,
  sar: 1,
  vtracks: [{ codec: 'h264', width: 1920, height: 1080, sar: 1 }],
  atracks: [{ codec: 'aac', lang: 'en', channels: 2 }],
  stracks: [],
};

function baseInput(over: Partial<OptionInputs> = {}): OptionInputs {
  return {
    cropw: '',
    croph: '',
    cropx: '',
    cropy: '',
    scalew: '',
    scaleh: '',
    deinterlace: false,
    fixSAR: false,
    burnSubs: false,
    strack: '',
    subpath: '',
    hasAudio: true,
    atrack: '0',
    fadeIn: '',
    fadeOut: '',
    amplify: '',
    start: '',
    end: '',
    vcodec: 'x264',
    acodec: 'aac',
    limit: '',
    quality: '',
    ab: '',
    mode2Pass: false,
    modeLimit: false,
    modeCRF: true,
    modeMT: 'row-mt',
    preset: 'veryslow',
    ...over,
  };
}

describe('deriveOptions', () => {
  it('crf mode forces limit to 0 and keeps default quality', () => {
    const r = deriveOptions(
      baseInput({ modeCRF: true, limit: '19' }),
      media,
      8,
    );
    expect(r.allValid).toBe(true);
    expect(r.limitNum).toBe(0);
    expect(r.vb).toBeNull();
    expect(r.values?.quality).toBe(23); // x264 default
    expect(r.values?.vb).toBeNull();
  });

  it('x264 produces mp4 container, others webm', () => {
    expect(
      deriveOptions(baseInput({ vcodec: 'x264' }), media, 8).container,
    ).toBe('mp4');
    expect(
      deriveOptions(baseInput({ vcodec: 'vp9' }), media, 8).container,
    ).toBe('webm');
    expect(
      deriveOptions(baseInput({ vcodec: 'av1' }), media, 8).container,
    ).toBe('webm');
  });

  it('copy is available only for aac/vorbis/opus source tracks', () => {
    const withAac = deriveOptions(baseInput(), media, 8);
    expect(withAac.copyAvailable).toBe(true);
    const mp3Media: MediaInfo = {
      ...media,
      atracks: [{ codec: 'mp3', channels: 2 }],
    };
    expect(deriveOptions(baseInput(), mp3Media, 8).copyAvailable).toBe(false);
    const opusMedia: MediaInfo = {
      ...media,
      atracks: [{ codec: 'opus', channels: 2 }],
    };
    expect(deriveOptions(baseInput(), opusMedia, 8).copyAvailable).toBe(true);
  });

  it('vorbis ab defaults to quality 4', () => {
    const r = deriveOptions(
      baseInput({ vcodec: 'vp9', acodec: 'vorbis', ab: '' }),
      media,
      8,
    );
    expect(r.abNum).toBe(4);
    expect(r.values?.ab).toBe(4);
  });

  it('aac ab defaults to 128 and validates the 128-768 range', () => {
    const r = deriveOptions(baseInput({ ab: '' }), media, 8);
    expect(r.abNum).toBe(128);
    const bad = deriveOptions(baseInput({ ab: '100' }), media, 8);
    expect(bad.allValid).toBe(false);
    expect(bad.values).toBeNull();
    expect(bad.errors.codecs.some((e) => e.field === 'ab')).toBe(true);
  });

  it('copy acodec ignores fade and amplify input', () => {
    const r = deriveOptions(
      baseInput({ acodec: 'copy', fadeIn: '1', amplify: '8' }),
      media,
      8,
    );
    expect(r.allValid).toBe(true);
    expect(r.values?.fadeIn).toBeNull();
    expect(r.values?.amplify).toBeNull();
    expect(r.values?.ab).toBeNull();
  });

  it('quality is null in non-CRF mode when empty', () => {
    const r = deriveOptions(
      baseInput({ modeCRF: false, quality: '' }),
      media,
      8,
    );
    expect(r.quality).toBeNull();
    expect(r.values?.quality).toBeNull();
    expect(r.vb).toBe(5000); // DEFAULT_BITRATE for empty limit
  });

  it('warns about short fragments in non-CRF mode', () => {
    const r = deriveOptions(
      baseInput({ modeCRF: false, start: '0', end: '3' }),
      media,
      8,
    );
    expect(r.warnings.codecs).toContain(
      'Consider CRF mode for such short fragment',
    );
    const crf = deriveOptions(
      baseInput({ modeCRF: true, start: '0', end: '3' }),
      media,
      8,
    );
    expect(crf.warnings.codecs).toHaveLength(0);
  });

  it('warns about out-of-range bitrate in limit mode with recommendation text', () => {
    // 1 MB over 60 s -> ~136 Kbps, way under the recommendation floor.
    const r = deriveOptions(
      baseInput({ modeCRF: false, modeLimit: true, limit: '1' }),
      media,
      8,
    );
    expect(
      r.warnings.codecs.some((w) =>
        w.includes('Recommended bitrates: 2000÷5000 (HD)'),
      ),
    ).toBe(true);
    // No warning in CRF mode.
    const crf = deriveOptions(
      baseInput({ modeCRF: true, modeLimit: true, limit: '1' }),
      media,
      8,
    );
    expect(
      crf.warnings.codecs.some((w) => w.includes('Recommended bitrates')),
    ).toBe(false);
  });

  it('rejects end <= start and invalid times', () => {
    const r = deriveOptions(baseInput({ start: '10', end: '10' }), media, 8);
    expect(r.allValid).toBe(false);
    expect(r.errors.codecs.some((e) => e.field === 'end')).toBe(true);
    const bad = deriveOptions(baseInput({ start: 'abc' }), media, 8);
    expect(bad.allValid).toBe(false);
    expect(bad.errors.codecs.some((e) => e.field === 'start')).toBe(true);
  });

  it('computes limit-mode video bitrate from target size', () => {
    const r = deriveOptions(
      baseInput({ modeCRF: false, modeLimit: true, limit: '19' }),
      media,
      8,
    );
    // floor(19 * 8192 / 60 - 128) = 2466
    expect(r.vb).toBe(2466);
    expect(r.values?.vb).toBe(2466);
  });

  it('groups field errors by tab', () => {
    const r = deriveOptions(
      baseInput({ cropw: '0', amplify: '100', quality: '99' }),
      media,
      8,
    );
    expect(r.errors.video.some((e) => e.field === 'cropw')).toBe(true);
    expect(r.errors.audio.some((e) => e.field === 'amplify')).toBe(true);
    expect(r.errors.codecs.some((e) => e.field === 'quality')).toBe(true);
    expect(r.allValid).toBe(false);
  });

  it('warns when anamorphic source has fixSAR unchecked', () => {
    const anamorphic: MediaInfo = { ...media, sar: 1.5 };
    const r = deriveOptions(baseInput({ fixSAR: false }), anamorphic, 8);
    expect(r.warnings.video.length).toBeGreaterThan(0);
    const fixed = deriveOptions(baseInput({ fixSAR: true }), anamorphic, 8);
    expect(fixed.warnings.video).toHaveLength(0);
  });

  it('uses overlay for image subtitles in burned subtitle output', () => {
    const imageSubs: MediaInfo = {
      ...media,
      stracks: [{ codec: 'hdmv_pgs_subtitle', lang: 'jpn' }],
    };
    const result = deriveOptions(
      baseInput({ burnSubs: true, strack: '0' }),
      imageSubs,
      8,
    );
    expect(result.allValid).toBe(true);
    expect(result.values?.strackn).toBe(0);
    expect(result.values?.graphicSubs).toBe(true);
  });

  it('allows text subtitles for burned subtitle output', () => {
    const textSubs: MediaInfo = {
      ...media,
      stracks: [{ codec: 'subrip', lang: 'eng' }],
    };
    const result = deriveOptions(
      baseInput({ burnSubs: true, strack: '0' }),
      textSubs,
      8,
    );
    expect(result.allValid).toBe(true);
    expect(result.values?.strackn).toBe(0);
    expect(result.values?.graphicSubs).toBe(false);
  });
});
