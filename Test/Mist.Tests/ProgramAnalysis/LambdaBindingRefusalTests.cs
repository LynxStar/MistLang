using Mist.Generated;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

// Tier 2: bind hand-built lambda syntax while the direct call chooses and closes its signature.
[Trait("Tier", "2")]
public sealed class LambdaBindingRefusalTests
{
    [Theory]
    [InlineData("lambda-whose-parameter-nothing-fixes", "MIST0391")]
    [InlineData("lambda-parameter-stated-against-an-inferred-type", "MIST0392")]
    [InlineData("lambda-body-broken-under-the-inferred-parameter", "MIST0301")]
    [InlineData("overloaded-callback-no-candidate-beats", "MIST0386")]
    public void LambdaParticipatesInRealBindingAndInference(string refusal, string diagnosticId)
    {
        var f = new BindingFragments();
        var owner = HandBuiltModel.Class("Callbacks");
        var intType = f.Built.Type("Int32");
        _ = f.Local("api", owner);
        ExpressionNode lambda;
        ExpressionNode[] arguments;
        if(refusal == "overloaded-callback-no-candidate-beats")
        {
            _ = BindingFragments.Member(owner, "Apply", intType, new FunctionType([intType], intType));
            _ = BindingFragments.Member(owner, "Apply", intType, new FunctionType([f.Built.Type("Int64")], intType));
            lambda = new LambdaFormExpressionNode { Lambda = BindingFragments.Lambda(BindingFragments.Expression(new IntegerLiteralExpressionPrimaryNode { IntegerLiteral = BindingFragments.Token("1") })) };
            arguments = [lambda];
        }
        else
        {
            var method = BindingFragments.Member(owner, "Apply", intType);
            var t = BindingFragments.Parameter(method, "T");
            var r = refusal == "lambda-whose-parameter-nothing-fixes" ? t : BindingFragments.Parameter(method, "R");
            if(refusal != "lambda-whose-parameter-nothing-fixes")
            {
                method.ParametersList.Add(new Parameter("value", t));
            }

            method.ParametersList.Add(new Parameter("callback", new FunctionType([t], r)));
            var body = refusal == "lambda-body-broken-under-the-inferred-parameter"
                ? BindingFragments.Expression(BindingFragments.Identifier("x"), MissingMember("Length"))
                : BindingFragments.Expression(BindingFragments.Identifier("x"));
            lambda = new LambdaFormExpressionNode
            {
                Lambda = BindingFragments.Lambda(body, refusal == "lambda-parameter-stated-against-an-inferred-type" ? "String" : null),
            };
            arguments = refusal == "lambda-whose-parameter-nothing-fixes"
                ? [lambda]
                : [BindingFragments.Expression(new IntegerLiteralExpressionPrimaryNode { IntegerLiteral = BindingFragments.Token("1") }), lambda];
        }

        var call = BindingFragments.At<MethodCallSuffixPostfixSuffixNode>();
        call.Name = BindingFragments.Token("Apply");
        call.Arguments = BindingFragments.Arguments(arguments);
        _ = f.Bind(BindingFragments.Identifier("api"), call);
        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal(diagnosticId, diagnostic.Number.Value);
        Assert.Equal(31, diagnostic.Position().Start.Column);
    }

    private static MemberAccessSuffixPostfixSuffixNode MissingMember(string name)
    {
        var access = BindingFragments.At<MemberAccessSuffixPostfixSuffixNode>();
        access.Name = BindingFragments.Token(name);
        return access;
    }
}