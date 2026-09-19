<script lang="ts">
import { probeMpv } from './player/probe';
import type { Source } from './state';

interface Props {
  source: Source;
  onDone: () => void;
  onCancel: () => void;
}
const { source, onDone, onCancel }: Props = $props();

let error = $state('');

$effect(() => {
  void (async () => {
    try {
      await probeMpv(source.path);
      onDone();
    } catch (e) {
      error = e instanceof Error ? e.message : String(e);
    }
  })();
});
</script>

<div class="info">
  {#if error}
    <p class="error-text">{error}</p>
    <button onclick={onCancel}>cancel</button>
  {:else}
    <p>Gathering video info…</p>
  {/if}
</div>

<style>
  .info { display: flex; flex-direction: column; align-items: center; justify-content: center; height: 100%; gap: 12px; width: 70%; margin: 0 auto; }
  .error-text { color: #d32f2f; max-height: 10em; overflow: hidden; }
  button { width: 250px; height: 40px; }
</style>
