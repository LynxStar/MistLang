using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.RuntimeDependentValues;

/// <summary>
/// An array return type whose length names a readonly parameter, judged through the refinement analyzer
/// over hand-built bodies: each return against it, the slot's contract at the signature, and the results of
/// two calls closed with one value.
/// </summary>
[Trait("Tier", "2")]
public sealed class ReturnLengthProofFlowTests
{
    private static readonly ParameterModifier Readonly = new(IsReadonly: true, IsRef: false, OwnershipKind.Unrestricted);

    private readonly HandBuiltSlots slots = new();

    public ReturnLengthProofFlowTests()
    {
        slots.Array.TypeParametersList[1].Domain = slots.ZeroOrPositive;
        _ = slots.Refinements.Built.Model.RegisterType(slots.Array);
    }

    private Method Sized(IMistType parameterType, out Parameter n)
    {
        n = new Parameter("n", parameterType, Readonly);
        var returned = slots.ArrayOf(new ParameterValue(n, slots.Int, HandBuiltModel.Node(4)));
        return new Method("Sized", returned, [n], new MemberModifiers()) { DeclarationNode = HandBuiltModel.Node(2) };
    }

    private IReadOnlyList<Diagnostic> AnalyzeIn(Method method, params Statement[] statements)
    {
        return slots.Refinements.Built.Analyze(new RefinementAnalyzer(), HandBuiltModel.Block(statements), member: method);
    }

    private IReadOnlyList<Diagnostic> AnalyzeSignature(Method method)
    {
        var owner = HandBuiltModel.Class("Api");
        owner.MethodsList.Add(method);
        return slots.Refinements.Built.Analyze(new RefinementAnalyzer(), types: [owner]);
    }

    private (LocalDeclarationStatement Declaration, SlotCapture Length) Called(Method sized, LocalVariable argument, string name)
    {
        var passed = HandBuiltRefinements.Read(argument);
        var length = new SlotCapture(argument.Name, slots.ZeroOrPositive, slots.Int, passed, passed.Node.Span.Start);
        var call = new InvocationExpression(
            HandBuiltModel.Node(), slots.Refinements.Literal(0L), [passed], slots.ArrayOf(length.Value), sized, [sized.ParametersList[0].Type])
        {
            Captures = [length],
        };

        return (new LocalDeclarationStatement(new LocalVariable(name, call.Type), call), length);
    }

    [Fact]
    public void AReturnSizedByTheParameterIsProvenAndALiteralLengthIsRefusedByName()
    {
        var sized = Sized(slots.ZeroOrPositive, out var n);
        var local = new LocalVariable("n", n.Type) { Parameter = n, IsReadonly = true };

        Assert.Empty(AnalyzeIn(sized, new ReturnStatement(slots.Create(slots.Capture(local)), sized.ReturnType)));

        var refused = Assert.Single(AnalyzeIn(sized, new ReturnStatement(slots.Refinements.Array(3), sized.ReturnType)));

        Assert.Equal("MIST0486", refused.Number.Value);
        Assert.Contains("'int[3]' is not proven to be 'int[n]': '3' and 'n' are not proven identical (fails when n <= 2)", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePlainIntParameterOwesTheLengthsContractAtTheSignature()
    {
        var refused = Assert.Single(AnalyzeSignature(Sized(slots.Int, out _)));

        Assert.Equal("MIST0272", refused.Number.Value);
        Assert.Contains("(fails when n <= -1)", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AWhereClauseThatBoundsTheParameterProvesTheLengthsContract()
    {
        Assert.Empty(AnalyzeSignature(Sized(slots.Refinements.Refine(slots.Int, ComparisonKind.GreaterThanOrEqual, 0L, name: null), out _)));
    }

    [Fact]
    public void TwoResultsOfACallWithOneValueAreOneTypeAndAnotherValuesIsNot()
    {
        var sized = Sized(slots.ZeroOrPositive, out _);
        var count = new LocalVariable("count", slots.ZeroOrPositive);
        var other = new LocalVariable("other", slots.ZeroOrPositive);
        var (a, length) = Called(sized, count, "a");
        var (b, _) = Called(sized, count, "b");
        var (c, _) = Called(sized, other, "c");

        LocalDeclarationStatement same(LocalDeclarationStatement source)
        {
            return new LocalDeclarationStatement(new LocalVariable("same", slots.ArrayOf(length.Value)), HandBuiltRefinements.Read(source.Local));
        }

        Assert.Empty(slots.Refinements.Built.Analyze(new RefinementAnalyzer(), HandBuiltModel.Block(a, b, same(b))));

        var refused = Assert.Single(slots.Refinements.Built.Analyze(new RefinementAnalyzer(), HandBuiltModel.Block(a, c, same(c))));

        Assert.Equal("MIST0486", refused.Number.Value);
        Assert.Contains("'int[other]' is not proven to be 'int[count]'", refused.Message, StringComparison.Ordinal);
    }
}