<script lang="ts">
import { parseTime, showTime } from '../../engine/time';

interface Props {
  playing: boolean;
  time: number;
  duration: number;
  mstart: number | null;
  mend: number | null;
  loop: boolean;
  muted: boolean;
  volume: number;
  hasAudio?: boolean;
  disabled?: boolean;
  fullscreen?: boolean;
  onTogglePlay: () => void;
  onMarkStart: () => void;
  onMarkEnd: () => void;
  onSeek: (t: number) => void;
  onToggleLoop: () => void;
  onToggleMute: () => void;
  onVolume: (v: number) => void;
  onFullscreen: () => void;
}
const {
  playing,
  time,
  duration,
  mstart,
  mend,
  loop,
  muted,
  volume,
  hasAudio = true,
  disabled = false,
  fullscreen = false,
  onTogglePlay,
  onMarkStart,
  onMarkEnd,
  onSeek,
  onToggleLoop,
  onToggleMute,
  onVolume,
  onFullscreen,
}: Props = $props();

let editing = $state(false);
let draft = $state('');
let invalid = $state(false);
let timeInput = $state<HTMLInputElement | null>(null);

export function focusTime(): void {
  editing = true;
  draft = showTime(time);
  invalid = false;
  // Wait a tick so the input exists before focusing.
  queueMicrotask(() => {
    timeInput?.focus();
    timeInput?.select();
  });
}

function commitTime(): void {
  try {
    onSeek(parseTime(draft));
    invalid = false;
    editing = false;
    timeInput?.blur();
  } catch {
    // Bad input stays editable with red styling until fixed or cancelled.
    invalid = true;
  }
}

function onTimeKeydown(e: KeyboardEvent): void {
  if (e.key === 'Enter') commitTime();
  else if (e.key === 'Escape') {
    editing = false;
    invalid = false;
    timeInput?.blur();
  }
  e.stopPropagation();
}

function preventContext(e: MouseEvent): void {
  e.preventDefault();
}

const atEnd = $derived(duration > 0 && time >= duration - 0.05);
</script>

<div class="controls">
  <button class="ctl" title={playing ? 'pause' : 'play'} {disabled} onclick={onTogglePlay}>
    {#if playing}
      <svg viewBox="0 0 16 16"><path d="M3 2h4v12H3zM9 2h4v12H9z" /></svg>
    {:else}
      <svg viewBox="0 0 16 16"><path d="M4 2l9 6-9 6z" /></svg>
    {/if}
  </button>

  <button
    class="ctl"
    title="mark start (right-click: seek to start)"
    disabled={disabled || atEnd || mstart === time}
    onclick={onMarkStart}
    oncontextmenu={(e) => {
      preventContext(e);
      if (mstart !== null) onSeek(mstart);
    }}
  >
    <svg viewBox="0 0 16 16"><path d="M9.6 5.4L12 3a2.8 2.8 0 1 0-2-2L7.6 3.4 4.8 6.2A3 3 0 0 0 3 5.8 2.8 2.8 0 1 0 5.8 8a3 3 0 0 0-.4-1.5l2.8-2.8L10.6 6a3 3 0 0 0-1-.2zM3 7a1 1 0 1 1 0 2 1 1 0 0 1 0-2zm10-4a1 1 0 1 1 0 2 1 1 0 0 1 0-2z"/></svg>
  </button>

  {#if editing}
    <input
      bind:this={timeInput}
      class="timebox"
      class:invalid
      bind:value={draft}
      onkeydown={onTimeKeydown}
      onblur={() => {
        editing = false;
        invalid = false;
      }}
    />
  {:else}
    <button class="timebox as-text" title="edit time (t)" onclick={focusTime}>
      {showTime(time)}
    </button>
  {/if}

  <button
    class="ctl flipped"
    title="mark end (right-click: seek to end)"
    disabled={disabled || time === 0 || mend === time}
    onclick={onMarkEnd}
    oncontextmenu={(e) => {
      preventContext(e);
      if (mend !== null) onSeek(mend);
    }}
  >
    <svg viewBox="0 0 16 16"><path d="M9.6 5.4L12 3a2.8 2.8 0 1 0-2-2L7.6 3.4 4.8 6.2A3 3 0 0 0 3 5.8 2.8 2.8 0 1 0 5.8 8a3 3 0 0 0-.4-1.5l2.8-2.8L10.6 6a3 3 0 0 0-1-.2zM3 7a1 1 0 1 1 0 2 1 1 0 0 1 0-2zm10-4a1 1 0 1 1 0 2 1 1 0 0 1 0-2z"/></svg>
  </button>

  <button
    class="ctl"
    class:pressed={loop}
    title="loop fragment"
    {disabled}
    onclick={onToggleLoop}
  >
    <svg viewBox="0 0 16 16"><path d="M4 4h7v2l3-3-3-3v2H3v6h2V6zm8 8H5v-2l-3 3 3 3v-2h8V8h-2v2z" transform="translate(0 1)"/></svg>
  </button>

  <div class="spacer"></div>

  <button class="ctl" title="fullscreen (f)" onclick={onFullscreen}>
    {#if fullscreen}
      <svg viewBox="0 0 16 16"><path d="M6 2v4H2v2h6V2H6zm4 0v6h6V6h-4V2h0zM2 10v2h4v4h2v-6H2zm8 0v6h2v-4h4v-2h-6z"/></svg>
    {:else}
      <svg viewBox="0 0 16 16"><path d="M2 2v6h2V4h4V2H2zm12 0v6h-2V4h-4V2h6zM2 10v4h6v-2H4v-2H2zm12 0v4h-6v-2h4v-2h2z"/></svg>
    {/if}
  </button>

  <div class="volume">
    <button
      class="ctl"
      title="mute (m)"
      disabled={disabled || !hasAudio}
      onclick={onToggleMute}
    >
      {#if muted || volume === 0}
        <svg viewBox="0 0 16 16"><path d="M1 5v6h3l4 4V1L4 5H1zm11.6 3l2.2-2.2-1.4-1.4-2.2 2.2-2.2-2.2-1.4 1.4L9.8 8l-2.2 2.2 1.4 1.4 2.2-2.2 2.2 2.2 1.4-1.4L12.6 8z"/></svg>
      {:else}
        <svg viewBox="0 0 16 16"><path d="M1 5v6h3l4 4V1L4 5H1zm9.5 3a2.5 2.5 0 0 0-1.5-2.3v4.6A2.5 2.5 0 0 0 10.5 8zM9 3.2v1.3a4 4 0 0 1 0 7v1.3A5.2 5.2 0 0 0 9 3.2z"/></svg>
      {/if}
    </button>
    <div class="volpop">
      <input
        type="range"
        class="volslider"
        min="0"
        max="100"
        value={muted ? 0 : volume}
        disabled={!hasAudio}
        oninput={(e) => onVolume(Number(e.currentTarget.value))}
      />
    </div>
  </div>
</div>

<style>
  .controls {
    display: flex;
    align-items: center;
    gap: 2px;
    padding: 4px 6px;
    background: #fafafa;
  }
  .ctl {
    width: 28px;
    height: 28px;
    display: inline-flex;
    align-items: center;
    justify-content: center;
    border: none;
    background: transparent;
    border-radius: 3px;
    cursor: pointer;
    padding: 0;
  }
  .ctl:hover:not(:disabled) {
    background: #e0e0e0;
  }
  .ctl:disabled {
    opacity: 0.35;
    cursor: default;
  }
  .ctl svg {
    width: 16px;
    height: 16px;
    fill: #333;
  }
  .ctl.pressed {
    box-shadow: inset 0 2px 4px rgba(0, 0, 0, 0.35);
    background: #e0e0e0;
  }
  .flipped svg {
    transform: scaleX(-1);
  }
  .timebox {
    width: 96px;
    height: 24px;
    font: inherit;
    font-family: 'Roboto Mono', monospace;
    font-size: 12px;
    text-align: center;
    border: 1px solid #bdbdbd;
    border-radius: 3px;
    background: #fff;
    color: #333;
  }
  .timebox.as-text {
    cursor: text;
  }
  .timebox.invalid {
    border-color: #d32f2f;
    color: #d32f2f;
    outline: none;
  }
  .spacer {
    flex: 1;
  }
  .volume {
    position: relative;
    display: inline-flex;
  }
  .volpop {
    display: none;
    position: absolute;
    bottom: 30px;
    right: 0;
    width: 32px;
    height: 110px;
    background: #fafafa;
    border: 1px solid #bdbdbd;
    border-radius: 3px;
    box-shadow: 0 2px 6px rgba(0, 0, 0, 0.2);
  }
  .volume:hover .volpop,
  .volpop:focus-within {
    display: flex;
    align-items: center;
    justify-content: center;
  }
  /* Rotated range reads as a vertical slider; transform keeps layout simple. */
  .volslider {
    width: 100px;
    transform: rotate(270deg);
  }
</style>
