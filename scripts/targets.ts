export type BuildTarget =
  | 'win32-x64'
  | 'darwin-x64'
  | 'darwin-arm64'
  | 'linux-x64';

export type BundleTarget = 'win-x64' | 'mac-x64' | 'mac-arm64' | 'linux-x64';

const aliases: Record<string, BuildTarget> = {
  'win-x64': 'win32-x64',
  'mac-x64': 'darwin-x64',
  'mac-arm64': 'darwin-arm64',
};

const bundleTargets: Record<BuildTarget, BundleTarget> = {
  'win32-x64': 'win-x64',
  'darwin-x64': 'mac-x64',
  'darwin-arm64': 'mac-arm64',
  'linux-x64': 'linux-x64',
};

export function normalizeTarget(value: string): BuildTarget {
  const target = aliases[value] ?? value;
  if (
    target !== 'win32-x64' &&
    target !== 'darwin-x64' &&
    target !== 'darwin-arm64' &&
    target !== 'linux-x64'
  ) {
    throw new Error(`Unsupported target: ${value}`);
  }
  return target;
}

export function bundleTarget(value: string): BundleTarget {
  return bundleTargets[normalizeTarget(value)];
}
