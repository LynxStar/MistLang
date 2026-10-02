using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

// Tier 3: the C# emitter over a real model, refusing a function its target has no delegate for
// (variadics Requirement 16.2). Nothing is compiled; what is asserted is the diagnostic and the line of
// the Mist source it lands on, counted from the probe's `using Standard;` as line 1.
[Trait("Tier", "3")]
public sealed class DelegateCeilingEmissionTests
{
    private const string Types = """
        public class Bundle<params TArgs>
        {
            public TResult Map<TResult>(Func<TArgs..., TResult> f) { return Map(f); }
        }
        public class Never<params TArgs>
        {
            public int Take(Func<int, int, int, int, int, int, int, int, int, int, int, int, int, int, int, int, int, int> f) { return 0; }
        }
        public class Narrow<params TArgs>
        {
            public int Take(Func<int, int, int, int, int, int, int, int, int, int, int, int, int, int, int, int, int> f) { return 0; }
        }
        """;

    private const string Closings = """
        var sixteen = new Bundle<int, int, int, int, int, int, int, int, int, int, int, int, int, int, int, int>();
        var seventeen = new Bundle<int, int, int, int, int, int, int, int, int, int, int, int, int, int, int, int, int>();
        """;

    private static readonly Lazy<ProgramModel.Model.ProgramModel> Model = new(() => StandardModelProbe.Build(Closings, out _, Types));

    [Fact]
    public void AFunctionWrittenPastTheCeiling_IsRefusedAtItsDeclaration_ThoughNothingInstantiatesIt()
    {
        var diagnostics = new DiagnosticBag();

        using(DelegateCeiling.Enter(Model.Value, diagnostics))
        {
        }

        var refusal = Assert.Single(diagnostics.ToImmutableArray());

        Assert.Equal(("MIST_VAR_0010", PipelineStage.Backend, 11), (refusal.Id.Value, refusal.Stage, refusal.Location.Start.Line));
        Assert.Contains("takes 17 inputs, and System.Func and System.Action hold at most 16", refusal.Message);
    }

    [Fact]
    public void AFunctionAPackWidensPastTheCeiling_IsRefusedAtTheUseThatFixedTheWidth()
    {
        var refusal = Assert.Single(Emit("Bundle", width: 17), diagnostic => diagnostic.Location.Start.Line != 11);

        Assert.Equal(("MIST_VAR_0010", 23), (refusal.Id.Value, refusal.Location.Start.Line));
        Assert.Contains("in 'Bundle.Map', where 'TArgs' holds 17 types, takes 17 inputs", refusal.Message);
    }

    [Fact]
    public void AFunctionRefusedWhereItIsWritten_IsNotRefusedAgainWhereItIsEmitted()
    {
        Assert.Equal(11, Assert.Single(Emit("Never", width: 2)).Location.Start.Line);
    }

    [Theory]
    [InlineData("Bundle", 16)]
    [InlineData("Bundle", 0)]
    [InlineData("Narrow", 17)]
    public void AFunctionWithinTheCeiling_IsEmitted(string typeName, int width)
    {
        Assert.DoesNotContain(Emit(typeName, width), diagnostic => diagnostic.Location.Start.Line != 11);
    }

    private static IReadOnlyList<Diagnostic> Emit(string typeName, int width)
    {
        var model = Model.Value;
        var diagnostics = new DiagnosticBag();
        var anyEmitted = false;
        var declared = model.TypesByFQN.Values.OfType<MemberType>()
            .Single(type => type.NamespaceName == "Probe" && MistType.StripArity(type.Name) == typeName);

        using(DelegateCeiling.Enter(model, diagnostics))
        using(Monomorph.For(declared.TypeParametersList, [width])?.Enter())
        {
            TypeEmitter.Emit(
                declared, ConstraintClosureMap.Build(model, diagnostics), model.VariadicUses, new CSharpWriter(), diagnostics, ref anyEmitted);
        }

        return diagnostics.ToImmutableArray();
    }
}