using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

// Tier 3: the projections a spread member's declaration emits. A field is assigned through, a property
// hands back a copy so an element is written back whole, and a struct absorbing a variadic base carries
// the base's projections itself, C# structs having no base to inherit them from. Asserted on the emitted
// text, since an element assigned through a property's copy and a projection missing from the struct
// both fail only in Roslyn.
[Trait("Tier", "3")]
public sealed class SpreadProjectionEmissionTests : ProgramModelProbe
{
    public SpreadProjectionEmissionTests()
        : base("MistSpreadProjection_")
    {
    }

    protected override string Fixtures { get; } = """
        public class Row<params Ts>
        {
            public Ts... Item;
            public Ts... Settable { get; set; }
            public Ts... Kept { get; private set; }
            public Ts... Fixed { get; }

            public Row(params Ts values)
            {
                Item... = values...;
                Settable... = values...;
                Kept... = values...;
                Fixed... = values...;
            }
        }

        public class Sub : Row<int, string>
        {
            public Sub() : base(1, "s") { }
        }

        public struct Pair<Type[2]:Ts>
        {
            public Ts... Item;
            public Ts... Slot { get; }
        }

        public struct Flat : Pair<int, string>
        {
            public int Extra;
        }
        """;

    protected override string Preamble { get; } = "var row = new Row<int, string>(1, \"a\"); var flat = new Flat();";

    [Theory]
    [InlineData("public T1 Item1 { get => Item.Item1; set => Item.Item1 = value; }")]
    [InlineData("public T2 Settable2 { get => Settable.Item2; set { var aggregate = Settable; aggregate.Item2 = value; Settable = aggregate; } }")]
    [InlineData("public T1 Kept1 { get => Kept.Item1; private set { var aggregate = Kept; aggregate.Item1 = value; Kept = aggregate; } }")]
    [InlineData("public T2 Fixed2 { get => Fixed.Item2; }")]
    public void AProjectionWritesWhereItsMemberCan_AndAPropertysBackWhole(string expected)
    {
        Assert.Contains(expected, Lines(Emit("Row", width: 2)));
    }

    [Theory]
    [InlineData("public int Item1 { get => Item.Item1; set => Item.Item1 = value; }")]
    [InlineData("public string Slot2 { get => Slot.Item2; private set { var aggregate = Slot; aggregate.Item2 = value; Slot = aggregate; } }")]
    public void AStructAbsorbingAVariadicBase_CarriesItsProjections(string expected)
    {
        Assert.Contains(expected, Lines(Emit("Flat", width: null)));
    }

    [Fact]
    public void AClassDerivingAVariadicBase_DeclaresNoneOfItsOwn()
    {
        Assert.DoesNotContain(Lines(Emit("Sub", width: null)), line => line.Contains("Item1", StringComparison.Ordinal));
    }

    private string Emit(string typeName, int? width)
    {
        var model = BuildModel(statement: "", extraTypes: "", extraPreamble: "", out var built);
        Assert.Empty(built.Where(d => d.Severity == DiagnosticSeverity.Error));

        var diagnostics = new DiagnosticBag();
        var writer = new CSharpWriter();
        var declared = (MemberType)model.TypesByFQN.Single(entry => MistType.StripArity(entry.Key) == $"Probe.{typeName}").Value;

        using(width is { } fixedWidth ? Monomorph.For(declared.TypeParametersList, [fixedWidth])?.Enter() : null)
        {
            TypeEmitter.EmitTypeBody(
                declared, ConstraintClosureMap.Build(model, diagnostics), model.VariadicUses, writer, diagnostics);
        }

        Assert.Empty(diagnostics.ToImmutableArray().Where(d => d.Severity == DiagnosticSeverity.Error));

        return writer.ToString();
    }

    private static List<string> Lines(string emitted)
    {
        return [.. emitted.Split('\n').Select(line => line.Trim())];
    }
}