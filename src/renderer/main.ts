import { defineMpvVideoElement } from 'electron-mpv-video/renderer';
import { mount } from 'svelte';
import App from './App.svelte';

defineMpvVideoElement();

const target = document.getElementById('app');
if (!target) throw new Error('missing #app element');
const app = mount(App, { target });

export default app;
