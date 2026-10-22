import { svelte } from '@sveltejs/vite-plugin-svelte';
import { defineConfig } from 'vitest/config';

export default defineConfig({
  plugins: [svelte()],
  test: {
    include: [
      'src/engine/**/*.test.ts',
      'src/main/*.test.ts',
      'src/renderer/player/*.test.ts',
      'src/renderer/encoder/*.test.ts',
    ],
  },
});
