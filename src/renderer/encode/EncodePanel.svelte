<script lang="ts">
import { baseName, outputFileName } from '../../engine/output';
import type { Source } from '../state';
import { encoding, openFolder, openOutput, startEncode } from './runner.svelte';

interface Props {
  source: Source;
  rawArgs: string;
  twoPass: boolean;
  container: 'mp4' | 'webm';
  duration: number;
  avgFrameRate: string;
  defaultDir: string;
  // Current player frame time in seconds; null while nothing plays.
  currentTime: number | null;
}
const {
  source,
  rawArgs,
  twoPass,
  container,
  duration,
  avgFrameRate,
  defaultDir,
  currentTime,
}: Props = $props();

const api = window.kurippu;

let previewText = $state('');
let title = $state(source.title ?? baseName(source));
let outpath = $state(
  `${defaultDir.replace(/[\\/]+$/, '')}/${outputFileName(baseName(source), 0, container)}`,
);
let outDir = $state(defaultDir);
let progress = $state(0);
let logText = $state('');
let lastOutput = $state<string | null>(null);
let menu = $state<{ x: number; y: number } | null>(null);
let logEl: HTMLPreElement;

function log(line: string): void {
  // Scroll only when the view is already at the bottom, so reading old
  // output is not interrupted by new lines.
  const atBottom =
    !logEl || logEl.scrollTop + logEl.clientHeight >= logEl.scrollHeight - 4;
  logText = `${logText}${line}\n`;
  if (atBottom)
    queueMicrotask(() => {
      if (logEl) logEl.scrollTop = logEl.scrollHeight;
    });
}

function run(kind: 'test' | 'normal'): void {
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
    },
    {
      log,
      progress: (p) => {
        progress = p;
      },
      finished: (path) => {
        lastOutput = path;
      },
    },
  );
}

function onClock(): void {
  if (currentTime != null) previewText = currentTime.toFixed(3);
}

async function onPreviewFile(): Promise<void> {
  const p = await api.openFileDialog([
    { name: 'Images', extensions: ['jpg', 'jpeg', 'png'] },
  ]);
  if (p) previewText = p.split(/[\\/]/).at(-1) ?? p;
}

async function onBrowse(): Promise<void> {
  const p = await api.saveOutputDialog(outpath, container);
  if (p) {
    outpath = p;
    const m = /^(.*)[\\/][^\\/]+$/.exec(p);
    if (m?.[1]) outDir = m[1];
  }
}

function selectLog(): void {
  const sel = window.getSelection();
  if (!sel || !logEl) return;
  const range = document.createRange();
  range.selectNodeContents(logEl);
  sel.removeAllRanges();
  sel.addRange(range);
}

function copyLog(): void {
  const sel = window.getSelection();
  const text =
    sel && logEl?.contains(sel.anchorNode) && !sel.isCollapsed
      ? sel.toString()
      : logText;
  void navigator.clipboard.writeText(text);
}

function onLogKeydown(e: KeyboardEvent): void {
  if (e.ctrlKey && e.key === 'c') {
    e.preventDefault();
    copyLog();
  }
}

function onLogContext(e: MouseEvent): void {
  e.preventDefault();
  menu = { x: e.clientX, y: e.clientY };
}
</script>

<div class="encode">
  <div class="inputs">
    <label>
      preview
      <input
        type="text"
        readonly
        value={previewText}
        title="Click to clear"
        disabled={encoding.current}
        onclick={() => { previewText = ''; }}
      />
    </label>
    <button title="Use current player time" disabled={encoding.current} onclick={onClock}>🕐</button>
    <button title="Pick a jpg/png image" disabled={encoding.current} onclick={() => void onPreviewFile()}>📂</button>
    <label>
      title
      <input type="text" bind:value={title} disabled={encoding.current} />
    </label>
    <label class="grow">
      path
      <input type="text" bind:value={outpath} disabled={encoding.current} />
    </label>
    <button title="Choose output file" disabled={encoding.current} onclick={() => void onBrowse()}>📂</button>
  </div>

  <div class="actions">
    <button onclick={() => void run('test')}>
      {encoding.current ? 'cancel' : 'test'}
    </button>
    <button onclick={() => void run('normal')}>
      {encoding.current ? 'cancel' : 'normal'}
    </button>
    <button
      disabled={lastOutput == null}
      onclick={() => { if (lastOutput) void openOutput(lastOutput); }}
      oncontextmenu={(e) => {
        e.preventDefault();
        if (lastOutput) void openFolder(lastOutput);
      }}
    >open</button>
    <progress value={progress} max="100"></progress>
  </div>

  <!-- svelte-ignore a11y_no_static_element_interactions -->
  <pre
    class="log"
    tabindex="0"
    bind:this={logEl}
    onkeydown={onLogKeydown}
    oncontextmenu={onLogContext}
  >{logText}</pre>

  {#if menu}
    <!-- svelte-ignore a11y_no_static_element_interactions a11y_click_events_have_key_events -->
    <div class="backdrop" onclick={() => { menu = null; }} oncontextmenu={(e) => { e.preventDefault(); menu = null; }}>
      <!-- svelte-ignore a11y_click_events_have_key_events a11y_no_static_element_interactions -->
      <div
        class="menu"
        style="left: {menu.x}px; top: {menu.y}px"
        onclick={(e) => e.stopPropagation()}
      >
        <button onclick={() => { logText = ''; menu = null; }}>Clear</button>
        <button onclick={() => { copyLog(); menu = null; }}>Copy</button>
        <button onclick={() => { selectLog(); menu = null; }}>Select all</button>
      </div>
    </div>
  {/if}
</div>

<style>
  .encode { display: flex; flex-direction: column; gap: 8px; height: 100%; padding: 8px; box-sizing: border-box; }
  .inputs { display: flex; align-items: flex-end; gap: 6px; }
  .inputs label { display: flex; flex-direction: column; font-size: 12px; color: #555; }
  .inputs label.grow { flex: 1; }
  .inputs input { font-family: inherit; }
  .actions { display: flex; gap: 8px; align-items: center; }
  .actions button { width: 90px; height: 32px; }
  .actions progress { flex: 1; height: 32px; }
  .log { flex: 1; margin: 0; padding: 6px; overflow: auto; font-family: monospace; font-size: 12px; background: #f8f8f8; white-space: pre-wrap; word-break: break-all; }
  .backdrop { position: fixed; inset: 0; z-index: 10; }
  .menu { position: fixed; display: flex; flex-direction: column; background: #fff; border: 1px solid #999; box-shadow: 2px 2px 6px rgba(0, 0, 0, 0.2); }
  .menu button { all: unset; padding: 6px 18px; font-size: 13px; cursor: pointer; }
  .menu button:hover { background: #e5f0fb; }
</style>
