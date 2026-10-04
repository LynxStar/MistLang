// Tier 3 (Component): which Standard declarations the backend supplies rather than emits. The rule
// is per declaration, and Void is the non-intrinsic case with a stand-in: its C# shape needs members
// Mist cannot spell, so it is held here to every member Void.mist declares, which is what keeps the
// two from drifting apart silently.

using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class StandardDeclarationEmissionTests : ProgramModelProbe
{
    public StandardDeclarationEmissionTests()
        : base("MistStandardEmission_")
    {
    }

    protected override string Fixtures { get; } = "";

    protected override string Preamble { get; } = "";

    [Fact]
    public void AnIntrinsicTypeEmitsNothing()
    {
        var model = BuildModel(statement: "", extraTypes: "", extraPreamble: "", out _);
        var int32 = Assert.IsAssignableFrom<ConcreteType>(model.TypesByFQN["Standard.Int32"]);
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();
        var anyEmitted = false;

        Assert.True(int32.IsIntrinsic);
        Assert.False(TypeEmitter.ProducesDeclaration(int32));

        TypeEmitter.Emit(int32, ConstraintClosureMap.Build(model, diagnostics), model.VariadicUses, writer, diagnostics, ref anyEmitted);

        Assert.False(anyEmitted);
        Assert.Equal("", writer.ToString());
    }

    [Fact]
    public void ACarriedBodyNamesTheReceiverWhereItWroteThisOrNothing()
    {
        var model = BuildModel(
            statement: "",
            extraTypes: """
                public struct Widget
                {
                    public int Value;
                    public bool IsEven() { return Value % 2 == 0; }
                    public bool IsOdd() { return !IsEven(); }
                    public Widget Same() { return this; }
                    public bool Negative { get { return Value < 0; } }
                    public bool Positive { get { return !Negative; } }
                    public static int Zero() { return 0; }
                    public static int One() { return Zero() + 1; }
                }
                """,
            extraPreamble: "",
            out _);
        var widget = Assert.IsAssignableFrom<ConcreteType>(model.TypesByFQN["Probe.Widget"]);
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();

        IntrinsicMemberEmitter.Emit([widget], writer, diagnostics);
        var carrier = writer.ToString();

        Assert.Empty(diagnostics.ToImmutableArray());
        AssertCarries(carrier, "extension(Probe.Widget @this)");
        AssertCarries(carrier, "return @this.Value % 2 == 0;");
        AssertCarries(carrier, "return !@this.IsEven();");
        AssertCarries(carrier, "return @this;");
        AssertCarries(carrier, "return !@this.Negative;");
        AssertCarries(carrier, "extension(Probe.Widget)");
        AssertCarries(carrier, "public static int One()");
        AssertCarries(carrier, "return Probe.Widget.Zero() + 1;");
    }

    internal static void AssertCarries(string carrier, string expected)
    {
        Assert.True(carrier.Contains(expected, StringComparison.Ordinal), $"expected '{expected}' in:\n{carrier}");
    }

    [Fact]
    public void VoidIsSuppliedByTheBackend()
    {
        var model = BuildModel(statement: "", extraTypes: "", extraPreamble: "", out _);

        Assert.False(TypeEmitter.ProducesDeclaration(Assert.IsAssignableFrom<DeclaredType>(model.TypesByFQN["Standard.Void"])));
    }

    [Fact]
    public void TheVoidStandInCarriesEveryMemberVoidMistDeclares()
    {
        var source = File.ReadAllText(Path.Combine(StdlibTestHelper.FindRepoRoot(), "Source", "Standard", "Void.mist"));
        var model = Build($"using Standard;\nnamespace Probe;\n{source}", out _);
        var declared = Assert.IsType<StructType>(model.TypesByFQN["Probe.Void"]);
        var writer = new CSharpWriter();

        VoidStandIn.Emit(writer);
        var standIn = writer.ToString();

        Assert.All(declared.PropertiesList, property => Assert.Contains($" {property.Name} ", standIn));
        Assert.All(declared.MethodsList, method => Assert.Contains($" {method.Name}(", standIn));
        Assert.All(declared.OperatorsList, op => Assert.Contains($"operator {op.Symbol}(Void ", standIn));
        Assert.All(
            declared.CastOperatorsList,
            cast => Assert.Contains($"{(cast.IsImplicit ? "implicit" : "explicit")} operator System.{cast.TargetType.Name}(Void ", standIn));
        Assert.All(declared.ConstructorsList, constructor => Assert.Empty(constructor.ParametersList));
        Assert.NotEmpty(declared.OperatorsList);
    }
}