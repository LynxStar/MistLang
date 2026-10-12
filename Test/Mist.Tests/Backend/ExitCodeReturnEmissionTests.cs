using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class ExitCodeReturnEmissionTests
{
    private readonly HandBuiltModel model = new();

    [Fact]
    public void AnExitCodeIsSetAndTheStatementsReturnNothing()
    {
        var integer = model.Type("Int32");

        var emitted = Emit(new ReturnStatement(HandBuiltModel.Literal(integer, 3L), integer) { IsExitCode = true });

        Assert.Equal("System.Environment.ExitCode = 3;\nreturn;", emitted);
    }

    [Fact]
    public void AnOrdinaryValuedReturnReturnsItsValue()
    {
        var integer = model.Type("Int32");

        Assert.Equal("return 3;", Emit(new ReturnStatement(HandBuiltModel.Literal(integer, 3L), integer)));
    }

    private static string Emit(Statement statement)
    {
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();
        BodyEmitter.EmitStatement(statement, writer, diagnostics);
        Assert.Empty(diagnostics.ToImmutableArray());
        return writer.ToString().Trim().ReplaceLineEndings("\n");
    }
}