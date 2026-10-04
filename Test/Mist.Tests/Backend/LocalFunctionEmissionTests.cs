// Tier 3 (Component): what a local function becomes in C#, asserted on the emitted TEXT because none
// of it is observable from output: `public` dropped and `static` kept print the same, a sync
// function the backend promoted to async (see AsyncPromotion) runs the same as a declared async one,
// and a renamed parameter computes the same value.

using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class LocalFunctionEmissionTests : ProgramModelProbe
{
    public LocalFunctionEmissionTests()
        : base("MistLocalFunctionEmission_")
    {
    }

    protected override string Fixtures { get; } = "";

    protected override string Preamble { get; } = "";

    [Fact]
    public void AnAccessibilityModifierIsDroppedAndStaticIsKept()
    {
        var emitted = EmitProbe("public static int Sq(int x) { return x * x; } var r = Sq(2);");

        Assert.Contains("static int Sq(int x)", emitted);
        Assert.DoesNotContain("public static int Sq", emitted);
    }

    [Fact]
    public void AFunctionWithoutStaticEmitsNone()
    {
        var emitted = EmitProbe("int Sq(int x) { return x * x; } var r = Sq(2);");

        Assert.Contains("int Sq(int x)", emitted);
        Assert.DoesNotContain("static int Sq", emitted);
    }

    [Fact]
    public void AnAsyncFunctionReturnsATaskAndASyncCallAwaitsIt()
    {
        var emitted = EmitProbe("async int Fetch() { return 1; } var t = Fetch();");

        Assert.Contains("async System.Threading.Tasks.Task<int> Fetch()", emitted);
        Assert.Contains("await Fetch()", emitted);
    }

    [Fact]
    public void ASyncFunctionThatAwaitsIsPromoted()
    {
        var emitted = EmitProbe("async int Fetch() { return 1; } int Doubled() { var v = Fetch(); return v * 2; } var r = Doubled();");

        Assert.Contains("async System.Threading.Tasks.Task<int> Doubled()", emitted);
        Assert.Contains("await Doubled()", emitted);
    }

    [Fact]
    public void AParameterShadowingAnEnclosingLocalIsRenamedApart()
    {
        var emitted = EmitProbe("var x = 1; int Sq(int x) { return x * x; } var r = Sq(x);");

        Assert.Contains("int Sq(int x_1)", emitted);
        Assert.Contains("return x_1 * x_1;", emitted);
        Assert.Contains("Sq(x)", emitted);
    }

    [Fact]
    public void ANameShadowingAnEnclosingLocalIsRenamedAtEverySite()
    {
        var emitted = EmitProbe("var n = 1; { int n(int q) { return q; } var r = n(2); func<int, int> f = n; } var s = n;");

        Assert.Contains("int n_1(int q)", emitted);
        Assert.Contains("n_1(2)", emitted);
        Assert.Contains("= n_1;", emitted);
    }

    [Fact]
    public void ATopLevelFunctionIsEmittedAmongTheTopLevelStatements()
    {
        var source = """
            using Standard;

            public static int Twice(int n)
            {
                return n * 2;
            }

            var r = Twice(2);
            """;

        var model = Build(source, out var diagnostics);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var unit = Assert.Single(model.SelfAssembly!.CompilationUnits);
        var writer = new CSharpWriter();

        using(AsyncPromotion.Enter(model))
        {
            BodyEmitter.EmitBlock(unit.TopLevelMethod!.Body!, writer, new DiagnosticBag());
        }

        var emitted = writer.ToString();
        Assert.Contains("static int Twice(int n)", emitted);
        Assert.DoesNotContain("public", emitted);
        Assert.Contains("Twice(2)", emitted);
    }

    private string EmitProbe(string statement)
    {
        var model = BuildModel(statement, extraTypes: "", extraPreamble: "", out var buildDiagnostics);
        Assert.Empty(buildDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var type = Assert.IsAssignableFrom<MemberType>(model.TypesByFQN["Probe.Probe"]);
        var emitDiagnostics = new DiagnosticBag();
        var writer = new CSharpWriter();

        using(AsyncPromotion.Enter(model))
        {
            TypeEmitter.EmitTypeBody(
                type, ConstraintClosureMap.Build(model, emitDiagnostics), model.VariadicUses, writer, emitDiagnostics);
        }

        Assert.Empty(emitDiagnostics.ToImmutableArray().Where(d => d.Severity == DiagnosticSeverity.Error));

        return writer.ToString();
    }
}