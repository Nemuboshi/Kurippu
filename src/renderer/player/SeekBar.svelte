<script lang="ts">
interface Props {
  duration: number;
  time: number;
  mstart: number | null;
  mend: number | null;
  onSeek: (t: number) => void;
}
const { duration, time, mstart, mend, onSeek }: Props = $props();

function pct(t: number): number {
  return duration > 0 ? Math.max(0, Math.min(100, (t / duration) * 100)) : 0;
}

// Three-segment track: gray outside the marked fragment, orange inside.
const trackBg = $derived.by(() => {
  const a = pct(mstart ?? 0);
  const b = pct(mend ?? duration);
  return `linear-gradient(to right, #bdbdbd 0 ${a}%, orange ${a}% ${b}%, #bdbdbd ${b}% 100%)`;
});
</script>

<input
  class="seekbar"
  type="range"
  min="0"
  max={duration}
  step="0.1"
  value={time}
  style:background={trackBg}
  oninput={(e) => onSeek(Number(e.currentTarget.value))}
/>

<style>
  .seekbar {
    -webkit-appearance: none;
    appearance: none;
    width: 100%;
    height: 5px;
    outline: none;
    cursor: pointer;
  }
  .seekbar::-webkit-slider-thumb {
    -webkit-appearance: none;
    width: 12px;
    height: 12px;
    border-radius: 50%;
    background: #555;
  }
  .seekbar::-moz-range-thumb {
    width: 12px;
    height: 12px;
    border: none;
    border-radius: 50%;
    background: #555;
  }
</style>
