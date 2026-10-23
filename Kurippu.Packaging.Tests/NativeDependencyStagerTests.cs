using System.Text;
using Kurippu.Packaging;

namespace Kurippu.Packaging.Tests;

public sealed class NativeDependencyStagerTests
{
    [Fact]
    public async Task Mac_stages_recursive_closure_and_rewrites_references()
    {
        using var fixture = new NativeFixture();
        var runner = fixture.CreateMacRunner();
        var stager = new NativeDependencyStager(runner);

        await stager.StageMacOSAsync(fixture.Output, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(fixture.Output, "libmpv.dylib")));
        Assert.True(File.Exists(Path.Combine(fixture.Output, "libavcodec.dylib")));
        Assert.Contains(runner.Calls, call => call.FileName == "install_name_tool" &&
            call.Arguments.SequenceEqual(["-change", "@rpath/libavcodec.dylib",
                "@loader_path/libavcodec.dylib",
                Path.Combine(fixture.Output, "libmpv.dylib")]));
        Assert.Contains(runner.Calls, call => call.FileName == "install_name_tool" &&
            call.Arguments.SequenceEqual(["-id", "@loader_path/libmpv.dylib",
                Path.Combine(fixture.Output, "libmpv.dylib")]));
        Assert.Equal(2, runner.Calls.Count(call => call.FileName == "codesign"));
    }

    [Fact]
    public async Task Linux_stages_recursive_closure_and_ignores_system_runtime_libraries()
    {
        using var fixture = new NativeFixture();
        var runner = fixture.CreateLinuxRunner();
        var stager = new NativeDependencyStager(runner);

        await stager.StageLinuxAsync(fixture.Output, CancellationToken.None,
            fixture.LinuxMpv);

        Assert.True(File.Exists(Path.Combine(fixture.Output, "libmpv.so")));
        Assert.True(File.Exists(Path.Combine(fixture.Output, "libavcodec.so.60")));
        Assert.False(File.Exists(Path.Combine(fixture.Output, "libc.so.6")));
        Assert.Contains(runner.Calls, call => call.FileName == "patchelf" &&
            call.Arguments.SequenceEqual(["--set-rpath", "$ORIGIN",
                Path.Combine(fixture.Output, "libavcodec.so.60")]));
    }

    [Fact]
    public async Task Mac_rid_on_a_non_mac_host_is_rejected_before_download()
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
    public async Task Linux_rid_on_a_non_linux_host_is_rejected_before_download()
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
                if (fileName == "brew" && arguments.SequenceEqual(["--prefix"]))
                    return new ProcessResult(0, Brew + Environment.NewLine, "");
                if (fileName == "brew" && arguments.SequenceEqual(["--prefix", "mpv"]))
                    return new ProcessResult(0, Mpv + Environment.NewLine, "");
                if (fileName == "otool" && arguments[1] == MacMpv)
                    return new ProcessResult(0,
                        $"{MacMpv}:\n    @rpath/libavcodec.dylib (compatibility version 1.0.0)\n" +
                        "    /usr/lib/libSystem.B.dylib (compatibility version 1.0.0)\n", "");
                if (fileName == "otool" && arguments[1] == MacCodec)
                    return new ProcessResult(0,
                        $"{MacCodec}:\n    /usr/lib/libSystem.B.dylib (compatibility version 1.0.0)\n",
                        "");
                return new ProcessResult(0, "", "");
            });

        public FakeProcessRunner CreateLinuxRunner() =>
            new(response: (fileName, arguments) =>
            {
                if (fileName == "ldd" && Path.GetFullPath(arguments[0]) == Path.GetFullPath(LinuxMpv))
                    return new ProcessResult(0,
                        $"    libavcodec.so.60 => {LinuxPosixCodec} (0x1)\n" +
                        $"    libc.so.6 => {LinuxPosixLibc} (0x2)\n", "");
                if (fileName == "ldd" && Path.GetFullPath(arguments[0]) == Path.GetFullPath(LinuxCodec))
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
