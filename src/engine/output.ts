// Output path derivation and validation. Pure: the caller passes an
// exists() probe and stat results, so this runs under vitest without fs.

export interface NameSource {
  path: string;
  saveAs?: string;
}

// Base output name: saveAs or the source file name, extension removed and
// any trailing "-N" counter stripped so re-encoding clip-1.mkv starts from
// "clip" again instead of stacking suffixes.
export function baseName(source: NameSource): string {
  const name = (source.saveAs ?? source.path).split(/[\\/]/).at(-1) ?? '';
  return name.replace(/\.[^.]*$/, '').replace(/(-\d+)+$/, '');
}

export function outputFileName(
  base: string,
  index: number,
  container: string,
): string {
  return `${base}${index > 0 ? `-${index}` : ''}.${container}`;
}

// First candidate name that does not exist on disk wins, so a finished
// encode never overwrites an earlier one.
export function outputPath(
  dir: string,
  base: string,
  container: string,
  exists: (path: string) => boolean,
): string {
  const sep = dir.includes('\\') && !dir.includes('/') ? '\\' : '/';
  for (let i = 0; ; i++) {
    const path = `${dir.replace(/[\\/]+$/, '')}${sep}${outputFileName(base, i, container)}`;
    if (!exists(path)) return path;
  }
}

export interface DirStat {
  exists: boolean;
  isDir: boolean;
  // Unknown when the caller cannot probe writability.
  writable?: boolean;
}

function isAbsoluteDir(dir: string): boolean {
  return dir.startsWith('/') || /^[A-Za-z]:[\\/]/.test(dir);
}

// Returns an error string, or null when the directory is usable.
export function validateOutputDir(dir: string, stat: DirStat): string | null {
  if (!isAbsoluteDir(dir)) return 'Output directory must be an absolute path';
  if (!stat.exists) return 'Output directory does not exist';
  if (!stat.isDir) return 'Output path is not a directory';
  if (stat.writable === false) return 'Output directory is not writable';
  return null;
}

export interface FileStat {
  exists: boolean;
  // Unknown when the caller cannot probe writability.
  writable?: boolean;
}

// Returns an error string, or null when ffmpeg may write here. A missing
// file is fine; an existing file must be writable.
export function validateOutputFile(stat: FileStat): string | null {
  if (stat.exists && stat.writable === false)
    return 'Output file exists and is not writable';
  return null;
}
