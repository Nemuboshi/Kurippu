import { describe, expect, it } from 'vitest';
import { mediaInfoFromMpv } from './probe';

describe('mediaInfoFromMpv', () => {
  it('maps libmpv tracks into encoder media info', () => {
    expect(
      mediaInfoFromMpv('clip.mkv', 12.5, [
        {
          type: 'video',
          codec: 'h264',
          'demux-w': 1920,
          'demux-h': 1080,
          'demux-par': 1.25,
          'container-fps': 29.97,
        },
        { type: 'audio', codec: 'aac', 'demux-channel-count': 6, lang: 'en' },
        { type: 'sub', codec: 'ass', lang: 'ja' },
      ]),
    ).toEqual({
      path: 'clip.mkv',
      duration: 12.5,
      width: 1920,
      height: 1080,
      sar: 1.25,
      vtracks: [
        {
          codec: 'h264',
          width: 1920,
          height: 1080,
          sar: 1.25,
          fps: 29.97,
          lang: undefined,
          channels: undefined,
        },
      ],
      atracks: [
        {
          codec: 'aac',
          width: undefined,
          height: undefined,
          sar: undefined,
          fps: undefined,
          lang: 'en',
          channels: 6,
        },
      ],
      stracks: [
        {
          codec: 'ass',
          width: undefined,
          height: undefined,
          sar: undefined,
          fps: undefined,
          lang: 'ja',
          channels: undefined,
        },
      ],
    });
  });

  it('rejects sources without video tracks', () => {
    expect(() =>
      mediaInfoFromMpv('audio.flac', 4, [{ type: 'audio', codec: 'flac' }]),
    ).toThrow('No video tracks found');
  });
});
