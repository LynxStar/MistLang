using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class BlockingConditionalStatementEmissionTests
{
    [Fact]
    public void DiscardedPromotedCall_BlocksInsideThePresenceGuardInASynchronousLambda()
    {
        var model = new HandBuiltModel();
        var node = HandBuiltModel.Node();
        var reader = HandBuiltModel.Class("Reader");
        var unit = model.Type("Void");
        var result = HandBuiltModel.Class("Reading");
        var method = new Method("Read", result, [], new MemberModifiers()) { DeclaringType = reader };
        var receiver = new LocalReferenceExpression(node,
            new LocalVariable("reader", model.Interner.GetOrCreateUnionType([reader, unit])));
        var optionalResult = model.Interner.GetOrCreateUnionType([result, unit]);
        var access = new MemberAccessExpression(node, receiver, method.Name, optionalResult, isConditional: true);
        var call = new InvocationExpression(node, access, [], optionalResult, method, null);
        var action = new FunctionType([], unit);
        var body = HandBuiltModel.Block(new ExpressionStatement(call));
        var lambda = new LambdaExpression(node, [], null, body, false, action);
        var declaration = new LocalDeclarationStatement(new LocalVariable("run", action), lambda);
        model.Model.TaskDrops.RecordDrop(method);

        using var promotion = AsyncPromotion.Enter(model.Model);
        BodyEmitter.ResetTemporaries();
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();
        BodyEmitter.EmitStatement(declaration, writer, diagnostics);

        const string expected = """
            System.Action run = () =>
            {
                if (reader is { } __e1)
                {
                    __e1.Read().GetAwaiter().GetResult();
                }
            };
            """;

        Assert.Empty(diagnostics.ToImmutableArray());
        Assert.Equal(expected.ReplaceLineEndings(" "), writer.ToString().TrimEnd().ReplaceLineEndings(" "), ignoreAllWhiteSpace: true);
    }
}