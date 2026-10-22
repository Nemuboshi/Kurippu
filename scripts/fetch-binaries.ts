import { execFile } from 'node:child_process';
import { copyFile, mkdir, readdir, rm, writeFile } from 'node:fs/promises';
import { basename, join } from 'node:path';
import { promisify } from 'node:util';
import extract from 'extract-zip';
import { bundleTarget, normalizeTarget } from './targets.ts';

const run = promisify(execFile);
const FFMPEG_VERSION = '7.1.1';
const target = normalizeTarget(
  process.argv[2] ?? `${process.platform}-${process.arch}`,
);
const sources: Record<string, string> = {
  'win32-x64':
    'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip',
  'darwin-x64': `https://evermeet.cx/ffmpeg/ffmpeg-${FFMPEG_VERSION}.zip`,
  'darwin-arm64': 'https://www.osxexperts.net/ffmpeg711arm.zip',
  'linux-x64':
    'https://johnvansickle.com/ffmpeg/releases/ffmpeg-7.0.2-amd64-static.tar.xz',
};
const url = sources[target];
if (!url) throw new Error(`Unsupported target: ${target}`);

const output = join(import.meta.dirname, '..', 'bin', bundleTarget(target));
const archive = join(output, basename(new URL(url).pathname));
await rm(output, { recursive: true, force: true });
await mkdir(output, { recursive: true });
const response = await fetch(url, { signal: AbortSignal.timeout(10 * 60_000) });
if (!response.ok || !response.body)
  throw new Error(`Download failed: ${response.status} ${url}`);
await writeFile(archive, new Uint8Array(await response.arrayBuffer()));

if (archive.endsWith('.zip')) await extract(archive, { dir: output });
else await run('tar', ['-xJf', archive, '-C', output]);
await rm(archive);

const executable = target.startsWith('win32') ? 'ffmpeg.exe' : 'ffmpeg';
async function find(dir: string): Promise<string | null> {
  for (const entry of await readdir(dir, { withFileTypes: true })) {
    const path = join(dir, entry.name);
    if (entry.isFile() && entry.name === executable) return path;
    if (entry.isDirectory()) {
      const nested = await find(path);
      if (nested) return nested;
    }
  }
  return null;
}
const found = await find(output);
if (!found) throw new Error(`Archive did not contain ${executable}`);
const destination = join(output, executable);
if (found !== destination) await copyFile(found, destination);
const label = target === 'win32-x64' ? 'release-essentials' : FFMPEG_VERSION;
console.log(`ffmpeg ${label}: ${destination}`);
