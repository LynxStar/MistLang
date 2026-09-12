using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

/// <summary>
/// Tier 3: a call that states its type arguments on an UNQUALIFIED name.
/// <para>
/// A DIFFERENT AST NODE FROM THE QUALIFIED FORM, which is the whole reason it went unbound.
/// `x.M&lt;int&gt;(...)` is a receiver plus a suffix and has always worked; `M&lt;int&gt;(...)` is
/// one primary node, and a primary the binder did not handle resolves to nothing at all rather
/// than to the wrong thing -- so nothing about the qualified form's tests could have caught it.
/// </para>
/// </summary>
public sealed class ExplicitGenericInvocationTests : ProgramModelProbe
{
    public ExplicitGenericInvocationTests()
        : base("MistExplicitGeneric_")
    {
    }

    protected override string Fixtures { get; } = "";

    protected override string Preamble { get; } = "";

    private const string Source = """
        namespace Probe;

        public class Probe
        {
            public static T Identity<T>(T value) { return value; }

            public static int Describe<params TArgs>(params TArgs args) { return TArgs.Length; }

            public static TOut Apply<TIn, TOut>(TIn value, func<TIn, TOut> f) { return f(value); }

            public static void Run()
            {
                var plain = Identity<int>(5);
                var packed = Describe<[int, string, bool]>(1, "x", true);
                var applied = Apply<int, string>(1, v => "x");
            }
        }
        """;

    private ImmutableArray<InvocationExpression> Calls(out ImmutableArray<Diagnostic> diagnostics)
    {
        var model = Build(Source, out diagnostics);
        var run = ((ClassType)model.TypesByFQN["Probe.Probe"]).MethodsList.Single(m => m.Name == "Run");

        Assert.NotNull(run.Body);

        return [.. run.Body.Statements
            .OfType<LocalDeclarationStatement>()
            .Select(declaration => declaration.Initializer)
            .OfType<InvocationExpression>()];
    }

    [Fact]
    public void StatedTypeArgumentsOnABareNameSelectTheMethod()
    {
        var calls = Calls(out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Equal(3, calls.Length);
        Assert.Equal(["Identity", "Describe", "Apply"], calls.Select(call => call.Method?.Name));
    }

    [Fact]
    public void StatedTypeArgumentsCloseTheTargetALambdaArgumentBindsAgainst()
    {
        var applied = Calls(out _)[2];
        var lambda = Assert.IsType<LambdaExpression>(applied.Arguments[1]);

        Assert.Equal("int", TypeDisplay.Mist(Assert.Single(lambda.Parameters).Type));
        Assert.Equal("func<int, string>", TypeDisplay.Mist(lambda.Type));
    }

    /// <summary>
    /// The pack is the argument, not three arguments: a bracketed list supplies the whole
    /// <c>Type[]</c> at once, which is how two packs on one declaration stay told apart.
    /// </summary>
    [Fact]
    public void ABracketedPackFillsTheOnePackSlot()
    {
        var packed = Calls(out _)[1];
        var stated = Assert.IsType<MethodReferenceExpression>(packed.Receiver).TypeArguments;

        var pack = Assert.IsType<TypePackValue>(Assert.IsType<LiftedType>(Assert.Single(stated)).Value);
        Assert.Equal(3, pack.Elements.Length);
    }
}