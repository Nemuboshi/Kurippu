<script lang="ts">
import type { Source, YtInfo } from './state';
import {
  audioFormats,
  defaultSubtitle,
  downloadArgs,
  parseProgress,
  resolveArgs,
  subtitleFormats,
  videoFormats,
  ytdlpLeftovers,
} from './ytdlp';

interface Props {
  onLoad: (source: Source) => void;
}
const { onLoad }: Props = $props();

let url = $state('');
let error = $state('');
let resolving = $state(false);
let info = $state<YtInfo | null>(null);
let vfid = $state('');
let afid = $state('');
let sid = $state('none');
let downloading = $state(false);
let downloadFailed = $state(false);
let progress = $state(0);
let statusLine = $state('');
let procId = $state<number | null>(null);
let outpath = $state('');
let offStart: (() => void) | null = null;

const api = window.kurippu;

async function pickFile(): Promise<void> {
  const path = await api.openVideoDialog();
  if (path) onLoad({ path });
}

function onDrop(e: DragEvent): void {
  e.preventDefault();
  const file = e.dataTransfer?.files[0];
  if (file)
    onLoad({ path: (file as File & { path?: string }).path ?? file.name });
}

async function startResolve(): Promise<void> {
  error = '';
  const ytdlp = await api.resolveYtDlp();
  if (!ytdlp) {
    error = 'yt-dlp not found, install: https://github.com/yt-dlp/yt-dlp';
    return;
  }
  resolving = true;
  const r = await api.runProcess(ytdlp, resolveArgs(url));
  resolving = false;
  if (r.code !== 0) {
    error = `yt-dlp failed with code ${r.code ?? r.signal}`;
    return;
  }
  try {
    info = JSON.parse(r.stdout.trim().split('\n').at(-1) ?? '') as YtInfo;
    vfid = videoFormats(info)[0]?.id ?? '';
    afid = audioFormats(info)[0]?.id ?? 'none';
    sid = defaultSubtitle(info);
  } catch {
    error = 'could not parse yt-dlp output';
    return;
  }
  // Debug override: skip the format picker and download a fixed format.
  const { format } = await api.debugSettings();
  if (format) {
    vfid = format;
    afid = 'none';
    sid = 'none';
    void startDownload();
  }
}

function onZoneClick(): void {
  if (error) {
    error = '';
    return;
  }
  if (url.trim()) void startResolve();
  else void pickFile();
}

async function startDownload(): Promise<void> {
  if (!info) return;
  const ytdlp = (await api.resolveYtDlp()) ?? 'yt-dlp';
  const ffmpeg = await api.resolveFfmpeg();
  outpath = await api.makeTempPath('.mkv');
  await api.registerTempFile(outpath);
  downloading = true;
  downloadFailed = false;
  progress = 0;
  statusLine = 'spawning yt-dlp';
  const off = api.onProcessLog((_id: number, text: string) => {
    const p = parseProgress(text);
    if (p != null) progress = p;
    const line = text
      .split(/[\r\n]+/)
      .filter(Boolean)
      .at(-1);
    if (line) statusLine = line;
  });
  const args = downloadArgs(
    info.webpage_url,
    vfid,
    afid,
    sid,
    info.requested_subtitles?.[sid]?.ext ?? 'srt',
    outpath,
    ffmpeg,
  );
  offStart?.();
  offStart = api.onProcessStart((id) => {
    procId = id;
  });
  const r = await api.runProcess(ytdlp, args);
  off();
  offStart?.();
  offStart = null;
  procId = null;
  if (r.code === 0) {
    onLoad({
      path: outpath,
      saveAs: info._filename,
      title: `${info.title} <${info.webpage_url}>`,
    });
  } else {
    downloadFailed = true;
    statusLine = `yt-dlp exited with ${r.code ?? r.signal}`;
  }
}

async function cancelDownload(): Promise<void> {
  if (procId != null) await api.killProcess(procId);
  offStart?.();
  offStart = null;
  procId = null;
  const subExt = info?.requested_subtitles?.[sid]?.ext ?? 'srt';
  downloading = false;
  info = null;
  if (outpath) {
    // Remove yt-dlp leftovers from the aborted download.
    await api.deleteFiles(ytdlpLeftovers(outpath, vfid, afid, sid, subExt));
    await api.unregisterTempFile(outpath);
  }
}
</script>

<div class="source">
  <!-- svelte-ignore a11y_click_events_have_key_events a11y_no_static_element_interactions -->
  <div class="zone" class:disabled={resolving || downloading} onclick={onZoneClick} ondrop={onDrop} ondragover={(e) => e.preventDefault()}>
    {#if downloading}
      <div class="status" class:error-text={downloadFailed}>{statusLine}</div>
      <progress value={progress} max="100"></progress>
      <div class="row">
        <button onclick={(e) => { e.stopPropagation(); void startDownload(); }} disabled={!downloadFailed}>retry</button>
        <button onclick={(e) => { e.stopPropagation(); void cancelDownload(); }}>cancel</button>
      </div>
    {:else if info}
      {@const vids = videoFormats(info)}
      {@const auds = audioFormats(info)}
      {@const subs = subtitleFormats(info)}
      <!-- svelte-ignore a11y_no_static_element_interactions -->
      <div class="formats" onclick={(e) => e.stopPropagation()}>
        <label>Video format
          <select bind:value={vfid} disabled={vids.length === 0}>
            {#each vids as f}<option value={f.id}>{f.label}</option>{/each}
            {#if vids.length === 0}<option>none</option>{/if}
          </select>
        </label>
        <label>Audio format
          <select bind:value={afid} disabled={auds.length <= 1}>
            {#each auds as f}<option value={f.id}>{f.label}</option>{/each}
          </select>
        </label>
        <label>Subtitles
          <select bind:value={sid} disabled={subs.length <= 1}>
            {#each subs as f}<option value={f.id}>{f.label}</option>{/each}
          </select>
        </label>
        <div class="row">
          <button onclick={() => void startDownload()} disabled={!vfid}>download</button>
          <button onclick={() => { info = null; }}>cancel</button>
        </div>
      </div>
    {:else}
      <p>Click/drag your source video here</p>
      <p>or <input type="text" placeholder="enter URL" bind:value={url} disabled={resolving} onclick={(e) => e.stopPropagation()} /></p>
      {#if error}
        <!-- svelte-ignore a11y_no_static_element_interactions -->
        <p class="error-text" onclick={(e) => e.stopPropagation()}>{error}</p>
      {/if}
    {/if}
  </div>
  <p class="hint">ⓘ Any site supported by yt-dlp is accepted</p>
</div>

<style>
  .source { padding: 30px 50px 100px; height: 100%; box-sizing: border-box; display: flex; flex-direction: column; }
  .zone { flex: 1; border: 2px dashed #ccc; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 8px; cursor: pointer; }
  .zone.disabled { cursor: not-allowed; opacity: 0.6; }
  .error-text { color: #d32f2f; max-height: 7em; overflow: hidden; }
  .formats { display: flex; flex-direction: column; gap: 8px; }
  .formats label { display: flex; justify-content: space-between; gap: 12px; }
  .row { display: flex; gap: 8px; }
  .row button { width: 250px; height: 40px; }
  progress { width: 505px; height: 40px; }
  .status { width: 505px; height: 40px; overflow: hidden; }
  .hint { color: #777; font-size: 12px; }
</style>
