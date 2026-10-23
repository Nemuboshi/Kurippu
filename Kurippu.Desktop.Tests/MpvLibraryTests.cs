using Kurippu.Desktop.Media;

namespace Kurippu.Desktop.Tests;

public class MpvLibraryTests
{
    [Theory]
    [InlineData("win", "libmpv-2.dll")]
    [InlineData("osx", "libmpv.dylib")]
    [InlineData("linux", "libmpv.so")]
    public void PlatformSpecificLibraryName(string platform, string expected) =>
        Assert.Equal(expected, MpvNative.LibraryFileName(platform));
}
