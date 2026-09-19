<script lang="ts">
import InfoPage from './Info.svelte';
import SourcePage from './Source.svelte';
import type { AppState, Source } from './state';

let appState = $state<AppState>('source');
let source = $state<Source | null>(null);

const api = window.kurippu;

function basename(p: string): string {
  return p.split(/[\\/]/).at(-1) ?? p;
}

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

function onCancel(): void {
  source = null;
  appState = 'source';
  updateTitle();
}
</script>

{#if appState === 'source'}
  <SourcePage onLoad={(s) => void loadSource(s)} />
{:else if appState === 'info' && source}
  <InfoPage {source} onDone={onProbed} onCancel={onCancel} />
{:else if appState === 'encoder' && source}
  <p style="padding: 20px">Encoder lands in the next task. Source: {source.path}</p>
{/if}
