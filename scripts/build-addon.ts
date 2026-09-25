// Build the Rust napi addon and install it at the path every consumer already
// expects. node-gyp wrote here too; during this migration the two alternate,
// and Task 9 removes the node-gyp writer.
import { execFileSync } from 'node:child_process';
import { copyFileSync, mkdirSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const crateDir = path.join(root, 'native', 'mpv');
const outDir = path.join(crateDir, 'build', 'Release');

function sourceName(): string {
  switch (process.platform) {
    case 'win32':
      return 'mpv_addon.dll';
    case 'darwin':
      return 'libmpv_addon.dylib';
    default:
      return 'libmpv_addon.so';
  }
}

function main(): void {
  execFileSync('cargo', ['build', '--release'], {
    cwd: crateDir,
    stdio: 'inherit',
  });
  const source = path.join(crateDir, 'target', 'release', sourceName());
  const destination = path.join(outDir, 'mpv_addon.node');
  mkdirSync(outDir, { recursive: true });
  copyFileSync(source, destination);
  console.log(`addon: ${source} -> ${destination}`);
}

main();
