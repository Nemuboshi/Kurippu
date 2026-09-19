// Parsing and formatting of the time strings used by fragment start/end inputs.

const TIME_RE = /^(?:(\d+):)?(?:(\d+)+:)?(\d+(?:\.\d+)?)$/;

// Parses "[hh]:[mm]:[ss[.xxx]]" into seconds. Throws on invalid input.
export function parseTime(str: string): number {
  const m = TIME_RE.exec(str.trim());
  if (!m) throw new Error('Invalid time');
  const [, first, second, secStr] = m;
  const sec = Number.parseFloat(secStr ?? '0');
  if (second !== undefined) {
    // Three groups: h:m:s.
    return (
      sec +
      60 * Number.parseInt(second, 10) +
      3600 * Number.parseInt(first ?? '0', 10)
    );
  }
  if (first !== undefined) {
    // Two groups: m:s. The regex captures minutes into the first group.
    return sec + 60 * Number.parseInt(first, 10);
  }
  return sec;
}

export interface ShowTimeOptions {
  ceil?: boolean;
  sep?: string;
}

// Formats seconds as "[h:]MM:SS.mmm". Milliseconds are floored by default.
export function showTime(sec: number, opts: ShowTimeOptions = {}): string {
  const sep = opts.sep ?? ':';
  const totalMs = opts.ceil ? Math.ceil(sec * 1000) : Math.floor(sec * 1000);
  const ms = totalMs % 1000;
  const totalSec = Math.floor(totalMs / 1000);
  const s = totalSec % 60;
  const totalMin = Math.floor(totalSec / 60);
  const min = totalMin % 60;
  const h = Math.floor(totalMin / 60);
  const mm = String(min).padStart(2, '0');
  const ss = String(s).padStart(2, '0');
  const mss = String(ms).padStart(3, '0');
  const hh = h > 0 ? `${String(h).padStart(2, '0')}${sep}` : '';
  return `${hh}${mm}${sep}${ss}.${mss}`;
}

// ceilFixed rounds up to `digits` decimals and returns a plain decimal string.
export function ceilFixed(value: number, digits: number): string {
  const factor = 10 ** digits;
  const v = Math.ceil(value * factor) / factor;
  return v.toFixed(digits);
}
