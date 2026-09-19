import type { MediaInfo, TrackInfo } from '../../engine/options';

interface MpvTrack {
  type?: string;
  codec?: string;
  lang?: string;
  'demux-w'?: number;
  'demux-h'?: number;
  'demux-par'?: number;
  'container-fps'?: number;
  'demux-channel-count'?: number;
  'demux-samplerate'?: number;
  'demux-bitrate'?: number;
  'demux-format'?: string;
  profile?: string;
}

function trackInfo(track: MpvTrack): TrackInfo {
  return {
    codec: track.codec ?? 'unknown',
    width: track['demux-w'],
    height: track['demux-h'],
    sar: track['demux-par'],
    fps: track['container-fps'],
    lang: track.lang,
    channels: track['demux-channel-count'],
    sampleRate: track['demux-samplerate'],
    bitrate: track['demux-bitrate'],
    profile: track.profile,
  };
}

export function mediaInfoFromMpv(
  path: string,
  duration: unknown,
  tracksValue: unknown,
  details: {
    title?: string;
    bitrate?: number;
    fileSize?: number;
    format?: string;
  } = {},
): MediaInfo {
  const tracks = Array.isArray(tracksValue) ? (tracksValue as MpvTrack[]) : [];
  const videos = tracks.filter((track) => track.type === 'video');
  if (videos.length === 0) throw new Error('No video tracks found');
  const vtracks = videos.map(trackInfo);
  const first = vtracks[0];
  if (!first) throw new Error('No video tracks found');
  return {
    path,
    duration: typeof duration === 'number' ? duration : 0,
    width: first.width ?? 0,
    height: first.height ?? 0,
    sar: first.sar && first.sar > 0 ? first.sar : 1,
    vtracks,
    atracks: tracks.filter((track) => track.type === 'audio').map(trackInfo),
    stracks: tracks.filter((track) => track.type === 'sub').map(trackInfo),
    ...details,
  };
}

export async function probeMpvSession(
  path: string,
  session: {
    open(source: string): Promise<void>;
    getProperty(name: string): Promise<unknown>;
    onEvent(
      callback: (event: { type: string; error?: string }) => void,
    ): () => void;
  },
): Promise<MediaInfo> {
  const loaded = new Promise<void>((resolve, reject) => {
    const timeout = setTimeout(() => {
      off();
      reject(new Error('Timed out while loading media'));
    }, 30_000);
    const off = session.onEvent((event) => {
      if (event.error) {
        clearTimeout(timeout);
        off();
        reject(new Error(event.error));
      } else if (event.type === 'file-loaded') {
        clearTimeout(timeout);
        off();
        resolve();
      }
    });
  });
  await session.open(path);
  await loaded;
  const [duration, tracks, metadata, bitrate, fileSize, format] =
    await Promise.all([
      session.getProperty('duration'),
      session.getProperty('track-list'),
      session.getProperty('metadata'),
      session.getProperty('demuxer-bitrate'),
      session.getProperty('file-size'),
      session.getProperty('file-format'),
    ]);
  const tags =
    metadata && typeof metadata === 'object'
      ? (metadata as Record<string, unknown>)
      : {};
  return mediaInfoFromMpv(path, duration, tracks, {
    title: typeof tags.title === 'string' ? tags.title : undefined,
    bitrate: typeof bitrate === 'number' ? bitrate : undefined,
    fileSize: typeof fileSize === 'number' ? fileSize : undefined,
    format: typeof format === 'string' ? format : undefined,
  });
}

export async function probeMpv(path: string): Promise<MediaInfo> {
  const session = await window._kurippuMpv.create({
    renderMode: 'canvas2d',
    width: 2,
    height: 2,
  });
  try {
    return await probeMpvSession(path, session);
  } finally {
    await session.destroy();
  }
}
