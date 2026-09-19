<script lang="ts">
import type { EncoderStore } from './store.svelte';

interface Props {
  store: EncoderStore;
}
const { store }: Props = $props();

// acodec=copy passes audio through untouched, so filters make no sense.
const fxDisabled = $derived(store.acodec === 'copy' || !store.hasAudio);
const media = $derived.by(() => store.media);
const noTracks = $derived.by(() => (media?.atracks.length ?? 0) === 0);
const validation = $derived.by(() => store.derived);
</script>

<div class="panel">
  <div class="row">
    <span class="name">audio track:</span>
    <input
      type="checkbox"
      bind:checked={store.hasAudio}
      disabled={noTracks}
    />
    <select bind:value={store.atrack} disabled={noTracks || !store.hasAudio}>
      {#each media?.atracks ?? [] as t, i (i)}
        <option value={String(i)}>#{i} ({t.codec}{t.lang ? `, ${t.lang}` : ''})</option>
      {/each}
    </select>
    {#if noTracks}
      <span class="muted">no audio tracks</span>
    {/if}
  </div>

  <div class="row">
    <span class="name">fade:</span>
    <input
      class="small"
      placeholder="in"
      bind:value={store.fadeIn}
      disabled={fxDisabled}
    />
    <input
      class="small"
      placeholder="out"
      bind:value={store.fadeOut}
      disabled={fxDisabled}
    />
  </div>

  <div class="row">
    <span class="name">amplify:</span>
    <input
      class="small"
      placeholder="1-64"
      bind:value={store.amplify}
      disabled={fxDisabled}
    />
  </div>

  {#each validation?.errors.audio ?? [] as e (e.field + e.message)}
    <p class="error">{e.field}: {e.message}</p>
  {/each}
  {#each validation?.warnings.audio ?? [] as w (w)}
    <p class="warning">{w}</p>
  {/each}
</div>

<style>
  .panel {
    padding: 10px;
    display: flex;
    flex-direction: column;
    gap: 8px;
  }
  .row {
    display: flex;
    align-items: center;
    gap: 6px;
  }
  .name {
    width: 110px;
    flex: none;
    text-align: right;
  }
  .small {
    width: 60px;
  }
  .muted {
    color: #999;
  }
  .error {
    color: #d32f2f;
    margin: 0;
  }
  .warning {
    color: #f57c00;
    margin: 0;
  }
</style>
