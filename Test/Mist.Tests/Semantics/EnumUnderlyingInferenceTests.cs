// Tier 3 (Component): the base an enum without an annotation takes, asserted on EnumType.UnderlyingType over
// a real ProgramModelBuilder run. No sample holds these shapes: samples/Enums annotates every enum it labels
// as inferred, and a sample can show a base only through what converts into it.

using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.ProgramModel;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

public sealed class EnumUnderlyingInferenceTests : ProgramModelProbe
{
    public EnumUnderlyingInferenceTests()
        : base("MistEnumUnderlying_")
    {
    }

    protected override string Fixtures { get; } = """
        public enum Other : int { One = 1 }

        public enum Code { Ok = 200 }

        public class Fathoms
        {
            public Fathoms() { }

            public static explicit operator Fathoms(double depth) { return new Fathoms(); }

            public static explicit operator Fathoms(string depth) { return new Fathoms(); }
        }
        """;

    protected override string Preamble { get; } = "";

    [Theory]
    [InlineData("enum E { A = 200, B = 1000 }", "int")]
    [InlineData("enum E { A = -10, B = 40 }", "int")]
    [InlineData("enum E { A = \"N\", B = \"S\" }", "string")]
    [InlineData("enum E { A = 0.5, B = 1.0 }", "double")]
    [InlineData("enum E { A = true, B = false }", "bool")]
    [InlineData("enum E { A = Other.One }", "Other")]
    public void AnEnumWhoseValuesShareOneType_TakesThatType(string declaration, string expected)
    {
        Assert.Equal(expected, UnderlyingOf(declaration));
    }

    [Theory]
    [InlineData("enum E { A = 1, B = \"x\" }", "int | string")]
    [InlineData("enum E { A = \"x\", B = 1, C = 2 }", "int | string")]
    [InlineData("enum E { A = 1, B = 3000000000L }", "int | long")]
    public void AnEnumWhoseValuesDifferInType_TakesTheUnionOfThem(string declaration, string expected)
    {
        Assert.Equal(expected, UnderlyingOf(declaration));
    }

    [Theory]
    [InlineData("enum E { A, B }", "byte")]
    [InlineData("enum E : long { A = 1, B = 2 }", "long")]
    [InlineData("enum E : int | string { A = 1 }", "int | string")]
    public void AnEnumWithNoWrittenValueOrWithAnAnnotation_TakesNothingFromItsValues(string declaration, string expected)
    {
        Assert.Equal(expected, UnderlyingOf(declaration));
    }

    [Theory]
    [InlineData("enum E { A = 200, B }", "int", 1, 201)]
    [InlineData("enum E { A, B = 20 }", "int", 0, 0)]
    [InlineData("enum E { A = \"N\", B }", "string", 1, "B")]
    public void AnOmittedValue_IsInferredOverTheBaseTheWrittenValuesDecide(
        string declaration, string expected, int omitted, object value)
    {
        var enumType = EnumOf(declaration, out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Equal(expected, TypeDisplay.Mist(enumType.UnderlyingType!));
        Assert.Equal(value, Assert.IsType<LiteralExpression>(enumType.MembersList[omitted].Value).Value);
    }

    [Fact]
    public void AnInferredBase_IsAlreadyKnownWhenABodyChoosesAnOperatorThroughIt()
    {
        var cast = InitializerOf<CastExpression>("var f = (Fathoms)Code.Ok;");

        Assert.Equal(WellKnownTypes.Double.FullyQualifiedName, cast.Operator?.SourceParameter.Type.FullyQualifiedName);
    }

    private string UnderlyingOf(string declaration)
    {
        var enumType = EnumOf(declaration, out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        return TypeDisplay.Mist(enumType.UnderlyingType!);
    }

    private EnumType EnumOf(string declaration, out ImmutableArray<Diagnostic> diagnostics)
    {
        var model = BuildModel(statement: "", extraTypes: $"public {declaration}", extraPreamble: "", out diagnostics);

        return Assert.IsType<EnumType>(model.TypesByFQN["Probe.E"]);
    }
}