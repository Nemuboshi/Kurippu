using Kurippu.Packaging;

namespace Kurippu.Packaging.Tests;

public sealed class LinuxNativeDependencyInspectorTests
{
    [Fact]
    public async Task Linux_inspector_parses_ldd_dependencies()
    {
        var runner = new FakeProcessRunner(response: (_, _) => new ProcessResult(0,
            """
                linux-vdso.so.1 (0x00007ffd)
                libavcodec.so.60 => /usr/lib/x86_64-linux-gnu/libavcodec.so.60 (0x00007f01)
                libc.so.6 => /lib/x86_64-linux-gnu/libc.so.6 (0x00007f02)
                /lib64/ld-linux-x86-64.so.2 (0x00007f03)
            """, ""));
        var inspector = new LinuxNativeDependencyInspector(runner);

        var dependencies = await inspector.GetDependenciesAsync("/usr/lib/libmpv.so",
            CancellationToken.None);

        Assert.Equal(["/usr/lib/x86_64-linux-gnu/libavcodec.so.60",
            "/lib/x86_64-linux-gnu/libc.so.6"], dependencies);
        var call = Assert.Single(runner.Calls);
        Assert.Equal("ldd", call.FileName);
        Assert.Equal(["/usr/lib/libmpv.so"], call.Arguments);
    }

    [Fact]
    public async Task Linux_inspector_rejects_missing_dependencies()
    {
        var runner = new FakeProcessRunner(response: (_, _) => new ProcessResult(0,
            "libmissing.so.1 => not found", ""));
        var inspector = new LinuxNativeDependencyInspector(runner);

        var exception = await Assert.ThrowsAsync<PackagingException>(() =>
            inspector.GetDependenciesAsync("/usr/lib/libmpv.so", CancellationToken.None));

        Assert.Contains("not found", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
