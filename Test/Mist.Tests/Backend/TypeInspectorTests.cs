using Mist.Backends.CSharpTranspiler;
using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.Common.Diagnostics;

namespace Mist.Tests.Backend;

[Trait("Tier", "3")]
public sealed class TypeInspectorTests(TypeInspectorProjectionFixture projection)
    : IClassFixture<TypeInspectorProjectionFixture>
{
    [Fact]
    public void Console_ReportsWhatTheProjectionExposesAndWhatItWithholds()
    {
        var lines = Report("System.Console");

        Assert.Equal("System.Console (class)", lines[0]);
        Assert.Contains("    method WriteLine(string) -> void", lines);
        Assert.Contains("    method GetHashCode() -> int [from System.Object]", lines);
        Assert.Contains(lines, line => line.StartsWith("    method MemberwiseClone() -> ", StringComparison.Ordinal)
            && line.EndsWith(" [protected] [from System.Object]", StringComparison.Ordinal));
        Assert.Contains("    CancelKeyPress: HIDDEN: event", lines);
        Assert.Contains("    Finalize: HIDDEN: finalizer [from System.Object]", lines);
    }

    [Fact]
    public void Encoding_WithholdsItsPointerOverloads_NamingTheType_AndKeepsTheOthers()
    {
        var lines = Report("System.Text.Encoding");

        Assert.Contains("    method GetBytes(string) -> byte[]", lines);
        Assert.Contains("    GetBytes: HIDDEN: signature uses System.Char*", lines);
    }

    [Fact]
    public void Enum_WithholdsWhatItsProjectionDoesNotInherit()
    {
        var lines = Report("System.DayOfWeek");

        Assert.Equal("System.DayOfWeek (enum)", lines[0]);
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
        Provider = AmbientTypeProviderV2Factory.Create("net10.0", [], diagnostics);

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