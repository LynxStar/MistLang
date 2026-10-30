using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;

namespace Mist.Driver;

internal sealed record NuGetRestoreEntry(ProviderCode Code, string Message);

internal static partial class NuGetRestoreFailure
{
    [GeneratedRegex(@"^(?:.*?\s:\s*)?(?<severity>error|warning)\s+(?<code>NU[0-9]{4}):\s*(?<message>.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EntryPattern();

    public static (int ExitCode, string Stdout, string Stderr)? Invoke(
        Func<(int ExitCode, string Stdout, string Stderr)> restore, string projectPath, DiagnosticBag diagnostics)
    {
        try
        {
            return restore();
        }
        catch(System.ComponentModel.Win32Exception exception)
        {
            PackageDiagnosticFacts.Network(diagnostics, exception.Message,
                frames: [new OperationFrame(projectPath, [], null)]);
            return null;
        }
    }

    public static ImmutableArray<NuGetRestoreEntry> Parse(string stdout, string stderr)
    {
        var entries = ImmutableArray.CreateBuilder<NuGetRestoreEntry>();
        foreach(var output in new[] { stdout, stderr })
        {
            foreach(var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var match = EntryPattern().Match(line.Trim());
                if(match.Success)
                {
                    var severity = match.Groups["severity"].Value.Equals("error", StringComparison.OrdinalIgnoreCase)
                        ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning;
                    entries.Add(new NuGetRestoreEntry(
                        new ProviderCode("nuget", match.Groups["code"].Value.ToUpperInvariant(), severity), line.Trim()));
                }
            }
        }

        return entries.ToImmutable();
    }

    public static PackageResolutionResult Report(
        ImmutableArray<NuGetPackageReference> packages, string stdout, string stderr, DiagnosticBag diagnostics)
    {
        var entries = Parse(stdout, stderr).Where(entry => entry.Code.NativeSeverity == DiagnosticSeverity.Error).ToList();
        if(entries.Count == 0)
        {
            var excerpt = string.IsNullOrWhiteSpace(stderr) ? stdout.Trim() : stderr.Trim();
            if(excerpt.Length == 0)
            {
                foreach(var package in packages)
                {
                    PackageDiagnosticFacts.Missing(package, "", null, diagnostics);
                }
            }
            else
            {
                PackageDiagnosticFacts.Network(diagnostics, excerpt);
            }
        }
        else
        {
            foreach(var entry in entries)
            {
                if(entry.Code.Code is "NU1101" or "NU1102" or "NU1103")
                {
                    foreach(var package in packages)
                    {
                        PackageDiagnosticFacts.Missing(package, entry.Message, entry.Code, diagnostics);
                    }
                }
                else
                {
                    PackageDiagnosticFacts.Network(diagnostics, entry.Message, entry.Code, network: entry.Code.Code == "NU1301");
                }
            }
        }

        return new PackageResolutionResult([], [], packages.Length);
    }
}