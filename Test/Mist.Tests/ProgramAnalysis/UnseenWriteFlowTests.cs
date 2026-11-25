using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

/// <summary>
/// A closure's write to storage outside it may run at any later point, so the enclosing flow keeps only that
/// holder's declared type; a closure's write to its own local or parameter is ordinary flow inside the closure and
/// keeps its facts there.
/// </summary>
[Trait("Tier", "2")]
public sealed class UnseenWriteFlowTests
{
    [Theory]
    [InlineData("captured-local-compound-assigned-in-a-lambda", true)]
    [InlineData("captured-local-only-read-in-a-lambda", false)]
    [InlineData("field-of-this-assigned-in-a-lambda", true)]
    [InlineData("field-of-this-only-read-in-a-lambda", false)]
    [InlineData("lambdas-own-local-assigned-and-needed-inside-it", false)]
    [InlineData("lambdas-own-parameter-assigned-and-needed-inside-it", false)]
    public void OnlyAClosuresWriteToStorageOutsideItEndsAFact(string form, bool refused)
    {
        var f = new FactHolderFragments();
        var d = FactHolderFragments.Int(f.H, "d");
        var t = FactHolderFragments.Int(f.H, "t");
        var p = new LocalVariable("p", f.H.Int) { Parameter = FactHolderFragments.Parameter("p", f.H.Int) };
        Expression field()
        {
            return FactHolderFragments.Member(f.This, f.A);
        }

        Statement lambda(params Statement[] statements)
        {
            return HandBuiltRefinements.Evaluate(new LambdaExpression(HandBuiltModel.Node(), [p], null, HandBuiltModel.Block(statements), false, f.H.Int));
        }

        Statement[] body = form switch
        {
            "captured-local-compound-assigned-in-a-lambda" => [FactHolderFragments.Declare(d, f.Number(5L)),
                lambda(HandBuiltRefinements.Evaluate(new CompoundAssignmentExpression(HandBuiltModel.Node(), FactHolderFragments.Read(d), BinaryOperatorKind.Add,
                    f.Number(1L), f.H.Int, null, IncrementForm.None))), f.Need(FactHolderFragments.Read(d))],
            "captured-local-only-read-in-a-lambda" => [FactHolderFragments.Declare(d, f.Number(5L)),
                lambda(HandBuiltRefinements.Evaluate(FactHolderFragments.Read(d))), f.Need(FactHolderFragments.Read(d))],
            "field-of-this-assigned-in-a-lambda" => [FactHolderFragments.Set(field(), f.Number(5L)),
                lambda(FactHolderFragments.Set(field(), f.Number(6L))), f.Need(field())],
            "field-of-this-only-read-in-a-lambda" => [FactHolderFragments.Set(field(), f.Number(5L)),
                lambda(HandBuiltRefinements.Evaluate(field())), f.Need(field())],
            "lambdas-own-local-assigned-and-needed-inside-it" => [lambda(FactHolderFragments.Declare(t, f.Number(0L)),
                FactHolderFragments.Set(FactHolderFragments.Read(t), f.Number(5L)), f.Need(FactHolderFragments.Read(t)))],
            "lambdas-own-parameter-assigned-and-needed-inside-it" => [lambda(FactHolderFragments.Set(FactHolderFragments.Read(p), f.Number(5L)),
                f.Need(FactHolderFragments.Read(p)))],
            _ => throw new InvalidOperationException(form),
        };

        var findings = f.H.Built.Analyze(new RefinementAnalyzer(), HandBuiltModel.Block(body));

        if(refused)
        {
            Assert.Equal("MIST0272", Assert.Single(findings).Number.Value);
        }
        else
        {
            Assert.Empty(findings);
        }
    }
}