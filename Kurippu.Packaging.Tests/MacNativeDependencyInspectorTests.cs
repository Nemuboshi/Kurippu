using Kurippu.Packaging;

namespace Kurippu.Packaging.Tests;

public sealed class MacNativeDependencyInspectorTests
{
    [Fact]
    public async Task Mac_inspector_parses_otool_dependencies()
    {
        var runner = new FakeProcessRunner(response: (_, _) => new ProcessResult(0,
            """
            /tmp/libmpv.dylib:
                @rpath/libavcodec.62.dylib (compatibility version 62.0.0, current version 62.0.0)
                /usr/lib/libSystem.B.dylib (compatibility version 1.0.0, current version 1345.0.0)
            """, ""));
        var inspector = new MacNativeDependencyInspector(runner);

        var dependencies = await inspector.GetDependenciesAsync("/tmp/libmpv.dylib",
            CancellationToken.None);

        Assert.Equal(["@rpath/libavcodec.62.dylib", "/usr/lib/libSystem.B.dylib"],
            dependencies);
        var call = Assert.Single(runner.Calls);
        Assert.Equal("otool", call.FileName);
        Assert.Equal(["-L", "/tmp/libmpv.dylib"], call.Arguments);
    }
}
