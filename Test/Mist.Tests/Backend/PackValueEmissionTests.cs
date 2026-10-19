using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class PackValueEmissionTests
{
    [Theory]
    [InlineData(0, "new System.Type[] {  }")]
    [InlineData(1, "new System.Type[] { typeof(T1) }")]
    [InlineData(2, "new System.Type[] { typeof(T1), typeof(T2) }")]
    public void PackExpressionIsAnArrayOfItsSpecializedElementTypes(int width, string expected)
    {
        var pack = new TypeParameter("Ts", 0, new Scope(null), null) { PackKind = TypePackKind.Unbracketed };
        using var specialization = Monomorph.For([pack], [width])!.Enter();
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();

        BodyEmitter.EmitExpression(new TypeReferenceExpression(null!, pack), writer, diagnostics);

        Assert.Equal(expected, writer.ToString());
        Assert.Empty(diagnostics.ToImmutableArray());
    }
}