using Mist.Backends.CSharpTranspiler;
using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.Common.Diagnostics;
using Mist.Tests.InteropFixtures;

namespace Mist.Tests.Backend;

[Trait("Tier", "3")]
public sealed class TypeInspectorTests(TypeInspectorProjectionFixture projection)
    : IClassFixture<TypeInspectorProjectionFixture>
{
    private const string Fixtures = "Mist.Tests.InteropFixtures";

    [Fact]
    public void Class_ReportsWhatTheProjectionExposesAndWhatItWithholds()
    {
        var lines = Report($"{Fixtures}.{nameof(Inspected)}");

        Assert.Equal($"{Fixtures}.{nameof(Inspected)} (class)", lines[0]);
        Assert.Contains("    method Write(string) -> void", lines);
        Assert.Contains($"    method Tally() -> int [from {Fixtures}.{nameof(InspectionBase)}]", lines);
        Assert.Contains($"    method Duplicate() -> InspectionBase [protected] [from {Fixtures}.{nameof(InspectionBase)}]", lines);
        Assert.Contains("    Changed: HIDDEN: event", lines);
        Assert.Contains("    Finalize: HIDDEN: finalizer", lines);
    }

    [Fact]
    public void Codec_WithholdsItsPointerOverloads_NamingTheType_AndKeepsTheOthers()
    {
        var lines = Report($"{Fixtures}.{nameof(Codec)}");

        Assert.Contains("    method GetBytes(string) -> byte[]", lines);
        Assert.Contains("    GetBytes: HIDDEN: signature uses System.Char*", lines);
    }

    [Fact]
    public void Bag_ReportsItsIndexerAsMistDeclaresOne_AndWithholdsTheExplicitInterfaceOne()
    {
        var lines = Report($"{Fixtures}.Bag`1");

        Assert.Contains("    indexer this[int]: T", lines);
        Assert.Contains($"    {Fixtures}.{nameof(IKeyed)}.Item: HIDDEN: non-public", lines);
    }

    [Fact]
    public void Enum_WithholdsWhatItsProjectionDoesNotInherit()
    {
        var lines = Report($"{Fixtures}.{nameof(SimpleEnum)}");

        Assert.Equal($"{Fixtures}.{nameof(SimpleEnum)} (enum)", lines[0]);
        Assert.DoesNotContain("  VISIBLE:", lines);
        Assert.Contains("    HasFlag: HIDDEN: not inherited in Mist [from System.Enum]", lines);
    }

    [Fact]
    public void UnresolvableName_ExitsNonZeroNamingIt()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        Assert.Equal(1, TypeInspector.Report(projection.Provider, "Nonexistent.Bogus.TypeThatDoesNotExist", output, error));
        Assert.Empty(output.ToString());
        Assert.Contains("'Nonexistent.Bogus.TypeThatDoesNotExist'", error.ToString());
    }

    private string[] Report(string typeName)
    {
        var output = new StringWriter();
        var error = new StringWriter();

        Assert.Equal(0, TypeInspector.Report(projection.Provider, typeName, output, error));
        Assert.Empty(error.ToString());
        return output.ToString().Split(Environment.NewLine);
    }
}

public sealed class TypeInspectorProjectionFixture : IDisposable
{
    public TypeInspectorProjectionFixture()
    {
        var diagnostics = new DiagnosticBag();
        Provider = AmbientProviderTestHelper.ProviderOver(diagnostics, typeof(Inspected).Assembly.Location);

        if(!TypeInspector.ProjectBesideStandard(Provider, diagnostics))
        {
            Provider.Dispose();
            throw new InvalidOperationException(string.Join(Environment.NewLine, diagnostics.ToImmutableArray().Select(DiagnosticFormatter.Format)));
        }
    }

    internal AmbientTypeProviderV2 Provider { get; }

    public void Dispose()
    {
        Provider.Dispose();
    }
}