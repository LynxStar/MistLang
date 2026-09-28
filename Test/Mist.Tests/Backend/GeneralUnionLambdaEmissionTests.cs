using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class GeneralUnionLambdaEmissionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AGeneralUnionArmCastsExpressionAndBlockLambdasToTheirDelegate(bool block)
    {
        var model = new HandBuiltModel();
        var text = model.Type("String");
        var parameter = new LocalVariable("text", text);
        var result = new LocalReferenceExpression(HandBuiltModel.Node(), parameter);
        var signature = model.Interner.GetOrCreateFunctionType([text], text);
        var body = block ? HandBuiltModel.Block(new ReturnStatement(result, text)) : null;
        var lambda = new LambdaExpression(HandBuiltModel.Node(), [parameter], block ? null : result, body, false, signature)
        {
            UnionCarrierDelegateType = signature,
        };

        var emitted = BodyEmitter.Render(lambda, new DiagnosticBag());

        Assert.StartsWith("(System.Func<string, string>)((string text) => ", emitted);
        Assert.EndsWith(")", emitted);
    }

    [Fact]
    public void AGeneralUnionCastUsesTheSelectedNominalDelegateArm()
    {
        var model = new HandBuiltModel();
        var text = model.Type("String");
        var parameter = new LocalVariable("text", text);
        var signature = model.Interner.GetOrCreateFunctionType([text], text);
        var delegateArm = new ClassType("Transform", "Probe", new Scope(null), null, null);
        var lambda = new LambdaExpression(HandBuiltModel.Node(), [parameter],
            new LocalReferenceExpression(HandBuiltModel.Node(), parameter), null, false, signature)
        {
            UnionCarrierDelegateType = delegateArm,
        };

        Assert.StartsWith("(Transform)((string text) => text)", BodyEmitter.Render(lambda, new DiagnosticBag()));
    }

    [Fact]
    public void OrdinaryLambdaEmissionKeepsItsExistingContextualConversion()
    {
        var model = new HandBuiltModel();
        var text = model.Type("String");
        var parameter = new LocalVariable("text", text);
        var signature = model.Interner.GetOrCreateFunctionType([text], text);
        var lambda = new LambdaExpression(HandBuiltModel.Node(), [parameter],
            new LocalReferenceExpression(HandBuiltModel.Node(), parameter), null, false, signature);

        Assert.Equal("(string text) => text", BodyEmitter.Render(lambda, new DiagnosticBag()));
    }
}