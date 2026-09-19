import type { KurippuApi } from '../preload/index';

declare global {
  interface Window {
    kurippu: KurippuApi;
  }
}
