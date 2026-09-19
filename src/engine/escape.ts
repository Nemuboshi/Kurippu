// Argument escaping for ffmpeg command lines and lavfi filter strings.

// Wraps a path in the ffmpeg file: protocol prefix.
export function fileArg(path: string): string {
  return `file:${path}`;
}

// Escapes a path inside a lavfi filter value, then wraps it in single quotes.
export function escapeFilterPath(path: string): string {
  const escaped = path
    .replace(/\\/g, '\\\\')
    .replace(/'/g, "'\\\\''")
    .replace(/:/g, '\\:');
  return `'${escaped}'`;
}

// Escapes a concat list line: file '<path>' with single quotes escaped.
export function concatListLine(path: string): string {
  return `file '${path.replace(/'/g, "'\\''")}'`;
}

// Escapes an argument for copy-paste into a shell: double quotes plus
// backslash escapes for \ " $ `.
export function escapeArg(arg: string): string {
  return `"${arg.replace(/\\/g, '\\\\').replace(/"/g, '\\"').replace(/\$/g, '\\$').replace(/`/g, '\\`')}"`;
}
