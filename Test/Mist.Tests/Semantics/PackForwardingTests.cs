using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

// Tier 3: where a spread of an open value pack is admitted (variadics Requirements 12.7, 12.8). The
// destination must take the same pack for every instantiation, and the binder's answer is the argument
// it leaves in the model: the spread itself where a value pack parameter absorbs it, and a refusal
// everywhere the destination's shape is fixed or its own pack is constrained beyond the forwarded one.
[Trait("Tier", "3")]
public sealed class PackForwardingTests : ProgramModelProbe
{
    public PackForwardingTests()
        : base("MistPackForwarding_")
    {
    }

    protected override string Fixtures { get; } = """
        public class Boxed<params TArgs>
        {
            public Boxed(params TArgs values) { }
            public int Apply(params TArgs args) { return TArgs.Length; }
        }

        public abstract INamed { string Name(); }

        public class Sinks
        {
            public static int Named<params TArgs>(params TArgs args) where TArgs : INamed { return TArgs.Length; }
            public static int Zeroed<params TArgs>(params TArgs args) where TArgs : default { return TArgs.Length; }
            public static int Count<params TArgs>(params TArgs args) { return TArgs.Length; }
            public static int Tagged<params TArgs>(string tag, params TArgs args) { return TArgs.Length; }
            public static int Lead<params TRest>(int first, params TRest rest) { return first; }
            public static int Sum3(int a, int b, int c) { return a + b + c; }
            public static int One(int x) { return x; }
            public static int All(params int[] xs) { return 0; }
        }
        """;

    protected override string Preamble { get; } = "";

    private const string OnePack = "<params TArgs>(params TArgs args)";

    [Theory]
    [InlineData(OnePack, "Sinks.Count(args...)")]
    [InlineData(OnePack, "Sinks.Count<[TArgs...]>(args...)")]
    [InlineData(OnePack, "Sinks.Tagged(\"t\", args...)")]
    [InlineData("<params TArgs>(Boxed<TArgs...> b, params TArgs args)", "b.Apply(args...)")]
    [InlineData("<params TArgs>(params TArgs args) where TArgs : INamed", "Sinks.Named(args...)")]
    [InlineData("<params TArgs>(params TArgs args) where TArgs : default", "Sinks.Zeroed(args...)")]
    public void ASpreadIntoAParameterOverTheSamePack_StaysASpreadOfThatPack(string signature, string call)
    {
        var forwarder = Forwarder($"public static int Run{signature} {{ return {call}; }}");
        var invocation = Assert.IsType<InvocationExpression>(Returned(forwarder));

        _ = Assert.IsType<SpreadExpression>(invocation.Arguments[^1]);

        var slot = Assert.IsType<TupleType>(invocation.ParameterTypes![^1]);
        var element = Assert.IsType<SpreadType>(Assert.Single(slot.ElementTypes));
        Assert.Same(forwarder.TypeParametersList[0], element.Operand);
    }

    [Fact]
    public void ASpreadIntoAConstructorOverTheSamePack_StaysASpread()
    {
        var forwarder = Forwarder(
            "public static Boxed<TArgs...> Run<params TArgs>(params TArgs args) { return new Boxed<TArgs...>(args...); }");
        var creation = Assert.IsType<NewExpression>(Returned(forwarder));

        _ = Assert.IsType<SpreadExpression>(Assert.Single(creation.Arguments));
    }

    [Theory]
    [InlineData(OnePack, "Sinks.Sum3(args...)")]
    [InlineData(OnePack, "Sinks.One(args...)")]
    [InlineData(OnePack, "Sinks.Lead(args...)")]
    [InlineData(OnePack, "Sinks.All(args...)")]
    [InlineData(OnePack, "Sinks.Count(1, args...)")]
    [InlineData("<params TArgs>(Boxed<int, string> b, params TArgs args)", "b.Apply(args...)")]
    [InlineData("<params TArgs, Type[]:TOther>(Boxed<TOther...> b, params TArgs args)", "b.Apply(args...)")]
    [InlineData(OnePack, "Sinks.Named(args...)")]
    [InlineData(OnePack, "Sinks.Named<[TArgs...]>(args...)")]
    [InlineData(OnePack, "Sinks.Zeroed(args...)")]
    [InlineData("<params TArgs>(params TArgs args) where TArgs : default", "Sinks.Named(args...)")]
    public void ASpreadIntoAnythingElse_IsRefused(string signature, string call)
    {
        var diagnostics = BuildRun(
            "", $"public class Forwarder {{ public static int Run{signature} {{ return {call}; }} }}");

        _ = Assert.Single(Errors(diagnostics, "MIST_VAR_0011"));
    }

    private Method Forwarder(string method)
    {
        var model = BuildModel("", $"public class Forwarder {{ {method} }}", "", out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        return Assert.Single(Assert.IsType<ClassType>(model.TypesByFQN["Probe.Forwarder"]).MethodsList);
    }

    private static Expression? Returned(Method method)
    {
        return Assert.Single(method.Body!.Statements.OfType<ReturnStatement>()).Value;
    }
}