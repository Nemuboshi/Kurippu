<script lang="ts">
import type { MediaInfo } from '../../engine/options';
import { baseName, outputFileName } from '../../engine/output';
import type { Source } from '../state';
import { encoding, openFolder, openOutput, startEncode } from './runner.svelte';

interface Props {
  source: Source;
  rawArgs: string;
  valid: boolean;
  twoPass: boolean;
  container: 'mp4' | 'webm';
  duration: number;
  avgFrameRate: string;
  defaultDir: string;
  currentTime: number | null;
  media: MediaInfo | null;
}
const {
  source,
  rawArgs,
  valid,
  twoPass,
  container,
  duration,
  avgFrameRate,
  defaultDir,
  currentTime,
  media,
}: Props = $props();
const api = window.kurippu;
let previewText = $state('');
let preview = $state<{ path: string; time: number | null } | null>(null);
let title = $state(source.title ?? baseName(source));
let outpath = $state('');
let progress = $state(0);
let logText = $state('');
let lastOutput = $state<string | null>(null);
let menu = $state<{ x: number; y: number } | null>(null);
let logEl: HTMLPreElement;
let initializedDir = $state('');
function join(dir: string, file: string): string {
  return `${dir.replace(/[\\/]+$/, '')}/${file}`;
}
async function nextPath(): Promise<void> {
  if (!defaultDir) return;
  for (let i = 0; ; i++) {
    const p = join(defaultDir, outputFileName(baseName(source), i, container));
    if (!(await api.statPath(p)).exists) {
      outpath = p;
      initializedDir = defaultDir;
      return;
    }
  }
}
$effect(() => {
  if (defaultDir && initializedDir !== defaultDir) void nextPath();
});
function log(line: string): void {
  const bottom =
    !logEl || logEl.scrollTop + logEl.clientHeight >= logEl.scrollHeight - 4;
  logText += `${line}\n`;
  if (bottom)
    queueMicrotask(() => {
      if (logEl) logEl.scrollTop = logEl.scrollHeight;
    });
}
function run(kind: 'test' | 'normal'): void {
  if (!valid) {
    log('Fix invalid parameters.');
    return;
  }
  lastOutput = null;
  void startEncode(
    {
      kind,
      rawArgs,
      title,
      outpath,
      twoPass,
      container,
      duration,
      avgFrameRate,
      preview,
      sourcePath: source.path,
      videoCodec:
        container === 'mp4'
          ? 'libx264'
          : rawArgs.includes('libaom-av1')
            ? 'libaom-av1'
            : rawArgs.includes('libvpx-vp9')
              ? 'libvpx-vp9'
              : 'libvpx',
      width: media?.width ?? 0,
      height: media?.height ?? 0,
      sar: media?.sar ?? 1,
      hasAudio: (media?.atracks.length ?? 0) > 0,
    },
    {
      log,
      progress: (p) => {
        progress = p;
      },
      finished: (path) => {
        lastOutput = path;
        void nextPath();
      },
    },
  );
}
function onClock(): void {
  if (currentTime != null) {
    preview = { path: source.path, time: currentTime };
    previewText = currentTime.toFixed(3);
  }
}
async function onPreviewFile(): Promise<void> {
  const p = await api.openFileDialog([
    { name: 'Images', extensions: ['jpg', 'jpeg', 'png'] },
  ]);
  if (p) {
    preview = { path: p, time: null };
    previewText = p.split(/[\\/]/).at(-1) ?? p;
  }
}
async function onBrowse(): Promise<void> {
  const p = await api.saveOutputDialog(outpath, container);
  if (p) outpath = p;
}
function clearPreview(): void {
  preview = null;
  previewText = '';
}
function selectLog(): void {
  const s = window.getSelection();
  if (!s || !logEl) return;
  const r = document.createRange();
  r.selectNodeContents(logEl);
  s.removeAllRanges();
  s.addRange(r);
}
function copyLog(): void {
  const s = window.getSelection();
  void navigator.clipboard.writeText(
    s && logEl?.contains(s.anchorNode) && !s.isCollapsed
      ? s.toString()
      : logText,
  );
}
</script>
<div class="encode">
 <div class="inputs"><label>preview<input readonly value={previewText} title="Click to clear" disabled={encoding.current} onclick={clearPreview}/></label><button title="Use current player time" disabled={encoding.current} onclick={onClock}>🕐</button><button title="Pick image" disabled={encoding.current} onclick={()=>void onPreviewFile()}>📂</button><label>title<input bind:value={title} disabled={encoding.current}/></label><label class="grow">path<input bind:value={outpath} disabled={encoding.current}/></label><button title="Choose output file" disabled={encoding.current} onclick={()=>void onBrowse()}>📂</button></div>
 <div class="actions"><button disabled={!valid&&!encoding.current} onclick={()=>void run('test')}>{encoding.current?'cancel':'test'}</button><button disabled={!valid&&!encoding.current} onclick={()=>void run('normal')}>{encoding.current?'cancel':'normal'}</button><button disabled={lastOutput==null} onclick={()=>{if(lastOutput)void openOutput(lastOutput);}} oncontextmenu={(e)=>{e.preventDefault();if(lastOutput)void openFolder(lastOutput);}}>open</button><progress value={progress} max="100"></progress></div>
 <!-- svelte-ignore a11y_no_noninteractive_tabindex a11y_no_noninteractive_element_interactions -->
 <pre class="log" tabindex="0" bind:this={logEl} onkeydown={(e)=>{if(e.ctrlKey&&e.key==='c'){e.preventDefault();copyLog();}}} oncontextmenu={(e)=>{e.preventDefault();menu={x:e.clientX,y:e.clientY};}}>{logText}</pre>
 {#if menu}<!-- svelte-ignore a11y_click_events_have_key_events a11y_no_static_element_interactions --><div class="backdrop" onclick={()=>{menu=null;}}><div class="menu" style="left:{menu.x}px;top:{menu.y}px" onclick={(e)=>e.stopPropagation()}><button onclick={()=>{logText='';menu=null;}}>Clear</button><button onclick={()=>{copyLog();menu=null;}}>Copy</button><button onclick={()=>{selectLog();menu=null;}}>Select all</button></div></div>{/if}
</div>
<style>.encode{display:flex;flex-direction:column;gap:8px;height:100%;padding:8px;box-sizing:border-box}.inputs{display:flex;align-items:flex-end;gap:6px}.inputs label{display:flex;flex-direction:column;font-size:12px;color:#555}.inputs .grow{flex:1}.actions{display:flex;gap:8px;align-items:center}.actions button{width:90px;height:32px}.actions progress{flex:1;height:32px}.log{flex:1;margin:0;padding:6px;overflow:auto;font:12px monospace;background:#f8f8f8;white-space:pre-wrap;word-break:break-all}.backdrop{position:fixed;inset:0;z-index:10}.menu{position:fixed;display:flex;flex-direction:column;background:#fff;border:1px solid #999}.menu button{padding:6px 18px}</style>
