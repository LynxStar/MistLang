// Tier 3 (Component): `void` in pattern position on the experimental pipeline. Against an optional it asks
// whether the value is absent, which C# spells as a null test whichever way the optional is represented; a
// carrier union keeps its tag test, and the literal entering a carrier is the unit value, `null` binding to
// whichever reference constituent's conversion C# finds first.

using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class VoidPatternEmissionTests
{
    private const string Types = """
        public struct Point
        {
            public int X;

            public Point(int x) { this.X = x; }
        }

        public class Node
        {
            public Node? Next;
            public int? Weight;

            public Node() { }
        }

        public class Slot<K> where K : struct
        {
            public static bool IsAbsent(K? value) { return value is void; }
        }
        """;

    [Theory]
    [InlineData("int? number = void; var absent = number is void;", "bool absent = number is null;")]
    [InlineData("int? number = void; var present = number is not void;", "bool present = number is not null;")]
    [InlineData("string? text = void; var absent = text is void;", "bool absent = text is null;")]
    [InlineData("Point? point = void; var absent = point is void;", "bool absent = point is null;")]
    [InlineData("Node? node = void; var absent = node is void;", "bool absent = node is null;")]
    [InlineData("int? number = void; var shown = number switch { void => 0, int held => held };", "number switch { null => 0, int held => held }")]
    [InlineData("var node = new Node(); var last = node is { Next: void, Weight: not void };", "node is { Next: null, Weight: not null }")]
    public void VoidTestedAgainstAnOptional_AsksWhetherTheValueIsAbsent(string statements, string expected)
    {
        Assert.Contains(expected, EmitRun("Probe", statements));
    }

    [Fact]
    public void VoidTestedAgainstAnOptionalOverATypeParameter_IsTheSameNullTest()
    {
        Assert.Contains("return value is null;", EmitRun("Slot<>", ""));
    }

    [Theory]
    [InlineData("int | string | void held = void;", "Union_Int32_String_Void held = Standard.Void.Instance;")]
    [InlineData("int | string | void held = 3; var absent = held is void;", "bool absent = held.AsVoidOrNull is Standard.Void;")]
    [InlineData("int? number = void;", "int? number = null;")]
    public void ACarrierUnion_KeepsItsTagTestAndTakesTheUnitValue(string statements, string expected)
    {
        Assert.Contains(expected, EmitRun("Probe", statements));
    }

    [Fact]
    public void VoidTestedAgainstAValueOfNoUnion_FindsAnAbsentOptionalOrTheUnitValue()
    {
        Assert.Contains(
            "bool absent = widened is (null or Standard.Void);",
            EmitRun("Probe", "int? number = void; unknown widened = number; var absent = widened is void;"));
    }

    private static string EmitRun(string typeName, string statements)
    {
        var model = StandardModelProbe.Build(statements, out var buildDiagnostics, Types);
        Assert.Empty(buildDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var type = Assert.IsAssignableFrom<MemberType>(model.TypesByFQN[$"Probe.{typeName}"]);
        var diagnostics = new DiagnosticBag();
        var writer = new CSharpWriter();

        BodyEmitter.ResetTemporaries();
        TypeEmitter.EmitTypeBody(type, ConstraintClosureMap.Build(model, diagnostics), model.VariadicUses, writer, diagnostics);

        Assert.Empty(diagnostics.ToImmutableArray().Where(d => d.Severity == DiagnosticSeverity.Error));

        return string.Join("\n", writer.ToString().ReplaceLineEndings("\n").Split('\n').Select(line => line.Trim()));
    }
}