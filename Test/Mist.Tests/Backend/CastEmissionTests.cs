// Tier 3 (Component): how a cast's operand is spelled in C#, over the real Standard sources. Asserted on the
// emitted text because the defect prints a plausible number: `(double)(a / b)` emitted bare divides after the
// cast and prints 3.5 where Mist, and classic, print 3. No Roslyn compile and no process spawning.

using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class CastEmissionTests
{
    [Theory]
    [InlineData("double ratio = (double)(a / b);", "double ratio = ((double)(a / b));")]
    [InlineData("long product = (long)(a * b);", "long product = ((long)(a * b));")]
    [InlineData("long negated = (long)-a;", "long negated = ((long)(-a));")]
    public void ACastsOperand_KeepsTheParenthesesItWasWrittenWith(string statement, string expected)
    {
        Assert.Contains(expected, Emit("int a = 7; int b = 2; " + statement), StringComparison.Ordinal);
    }

    private static string Emit(string statements)
    {
        var model = StandardModelProbe.Build(statements, out var built);
        Assert.Empty(built.Where(d => d.Severity == DiagnosticSeverity.Error));

        var run = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Probe"]).MethodsList.Single(m => m.Name == "Run");
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();

        Assert.NotNull(run.Body);

        using(IntrinsicSupply.Enter(model, Type.GetType))
        using(AsyncPromotion.Enter(model))
        {
            BodyEmitter.EmitMethodBody(run, run.Body, writer, diagnostics);
        }

        Assert.Empty(diagnostics.ToImmutableArray().Where(d => d.Severity == DiagnosticSeverity.Error));
        return writer.ToString();
    }
}