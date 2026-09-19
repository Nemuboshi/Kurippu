import { execFileSync } from 'node:child_process';
import { existsSync } from 'node:fs';
import { delimiter, join } from 'node:path';

// Binary path resolution. Lookup order: BORAM_* env var, bundled copy next to
// the app, PATH. Linux goes straight to PATH for ffmpeg.

function which(bin: string): string | null {
  try {
    const cmd = process.platform === 'win32' ? 'where' : 'which';
    const out = execFileSync(cmd, [bin], {
      encoding: 'utf8',
      stdio: ['ignore', 'pipe', 'ignore'],
    });
    const first = out.split(/\r?\n/)[0]?.trim();
    return first || null;
  } catch {
    return null;
  }
}

function bundledPath(name: string): string | null {
  // Binaries are unpacked next to the packaged app via extraResources.
  const dir = process.resourcesPath;
  if (!dir) return null;
  const exe = process.platform === 'win32' ? `${name}.exe` : name;
  const p = join(dir, 'bin', exe);
  return existsSync(p) ? p : null;
}

export function resolveFfmpeg(): string | null {
  const env = process.env.BORAM_FFMPEG;
  if (env && existsSync(env)) return env;
  if (process.platform === 'linux') return which('ffmpeg');
  return bundledPath('ffmpeg') ?? which('ffmpeg');
}

export function resolveYtDlp(): string | null {
  const env = process.env.BORAM_YT_DLP;
  if (env && existsSync(env)) return env;
  return which('yt-dlp');
}

export function pathDirs(): string[] {
  return (process.env.PATH ?? '').split(delimiter).filter(Boolean);
}
