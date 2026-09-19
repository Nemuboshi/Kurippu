<script lang="ts">
import EncoderPage from './EncoderPage.svelte';
import InfoPage from './Info.svelte';
import SourcePage from './Source.svelte';
import type { AppState, Source } from './state';

let appState = $state<AppState>('source');
let source = $state<Source | null>(null);

const api = window.kurippu;

function basename(p: string): string {
  return p.split(/[\\/]/).at(-1) ?? p;
}

$effect(() => {
  void api.setHasSource(source !== null);
});

function updateTitle(): void {
  if (!source) {
    document.title = 'kurippu';
  } else {
    document.title = source.title ?? basename(source.path);
  }
}

async function loadSource(s: Source): Promise<void> {
  if (source) {
    const ok = await api.confirmDialog('Discard current source?');
    if (!ok) return;
  }
  source = s;
  appState = 'info';
  updateTitle();
}

function onProbed(): void {
  appState = 'encoder';
}

async function changeSource(): Promise<void> {
  if (!(await api.confirmDialog('Discard current source?'))) return;
  source = null;
  appState = 'source';
  updateTitle();
}

function onCancel(): void {
  source = null;
  appState = 'source';
  updateTitle();
}
</script>

<div class:hidden={appState !== 'source'}><SourcePage onLoad={(s) => void loadSource(s)} /></div>
{#if source}
  <div class:hidden={appState !== 'info'}><InfoPage {source} onDone={onProbed} onCancel={onCancel} /></div>
  <div class:hidden={appState !== 'encoder'}><EncoderPage {source} onChangeSource={() => void changeSource()} /></div>
{/if}

<style>
  div { height: 100%; }
  .hidden { display: none; }
</style>
