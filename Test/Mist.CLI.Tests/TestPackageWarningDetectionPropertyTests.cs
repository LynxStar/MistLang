// Feature: attributes-and-test-command, Property 5: Test Package Warning Detection
// **Validates: Requirements 6.3**
//
// For any Mist program where at least one method has an attribute whose unqualified
// name ends in Fact, Theory, Test, or TestMethod, and the project's package list does
// not contain any package name matching "Test" or "xunit" (case-insensitive substring),
// the system SHALL emit warning diagnostic MIST0102. If the package list does
// contain such a package, no warning SHALL be emitted.

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using Mist.Driver.Contracts;

namespace Mist.CLI.Tests;

[Trait("Tier", "1")]
public sealed class TestPackageWarningDetectionPropertyTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // Generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates attribute names that end in one of the test-matching suffixes.
    /// </summary>
    private static Gen<string> GenTestAttributeName()
    {
        var suffixes = new[] { "Fact", "Theory", "Test", "TestMethod" };
        var prefixes = new[] { "", "My", "Xunit.", "NUnit.Framework.", "Custom.Namespace." };

        return from prefix in Gen.Elements(prefixes)
               from suffix in Gen.Elements(suffixes)
               select prefix + suffix;
    }

    /// <summary>
    /// Generates attribute names that do NOT match any test suffix.
    /// </summary>
    private static Gen<string> GenNonTestAttributeName()
    {
        return Gen.Elements(
            "Serializable", "Obsolete", "DataContract", "JsonProperty",
            "Range", "Required", "Display", "Authorize",
            "HttpGet", "HttpPost", "Route", "ApiController",
            "DebuggerDisplay", "Conditional", "Flags", "DllImport",
            "MyCustom", "Facts", "Testing", "Theoretical");
    }

    /// <summary>
    /// Generates a Mist source file content string containing an attribute on a method.
    /// The attribute name may or may not match the test-attribute pattern.
    /// </summary>
    private static Gen<string> GenSourceWithAttribute(Gen<string> attributeNameGen)
    {
        return from attrName in attributeNameGen
               from hasArgs in Gen.Elements(true, false)
               let attrSyntax = hasArgs ? $"[{attrName}(1, 2, 3)]" : $"[{attrName}]"
               select $"class MyTests\n{{\n    {attrSyntax}\n    public void SomeMethod()\n    {{\n    }}\n}}";
    }

    /// <summary>
    /// Generates package names that contain "Test" or "xunit" (case-insensitive).
    /// </summary>
    private static Gen<string> GenTestFrameworkPackageName()
    {
        return Gen.Elements(
            "xunit", "xunit.runner.visualstudio", "Microsoft.NET.Test.Sdk",
            "NUnit.Test", "MSTest.TestFramework", "MSTest.TestAdapter",
            "xUnit.core", "FluentAssertions.Testing", "MyTestHelper",
            "Xunit", "XUNIT.Runner", "nunit.testing.extensions");
    }

    /// <summary>
    /// Generates package names that do NOT contain "Test" or "xunit".
    /// </summary>
    private static Gen<string> GenNonTestPackageName()
    {
        return Gen.Elements(
            "Newtonsoft.Json", "System.CommandLine", "Serilog",
            "AutoMapper", "FluentValidation", "MediatR",
            "Polly", "Dapper", "Npgsql", "StackExchange.Redis",
            "Microsoft.Extensions.Logging", "Swashbuckle.AspNetCore",
            "Moq", "BenchmarkDotNet", "Humanizer");
    }

    /// <summary>
    /// Generates a package list that contains at least one test framework package.
    /// </summary>
    private static Gen<ImmutableArray<NuGetPackageReference>> GenPackagesWithTestFramework()
    {
        return from testPkg in GenTestFrameworkPackageName()
               from extraCount in Gen.Choose(0, 3)
               from extras in Gen.ArrayOf(GenNonTestPackageName(), extraCount)
               let allPkgs = extras.Select(n => new NuGetPackageReference(n, "1.0.0"))
                   .Prepend(new NuGetPackageReference(testPkg, "1.0.0"))
                   .ToImmutableArray()
               select allPkgs;
    }

    /// <summary>
    /// Generates a package list that does NOT contain any test framework package.
    /// </summary>
    private static Gen<ImmutableArray<NuGetPackageReference>> GenPackagesWithoutTestFramework()
    {
        return from count in Gen.Choose(0, 4)
               from names in Gen.ArrayOf(GenNonTestPackageName(), count)
               select names.Select(n => new NuGetPackageReference(n, "1.0.0"))
                   .ToImmutableArray();
    }

    // ─── Scenario model ──────────────────────────────────────────────────

    /// <summary>
    /// Represents a test scenario combining source content and package list.
    /// </summary>
    private record Scenario(
        string SourceContent,
        ImmutableArray<NuGetPackageReference> Packages,
        bool ExpectTestAttributes,
        bool ExpectTestFrameworkPackage)
    {
        public bool ExpectWarning => ExpectTestAttributes && !ExpectTestFrameworkPackage;
    }

    /// <summary>
    /// Generates scenarios covering all four quadrants:
    /// (has test attrs, has framework pkg) => no warning
    /// (has test attrs, no framework pkg) => WARNING
    /// (no test attrs, has framework pkg) => no warning
    /// (no test attrs, no framework pkg) => no warning
    /// </summary>
    private static Gen<Scenario> GenScenario()
    {
        // Quadrant 1: test attrs + framework pkg => no warning
        var q1 = from source in GenSourceWithAttribute(GenTestAttributeName())
                 from packages in GenPackagesWithTestFramework()
                 select new Scenario(source, packages, true, true);

        // Quadrant 2: test attrs + no framework pkg => WARNING
        var q2 = from source in GenSourceWithAttribute(GenTestAttributeName())
                 from packages in GenPackagesWithoutTestFramework()
                 select new Scenario(source, packages, true, false);

        // Quadrant 3: no test attrs + framework pkg => no warning
        var q3 = from source in GenSourceWithAttribute(GenNonTestAttributeName())
                 from packages in GenPackagesWithTestFramework()
                 select new Scenario(source, packages, false, true);

        // Quadrant 4: no test attrs + no framework pkg => no warning
        var q4 = from source in GenSourceWithAttribute(GenNonTestAttributeName())
                 from packages in GenPackagesWithoutTestFramework()
                 select new Scenario(source, packages, false, false);

        return Gen.Frequency(
            (3, q1),
            (3, q2),
            (2, q3),
            (2, q4));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property tests
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 5: Test Package Warning Detection.
    /// For any combination of method attribute names and package lists, the warning
    /// presence/absence matches the expected condition:
    /// - Warning emitted when test-attributed methods exist AND no test framework package
    /// - No warning when no test-attributed methods exist OR test framework packages present
    /// </summary>
    [Fact]
    public void Warning_PresenceMatchesExpectedCondition()
    {
        var samples = GenScenario().Sample(200).ToList();

        for(var i = 0; i < samples.Count; i++)
        {
            var scenario = samples[i];
            var actualWarning = TestFrameworkDetection.ShouldWarn([scenario.SourceContent], scenario.Packages);

            if(actualWarning != scenario.ExpectWarning)
            {
                var packageNames = scenario.Packages.IsDefaultOrEmpty
                    ? "(empty)"
                    : string.Join(", ", scenario.Packages.Select(p => p.Name));

                throw new Exception(
                    $"Property failed on sample {i}/{samples.Count}.\n" +
                    $"  Source snippet: {scenario.SourceContent.ReplaceLineEndings(" | ")}\n" +
                    $"  Packages: [{packageNames}]\n" +
                    $"  Expected test attributes: {scenario.ExpectTestAttributes}\n" +
                    $"  Expected test framework pkg: {scenario.ExpectTestFrameworkPackage}\n" +
                    $"  Expected warning: {scenario.ExpectWarning}\n" +
                    $"  Actual warning: {actualWarning}");
            }
        }
    }

    /// <summary>
    /// Property 5 (regex sub-property): Test attribute names ending in the expected
    /// suffixes always match the regex pattern, regardless of prefix or qualifier.
    /// </summary>
    [Fact]
    public void TestAttributeNames_AlwaysMatchRegex()
    {
        var samples = GenTestAttributeName().Sample(100).ToList();

        for(var i = 0; i < samples.Count; i++)
        {
            var attrName = samples[i];
            var source = $"[{attrName}]";
            var matches = TestFrameworkDetection.HasTestAttributes(source);

            if(!matches)
            {
                throw new Exception(
                    $"Property failed on sample {i}/{samples.Count}.\n" +
                    $"  Attribute name: \"{attrName}\"\n" +
                    $"  Source: \"{source}\"\n" +
                    $"  Expected regex match but got no match.");
            }
        }
    }

    /// <summary>
    /// Property 5 (regex sub-property): Non-test attribute names never match
    /// the test attribute regex pattern.
    /// </summary>
    [Fact]
    public void NonTestAttributeNames_NeverMatchRegex()
    {
        var samples = GenNonTestAttributeName().Sample(100).ToList();

        for(var i = 0; i < samples.Count; i++)
        {
            var attrName = samples[i];
            var source = $"[{attrName}]";
            var matches = TestFrameworkDetection.HasTestAttributes(source);

            if(matches)
            {
                throw new Exception(
                    $"Property failed on sample {i}/{samples.Count}.\n" +
                    $"  Attribute name: \"{attrName}\"\n" +
                    $"  Source: \"{source}\"\n" +
                    $"  Expected no regex match but got a match.");
            }
        }
    }

    /// <summary>
    /// Property 5 (package sub-property): Package lists with at least one
    /// test framework package are always detected as having a framework.
    /// </summary>
    [Fact]
    public void PackagesWithTestFramework_AlwaysDetected()
    {
        var samples = GenPackagesWithTestFramework().Sample(100).ToList();

        for(var i = 0; i < samples.Count; i++)
        {
            var packages = samples[i];
            var detected = TestFrameworkDetection.HasFrameworkPackage(packages);

            if(!detected)
            {
                var names = string.Join(", ", packages.Select(p => p.Name));
                throw new Exception(
                    $"Property failed on sample {i}/{samples.Count}.\n" +
                    $"  Packages: [{names}]\n" +
                    $"  Expected test framework detected but was not.");
            }
        }
    }

    /// <summary>
    /// Property 5 (package sub-property): Package lists without any test
    /// framework package are never falsely detected.
    /// </summary>
    [Fact]
    public void PackagesWithoutTestFramework_NeverDetected()
    {
        var samples = GenPackagesWithoutTestFramework().Sample(100).ToList();

        for(var i = 0; i < samples.Count; i++)
        {
            var packages = samples[i];
            var detected = TestFrameworkDetection.HasFrameworkPackage(packages);

            if(detected)
            {
                var names = packages.IsDefaultOrEmpty
                    ? "(empty)"
                    : string.Join(", ", packages.Select(p => p.Name));
                throw new Exception(
                    $"Property failed on sample {i}/{samples.Count}.\n" +
                    $"  Packages: [{names}]\n" +
                    $"  Expected no test framework detected but was.");
            }
        }
    }
}