<script lang="ts">
import type { Snippet } from 'svelte';
import type { EncoderStore, SubTab } from './store.svelte';

interface Props {
  store: EncoderStore;
  info?: Snippet;
  video?: Snippet;
  audio?: Snippet;
  codecs?: Snippet;
  encode?: Snippet;
}
const { store, info, video, audio, codecs, encode }: Props = $props();

const tabs: { id: SubTab; label: string }[] = [
  { id: 'info', label: 'info' },
  { id: 'video', label: 'video fx' },
  { id: 'audio', label: 'audio fx' },
  { id: 'codecs', label: 'codecs' },
  { id: 'encode', label: 'encode' },
];

// Error/warning counts per tab, used to tint the tab titles.
function issues(id: SubTab): { errors: number; warnings: number } {
  const d = store.derived;
  if (!d) return { errors: 0, warnings: 0 };
  if (id === 'video' || id === 'audio' || id === 'codecs') {
    return { errors: d.errors[id].length, warnings: d.warnings[id].length };
  }
  return { errors: 0, warnings: 0 };
}
</script>

<div class="subtabs">
  <div class="tab-head">
    {#each tabs as tab (tab.id)}
      {@const n = issues(tab.id)}
      <button
        class="tab-title"
        class:active={store.activeTab === tab.id}
        class:has-errors={n.errors > 0}
        class:has-warnings={n.errors === 0 && n.warnings > 0}
        onclick={() => (store.activeTab = tab.id)}
      >
        {tab.label}
      </button>
    {/each}
  </div>
  <div class="tab-body">
    {#if store.activeTab === 'info'}
      {#if info}
        {@render info()}
      {:else}
        <p class="placeholder">info tab placeholder</p>
      {/if}
    {:else if store.activeTab === 'video'}
      {@render video?.()}
    {:else if store.activeTab === 'audio'}
      {@render audio?.()}
    {:else if store.activeTab === 'codecs'}
      {@render codecs?.()}
    {:else}
      {@render encode?.()}
    {/if}
  </div>
</div>

<style>
  .subtabs {
    display: flex;
    flex-direction: column;
    height: 100%;
  }
  .tab-head {
    display: flex;
    height: 35px;
    flex: none;
    border-bottom: 1px solid #ddd;
  }
  .tab-title {
    flex: 1;
    border: none;
    background: none;
    font: inherit;
    text-transform: lowercase;
    cursor: pointer;
    color: #333;
  }
  .tab-title.active {
    background: #eee;
    box-shadow: inset 0 -2px 0 #ff9800;
  }
  .tab-title.has-errors {
    color: #d32f2f;
  }
  .tab-title.has-warnings {
    color: #f57c00;
  }
  .tab-body {
    flex: 1;
    min-height: 0;
    overflow-y: auto;
  }
  .placeholder {
    padding: 20px;
    color: #999;
  }
</style>
