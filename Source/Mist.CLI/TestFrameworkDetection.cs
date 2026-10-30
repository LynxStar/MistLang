using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Mist.Driver.Contracts;

namespace Mist.CLI;

public static class TestFrameworkDetection
{
    private static readonly Regex AttributePattern = new(@"\[\s*[\w.]*(?:Fact|Theory|Test|TestMethod)\s*[\(\]]", RegexOptions.Compiled);

    public static bool HasTestAttributes(string source)
    {
        return AttributePattern.IsMatch(source);
    }

    public static bool HasFrameworkPackage(ImmutableArray<NuGetPackageReference> packages)
    {
        return !packages.IsDefaultOrEmpty && packages.Any(package => package.Name.Contains("Test", StringComparison.OrdinalIgnoreCase)
            || package.Name.Contains("xunit", StringComparison.OrdinalIgnoreCase));
    }

    public static bool ShouldWarn(IEnumerable<string> sourceContents, ImmutableArray<NuGetPackageReference> packages)
    {
        return sourceContents.Any(HasTestAttributes) && !HasFrameworkPackage(packages);
    }
}