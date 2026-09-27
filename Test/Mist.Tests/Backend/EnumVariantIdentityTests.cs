using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

/// <summary>
/// Which of an enum's two emitted lookup chains reads identity and which reads the value.
/// <para>
/// THE SAMPLE WITNESSES THE NAMES BUT NOT THE PAIRING. AllFeaturesV2 goes red if ToString stops
/// naming the right variant, so that half is guarded; nothing there can tell whether the conversion
/// operator still matches on the VALUE, which it must -- it is handed a value and has nothing else
/// to go on. Getting that one wrong the other way would make every conversion return null, and the
/// two chains sit four lines apart.
/// </para>
/// </summary>
public sealed class EnumVariantIdentityTests : ProgramModelProbe
{
    public EnumVariantIdentityTests()
        : base("MistEnumIdentity_")
    {
    }

    protected override string Fixtures { get; } = """
        public readonly struct Discarding
        {
            public Discarding(int x)
            {
            }
        }

        public enum Corner : Discarding
        {
            First = new Discarding(1),
            Second = new Discarding(2)
        }

        public enum Rank : int
        {
            Low = 1,
            High = 2
        }
        """;

    protected override string Preamble { get; } = "";

    private string Emit(string enumName)
    {
        var model = BuildModel(statement: "", extraTypes: "", extraPreamble: "", out var buildDiagnostics);
        Assert.Empty(buildDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var type = Assert.IsType<EnumType>(model.TypesByFQN[$"Probe.{enumName}"]);
        var emitDiagnostics = new DiagnosticBag();
        var writer = new CSharpWriter();

        EnumEmitter.Emit(type, writer, emitDiagnostics);

        Assert.Empty(emitDiagnostics.ToImmutableArray().Where(d => d.Severity == DiagnosticSeverity.Error));

        return writer.ToString();
    }

    /// <summary>
    /// Sliced to the terminating fallback rather than to the next return, because the chain this is
    /// about is made OF returns.
    /// </summary>
    private static string Section(string emitted, string header, string terminator)
    {
        var start = emitted.IndexOf(header, StringComparison.Ordinal);
        Assert.True(start >= 0, $"'{header}' is not in the emitted enum:\n{emitted}");

        var end = emitted.IndexOf(terminator, start + header.Length, StringComparison.Ordinal);
        Assert.True(end >= 0, $"'{header}' does not reach '{terminator}':\n{emitted}");

        return emitted[start..end];
    }

    private static string ToStringBody(string emitted)
    {
        return Section(emitted, "public override string ToString()", "return Value.ToString(");
    }

    private static string ConversionBody(string emitted)
    {
        return Section(emitted, "public static explicit operator", "return null;");
    }

    [Theory]
    [InlineData("Corner", "First")]
    [InlineData("Corner", "Second")]
    [InlineData("Rank", "Low")]
    [InlineData("Rank", "High")]
    public void AVariantIsNamedByWhichSingletonItIs(string enumName, string variant)
    {
        Assert.Contains(
            $"ReferenceEquals(this, {enumName}.{variant})) return \"{variant}\";",
            ToStringBody(Emit(enumName)));
    }

    /// <summary>
    /// Two variants of Corner hold equal values, because Discarding keeps nothing it is constructed
    /// with. Reading the value to pick a name answers First for both, which is the whole defect this
    /// pins -- and it is invisible unless a base type throws its arguments away, since Mist otherwise
    /// refuses two variants with the same value outright.
    /// </summary>
    [Fact]
    public void NamingNeverConsultsTheValue()
    {
        Assert.DoesNotContain("Equals(Value,", ToStringBody(Emit("Corner")));
        Assert.DoesNotContain("Equals(Value,", ToStringBody(Emit("Rank")));
    }

    [Theory]
    [InlineData("Corner", "First")]
    [InlineData("Rank", "Low")]
    public void ConvertingFromTheUnderlyingTypeStillReadsTheValue(string enumName, string variant)
    {
        Assert.Contains(
            $"if(Equals(value, {enumName}.{variant}.Value)) return {enumName}.{variant};",
            ConversionBody(Emit(enumName)));
    }
}