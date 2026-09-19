<script lang="ts">
import { type EncoderStore, EXTERNAL_TRACK } from './store.svelte';

interface Props {
  store: EncoderStore;
}
const { store }: Props = $props();

// Track option labels: "#i (codec, WxH)" / "#i (codec, lang)".
function trackLabel(i: number, codec: string, extra: string): string {
  return extra ? `#${i} (${codec}, ${extra})` : `#${i} (${codec})`;
}

const media = $derived.by(() => store.media);
const validation = $derived.by(() => store.derived);

const subsDisabled = $derived(
  (media?.stracks.length ?? 0) === 0 && store.subpath === '',
);

function basename(p: string): string {
  return p.split(/[\\/]/).at(-1) ?? p;
}
</script>

<div class="panel">
  <label class="row">
    <span class="name">video track:</span>
    <select
      value={store.vtrack}
      onchange={(e) => store.selectVTrack(Number(e.currentTarget.value))}
    >
      {#each media?.vtracks ?? [] as t, i (i)}
        <option value={String(i)}>
          {trackLabel(i, t.codec, `${t.width ?? '?'}x${t.height ?? '?'}`)}
        </option>
      {/each}
    </select>
  </label>

  <label class="row">
    <span class="name">deinterlace:</span>
    <input type="checkbox" bind:checked={store.deinterlace} />
    <button
      class="icon"
      title="auto-detect interlacing"
      disabled={store.detectingInterlace}
      onclick={() => void store.detectInterlace()}
    >
      {store.detectingInterlace ? '…' : '🔍'}
    </button>
  </label>

  <div class="row">
    <span class="name">crop:</span>
    <input class="small" placeholder="w" bind:value={store.cropw} />
    <input class="small" placeholder="h" bind:value={store.croph} />
    <input class="small" placeholder="x" bind:value={store.cropx} />
    <input class="small" placeholder="y" bind:value={store.cropy} />
    <button
      class="icon"
      title="auto-detect black bars"
      disabled={store.detectingCrop}
      onclick={() => void store.detectCrop()}
    >
      {store.detectingCrop ? '…' : '👁'}
    </button>
    <button class="icon" title="clear crop" onclick={() => store.clearCrop()}>
      ✕
    </button>
  </div>

  <div class="row">
    <span class="name">scale:</span>
    <input class="small" placeholder="w" bind:value={store.scalew} />
    <input class="small" placeholder="h" bind:value={store.scaleh} />
  </div>

  <label class="row">
    <span class="name">fix SAR:</span>
    <input type="checkbox" bind:checked={store.fixSAR} />
    {#if validation?.anamorph && !store.fixSAR}
      <span class="warning">anamorphic source, aspect ratio will be wrong</span>
    {/if}
  </label>

  <div class="row">
    <span class="name">burn subs:</span>
    <input
      type="checkbox"
      bind:checked={store.burnSubs}
      disabled={subsDisabled}
    />
    <select
      value={store.strack}
      disabled={subsDisabled}
      onchange={(e) => store.selectStrack(e.currentTarget.value)}
    >
      {#each media?.stracks ?? [] as t, i (i)}
        <option value={String(i)}>{trackLabel(i, t.codec, t.lang ?? '')}</option>
      {/each}
      {#if store.subpath}
        <option value={EXTERNAL_TRACK}>{basename(store.subpath)}</option>
      {/if}
    </select>
    <button class="icon" title="external subtitle file" onclick={() => void store.pickExternalSubs()}>
      📂
    </button>
  </div>

  {#each validation?.errors.video ?? [] as e (e.field + e.message)}
    <p class="error">{e.field}: {e.message}</p>
  {/each}
  {#each validation?.warnings.video ?? [] as w (w)}
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
  .icon {
    width: 28px;
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
