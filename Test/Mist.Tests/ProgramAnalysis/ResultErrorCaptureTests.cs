using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.HandBuiltModel;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class ResultErrorCaptureTests
{
    [Fact]
    public void IdentityInAFailureArmLambdaRejectsTheCapturedError()
    {
        var shapes = new ResultModel();
        var resultType = shapes.Result(shapes.Integer, shapes.Text);
        var result = new LocalVariable("result", resultType) { Parameter = new Parameter("result", resultType) };
        var error = new LocalVariable("error", shapes.Text) { IsKnownResultError = true };
        var inspection = shapes.Inspect(new LocalReferenceExpression(Node(), result));
        var failure = shapes.Built.Interner.GetOrCreateTupleType([shapes.False, shapes.Text]);
        var pattern = new TuplePattern(Node(), [shapes.Tag(false), new CatchAllPattern(Node(), shapes.Text, error)], failure, null, null)
        {
            CorrelatedInput = (UnionType)inspection.Type,
            Constituent = failure,
        };
        var condition = new IsPatternExpression(Node(), inspection, pattern, shapes.Boolean);

        var plain = new LocalVariable("plain", shapes.Text) { IsReadonly = true };
        var function = shapes.Built.Interner.GetOrCreateFunctionType([], shapes.Text);
        var rejected = new ResultIdentityExpression(Node(12), new LocalReferenceExpression(Node(11), error));
        var admitted = new ResultIdentityExpression(Node(22), new LocalReferenceExpression(Node(21), plain));
        var errorLambda = new LambdaExpression(Node(10), [], rejected, null, false, function);
        var plainLambda = new LambdaExpression(Node(20), [], admitted, null, false, function);
        var body = Block(new IfStatement(condition, Block(
            new LocalDeclarationStatement(plain, Literal(shapes.Text, "plain")),
            new LocalDeclarationStatement(new LocalVariable("readError", function), errorLambda),
            new LocalDeclarationStatement(new LocalVariable("readPlain", function), plainLambda)), null));

        var finding = Assert.Single(shapes.Built.Analyze(new ResultAnalyzer(), body));
        Assert.Equal("MIST0482", finding.Number.Value);
        Assert.Equal(DiagnosticHelper.ResolveLocation(rejected.Node).Location, finding.Location);
    }
}