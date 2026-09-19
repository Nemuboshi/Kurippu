import { defineConfig } from 'vitest/config';
import { svelte } from '@sveltejs/vite-plugin-svelte';

export default defineConfig({
  plugins: [svelte()],
  test: {
    include: [
      'src/engine/**/*.test.ts',
      'src/renderer/player/*.test.ts',
      'src/renderer/encoder/*.test.ts',
    ],
  },
});
