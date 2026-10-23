using System.Text;
using Kurippu.Packaging;

namespace Kurippu.Packaging.Tests;

public sealed class NativeDependencyStagerTests
{
    [Fact]
    public async Task MacStagesRecursiveClosureAndRewritesReferences()
    {
        using var fixture = new NativeFixture();
        var runner = fixture.CreateMacRunner();
        var stager = new NativeDependencyStager(runner);

        await stager.StageMacOSAsync(fixture.Output, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(fixture.Output, "libmpv.dylib")));
        Assert.True(File.Exists(Path.Combine(fixture.Output, "libavcodec.dylib")));
        Assert.Contains(runner.Calls, call => string.Equals(call.FileName, "install_name_tool", StringComparison.Ordinal) &&
            call.Arguments.SequenceEqual(["-change", "@rpath/libavcodec.dylib",
                "@loader_path/libavcodec.dylib",
                Path.Combine(fixture.Output, "libmpv.dylib")]));
        Assert.Contains(runner.Calls, call => string.Equals(call.FileName, "install_name_tool", StringComparison.Ordinal) &&
            call.Arguments.SequenceEqual(["-id", "@loader_path/libmpv.dylib",
                Path.Combine(fixture.Output, "libmpv.dylib")]));
        Assert.Equal(2, runner.Calls.Count(call => string.Equals(call.FileName, "codesign", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task LinuxStagesRecursiveClosureAndIgnoresSystemRuntimeLibraries()
    {
        using var fixture = new NativeFixture();
        var runner = fixture.CreateLinuxRunner();
        var stager = new NativeDependencyStager(runner);

        await stager.StageLinuxAsync(fixture.Output, CancellationToken.None,
            fixture.LinuxMpv);

        Assert.True(File.Exists(Path.Combine(fixture.Output, "libmpv.so")));
        Assert.True(File.Exists(Path.Combine(fixture.Output, "libavcodec.so.60")));
        Assert.False(File.Exists(Path.Combine(fixture.Output, "libc.so.6")));
        Assert.Contains(runner.Calls, call => string.Equals(call.FileName, "patchelf", StringComparison.Ordinal) &&
            call.Arguments.SequenceEqual(["--set-rpath", "$ORIGIN",
                Path.Combine(fixture.Output, "libavcodec.so.60")]));
    }

    [Fact]
    public async Task MacRidOnANonMacHostIsRejectedBeforeDownload()
    {
        if (OperatingSystem.IsMacOS()) return;
        using var fixture = new NativeFixture();
        var downloader = new FakeAssetDownloader();
        var stager = new RuntimeStager(downloader, new FakeProcessRunner(),
            new NativeDependencyStager(new FakeProcessRunner()));

        var exception = await Assert.ThrowsAsync<PackagingException>(() =>
            stager.StageAsync("osx-arm64", fixture.Output, CancellationToken.None));

        Assert.Contains("macOS", exception.Message, StringComparison.Ordinal);
        Assert.Empty(downloader.Calls);
    }

    [Fact]
    public async Task LinuxRidOnANonLinuxHostIsRejectedBeforeDownload()
    {
        if (OperatingSystem.IsLinux()) return;
        using var fixture = new NativeFixture();
        var downloader = new FakeAssetDownloader();
        var stager = new RuntimeStager(downloader, new FakeProcessRunner(),
            new NativeDependencyStager(new FakeProcessRunner()));

        var exception = await Assert.ThrowsAsync<PackagingException>(() =>
            stager.StageAsync("linux-x64", fixture.Output, CancellationToken.None));

        Assert.Contains("Linux", exception.Message, StringComparison.Ordinal);
        Assert.Empty(downloader.Calls);
    }

    private sealed class NativeFixture : IDisposable
    {
        public NativeFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "kurippu-native-tests-" +
                Guid.NewGuid().ToString("N"));
            Brew = Path.Combine(Root, "brew");
            Mpv = Path.Combine(Root, "mpv");
            Output = Path.Combine(Root, "output");
            Directory.CreateDirectory(Path.Combine(Brew, "lib"));
            Directory.CreateDirectory(Path.Combine(Mpv, "lib"));
            Directory.CreateDirectory(Output);

            MacMpv = Path.Combine(Mpv, "lib", "libmpv.dylib");
            MacCodec = Path.Combine(Brew, "lib", "libavcodec.dylib");
            File.WriteAllText(MacMpv, "mpv", Encoding.UTF8);
            File.WriteAllText(MacCodec, "codec", Encoding.UTF8);

            LinuxMpv = PosixPath("linux", "libmpv.so.2");
            LinuxCodec = PosixPath("linux", "libavcodec.so.60");
            LinuxLibc = PosixPath("linux", "libc.so.6");
            Directory.CreateDirectory(Path.GetDirectoryName(LinuxMpv)!);
            File.WriteAllText(LinuxMpv, "mpv", Encoding.UTF8);
            File.WriteAllText(LinuxCodec, "codec", Encoding.UTF8);
            File.WriteAllText(LinuxLibc, "libc", Encoding.UTF8);
        }

        public string Root { get; }
        public string Brew { get; }
        public string Mpv { get; }
        public string Output { get; }
        public string MacMpv { get; }
        public string MacCodec { get; }
        public string LinuxMpv { get; }
        public string LinuxCodec { get; }
        public string LinuxLibc { get; }
        public string LinuxPosixMpv => ToPosix(LinuxMpv);
        public string LinuxPosixCodec => ToPosix(LinuxCodec);
        public string LinuxPosixLibc => ToPosix(LinuxLibc);

        private string PosixPath(params string[] parts)
        {
            var path = Root.Replace('\\', '/');
            foreach (var part in parts) path += "/" + part;
            if (!OperatingSystem.IsWindows()) return path;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            return path;
        }

        private static string ToPosix(string path) => path.Replace('\\', '/');

        public FakeProcessRunner CreateMacRunner() =>
            new(response: (fileName, arguments) =>
            {
                if (string.Equals(fileName, "brew", StringComparison.Ordinal) && arguments.SequenceEqual(["--prefix"], StringComparer.Ordinal))
                    return new ProcessResult(0, Brew + Environment.NewLine, "");
                if (string.Equals(fileName, "brew", StringComparison.Ordinal) && arguments.SequenceEqual(["--prefix", "mpv"], StringComparer.Ordinal))
                    return new ProcessResult(0, Mpv + Environment.NewLine, "");
                if (string.Equals(fileName, "otool", StringComparison.Ordinal) && string.Equals(arguments[1], MacMpv, StringComparison.Ordinal))
                    return new ProcessResult(0,
                        $"{MacMpv}:\n    @rpath/libavcodec.dylib (compatibility version 1.0.0)\n" +
                        "    /usr/lib/libSystem.B.dylib (compatibility version 1.0.0)\n", "");
                if (string.Equals(fileName, "otool", StringComparison.Ordinal) && string.Equals(arguments[1], MacCodec, StringComparison.Ordinal))
                    return new ProcessResult(0,
                        $"{MacCodec}:\n    /usr/lib/libSystem.B.dylib (compatibility version 1.0.0)\n",
                        "");
                return new ProcessResult(0, "", "");
            });

        public FakeProcessRunner CreateLinuxRunner() =>
            new(response: (fileName, arguments) =>
            {
                if (string.Equals(fileName, "ldd", StringComparison.Ordinal) && string.Equals(Path.GetFullPath(arguments[0]), Path.GetFullPath(LinuxMpv), StringComparison.Ordinal))
                    return new ProcessResult(0,
                        $"    libavcodec.so.60 => {LinuxPosixCodec} (0x1)\n" +
                        $"    libc.so.6 => {LinuxPosixLibc} (0x2)\n", "");
                if (string.Equals(fileName, "ldd", StringComparison.Ordinal) &&
                    string.Equals(Path.GetFullPath(arguments[0]), Path.GetFullPath(LinuxCodec), StringComparison.Ordinal))
                    return new ProcessResult(0,
                        $"    libc.so.6 => {LinuxPosixLibc} (0x2)\n", "");
                return new ProcessResult(0, "", "");
            });

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
