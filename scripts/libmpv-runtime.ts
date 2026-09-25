// Validate the libmpv SDK and stage runtime libraries beside the native addon.
import { execFileSync } from 'node:child_process';
import {
  chmodSync,
  copyFileSync,
  type Dirent,
  existsSync,
  mkdirSync,
  readdirSync,
  readFileSync,
  realpathSync,
  rmSync,
  statSync,
} from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { normalizeTarget } from './targets.ts';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const mode = process.argv[2];
const target = normalizeTarget(
  process.argv[3] ?? `${process.platform}-${process.arch}`,
);
const outputDir = path.join(root, 'native', 'mpv', 'build', 'Release');
const addonPath = path.join(outputDir, 'mpv_addon.node');

interface TargetPaths {
  includeDir: string;
  library: string;
  runtimeDir: string;
}

function fail(message: string): never {
  console.error(`libmpv: ${message}`);
  process.exit(1);
}

function requireFile(file: string, description: string): void {
  if (!existsSync(file) || !statSync(file).isFile()) {
    fail(`${description} not found: ${file}`);
  }
}

function requireDirectory(directory: string, description: string): void {
  if (!existsSync(directory) || !statSync(directory).isDirectory()) {
    fail(`${description} not found: ${directory}`);
  }
}

function copyRuntimeFile(source: string, destination: string): void {
  rmSync(destination, { force: true });
  copyFileSync(source, destination);
  chmodSync(destination, 0o755);
}

function copyMatching(sourceDir: string, extension: string): string[] {
  if (!existsSync(sourceDir)) return [];
  const copied: string[] = [];
  for (const entry of readdirSync(sourceDir, { withFileTypes: true })) {
    if (!isFileLike(entry)) continue;
    const name = entry.name.toLowerCase();
    const matches =
      extension === '.so'
        ? name.includes('.so')
        : name.endsWith(extension.toLowerCase());
    if (!matches) continue;
    const source = path.join(sourceDir, entry.name);
    if (extension === '.so' && !isElfFile(source)) continue;
    const destination = path.join(outputDir, entry.name);
    copyRuntimeFile(source, destination);
    copied.push(destination);
  }
  return copied;
}

function isElfFile(file: string): boolean {
  try {
    const magic = readFileSync(file).subarray(0, 4);
    return magic.equals(Buffer.from([0x7f, 0x45, 0x4c, 0x46]));
  } catch {
    return false;
  }
}

// Symlinks to files count; package managers often use them for shared libraries.
function isFileLike(entry: Dirent): boolean {
  return entry.isFile() || entry.isSymbolicLink();
}

function configuredPaths(): TargetPaths {
  if (target.startsWith('darwin-')) {
    const prefix = target === 'darwin-arm64' ? '/opt/homebrew' : '/usr/local';
    return {
      includeDir: path.resolve(
        process.env.MPV_INCLUDE_DIR || path.join(prefix, 'include'),
      ),
      library: path.resolve(
        process.env.MPV_LIB || path.join(prefix, 'lib', 'libmpv.dylib'),
      ),
      runtimeDir: path.resolve(
        process.env.MPV_RUNTIME_DIR || path.join(prefix, 'opt', 'mpv', 'lib'),
      ),
    };
  }

  if (target === 'win32-x64') {
    const home = path.join(os.homedir(), 'libmpv');
    return {
      includeDir: path.resolve(
        process.env.MPV_INCLUDE_DIR || path.join(home, 'include'),
      ),
      library: path.resolve(
        process.env.MPV_LIB || path.join(home, 'lib', 'mpv.lib'),
      ),
      runtimeDir: path.resolve(
        process.env.MPV_RUNTIME_DIR || path.join(home, 'bin'),
      ),
    };
  }

  const linuxLibrary =
    process.env.MPV_LIB ||
    [
      '/opt/libmpv/lib/libmpv.so',
      '/usr/local/lib/libmpv.so',
      '/usr/lib/x86_64-linux-gnu/libmpv.so',
      '/usr/lib64/libmpv.so',
      '/usr/lib/libmpv.so',
    ].find((candidate) => existsSync(candidate)) ||
    '/opt/libmpv/lib/libmpv.so';
  return {
    includeDir: path.resolve(process.env.MPV_INCLUDE_DIR || '/usr/include'),
    library: path.resolve(linuxLibrary),
    runtimeDir: path.resolve(
      process.env.MPV_RUNTIME_DIR || path.dirname(linuxLibrary),
    ),
  };
}

function validateHeaders(includeDir: string): void {
  for (const header of ['client.h', 'render.h', 'render_gl.h']) {
    requireFile(path.join(includeDir, 'mpv', header), `mpv/${header}`);
  }
}

function check(): void {
  const paths = configuredPaths();
  validateHeaders(paths.includeDir);
  requireDirectory(paths.runtimeDir, 'libmpv runtime directory');

  if (target.startsWith('darwin-')) {
    requireFile(paths.library, 'libmpv link library');
    if (
      !readdirSync(paths.runtimeDir).some((name) => name.endsWith('.dylib'))
    ) {
      fail(`libmpv runtime dylib not found in: ${paths.runtimeDir}`);
    }
    return;
  }

  if (target === 'win32-x64') {
    requireFile(paths.library, 'libmpv import library');
    const dlls = readdirSync(paths.runtimeDir).map((name) =>
      name.toLowerCase(),
    );
    if (!dlls.includes('libmpv-2.dll') && !dlls.includes('mpv-2.dll')) {
      fail(`libmpv runtime DLL not found in: ${paths.runtimeDir}`);
    }
    return;
  }

  requireFile(paths.library, 'libmpv link library');
  if (!readdirSync(paths.runtimeDir).some((name) => name.includes('.so'))) {
    fail(`libmpv runtime shared library not found in: ${paths.runtimeDir}`);
  }
}

function canonicalPath(file: string): string {
  try {
    return realpathSync(file);
  } catch {
    return path.resolve(file);
  }
}

function macDependencies(file: string): string[] {
  return execFileSync('otool', ['-L', file], { encoding: 'utf8' })
    .split('\n')
    .slice(1)
    .map((line) => /^\s+(\S+)\s+\(/.exec(line)?.[1])
    .filter((dependency): dependency is string => dependency != null);
}

function macRpaths(file: string): string[] {
  const lines = execFileSync('otool', ['-l', file], {
    encoding: 'utf8',
  }).split('\n');
  const rpaths: string[] = [];
  for (let i = 0; i < lines.length; i++) {
    if (!lines[i]?.includes('cmd LC_RPATH')) continue;
    for (let j = i + 1; j < Math.min(i + 5, lines.length); j++) {
      const match = /^\s+path (\S+) \(offset/.exec(lines[j] ?? '');
      if (match?.[1]) rpaths.push(match[1]);
    }
  }
  return rpaths;
}

function isSystemMacDependency(reference: string): boolean {
  return reference.startsWith('/System/') || reference.startsWith('/usr/lib/');
}

function resolveMacDependency(
  reference: string,
  owner: string,
  roots: string[],
): string | null {
  if (isSystemMacDependency(reference)) return null;
  const candidates: string[] = [];
  if (reference.startsWith('/')) candidates.push(reference);
  else if (reference.startsWith('@loader_path/')) {
    candidates.push(path.join(path.dirname(owner), reference.slice(13)));
  } else if (reference.startsWith('@executable_path/')) {
    candidates.push(
      path.join(
        path.dirname(addonPath),
        reference.slice('@executable_path/'.length),
      ),
    );
  } else if (reference.startsWith('@rpath/')) {
    const suffix = reference.slice('@rpath/'.length);
    for (const rpath of macRpaths(owner)) {
      const expanded = rpath
        .replace('@loader_path', path.dirname(owner))
        .replace('@executable_path', path.dirname(addonPath));
      candidates.push(path.join(expanded, suffix));
    }
    for (const root of roots) candidates.push(path.join(root, suffix));
  }
  const found = candidates.find((candidate) => existsSync(candidate));
  if (found) return found;
  fail(`macOS dependency not found: ${reference} (needed by ${owner})`);
}

function stageMac(paths: TargetPaths): void {
  const prefix = target === 'darwin-arm64' ? '/opt/homebrew' : '/usr/local';
  const roots = [
    paths.runtimeDir,
    path.dirname(paths.library),
    path.join(prefix, 'lib'),
  ];
  const staged = new Map<string, string>();
  const changes: {
    owner: string;
    reference: string;
    replacement: string;
  }[] = [];

  const visit = (source: string, preferredName?: string): string => {
    const key = canonicalPath(source);
    const existing = staged.get(key);
    if (existing) return existing;
    const destination = path.join(
      outputDir,
      preferredName ?? path.basename(source),
    );
    copyRuntimeFile(source, destination);
    staged.set(key, destination);
    for (const reference of macDependencies(source)) {
      const dependency = resolveMacDependency(reference, source, roots);
      if (!dependency) continue;
      const dependencyDestination = visit(dependency);
      changes.push({
        owner: destination,
        reference,
        replacement: `@loader_path/${path.basename(dependencyDestination)}`,
      });
    }
    return destination;
  };

  const stagedLibrary = visit(paths.library, 'libmpv.dylib');
  for (const reference of macDependencies(addonPath)) {
    const dependency = resolveMacDependency(reference, addonPath, roots);
    if (!dependency) continue;
    const dependencyDestination = visit(
      dependency,
      canonicalPath(dependency) === canonicalPath(paths.library)
        ? 'libmpv.dylib'
        : undefined,
    );
    changes.push({
      owner: addonPath,
      reference,
      replacement: `@loader_path/${path.basename(dependencyDestination)}`,
    });
  }

  for (const file of staged.values()) {
    execFileSync('install_name_tool', [
      '-id',
      `@loader_path/${path.basename(file)}`,
      file,
    ]);
  }
  for (const change of changes) {
    execFileSync('install_name_tool', [
      '-change',
      change.reference,
      change.replacement,
      change.owner,
    ]);
  }
  for (const file of [...new Set([...staged.values(), stagedLibrary])]) {
    execFileSync('codesign', ['--force', '--sign', '-', file]);
  }
  console.log(`libmpv: staged macOS runtime in ${outputDir}`);
}

const linuxSystemLibraries = [
  /^ld-linux(?:-[^/]+)?\.so/,
  /^libc\.so/,
  /^libdl\.so/,
  /^libm\.so/,
  /^libpthread\.so/,
  /^librt\.so/,
];

function isSystemLinuxDependency(file: string): boolean {
  const name = path.basename(file);
  return (
    name === 'linux-vdso.so.1' ||
    linuxSystemLibraries.some((pattern) => pattern.test(name))
  );
}

function linuxDependencies(file: string, runtimeDir: string): string[] {
  // Never search outputDir: ldd must resolve to the original system file,
  // otherwise a previously staged copy becomes a self-copy source.
  const libraryPath = [runtimeDir, process.env.LD_LIBRARY_PATH]
    .filter(Boolean)
    .join(path.delimiter);
  const output = execFileSync('ldd', [file], {
    encoding: 'utf8',
    env: { ...process.env, LD_LIBRARY_PATH: libraryPath },
  });
  if (output.includes('not found')) {
    fail(`Linux dependency is missing for: ${file}`);
  }
  const dependencies: string[] = [];
  for (const line of output.split('\n')) {
    const mapped = /=>\s+(\/\S+)\s+\(/.exec(line)?.[1];
    const direct = /^\s*(\/\S+)\s+\(/.exec(line)?.[1];
    const dependency = mapped ?? direct;
    if (dependency && !dependencies.includes(dependency))
      dependencies.push(dependency);
  }
  return dependencies;
}

function stageLinux(paths: TargetPaths): void {
  const stagedLibrary = path.join(outputDir, 'libmpv.so');
  copyRuntimeFile(paths.library, stagedLibrary);
  const staged = new Map<string, string>([
    [canonicalPath(paths.library), stagedLibrary],
  ]);
  const stagedFiles = [stagedLibrary];
  const pending = [addonPath, paths.library];
  while (pending.length > 0) {
    const file = pending.shift();
    if (!file) continue;
    for (const dependency of linuxDependencies(file, paths.runtimeDir)) {
      if (isSystemLinuxDependency(dependency)) continue;
      const key = canonicalPath(dependency);
      const existing = staged.get(key);
      if (existing) {
        const alias = path.join(outputDir, path.basename(dependency));
        if (
          path.basename(existing) !== path.basename(alias) &&
          !existsSync(alias)
        ) {
          copyRuntimeFile(dependency, alias);
          stagedFiles.push(alias);
        }
        continue;
      }
      const destination = path.join(outputDir, path.basename(dependency));
      copyRuntimeFile(dependency, destination);
      staged.set(key, destination);
      stagedFiles.push(destination);
      pending.push(dependency);
    }
  }

  for (const file of [addonPath, ...stagedFiles]) {
    execFileSync('patchelf', ['--set-rpath', '$ORIGIN', file]);
  }
  console.log(
    `libmpv: staged ${stagedFiles.length} Linux runtime shared library(s) in ${outputDir}`,
  );
}

function stage(): void {
  requireFile(addonPath, 'native addon');
  mkdirSync(outputDir, { recursive: true });
  const paths = configuredPaths();

  if (target.startsWith('darwin-')) {
    stageMac(paths);
    return;
  }
  if (target === 'win32-x64') {
    const copied = copyMatching(paths.runtimeDir, '.dll');
    if (copied.length === 0) fail('no runtime DLLs were staged');
    console.log(
      `libmpv: staged ${copied.length} Windows runtime DLL(s) in ${outputDir}`,
    );
    return;
  }
  stageLinux(paths);
}

if (mode === 'check') check();
else if (mode === 'stage') stage();
else fail('expected command: check or stage');
