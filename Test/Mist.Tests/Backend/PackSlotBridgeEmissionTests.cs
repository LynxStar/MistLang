using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

// Tier 3: what a conformer of a pack-typed slot emits beside its own member. Asserted on the emitted
// text, since a conformer left without the interface's tuple-taking signature, or given it twice, fails
// only in Roslyn.
[Trait("Tier", "3")]
public sealed class PackSlotBridgeEmissionTests
{
    private const string Types = """
        public abstract IApplier<params TArgs, TResult> { TResult Apply(params TArgs args); }
        public abstract IHandler<params TArgs> { void Handle(string tag, params TArgs args); }
        public abstract ICounter<params TArgs> { int Count(params TArgs args) { return 7; } }

        public class Adder : IApplier<int, int, int> { public int Apply(int a, int b) { return a + b; } }
        public class Lengther : IApplier<string, int> { public int Apply(string s) { return 0; } }
        public class Constant : IApplier<int> { public int Apply() { return 42; } }
        public class Printer : IHandler<int, bool> { public void Handle(string tag, int a, bool b) { } }
        public class Pre<params TRest> : IApplier<int, TRest..., int> { public int Apply(int first, params TRest rest) { return first; } }
        public class Boxed<params TArgs> : IApplier<TArgs..., int> { public int Apply(params TArgs args) { return 0; } }
        public class Plain : ICounter<int, int> { }
        public class Own : ICounter<int, int> { public int Count(int a, int b) { return a + b; } }
        public class Kept<Type[1]:TRest> : IApplier<int, TRest..., int> { public TRest[0]? Last; public int Apply(int first, params TRest rest) { return first; } }
        """;

    private const string Closings = """
        var pre = new Pre<string, bool>();
        var boxed = new Boxed<int, string>();
        var kept = new Kept<string>();
        """;

    private static readonly Lazy<ProgramModel.Model.ProgramModel> Model = new(() =>
    {
        var model = StandardModelProbe.Build(Closings, out var diagnostics, Types);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        return model;
    });

    [Theory]
    [InlineData("Adder", "int IApplier_2<int, int, int>.Apply((int, int) args) => this.Apply(args.Item1, args.Item2);")]
    [InlineData("Lengther", "int IApplier_1<string, int>.Apply(System.ValueTuple<string> args) => this.Apply(args.Item1);")]
    [InlineData("Constant", "int IApplier_0<int>.Apply(System.ValueTuple args) => this.Apply();")]
    [InlineData("Printer", "void IHandler_2<int, bool>.Handle(string tag, (int, bool) args) => this.Handle(tag, args.Item1, args.Item2);")]
    [InlineData("Pre", "int IApplier_3<int, T1, T2, int>.Apply((int, T1, T2) args) => this.Apply(args.Item1, (args.Item2, args.Item3));")]
    [InlineData("Own", "int ICounter_2<int, int>.Count((int, int) args) => this.Count(args.Item1, args.Item2);")]
    public void AMemberWrittenAtThePacksArity_IsReachedThroughAnExplicitImplementation(string typeName, string bridge)
    {
        _ = Assert.Single(Lines(Emit(typeName)), line => line == bridge);
    }

    [Theory]
    [InlineData("Boxed")]
    [InlineData("Plain")]
    public void AMemberThatAlreadyTakesTheTuple_NeedsNone(string typeName)
    {
        Assert.DoesNotContain(Lines(Emit(typeName)), line => line.Contains("=> this.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Plain", 1)]
    [InlineData("Own", 0)]
    public void ADefaultIsRestatedOnlyWhereTheConformerDidNotWriteItsOwn(string typeName, int restated)
    {
        Assert.Equal(restated, Lines(Emit(typeName)).Count(line => line == "public int Count((int, int) args)"));
    }

    [Fact]
    public void AConformerEmittedPerInstantiation_BridgesThroughThePackItsInstanceCloses()
    {
        var model = Model.Value;
        var diagnostics = new DiagnosticBag();
        var writer = new CSharpWriter();
        using var specialization = UnionSpecialization.Enter(model, diagnostics);
        var declared = model.TypesByFQN.Values.OfType<MemberType>()
            .Single(type => type.NamespaceName == "Probe" && MistType.StripArity(type.Name) == "Kept");

        using(TypeEmitter.Specializing(Assert.Single(UnionSpecialization.InstancesOf(declared))))
        {
            TypeEmitter.EmitTypeBody(
                declared, ConstraintClosureMap.Build(model, diagnostics), model.VariadicUses, writer, diagnostics);
        }

        Assert.Empty(diagnostics.ToImmutableArray().Where(d => d.Severity == DiagnosticSeverity.Error));
        _ = Assert.Single(
            Lines(writer.ToString()),
            line => line == "int IApplier_2<int, string, int>.Apply((int, string) args) => this.Apply(args.Item1, System.ValueTuple.Create(args.Item2));");
    }

    private static string Emit(string typeName)
    {
        var model = Model.Value;
        var diagnostics = new DiagnosticBag();
        var writer = new CSharpWriter();
        var declared = model.TypesByFQN.Values.OfType<MemberType>()
            .Single(type => type.NamespaceName == "Probe" && MistType.StripArity(type.Name) == typeName);

        using(Monomorph.For(declared.TypeParametersList, [2])?.Enter())
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