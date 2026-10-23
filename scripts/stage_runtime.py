"""Stage RID-specific ffmpeg and libmpv assets beside a published desktop app."""

import hashlib
import os
import pathlib
import re
import shutil
import subprocess
import sys
import tarfile
import tempfile
import urllib.request
import zipfile


WIN_RELEASE = "https://github.com/shinchiro/mpv-winbuild-cmake/releases/download/20261021/"
WIN_ASSETS = {
    "mpv-dev-x86_64-20261021-git-e471496c56.7z": "efc719eff3dc5954b4a5efc800632808bcec01cc9034940dd14e5eb7fe9ea633",
    "ffmpeg-x86_64-git-9a861e968.7z": "fc9c135b799b410ffc78517f428904c299bc64cd8144f21f63f84fcd8c35ea19",
}
FFMPEG_ASSETS = {
    "osx-x64": ("https://evermeet.cx/ffmpeg/ffmpeg-7.1.1.zip", "8d7917c1cebd7a29e68c0a0a6cc4ecc3fe05c7fffed958636c7018b319afdda4"),
    "osx-arm64": ("https://www.osxexperts.net/ffmpeg711arm.zip", "59e39a5cec2e5d2307ed079c53227a9181e64b87454ed4de998349e044bfdc70"),
    "linux-x64": ("https://johnvansickle.com/ffmpeg/releases/ffmpeg-7.0.2-amd64-static.tar.xz", "abda8d77ce8309141f83ab8edf0596834087c52467f6badf376a6a2a4c87cf67"),
}


def download(url, archive, checksum=None):
    request = urllib.request.Request(url, headers={"User-Agent": "Kurippu-build"})
    with urllib.request.urlopen(request, timeout=120) as response, archive.open("wb") as output:
        shutil.copyfileobj(response, output)
    if checksum and hashlib.sha256(archive.read_bytes()).hexdigest() != checksum:
        raise ValueError(f"Checksum mismatch: {archive}")


def extract(archive, output):
    if archive.suffix == ".zip":
        with zipfile.ZipFile(archive) as zf:
            zf.extractall(output)
    elif archive.suffix == ".xz":
        with tarfile.open(archive, "r:xz") as tf:
            for member in tf.getmembers():
                destination = (output / member.name).resolve()
                if not destination.is_relative_to(output.resolve()):
                    raise ValueError("Archive path traversal")
            tf.extractall(output, filter="data")
    else:
        seven = shutil.which("7z") or r"C:\Program Files\7-Zip\7z.exe"
        subprocess.run([seven, "x", str(archive), f"-o{output}", "-y"], check=True,
                       stdout=subprocess.DEVNULL)


def stage_ffmpeg(rid, output, scratch):
    if rid == "win-x64":
        name = "ffmpeg-x86_64-git-9a861e968.7z"
        url, checksum = WIN_RELEASE + name, WIN_ASSETS[name]
    else:
        url, checksum = FFMPEG_ASSETS[rid]
    archive = scratch / pathlib.PurePosixPath(url).name
    download(url, archive, checksum)
    extracted = scratch / "ffmpeg"
    extracted.mkdir()
    extract(archive, extracted)
    executable = "ffmpeg.exe" if rid == "win-x64" else "ffmpeg"
    source = next(extracted.rglob(executable), None)
    if source is None:
        raise FileNotFoundError(f"Archive did not contain {executable}")
    target = output / executable
    shutil.copy2(source, target)
    target.chmod(0o755)
    subprocess.run([str(target), "-version"], check=True, stdout=subprocess.DEVNULL)


def stage_windows(output, scratch):
    name = "mpv-dev-x86_64-20261021-git-e471496c56.7z"
    archive = scratch / name
    download(WIN_RELEASE + name, archive, WIN_ASSETS[name])
    extracted = scratch / "mpv"
    extracted.mkdir()
    extract(archive, extracted)
    for source in extracted.rglob("*.dll"):
        shutil.copy2(source, output / source.name)
    if not (output / "libmpv-2.dll").is_file():
        raise FileNotFoundError("libmpv-2.dll")


def mac_dependencies(file):
    lines = subprocess.check_output(["otool", "-L", str(file)], text=True).splitlines()[1:]
    return [match.group(1) for line in lines if (match := re.match(r"\s+(\S+)\s+\(", line))]


def stage_macos(output):
    prefix = pathlib.Path(subprocess.check_output(["brew", "--prefix"], text=True).strip())
    mpv_prefix = pathlib.Path(subprocess.check_output(["brew", "--prefix", "mpv"], text=True).strip())
    source = mpv_prefix / "lib/libmpv.dylib"
    if not source.exists():
        raise FileNotFoundError(source)
    staged = {}

    def visit(file):
        real = pathlib.Path(file).resolve()
        if real in staged:
            return staged[real]
        target = output / ("libmpv.dylib" if real == source.resolve() else real.name)
        shutil.copy2(real, target)
        staged[real] = target
        for reference in mac_dependencies(real):
            if reference.startswith(("/usr/lib/", "/System/")):
                continue
            candidate = pathlib.Path(reference)
            if reference.startswith("@rpath/"):
                name = reference.split("/", 1)[1]
                candidates = [real.parent / name, prefix / "lib" / name]
                candidates.extend((prefix / "opt").glob("*/lib/" + name))
                candidate = next((item for item in candidates if item.exists()), candidate)
            elif reference.startswith("@loader_path/"):
                candidate = real.parent / reference.split("/", 1)[1]
            if not candidate.exists():
                raise FileNotFoundError(f"{reference} needed by {real}")
            dependency = visit(candidate)
            subprocess.run(["install_name_tool", "-change", reference,
                            "@loader_path/" + dependency.name, str(target)], check=True)
        subprocess.run(["install_name_tool", "-id", "@loader_path/" + target.name, str(target)], check=True)
        return target

    visit(source)
    for file in staged.values():
        subprocess.run(["codesign", "--force", "--sign", "-", str(file)], check=True)


def stage_linux(output):
    candidates = [pathlib.Path("/usr/lib/x86_64-linux-gnu/libmpv.so"),
                  pathlib.Path("/usr/lib/x86_64-linux-gnu/libmpv.so.2")]
    source = next((path for path in candidates if path.exists()), None)
    if source is None:
        raise FileNotFoundError("libmpv.so")
    staged = set()
    ignored = re.compile(r"^(?:libc|libm|libdl|libpthread|librt|ld-linux).*\.so")

    def visit(file, name):
        target = output / name
        if target in staged:
            return
        shutil.copy2(file, target)
        staged.add(target)
        result = subprocess.check_output(["ldd", str(file)], text=True)
        if "not found" in result:
            raise RuntimeError(f"Missing dependency for {file}: {result}")
        for dependency, location in re.findall(r"\s+(\S+\.so(?:\.\d+)*)\s+=>\s+(/\S+)", result):
            if not ignored.match(dependency):
                visit(pathlib.Path(location), dependency)
        subprocess.run(["patchelf", "--set-rpath", "$ORIGIN", str(target)], check=True)

    visit(source, "libmpv.so")


def strip_release_symbols(output):
    for symbol in output.glob("*.pdb"):
        symbol.unlink()


def main():
    if len(sys.argv) != 3:
        raise SystemExit("usage: stage_runtime.py <win-x64|osx-x64|osx-arm64|linux-x64> <publish-dir>")
    rid, directory = sys.argv[1:]
    if rid not in ("win-x64", "osx-x64", "osx-arm64", "linux-x64"):
        raise ValueError(rid)
    output = pathlib.Path(directory).resolve()
    output.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="kurippu-assets-") as tmp:
        scratch = pathlib.Path(tmp)
        stage_ffmpeg(rid, output, scratch)
        if rid == "win-x64":
            stage_windows(output, scratch)
        elif rid.startswith("osx-"):
            stage_macos(output)
        else:
            stage_linux(output)
    strip_release_symbols(output)
    print(f"Runtime assets ready: {rid} -> {output}")


if __name__ == "__main__":
    main()
