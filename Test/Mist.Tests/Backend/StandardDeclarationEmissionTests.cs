// Tier 3 (Component): which Standard declarations the backend supplies rather than emits. The rule is
// per declaration (order 186 of backlog/emitter-v2-capability-progression.yaml), and Void is its one
// non-intrinsic case: its C# shape needs members Mist cannot spell, so the stand-in is held here to every
// member Void.mist declares, which is what keeps the two from drifting apart silently.

using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
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
    public void AnOrdinaryMemberOfAnIntrinsicTypeIsRefusedRatherThanDropped()
    {
        var model = BuildModel(statement: "", extraTypes: "", extraPreamble: "", out _);
        var int32 = Assert.IsAssignableFrom<ConcreteType>(model.TypesByFQN["Standard.Int32"]);
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();
        var anyEmitted = false;

        int32.MethodsList.Add(new Method("Twice", int32, [], new MemberModifiers { Accessibility = Accessibility.Public }));
        TypeEmitter.Emit(int32, ConstraintClosureMap.Build(model, diagnostics), model.VariadicUses, writer, diagnostics, ref anyEmitted);

        var refusal = Assert.Single(diagnostics.ToImmutableArray(), d => d.Id == BackendDiagnostics.IntrinsicTypeOrdinaryMember.Id);
        Assert.Contains("'Int32' declares 'Twice'", refusal.Message);
        Assert.False(anyEmitted);
        Assert.Equal("", writer.ToString());
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