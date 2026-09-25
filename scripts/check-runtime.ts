import { existsSync, readdirSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { bundleTarget, normalizeTarget } from './targets.ts';

const root = join(import.meta.dirname, '..');
const target = normalizeTarget(
  process.argv[2] ?? `${process.platform}-${process.arch}`,
);
const bundle = bundleTarget(target);
const binaryName = target === 'win32-x64' ? 'ffmpeg.exe' : 'ffmpeg';
const binaryPath = join(root, 'bin', bundle, binaryName);
const nativeDir = join(root, 'native', 'mpv', 'build', 'Release');
const addonPath = join(nativeDir, 'mpv_addon.node');

function requireFile(path: string, description: string): void {
  if (!existsSync(path) || !statSync(path).isFile()) {
    throw new Error(`${description} not found: ${path}`);
  }
}

requireFile(binaryPath, 'ffmpeg binary');
requireFile(addonPath, 'native addon');

const nativeFiles = readdirSync(nativeDir);
if (target === 'win32-x64') {
  if (!nativeFiles.some((name) => /^(?:lib)?mpv-2\.dll$/i.test(name))) {
    throw new Error(`libmpv runtime DLL not found in: ${nativeDir}`);
  }
} else if (target.startsWith('darwin-')) {
  if (!nativeFiles.some((name) => name.toLowerCase().endsWith('.dylib'))) {
    throw new Error(`libmpv runtime dylib not found in: ${nativeDir}`);
  }
} else if (!nativeFiles.some((name) => /\.so(?:\.|$)/i.test(name))) {
  throw new Error(`libmpv runtime shared library not found in: ${nativeDir}`);
}

console.log(`runtime assets ready for ${target}`);
