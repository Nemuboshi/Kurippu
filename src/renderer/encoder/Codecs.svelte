<script lang="ts">
import { X264_PRESETS } from '../../engine/constants';
import { showTime } from '../../engine/time';
import type { EncoderStore } from './store.svelte';

interface Props {
  store: EncoderStore;
}
const { store }: Props = $props();

const validation = $derived.by(() => store.derived);

// x264 output is mp4, which only carries aac/ac3/mp3; webm carries
// opus/vorbis. copy needs a source track in a copyable codec.
const audioCodecs: readonly string[] =
  store.vcodec === 'x264' ? ['aac', 'copy'] : ['opus', 'vorbis', 'aac', 'copy'];

// Fall back to a valid codec when a vcodec switch makes the current acodec
// impossible (x264 loses opus/vorbis; a non-copyable track loses copy).
$effect(() => {
  if (!audioCodecs.includes(store.acodec)) {
    store.selectACodec('aac');
  }
  if (store.acodec === 'copy' && !validation?.copyAvailable) {
    store.selectACodec('aac');
  }
});

const showMT = $derived(store.vcodec === 'vp9' || store.vcodec === 'av1');
const limitPlaceholder = $derived(store.modeLimit ? 'limit' : 'bitrate');
const abPlaceholder = $derived(
  store.acodec === 'vorbis' ? 'quality' : 'bitrate',
);
const abDisabled = $derived(store.acodec === 'copy' || !store.hasAudio);
const durationText = $derived(
  validation ? `(${showTime(validation.duration)})` : '',
);
</script>

<div class="panel">
  <div class="row">
    <span class="name">fragment:</span>
    <input class="time" placeholder="start" bind:value={store.start} />
    <span>÷</span>
    <input class="time" placeholder="end" bind:value={store.end} />
    <button class="link" title="reset fragment" onclick={() => store.resetFragment()}>
      {durationText}
    </button>
  </div>

  <div class="row">
    <span class="name">video codec:</span>
    <select bind:value={store.vcodec}>
      <option value="av1">av1</option>
      <option value="vp9">vp9</option>
      <option value="vp8">vp8</option>
      <option value="x264">x264</option>
    </select>
    <input
      class="small"
      placeholder={limitPlaceholder}
      bind:value={store.limit}
      disabled={store.modeCRF}
    />
    <input class="small" placeholder="quality" bind:value={store.quality} />
    {#if !store.modeCRF && validation?.vb != null && validation.allValid}
      <span class="muted">({validation.vb} Kbps)</span>
    {/if}
  </div>

  <div class="row">
    <span class="name">audio codec:</span>
    <select
      value={store.acodec}
      onchange={(e) => store.selectACodec(e.currentTarget.value)}
      disabled={!store.hasAudio}
    >
      {#each audioCodecs as c (c)}
        {#if c !== 'copy' || validation?.copyAvailable}
          <option value={c}>{c}</option>
        {/if}
      {/each}
    </select>
    <input
      class="small"
      placeholder={abPlaceholder}
      bind:value={store.ab}
      disabled={abDisabled}
    />
  </div>

  <div class="row">
    <span class="name">mode:</span>
    <label>
      <input
        type="checkbox"
        checked={store.mode2Pass}
        onchange={(e) => store.setMode2Pass(e.currentTarget.checked)}
      />
      2pass
    </label>
    <label>
      <input
        type="checkbox"
        checked={store.modeLimit}
        onchange={(e) => store.setModeLimit(e.currentTarget.checked)}
      />
      limit
    </label>
    <label>
      <input
        type="checkbox"
        checked={store.modeCRF}
        onchange={(e) => store.setModeCRF(e.currentTarget.checked)}
      />
      crf
    </label>
    {#if showMT}
      <select bind:value={store.modeMT}>
        <option value="row-mt">row-mt</option>
        <option value="tile-col">tile-col</option>
        <option value="no-mt">no-mt</option>
      </select>
    {/if}
  </div>

  {#if store.vcodec === 'x264'}
    <div class="row">
      <span class="name">preset:</span>
      <select bind:value={store.preset}>
        {#each X264_PRESETS as p (p)}
          <option value={p}>{p}</option>
        {/each}
      </select>
    </div>
  {/if}

  <div class="row raw">
    <span class="name">raw args:</span>
    <textarea
      rows="4"
      bind:value={store.rawArgs}
      disabled={!validation?.allValid}
      spellcheck="false"
    ></textarea>
  </div>

  {#each validation?.errors.codecs ?? [] as e (e.field + e.message)}
    <p class="error">{e.field}: {e.message}</p>
  {/each}
  {#each validation?.warnings.codecs ?? [] as w (w)}
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
  .row.raw {
    align-items: flex-start;
  }
  .name {
    width: 110px;
    flex: none;
    text-align: right;
  }
  .time {
    width: 110px;
  }
  .small {
    width: 70px;
  }
  .link {
    border: none;
    background: none;
    color: #666;
    cursor: pointer;
    font: inherit;
    padding: 0;
  }
  textarea {
    flex: 1;
    font-family: monospace;
    font-size: 12px;
    resize: vertical;
  }
  .muted {
    color: #666;
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
