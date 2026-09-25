import { execFileSync } from 'node:child_process';
import { existsSync } from 'node:fs';
import { join } from 'node:path';

function env(name: string): string | undefined {
  return process.env[`KURIPPU_${name}`];
}

function which(bin: string): string | null {
  try {
    const cmd = process.platform === 'win32' ? 'where' : 'which';
    const out = execFileSync(cmd, [bin], {
      encoding: 'utf8',
      stdio: ['ignore', 'pipe', 'ignore'],
    });
    return out.split(/\r?\n/)[0]?.trim() || null;
  } catch {
    return null;
  }
}

function bundledPath(name: string): string | null {
  const exe = process.platform === 'win32' ? `${name}.exe` : name;
  const path = join(process.resourcesPath, exe);
  return existsSync(path) ? path : null;
}

export function resolveFfmpeg(): string | null {
  const override = env('FFMPEG');
  if (override && existsSync(override)) return override;
  return bundledPath('ffmpeg') ?? which('ffmpeg');
}

export function resolveYtDlp(): string | null {
  const override = env('YT_DLP');
  if (override && existsSync(override)) return override;
  return which('yt-dlp');
}

export function debugEnabled(): boolean {
  return envFlag('DEBUG');
}

export function noHardwareAcceleration(): boolean {
  return envFlag('NO_HWACCEL');
}

function envFlag(name: string): boolean {
  const value = env(name)?.trim().toLowerCase();
  return value === '1' || value === 'true' || value === 'yes';
}

export function debugValue(name: 'TAB' | 'FORMAT'): string | undefined {
  return env(`DEBUG_${name}`);
}
