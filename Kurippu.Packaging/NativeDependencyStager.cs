using System.Text.RegularExpressions;

namespace Kurippu.Packaging;

public sealed partial class NativeDependencyStager(IProcessRunner processRunner)
{
    public async Task StageMacOSAsync(string outputDirectory,
        CancellationToken cancellationToken)
    {
        var output = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(output);
        var prefix = (await RunRequired("brew", ["--prefix"], cancellationToken).ConfigureAwait(false))
            .Trim();
        var mpvPrefix = (await RunRequired("brew", ["--prefix", "mpv"], cancellationToken).ConfigureAwait(false))
            .Trim();
        var source = Path.Combine(mpvPrefix, "lib", "libmpv.dylib");
        if (!File.Exists(source))
            throw new PackagingException($"macOS mpv library was not found: {source}");

        var inspector = new MacNativeDependencyInspector(processRunner);
        var staged = new Dictionary<string, string>(StringComparer.Ordinal);
        var prefixRoot = Path.GetFullPath(prefix);
        var brewOptRoot = Path.Combine(prefixRoot, "opt");

        async Task<string> VisitAsync(string file)
        {
            var real = Path.GetFullPath(file);
            if (staged.TryGetValue(real, out var alreadyStaged)) return alreadyStaged;

            var target = Path.Combine(output,
                string.Equals(real, Path.GetFullPath(source), StringComparison.Ordinal)
                    ? "libmpv.dylib"
                    : Path.GetFileName(real));
            File.Copy(real, target, overwrite: true);
            staged[real] = target;

            foreach (var reference in await inspector.GetDependenciesAsync(real, cancellationToken).ConfigureAwait(false))
            {
                if (reference.StartsWith("/usr/lib/", StringComparison.Ordinal) ||
                    reference.StartsWith("/System/", StringComparison.Ordinal))
                    continue;

                string candidate;
                if (reference.StartsWith("@rpath/", StringComparison.Ordinal))
                {
                    var name = reference["@rpath/".Length..];
                    candidate = FindRPathDependency(real, name, prefixRoot, brewOptRoot)
                        ?? throw new PackagingException(
                            $"{reference} needed by {real} could not be resolved.");
                }
                else if (reference.StartsWith("@loader_path/", StringComparison.Ordinal))
                {
                    candidate = Path.Combine(Path.GetDirectoryName(real)!,
                        reference["@loader_path/".Length..]);
                }
                else
                {
                    candidate = reference;
                }

                if (!File.Exists(candidate))
                    throw new PackagingException($"{reference} needed by {real} was not found.");
                var dependency = await VisitAsync(candidate).ConfigureAwait(false);
                await RunRequired("install_name_tool",
                    ["-change", reference, "@loader_path/" + Path.GetFileName(dependency), target],
                    cancellationToken).ConfigureAwait(false);
            }

            await RunRequired("install_name_tool",
                ["-id", "@loader_path/" + Path.GetFileName(target), target], cancellationToken).ConfigureAwait(false);
            return target;
        }

        await VisitAsync(source).ConfigureAwait(false);
        foreach (var target in staged.Values)
            await RunRequired("codesign", ["--force", "--sign", "-", target], cancellationToken).ConfigureAwait(false);
    }

    public async Task StageLinuxAsync(string outputDirectory,
        CancellationToken cancellationToken, string? sourceOverride = null)
    {
        var output = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(output);
        var source = sourceOverride ?? new[]
        {
            "/usr/lib/x86_64-linux-gnu/libmpv.so",
            "/usr/lib/x86_64-linux-gnu/libmpv.so.2"
        }.FirstOrDefault(File.Exists)
            ?? throw new PackagingException("libmpv.so was not found on this Linux host.");

        var inspector = new LinuxNativeDependencyInspector(processRunner);
        var staged = new HashSet<string>(StringComparer.Ordinal);

        async Task VisitAsync(string file, string name)
        {
            var target = Path.Combine(output, name);
            if (!staged.Add(target)) return;
            File.Copy(file, target, overwrite: true);

            foreach (var dependency in await inspector.GetDependenciesAsync(file,
                         cancellationToken).ConfigureAwait(false))
            {
                var dependencyName = Path.GetFileName(dependency);
                if (IgnoredLinuxDependency().IsMatch(dependencyName)) continue;
                await VisitAsync(dependency, dependencyName).ConfigureAwait(false);
            }

            await RunRequired("patchelf", ["--set-rpath", "$ORIGIN", target],
                cancellationToken).ConfigureAwait(false);
        }

        await VisitAsync(source, "libmpv.so").ConfigureAwait(false);
    }

    private static string? FindRPathDependency(string file, string name, string prefix,
        string optRoot)
    {
        var candidates = new List<string> { Path.Combine(Path.GetDirectoryName(file)!, name) };
        candidates.Add(Path.Combine(prefix, "lib", name));
        if (Directory.Exists(optRoot))
            candidates.AddRange(Directory.EnumerateDirectories(optRoot)
                .Select(directory => Path.Combine(directory, "lib", name)));
        return candidates.FirstOrDefault(File.Exists);
    }

    private async Task<string> RunRequired(string fileName,
        IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(fileName, arguments, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw MacNativeDependencyInspector.ProcessFailure(fileName, string.Join(' ', arguments),
                result);
        return result.StandardOutput;
    }

    [GeneratedRegex(@"^(?:libc|libm|libdl|libpthread|librt|ld-linux).*\.so",
        RegexOptions.CultureInvariant)]
    private static partial Regex IgnoredLinuxDependency();
}
