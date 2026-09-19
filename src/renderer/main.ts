import { mount } from 'svelte';
import App from './App.svelte';
import { defineMpvVideoElement } from './player/mpv-video';

defineMpvVideoElement();

const target = document.getElementById('app');
if (!target) throw new Error('missing #app element');
const app = mount(App, { target });

export default app;
