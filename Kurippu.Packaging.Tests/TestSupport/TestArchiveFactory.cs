namespace Kurippu.Packaging.Tests;

public static class TestArchiveFactory
{
    // SharpCompress cannot write XZ streams. These tiny fixtures were created
    // once with the standard tar/xz tools and are embedded to keep tests portable.
    public static byte[] SampleTarXz { get; } = Convert.FromBase64String(
        "/Td6WFoAAATm1rRGBMBzgBQhARYAAAAAAAAAAMzphvLgCf8Aa10ANxlKzh/yEXWGFVIGED3L8YFZCrv51rEKKsdaOYab9XxrEPwC43r3UrStXOGAlWVDLKoqi/8xt4ghtXsx5zUicVkGocyClTM/+U7AvbyRbWX6EAMHCL3wRbcvvww8hImSThuGPdVnJdvSQaAAAP5skOzquyzGAAGPAYAUAAD35qXnscRn+wIAAAAABFla");

    public static byte[] SymbolicLinkTarXz { get; } = Convert.FromBase64String(
        "/Td6WFoAAATm1rRGBMCrAYAUIQEWAAAAAAAAAO5llxPgCf8Ao10AKBhLBIZIGx7nZy2O56O7w1dusGGLSXXmeWvmMEeof8kIZP8BIfo7/C3ThPOnv2sQWNZ/4bh405lymg7xmzo7xx1DABznqdnsBT8+ltJ/plV2euWfXPb7gPCuQqFG4Xc0ylMTqN3Z0vDzNITy7xXJZN2YDEnsEC2EXITTbV5i/bHTzNSx8vSor8qcFcS2UpX5z/kkYGz+qPAnRedl7k9w5zGsyAAAZycAZznB2zYAAccBgBQAAPZurgSxxGf7AgAAAAAEWVo=");

    public static void WriteEmbeddedTarXz(string destination, byte[] content) =>
        File.WriteAllBytes(destination, content);
}
