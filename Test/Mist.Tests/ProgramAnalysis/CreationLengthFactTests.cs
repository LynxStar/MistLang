using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class CreationLengthFactTests
{
    private readonly HandBuiltRefinements h = new();

    [Theory]
    [InlineData("array", true)]
    [InlineData("array", false)]
    [InlineData("stack", true)]
    public void AnIndexBelowARunTimeSizeIsProvenAfterTheCreation(string kind, bool declared)
    {
        var (run, n, _, i) = Parameters();
        var a = new LocalVariable("a", Sequence(kind));
        var created = Create(kind, HandBuiltRefinements.Read(n));
        Statement[] binding = declared
            ? [new LocalDeclarationStatement(a, created)]
            : [new LocalDeclarationStatement(a, null), HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(HandBuiltRefinements.Read(a), created))];

        var body = HandBuiltModel.Block([.. binding, Guarded(a, i, n)]);

        Assert.Empty(h.Built.Analyze(new RefinementAnalyzer(), body, member: run));
    }

    [Theory]
    [InlineData("sequence")]
    [InlineData("size")]
    public void RewritingTheSequenceOrItsSizeEndsTheFact(string rewritten)
    {
        var (run, n, m, i) = Parameters();
        var k = new LocalVariable("k", h.Int);
        var a = new LocalVariable("a", Sequence("array"));
        Statement rewrite = rewritten == "sequence"
            ? HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(HandBuiltRefinements.Read(a), Create("array", HandBuiltRefinements.Read(m))))
            : HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(HandBuiltRefinements.Read(k), HandBuiltRefinements.Read(m)));
        var body = HandBuiltModel.Block(
            new LocalDeclarationStatement(k, HandBuiltRefinements.Read(n)),
            new LocalDeclarationStatement(a, Create("array", HandBuiltRefinements.Read(k))),
            rewrite,
            Guarded(a, i, k));

        Assert.Equal("MIST0275", Assert.Single(h.Built.Analyze(new RefinementAnalyzer(), body, member: run)).Number.Value);
    }

    private (Method Run, LocalVariable N, LocalVariable M, LocalVariable I) Parameters()
    {
        var size = h.Refine(h.Int, ComparisonKind.GreaterThanOrEqual, 0L, "ZeroOrPositiveInt");
        var parameters = new[] { "n", "m", "i" }.Select(name => new Parameter(name, size)).ToArray();
        LocalVariable local(int index)
        {
            return new LocalVariable(parameters[index].Name, size) { Parameter = parameters[index] };
        }

        return (new Method("Run", h.Int, [.. parameters], new MemberModifiers()), local(0), local(1), local(2));
    }

    private IMistType Sequence(string kind)
    {
        return h.Sequence(kind == "stack" ? "Span" : "Array");
    }

    private Expression Create(string kind, Expression size)
    {
        return kind == "stack"
            ? new StackallocExpression(HandBuiltModel.Node(), h.Int, size, Sequence(kind))
            : new ArrayCreationExpression(HandBuiltModel.Node(), h.Int, size, Sequence(kind));
    }

    private IfStatement Guarded(LocalVariable sequence, LocalVariable index, LocalVariable bound)
    {
        return HandBuiltRefinements.If(
            h.Binary(HandBuiltRefinements.Read(index), BinaryOperatorKind.LessThan, HandBuiltRefinements.Read(bound)),
            HandBuiltRefinements.Evaluate(h.At(HandBuiltRefinements.Read(sequence), HandBuiltRefinements.Read(index))));
    }
}