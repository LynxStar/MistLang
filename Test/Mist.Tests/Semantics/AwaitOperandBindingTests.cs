using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Generated;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Generators;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class AwaitOperandBindingTests
{
    private const string AwaitPath = "Probe/Await.mist";

    [Theory]
    [InlineData("Task<>")]
    [InlineData("ValueTask<>")]
    public void AwaitOfATaskBindsToItsDropAndYieldsTheResult(string kind)
    {
        var f = new BindingFragments();
        var (task, drop) = TaskDefinition(f, "Standard", kind);
        _ = f.Local("pending", f.Built.Interner.GetOrCreateConstructedType(task, [f.Built.Type("Int32")]));

        var syntax = Await("pending");
        var call = Assert.IsType<InvocationExpression>(Bind(f, syntax));

        Assert.Same(drop, call.Method);
        Assert.Same(f.Built.Type("Int32"), call.Type);
        Assert.Same(syntax, call.Node);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Theory]
    [InlineData("plain-value", "Int32")]
    [InlineData("task-lookalike-outside-standard", "Task<Int32>")]
    public void AwaitOfANonTaskIsRefusedOnceAtTheAwait(string operand, string operandTypeName)
    {
        var f = new BindingFragments();
        var intType = f.Built.Type("Int32");
        IMistType type = operand == "plain-value"
            ? intType
            : f.Built.Interner.GetOrCreateConstructedType(TaskDefinition(f, "Probe", "Task<>").Definition, [intType]);
        _ = f.Local("value", type);

        var syntax = Await("value");
        var error = Assert.IsType<ErrorExpression>(Bind(f, syntax));

        Assert.Same(syntax, error.Node);
        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST_PM_AWAIT_OF_NON_TASK", diagnostic.Id.Value);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal($"'await' requires a task, but '{operandTypeName}' is not one.", diagnostic.Message);
        Assert.Equal(AwaitPath, diagnostic.CompilationUnitPath);
        Assert.Equal(new LinePositionSpan(new LinePosition(1, 13), new LinePosition(1, 14)), diagnostic.Location);
    }

    [Theory]
    [InlineData("unresolved-name", "MIST_PM_UNRESOLVED_IDENTIFIER")]
    [InlineData("failed-local", null)]
    public void AwaitOfAValueThatAlreadyFailedAddsNoSecondComplaint(string operand, string? firstComplaint)
    {
        var f = new BindingFragments();
        if(operand == "failed-local")
        {
            _ = f.Local("value", ErrorType.Instance, failed: true);
        }

        var syntax = Await("value");
        var error = Assert.IsType<ErrorExpression>(Bind(f, syntax));

        Assert.Same(syntax, error.Node);
        string[] expected = firstComplaint is null ? [] : [firstComplaint];
        Assert.Equal(expected, f.Diagnostics.ToImmutableArray().Select(diagnostic => diagnostic.Id.Value));
    }

    [Fact]
    public void AnErrorTypedValueNothingReportedIsStillRefusedAtTheAwait()
    {
        var f = new BindingFragments();
        _ = f.Local("value", ErrorType.Instance);

        var syntax = Await("value");
        var error = Assert.IsType<ErrorExpression>(Bind(f, syntax));

        Assert.Same(syntax, error.Node);
        Assert.True(error.IsReported);
        var operand = Assert.IsType<LocalReferenceExpression>(Assert.Single(error.Children));
        Assert.False(operand.Local.IsFailed);
        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST_PM_AWAIT_OF_NON_TASK", diagnostic.Id.Value);
        Assert.Equal(AwaitPath, diagnostic.CompilationUnitPath);
        Assert.Equal(new LinePositionSpan(new LinePosition(1, 13), new LinePosition(1, 14)), diagnostic.Location);
    }

    [Fact]
    public void AwaitOfAnUnknownMemberRetainsItsOperandAndDefersTheConsequentComplaint()
    {
        var f = new BindingFragments();
        _ = f.Local("value", UnknownType.Instance);
        var length = BindingFragments.At<MemberAccessSuffixPostfixSuffixNode>();
        length.Name = BindingFragments.Token("Length");

        var syntax = Await("value", length);
        var error = Assert.IsType<ErrorExpression>(Bind(f, syntax));

        Assert.Same(syntax, error.Node);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
        Assert.False(error.IsReported);
        var access = Assert.IsType<MemberAccessExpression>(Assert.Single(error.Children));
        Assert.Equal("Length", access.MemberName);
        _ = Assert.IsType<LocalReferenceExpression>(access.Receiver);
        var diagnostic = Assert.IsType<Diagnostic>(error.DeferredDiagnostic);
        Assert.Equal("MIST_PM_AWAIT_OF_NON_TASK", diagnostic.Id.Value);
        Assert.Equal(AwaitPath, diagnostic.CompilationUnitPath);
        Assert.Equal(new LinePositionSpan(new LinePosition(1, 13), new LinePosition(1, 14)), diagnostic.Location);
    }

    [Fact]
    public void AwaitOfAnInvalidBinaryRetainsBothOperandsUntilTheOperatorIsJudged()
    {
        var f = new BindingFragments();
        var box = HandBuiltModel.Class("Box");
        _ = f.Local("box", box);
        _ = f.Local("number", f.Built.Type("Int32"));
        var sum = MistAstGenerator.MakeAdditive(
            BindingFragments.Expression(BindingFragments.Identifier("box")),
            BindingFragments.Expression(BindingFragments.Identifier("number")));
        var parenthesized = BindingFragments.At<ParenthesizedExpressionPrimaryNode>();
        parenthesized.Inner = sum;
        var location = HandBuiltModel.Node(12, AwaitPath);
        var syntax = new AwaitExpressionUnaryNode
        {
            Span = location.Span,
            Origin = location.Origin,
            Operand = new PostfixExpressionUnaryNode { Primary = new PostfixNode { Target = parenthesized } },
        };

        var error = Assert.IsType<ErrorExpression>(Bind(f, syntax));

        Assert.Empty(f.Diagnostics.ToImmutableArray());
        Assert.False(error.IsReported);
        var binary = Assert.IsType<BinaryExpression>(Assert.Single(error.Children));
        Assert.Same(box, binary.Left.Type);
        Assert.Same(f.Built.Type("Int32"), binary.Right.Type);
        Assert.Equal(BinaryOperatorKind.Add, binary.OperatorKind);
        Assert.Equal("MIST_PM_AWAIT_OF_NON_TASK", Assert.IsType<Diagnostic>(error.DeferredDiagnostic).Id.Value);
    }

    private static (ClassType Definition, Method Drop) TaskDefinition(BindingFragments f, string namespaceName, string name)
    {
        var task = namespaceName == "Standard" ? f.Built.Type(name) : new ClassType(name, namespaceName, new Scope(null), null, null);
        var result = new TypeParameter("T", 0, task.Scope, task);
        task.TypeParametersList.Add(result);
        var drop = new Method("Drop", result, [], DeclarationFragments.Public with { IsIntrinsic = true })
        {
            DeclaringType = task,
            Scope = task.Scope,
        };
        task.MethodsList.Add(drop);
        return (task, drop);
    }

    private static AwaitExpressionUnaryNode Await(string operand, params PostfixSuffixNode[] suffixes)
    {
        var location = HandBuiltModel.Node(12, AwaitPath);
        var operandNode = BindingFragments.At<PostfixExpressionUnaryNode>();
        operandNode.Primary = new PostfixNode { Target = BindingFragments.Identifier(operand), Suffix = [.. suffixes] };
        return new AwaitExpressionUnaryNode { Span = location.Span, Origin = location.Origin, Operand = operandNode };
    }

    private static Expression Bind(BindingFragments f, AwaitExpressionUnaryNode syntax)
    {
        return ExpressionBinder.BindExpression(BindingFragments.Expression(syntax), f.Binding, f.Builder);
    }
}