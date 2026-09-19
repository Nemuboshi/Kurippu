// App-level state: one encode instance, three-state machine.

export interface Source {
  path: string;
  saveAs?: string;
  title?: string;
}

export type AppState = 'source' | 'info' | 'encoder';

export interface YtFormat {
  format_id: string;
  vcodec?: string;
  acodec?: string;
  width?: number;
  height?: number;
  fps?: number;
  abr?: number;
  filesize?: number;
  filesize_approx?: number;
  ext?: string;
}

export interface YtInfo {
  title: string;
  webpage_url: string;
  _filename: string;
  formats: YtFormat[];
  requested_subtitles?: Record<string, { ext: string }>;
}
