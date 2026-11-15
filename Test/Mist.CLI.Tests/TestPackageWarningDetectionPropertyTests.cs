using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using Mist.Driver.Contracts;

namespace Mist.CLI.Tests;

[Trait("Tier", "1")]
public sealed class TestPackageWarningDetectionPropertyTests
{
    private static Gen<string> GenTestAttributeName()
    {
        var suffixes = new[] { "Fact", "Theory", "Test", "TestMethod" };
        var prefixes = new[] { "", "My", "Xunit.", "NUnit.Framework.", "Custom.Namespace." };

        return from prefix in Gen.Elements(prefixes)
               from suffix in Gen.Elements(suffixes)
               select prefix + suffix;
    }

    private static Gen<string> GenNonTestAttributeName()
    {
        return Gen.Elements(
            "Serializable", "Obsolete", "DataContract", "JsonProperty",
            "Range", "Required", "Display", "Authorize",
            "HttpGet", "HttpPost", "Route", "ApiController",
            "DebuggerDisplay", "Conditional", "Flags", "DllImport",
            "MyCustom", "Facts", "Testing", "Theoretical");
    }

    private static Gen<string> GenSourceWithAttribute(Gen<string> attributeNameGen)
    {
        return from attrName in attributeNameGen
               from hasArgs in Gen.Elements(true, false)
               let attrSyntax = hasArgs ? $"[{attrName}(1, 2, 3)]" : $"[{attrName}]"
               select $"class MyTests\n{{\n    {attrSyntax}\n    public void SomeMethod()\n    {{\n    }}\n}}";
    }

    private static Gen<string> GenTestFrameworkPackageName()
    {
        return Gen.Elements(
            "xunit", "xunit.runner.visualstudio", "Microsoft.NET.Test.Sdk",
            "NUnit.Test", "MSTest.TestFramework", "MSTest.TestAdapter",
            "xUnit.core", "FluentAssertions.Testing", "MyTestHelper",
            "Xunit", "XUNIT.Runner", "nunit.testing.extensions");
    }

    private static Gen<string> GenNonTestPackageName()
    {
        return Gen.Elements(
            "Newtonsoft.Json", "System.CommandLine", "Serilog",
            "AutoMapper", "FluentValidation", "MediatR",
            "Polly", "Dapper", "Npgsql", "StackExchange.Redis",
            "Microsoft.Extensions.Logging", "Swashbuckle.AspNetCore",
            "Moq", "BenchmarkDotNet", "Humanizer");
    }

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

    private static Gen<ImmutableArray<NuGetPackageReference>> GenPackagesWithoutTestFramework()
    {
        return from count in Gen.Choose(0, 4)
               from names in Gen.ArrayOf(GenNonTestPackageName(), count)
               select names.Select(n => new NuGetPackageReference(n, "1.0.0"))
                   .ToImmutableArray();
    }

    private record Scenario(
        string SourceContent,
        ImmutableArray<NuGetPackageReference> Packages,
        bool ExpectTestAttributes,
        bool ExpectTestFrameworkPackage)
    {
        public bool ExpectWarning => ExpectTestAttributes && !ExpectTestFrameworkPackage;
    }

    private static Gen<Scenario> GenScenario()
    {
        var q1 = from source in GenSourceWithAttribute(GenTestAttributeName())
                 from packages in GenPackagesWithTestFramework()
                 select new Scenario(source, packages, true, true);

        var q2 = from source in GenSourceWithAttribute(GenTestAttributeName())
                 from packages in GenPackagesWithoutTestFramework()
                 select new Scenario(source, packages, true, false);

        var q3 = from source in GenSourceWithAttribute(GenNonTestAttributeName())
                 from packages in GenPackagesWithTestFramework()
                 select new Scenario(source, packages, false, true);

        var q4 = from source in GenSourceWithAttribute(GenNonTestAttributeName())
                 from packages in GenPackagesWithoutTestFramework()
                 select new Scenario(source, packages, false, false);

        return Gen.Frequency(
            (3, q1),
            (3, q2),
            (2, q3),
            (2, q4));
    }

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