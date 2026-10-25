using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;

namespace Mist.Driver;

/// <summary>
/// Builds the C# projects a reference graph names with the .NET SDK, before any Mist source is loaded,
/// and reads each one's output back from MSBuild rather than assuming a <c>bin/</c> layout.
/// </summary>
public static class CSharpProjectBuilder
{
    internal static readonly TimeSpan BuildTimeout = TimeSpan.FromSeconds(600);
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(120);

    public static (ImmutableArray<ExternalAssembly> Assemblies, int BuiltCount) BuildAll(
        ImmutableArray<ExternalReference> references,
        string targetFramework,
        string manifestPath,
        DiagnosticBag diagnostics)
    {
        var chain = BuildChain.Format(BuildChain.Extend(BuildChain.Current(), manifestPath));
        return BuildAll(references, targetFramework, chain, diagnostics, DotnetProcess.Run);
    }

    internal static (ImmutableArray<ExternalAssembly> Assemblies, int BuiltCount) BuildAll(
        ImmutableArray<ExternalReference> references,
        string targetFramework,
        string chain,
        DiagnosticBag diagnostics,
        Func<DotnetInvocation, DotnetOutcome> run)
    {
        var assemblies = ImmutableArray.CreateBuilder<ExternalAssembly>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var built = new Dictionary<string, ExternalAssembly>(StringComparer.OrdinalIgnoreCase);

        foreach(var reference in references)
        {
            if(reference.Kind == ReferenceKind.Assembly)
            {
                if(paths.Add(reference.Path))
                {
                    assemblies.Add(new ExternalAssembly(reference.Name, reference.Path, [], null, ReferenceKind.Assembly));
                }

                continue;
            }

            if(!built.TryGetValue(reference.Path, out var output))
            {
                if(Build(reference, targetFramework, chain, diagnostics, run) is not { } fresh)
                {
                    break;
                }

                output = fresh;
                built.Add(reference.Path, output);
            }

            var producedName = Path.GetFileNameWithoutExtension(output.Path);
            if(ProjectReferenceResolver.KeyMismatch(reference.Name, ReferenceKind.CSharpProject, producedName, reference.Path) is { } mismatch)
            {
                diagnostics.Add(mismatch);
                continue;
            }

            if(paths.Add(output.Path))
            {
                assemblies.Add(output with { Name = reference.Name });
            }
        }

        return (assemblies.ToImmutable(), built.Count);
    }

    internal static ExternalAssembly? Build(
        ExternalReference reference,
        string targetFramework,
        string chain,
        DiagnosticBag diagnostics,
        Func<DotnetInvocation, DotnetOutcome> run)
    {
        var directory = Path.GetDirectoryName(reference.Path)!;
        var environment = ImmutableDictionary<string, string>.Empty.Add(BuildChain.VariableName, chain);

        var build = run(new DotnetInvocation(
            $"build \"{reference.Path}\" -c Debug -f {targetFramework} --nologo -v:quiet", directory, environment, BuildTimeout));
        if(build.TimedOut || build.ExitCode != 0)
        {
            var reason = build.TimedOut
                ? $"dotnet build timed out after {(int)BuildTimeout.TotalSeconds} seconds."
                : ErrorLines(build, "dotnet build");
            diagnostics.Report(DriverDiagnostics.CSharpProjectBuildFailed, default, reference.DeclaringManifestPath, (object?)reference.Path, reference.Name, reason);
            return null;
        }

        var query = run(new DotnetInvocation(
            $"msbuild \"{reference.Path}\" -getProperty:TargetPath -p:Configuration=Debug -p:TargetFramework={targetFramework}",
            directory, environment, QueryTimeout));
        var targetPath = query.ExitCode == 0 && !query.TimedOut ? LastLine(query.Stdout) : "";
        if(targetPath.Length == 0 || !File.Exists(targetPath))
        {
            diagnostics.Report(
                DriverDiagnostics.CSharpProjectBuildFailed, default, reference.DeclaringManifestPath,
                (object?)reference.Path, reference.Name, $"MSBuild reported no built output (TargetPath '{targetPath}').");
            return null;
        }

        var outputDirectory = Path.GetDirectoryName(targetPath)!;
        var closure = Directory.GetFiles(outputDirectory, "*.dll")
            .Where(file => !string.Equals(file, targetPath, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToImmutableArray();
        var runtimeConfig = Path.ChangeExtension(targetPath, ".runtimeconfig.json");

        return new ExternalAssembly(
            reference.Name, targetPath, closure, File.Exists(runtimeConfig) ? runtimeConfig : null, ReferenceKind.CSharpProject);
    }

    internal static string ErrorLines(DotnetOutcome outcome, string command)
    {
        var lines = (outcome.Stdout + "\n" + outcome.Stderr)
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Contains("error", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return lines.Count == 0
            ? $"{command} exited with code {outcome.ExitCode}."
            : string.Join(Environment.NewLine, lines);
    }

    private static string LastLine(string text)
    {
        return text.Split('\n').Select(line => line.Trim()).LastOrDefault(line => line.Length > 0) ?? "";
    }
}