import { unlink } from 'node:fs/promises';
import { dirname, isAbsolute, relative, resolve } from 'node:path';

// Registry of temp files (downloads, test outputs, passlogs, preview frames)
// that must be deleted when the app quits.
const registry = new Set<string>();
const directories = new Set<string>();

export function registerTempFile(path: string): void {
  const resolved = resolve(path);
  registry.add(resolved);
  directories.add(dirname(resolved));
}

export function isRegisteredTempFile(path: string): boolean {
  return registry.has(resolve(path));
}

export function isTempPath(path: string): boolean {
  const resolved = resolve(path);
  return [...directories].some((directory) => {
    const child = relative(directory, resolved);
    return child !== '' && !child.startsWith('..') && !isAbsolute(child);
  });
}

export async function cleanupTempFiles(): Promise<void> {
  const paths = [...registry];
  registry.clear();
  await Promise.allSettled(paths.map((p) => unlink(p)));
}
