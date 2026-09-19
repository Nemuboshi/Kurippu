// Builds the MSVC import library (mpv.lib) for libmpv on Windows.
//
// The official mpv Windows dev archives are MinGW builds: they ship a GCC
// style libmpv.dll.a that the MSVC linker cannot read, not the mpv.lib that
// binding.gyp links against. This script derives mpv.lib from the exported
// symbol table of the runtime DLL instead, so the same dev archive works.
//
// Usage: pnpm tsx scripts/libmpv-importlib.ts
// Requires the Visual Studio C++ build tools (dumpbin.exe + lib.exe).
import { execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';

const home = path.join(os.homedir(), 'libmpv');
const dll = process.env.MPV_DLL ?? path.join(home, 'bin', 'libmpv-2.dll');
const outDir = process.env.MPV_LIB_DIR ?? path.join(home, 'lib');
const outFile = path.join(outDir, 'mpv.lib');
const vswhere =
  'C:\\Program Files (x86)\\Microsoft Visual Studio\\Installer\\vswhere.exe';

function fail(message: string): never {
  console.error(`error: ${message}`);
  process.exit(1);
}

// Locates the Visual Studio Developer Command Prompt setup script, which is
// the supported way to get dumpbin/lib plus the CRT they depend on on PATH.
function findDevCmd(): string {
  if (!existsSync(vswhere)) fail('vswhere not found; install the VS C++ tools');
  const found = execFileSync(
    vswhere,
    ['-latest', '-products', '*', '-requires', 'Microsoft.VisualStudio.Component.VC.Tools', '-find', '**\\VsDevCmd.bat'],
    { encoding: 'utf8' },
  )
    .split('\n')
    .map((line) => line.trim())
    .filter(Boolean);
  const devcmd = found[0];
  if (!devcmd || !existsSync(devcmd)) fail('VsDevCmd.bat not found');
  return devcmd;
}

// dumpbin /exports prints a fixed-width table; each export line is
// "  ordinal  hint  name". The .def only needs the names.
function parseExports(output: string): string[] {
  const names: string[] = [];
  let inTable = false;
  for (const line of output.split('\n')) {
    if (/ordinal\s+name/i.test(line)) {
      inTable = true;
      continue;
    }
    if (!inTable) continue;
    // Blank line or a new section header ends the export table.
    if (line.trim() === '' || /^[A-Za-z ].*$/.test(line) && !/\d/.test(line)) {
      if (names.length > 0) break;
      continue;
    }
    const m = /^\s+\d+\s+[0-9A-Fa-f]+\s+(\S+)/.exec(line);
    if (m && m[1]) names.push(m[1]);
  }
  return [...new Set(names)];
}

function main(): void {
  if (!existsSync(dll)) fail(`runtime DLL not found: ${dll}`);
  const devcmd = findDevCmd();
  mkdirSync(outDir, { recursive: true });

  const exportsOut = execFileSync(
    'cmd.exe',
    ['/c', `call "${devcmd}" >nul && dumpbin /exports "${dll}"`],
    { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 },
  );
  const names = parseExports(exportsOut);
  if (names.length === 0) fail('no exports parsed from the DLL');
  console.log(`found ${names.length} exported symbols`);

  const defPath = path.join(outDir, 'mpv.def');
  writeFileSync(
    defPath,
    [`LIBRARY ${path.basename(dll)}`, 'EXPORTS', ...names].join('\r\n') + '\r\n',
  );

  // /machine:x64 pins the target arch; the name must match what binding.gyp
  // links (mpv.lib).
  execFileSync(
    'cmd.exe',
    [
      '/c',
      `call "${devcmd}" >nul && lib /def:"${defPath}" /out:"${outFile}" /machine:x64`,
    ],
    { encoding: 'utf8', stdio: 'inherit' },
  );
  if (!existsSync(outFile)) fail(`import library was not produced: ${outFile}`);
  console.log(`wrote ${outFile}`);
}

main();
