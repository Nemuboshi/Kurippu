import { unlink } from 'node:fs/promises';

// Registry of temp files (downloads, test outputs, passlogs, preview frames)
// that must be deleted when the app quits.
const registry = new Set<string>();

export function registerTempFile(path: string): void {
  registry.add(path);
}

export function unregisterTempFile(path: string): void {
  registry.delete(path);
}

export async function cleanupTempFiles(): Promise<void> {
  const paths = [...registry];
  registry.clear();
  await Promise.allSettled(paths.map((p) => unlink(p)));
}
