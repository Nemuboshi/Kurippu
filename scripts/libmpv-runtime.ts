// libmpv SDK validation and runtime staging for the native/mpv addon.
// node-gyp links against an import library; the runtime DLL/dylib must sit
// next to the built .node so the addon can load it at runtime.
import { execFileSync } from 'node:child_process';
import {
  chmodSync,
  copyFileSync,
  type Dirent,
  existsSync,
  mkdirSync,
  readdirSync,
  rmSync,
  statSync,
} from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const target = `${process.platform}-${process.arch}`;
const outputDir = path.join(root, 'native', 'mpv', 'build', 'Release');
const addonPath = path.join(outputDir, 'mpv_addon.node');
const mode = process.argv[2];

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
  if (!existsSync(file)) fail(`${description} not found: ${file}`);
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
    if (!entry.name.toLowerCase().endsWith(extension)) continue;
    const destination = path.join(outputDir, entry.name);
    copyRuntimeFile(path.join(sourceDir, entry.name), destination);
    copied.push(destination);
  }
  return copied;
}

// Symlinks to files count; node-gyp output dirs may contain both.
function isFileLike(entry: Dirent): boolean {
  return entry.isFile() || entry.isSymbolicLink();
}

function configuredPaths(): TargetPaths {
  if (target === 'darwin-arm64') {
    return {
      includeDir: path.resolve(
        process.env.MPV_INCLUDE_DIR || '/opt/homebrew/include',
      ),
      library: path.resolve(
        process.env.MPV_LIB || '/opt/homebrew/lib/libmpv.dylib',
      ),
      runtimeDir: path.resolve(
        process.env.MPV_RUNTIME_DIR || '/opt/homebrew/opt/mpv/lib',
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

  fail(`unsupported build target: ${target}`);
}

function validateHeaders(includeDir: string): void {
  for (const header of ['client.h', 'render.h', 'render_gl.h']) {
    requireFile(path.join(includeDir, 'mpv', header), `mpv/${header}`);
  }
}

function getMacInstallName(library: string): string {
  const lines = execFileSync('otool', ['-D', library], {
    encoding: 'utf8',
  })
    .split('\n')
    .map((line) => line.trim())
    .filter(Boolean);
  // otool -D prints the Mach-O path first, then the install name.
  const installName = lines[1];
  if (!installName) fail(`unable to read install name from: ${library}`);
  return installName;
}

function check(): void {
  if (target === 'darwin-arm64') {
    const paths = configuredPaths();
    validateHeaders(paths.includeDir);
    requireFile(paths.library, 'libmpv link library');
    requireDirectory(paths.runtimeDir, 'libmpv runtime directory');
    return;
  }

  if (target === 'win32-x64') {
    const paths = configuredPaths();
    validateHeaders(paths.includeDir);
    requireFile(paths.library, 'libmpv import library');
    requireDirectory(paths.runtimeDir, 'libmpv runtime directory');
    const dlls = readdirSync(paths.runtimeDir).map((name) =>
      name.toLowerCase(),
    );
    if (!dlls.includes('libmpv-2.dll') && !dlls.includes('mpv-2.dll')) {
      fail(`libmpv runtime DLL not found in: ${paths.runtimeDir}`);
    }
    return;
  }

  fail(`unsupported build target: ${target}`);
}

function stage(): void {
  requireFile(addonPath, 'native addon');
  mkdirSync(outputDir, { recursive: true });

  if (target === 'darwin-arm64') {
    const paths = configuredPaths();
    const stagedLibrary = path.join(outputDir, 'libmpv.dylib');
    copyMatching(paths.runtimeDir, '.dylib');
    copyRuntimeFile(paths.library, stagedLibrary);

    const installName = getMacInstallName(paths.library);
    execFileSync('install_name_tool', [
      '-change',
      installName,
      '@loader_path/libmpv.dylib',
      addonPath,
    ]);
    execFileSync('install_name_tool', [
      '-id',
      '@loader_path/libmpv.dylib',
      stagedLibrary,
    ]);
    execFileSync('codesign', ['--force', '--sign', '-', stagedLibrary]);
    console.log(`libmpv: staged macOS runtime in ${outputDir}`);
    return;
  }

  if (target === 'win32-x64') {
    const copied = copyMatching(configuredPaths().runtimeDir, '.dll');
    if (copied.length === 0) fail('no runtime DLLs were staged');
    console.log(
      `libmpv: staged ${copied.length} Windows runtime DLL(s) in ${outputDir}`,
    );
    return;
  }

  fail(`unsupported build target: ${target}`);
}

if (mode === 'check') check();
else if (mode === 'stage') stage();
else fail('expected command: check or stage');
