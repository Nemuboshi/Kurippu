// Builds the MSVC import library (mpv.lib) for libmpv on Windows.
//
// The official mpv Windows dev archives are MinGW builds: they ship a GCC
// style libmpv.dll.a that the MSVC linker cannot read, not the mpv.lib that
// binding.gyp links against. This script derives mpv.lib from the exported
// symbol table of the runtime DLL instead, so the same dev archive works.
//
// Usage: pnpm libmpv:importlib
// Requires the Visual Studio C++ build tools (dumpbin.exe + lib.exe).
import { execFileSync } from 'node:child_process';
import {
  existsSync,
  mkdirSync,
  readdirSync,
  rmSync,
  writeFileSync,
} from 'node:fs';
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

// Locates the MSVC toolchain bin directory. The Visual Studio dev prompt
// is the supported entry point, but the 17.14 BuildTools devcmd fails to
// evaluate its own install path, so the tools are located directly instead.
function findMsvcBin(): string {
  if (!existsSync(vswhere)) fail('vswhere not found; install the VS C++ tools');
  const install = execFileSync(
    vswhere,
    ['-latest', '-products', '*', '-property', 'installationPath'],
    { encoding: 'utf8' },
  )
    .split('\n')
    .map((line) => line.trim())
    .filter(Boolean)[0];
  if (!install) fail('Visual Studio installation not found');
  const toolsDir = path.join(install, 'VC', 'Tools', 'MSVC');
  if (!existsSync(toolsDir)) fail(`MSVC tools not found under ${install}`);
  const versions = readdirSync(toolsDir, { withFileTypes: true })
    .filter((entry) => entry.isDirectory())
    .map((entry) => entry.name)
    .sort();
  const newest = versions.at(-1);
  if (!newest) fail(`no MSVC toolchain under ${toolsDir}`);
  const bin = path.join(toolsDir, newest, 'bin', 'Hostx64', 'x64');
  for (const tool of ['dumpbin.exe', 'lib.exe']) {
    if (!existsSync(path.join(bin, tool))) fail(`${tool} missing in ${bin}`);
  }
  return bin;
}

// dumpbin /exports prints a fixed-width table; each export line is
// "  ordinal  hint  RVA  name". The .def only needs the names. The table is
// bounded by blank lines, so tracking blank separation is more reliable than
// pattern-guessing the section that follows it.
function parseExports(output: string): string[] {
  const names: string[] = [];
  let inTable = false;
  let sawName = false;
  for (const line of output.split('\n')) {
    if (/^\s*ordinal\b/i.test(line)) {
      inTable = true;
      continue;
    }
    if (!inTable) continue;
    if (line.trim() === '') {
      // The export table ends at the first blank line after real names.
      if (sawName) break;
      continue;
    }
    // Columns are ordinal, hint, RVA, name. The name is the last token and
    // the only one a .def file needs.
    const m = /^\s+\d+\s+[0-9A-Fa-f]+\s+[0-9A-Fa-f]+\s+(\S+)/.exec(line);
    if (m?.[1]) {
      sawName = true;
      names.push(m[1]);
    }
  }
  return [...new Set(names)];
}

// Runs dumpbin/lib with the MSVC bin directory on PATH. The temp batch file
// sidesteps cmd.exe's nested-quote escaping.
function runInMsvcEnv(msvcBin: string, body: string): string {
  const bat = path.join(os.tmpdir(), `kurippu-mpv-${process.pid}.cmd`);
  const crlf = (s: string): string => s.split('\n').join('\r\n');
  writeFileSync(
    bat,
    crlf(`@echo off\nset "PATH=${msvcBin};%PATH%"\n${body}\n`),
  );
  try {
    return execFileSync('cmd.exe', ['/c', bat], {
      encoding: 'utf8',
      maxBuffer: 64 * 1024 * 1024,
      stdio: ['ignore', 'pipe', 'pipe'],
    });
  } finally {
    rmSync(bat, { force: true });
  }
}

function main(): void {
  if (!existsSync(dll)) fail(`runtime DLL not found: ${dll}`);
  const msvcBin = findMsvcBin();
  mkdirSync(outDir, { recursive: true });

  const exportsOut = runInMsvcEnv(msvcBin, `dumpbin /exports "${dll}"`);
  const names = parseExports(exportsOut);
  if (names.length === 0) fail('no exports parsed from the DLL');
  console.log(`found ${names.length} exported symbols`);

  const defPath = path.join(outDir, 'mpv.def');
  writeFileSync(
    defPath,
    // The LIBRARY line names the DLL the import library points at. Without it
    // link.exe derives the name from the .def file (mpv.def -> mpv.dll), but
    // the runtime DLL is libmpv-2.dll.
    [`LIBRARY ${path.basename(dll)}`, 'EXPORTS', ...names].join('\r\n') +
      '\r\n',
  );

  // /machine:x64 pins the target arch; the name must match what binding.gyp
  // links (mpv.lib).
  runInMsvcEnv(msvcBin, `lib /def:"${defPath}" /out:"${outFile}" /machine:x64`);
  if (!existsSync(outFile)) fail(`import library was not produced: ${outFile}`);
  console.log(`wrote ${outFile}`);
}

main();
