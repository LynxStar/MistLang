using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "3")]
public sealed class UnionPatternMatchingEmissionTests
{
    [Fact]
    public void ACatchAllAgainstAUnion_MatchesTheCarrierAsItStands()
    {
        Assert.Contains(
            "bool bound = held is var whole;",
            EmitRun("int | string held = 3; var bound = held is var whole;"));
    }

    private static string EmitRun(string statements)
    {
        var model = StandardModelProbe.Build(statements, out var buildDiagnostics);
        Assert.Empty(buildDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var type = Assert.IsAssignableFrom<MemberType>(model.TypesByFQN["Probe.Probe"]);
        var diagnostics = new DiagnosticBag();
        var writer = new CSharpWriter();

        BodyEmitter.ResetTemporaries();
        TypeEmitter.EmitTypeBody(type, ConstraintClosureMap.Build(model, diagnostics), model.VariadicUses, writer, diagnostics);

        Assert.Empty(diagnostics.ToImmutableArray().Where(d => d.Severity == DiagnosticSeverity.Error));

        return string.Join("\n", writer.ToString().ReplaceLineEndings("\n").Split('\n').Select(line => line.Trim()));
    }
}