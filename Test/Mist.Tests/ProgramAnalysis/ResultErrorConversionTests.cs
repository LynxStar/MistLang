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
public sealed class ResultErrorConversionTests
{
    [Fact]
    public void ImplicitConversionPreservesProvenErrorThroughALocalCopy()
    {
        var shapes = new ResultModel();
        var errorCode = Struct("ErrorCode");
        var wrappedError = Struct("WrappedError");
        var conversion = new ConversionFunction(Conversion(wrappedError, errorCode, wrappedError), errorCode, wrappedError);

        var resultType = shapes.Result(shapes.Integer, errorCode);
        var result = new LocalVariable("result", resultType) { Parameter = new Parameter("result", resultType) };
        var plain = new LocalVariable("plain", errorCode) { Parameter = new Parameter("plain", errorCode) };
        var error = new LocalVariable("error", errorCode) { IsKnownResultError = true };
        var inspection = shapes.Inspect(new LocalReferenceExpression(Node(), result));
        var failure = shapes.Built.Interner.GetOrCreateTupleType([shapes.False, errorCode]);
        var pattern = new TuplePattern(Node(), [shapes.Tag(false), new CatchAllPattern(Node(), errorCode, error)], failure, null, null)
        {
            CorrelatedInput = (UnionType)inspection.Type,
            Constituent = failure,
        };
        var condition = new IsPatternExpression(Node(), inspection, pattern, shapes.Boolean);

        var errorCopy = new LocalVariable("errorCopy", wrappedError);
        var plainCopy = new LocalVariable("plainCopy", wrappedError);
        var errorReference = new LocalReferenceExpression(Node(), error);
        var plainReference = new LocalReferenceExpression(Node(), plain);
        var rejected = new ResultIdentityExpression(Node(10), new LocalReferenceExpression(Node(9), errorCopy));
        var admitted = new ResultIdentityExpression(Node(20), new LocalReferenceExpression(Node(19), plainCopy));
        var body = Block(new IfStatement(condition, Block(
            new LocalDeclarationStatement(errorCopy, new ImplicitConversionExpression(errorReference.Node, errorReference, wrappedError, [conversion])),
            new LocalDeclarationStatement(plainCopy, new ImplicitConversionExpression(plainReference.Node, plainReference, wrappedError, [conversion])),
            new LocalDeclarationStatement(new LocalVariable("rejected", wrappedError), rejected),
            new LocalDeclarationStatement(new LocalVariable("admitted", wrappedError), admitted)), null));

        var finding = Assert.Single(shapes.Built.Analyze(new ResultAnalyzer(), body));
        Assert.Equal(ProgramAnalysisDiagnostics.ResultErrorIsNotIdentity.Number, finding.Number);
        Assert.Equal(DiagnosticHelper.ResolveLocation(rejected.Node).Location, finding.Location);
    }
}