<script lang="ts">
import type { MediaInfo } from '../engine/options';
import { showTime } from '../engine/time';
import EncodePanel from './encode/EncodePanel.svelte';
import AudioFx from './encoder/AudioFx.svelte';
import Codecs from './encoder/Codecs.svelte';
import SubTabs from './encoder/SubTabs.svelte';
import { EncoderStore } from './encoder/store.svelte';
import VideoFx from './encoder/VideoFx.svelte';
import type { CropRect } from './player/math';
import Player from './player/Player.svelte';
import type { Source } from './state';

interface Props {
  source: Source;
}
const { source }: Props = $props();

const store = new EncoderStore();
let currentTime = $state<number | null>(null);

// New files land next to the source file until the user picks a path.
const sourceDir = source.path.replace(/[\\/][^\\/]+$/, '');

function onProbe(info: MediaInfo): void {
  store.setMedia(info);
}

// The overlay works in source pixels, so its rect maps onto the crop inputs
// one to one. Clearing the overlay clears the inputs.
function onCrop(crop: CropRect | null): void {
  if (!crop) {
    store.clearCrop();
    return;
  }
  store.cropw = String(crop.w);
  store.croph = String(crop.h);
  store.cropx = String(crop.x);
  store.cropy = String(crop.y);
}
</script>

<div class="encoder">
  <div class="player-pane">
    <Player
      src={source.path}
      {onCrop}
      onProbe={onProbe}
      onTime={(t) => {
        currentTime = t;
      }}
    />
  </div>

  <div class="tabs-pane">
    <SubTabs {store}>
      {#snippet info()}
        <dl class="info">
          <dt>file</dt>
          <dd>{source.path}</dd>
          <dt>duration</dt>
          <dd>{store.media ? showTime(store.media.duration) : '—'}</dd>
          <dt>size</dt>
          <dd>
            {store.media ? `${store.media.width}x${store.media.height}` : '—'}
          </dd>
          <dt>video tracks</dt>
          <dd>{store.media?.vtracks.length ?? 0}</dd>
          <dt>audio tracks</dt>
          <dd>{store.media?.atracks.length ?? 0}</dd>
          <dt>subtitle tracks</dt>
          <dd>{store.media?.stracks.length ?? 0}</dd>
        </dl>
      {/snippet}

      {#snippet video()}
        <VideoFx {store} />
      {/snippet}

      {#snippet audio()}
        <AudioFx {store} />
      {/snippet}

      {#snippet codecs()}
        <Codecs {store} />
      {/snippet}

      {#snippet encode()}
        <EncodePanel
          {source}
          rawArgs={store.rawArgs}
          twoPass={store.mode2Pass}
          container={store.derived?.container ?? 'mp4'}
          duration={store.media?.duration ?? 0}
          avgFrameRate={String(store.media?.vtracks[0]?.fps ?? 25)}
          defaultDir={sourceDir}
          {currentTime}
        />
      {/snippet}
    </SubTabs>
  </div>
</div>

<style>
  .encoder {
    display: flex;
    height: 100%;
    min-height: 0;
  }
  .player-pane {
    flex: 1;
    min-width: 0;
  }
  .tabs-pane {
    width: 420px;
    flex: none;
    border-left: 1px solid #ddd;
    min-height: 0;
  }
  .info {
    display: grid;
    grid-template-columns: max-content 1fr;
    gap: 4px 10px;
    margin: 0;
    padding: 10px;
    font-size: 13px;
  }
  .info dt {
    color: #777;
  }
  .info dd {
    margin: 0;
    word-break: break-all;
  }
</style>
