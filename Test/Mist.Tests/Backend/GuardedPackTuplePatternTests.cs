using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class GuardedPackTuplePatternTests
{
    [Fact]
    public void AGuardedOpenPackTuple_KeepsTheFallbackAtMatchingWidth()
    {
        var built = new HandBuiltModel();
        var node = HandBuiltModel.Node();
        var pack = new TypeParameter("TArgs", 0, new Scope(null), null) { PackKind = TypePackKind.Unbracketed };
        var first = pack.ElementAt(0);
        var second = pack.ElementAt(1);
        var tested = built.Interner.GetOrCreateTupleType([first, second]);
        var scrutineeType = built.Interner.GetOrCreateTupleType([new SpreadType(pack)]);
        var scrutinee = HandBuiltRefinements.Local(scrutineeType, "args");
        var guard = HandBuiltRefinements.Local(built.Type("Boolean"), "enabled");
        var integer = built.Type("Int32");
        var tuple = new TuplePattern(node,
            [new CatchAllPattern(node, first, null), new CatchAllPattern(node, second, null)],
            tested, tested, binding: null)
        { OpenPack = pack };
        var expression = new SwitchExpression(node, scrutinee,
        [
            new SwitchArm(node, tuple, guard, HandBuiltModel.Literal(integer, 2)),
            new SwitchArm(node, new CatchAllPattern(node, scrutineeType, null), null, HandBuiltModel.Literal(integer, 0)),
        ], integer);
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();

        using var specialization = Monomorph.For([pack], [2])!.Enter();
        BodyEmitter.EmitExpression(expression, writer, diagnostics);

        Assert.Empty(diagnostics.ToImmutableArray());
        Assert.Equal("args switch { (var _, var _) when enabled => 2, var _ => 0 }", writer.ToString());
    }
}