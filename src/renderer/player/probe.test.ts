import { describe, expect, it } from 'vitest';
import { mediaInfoFromMpv } from './probe';

describe('mediaInfoFromMpv', () => {
  it('maps libmpv tracks into encoder media info', () => {
    expect(
      mediaInfoFromMpv('clip.mkv', 12.5, [
        {
          id: 1,
          type: 'video',
          codec: 'h264',
          'demux-w': 1920,
          'demux-h': 1080,
          'demux-par': 1.25,
          'demux-fps': 23.976,
          'container-fps': 29.97,
          selected: true,
        },
        {
          id: 2,
          type: 'audio',
          codec: 'aac',
          'demux-channel-count': 6,
          lang: 'en',
          selected: true,
        },
        {
          id: 3,
          type: 'sub',
          codec: 'ass',
          lang: 'ja',
          title: 'Japanese',
          selected: false,
        },
      ]),
    ).toEqual({
      path: 'clip.mkv',
      duration: 12.5,
      width: 1920,
      height: 1080,
      sar: 1.25,
      vtracks: [
        {
          id: 1,
          codec: 'h264',
          width: 1920,
          height: 1080,
          sar: 1.25,
          fps: 23.976,
          lang: undefined,
          channels: undefined,
          selected: true,
        },
      ],
      atracks: [
        {
          id: 2,
          codec: 'aac',
          width: undefined,
          height: undefined,
          sar: undefined,
          fps: undefined,
          lang: 'en',
          channels: 6,
          selected: true,
        },
      ],
      stracks: [
        {
          id: 3,
          codec: 'ass',
          width: undefined,
          height: undefined,
          sar: undefined,
          fps: undefined,
          lang: 'ja',
          channels: undefined,
          selected: false,
          title: 'Japanese',
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
