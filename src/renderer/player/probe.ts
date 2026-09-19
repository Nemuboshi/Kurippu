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
  };
}

export function mediaInfoFromMpv(
  path: string,
  duration: unknown,
  tracksValue: unknown,
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
  const [duration, tracks] = await Promise.all([
    session.getProperty('duration'),
    session.getProperty('track-list'),
  ]);
  return mediaInfoFromMpv(path, duration, tracks);
}

export async function probeMpv(path: string): Promise<MediaInfo> {
  const session = await window._electronMpvVideo.create({
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
