using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

public sealed class UnaryNegationRuleTests
{
    [Theory]
    [Trait("Tier", "1")]
    [InlineData("Byte")]
    [InlineData("UInt16")]
    [InlineData("UInt32")]
    [InlineData("UInt64")]
    [InlineData("Char")]
    public void AnUnsignedTypeOrChar_CannotBeNegated(string type)
    {
        var operand = new HandBuiltModel().Type(type);

        Assert.Equal("MIST0200", OperatorDecision.Unary(UnaryOperatorKind.Negate, operand)?.Number.Value);
    }

    [Theory]
    [Trait("Tier", "1")]
    [InlineData("SByte")]
    [InlineData("Int16")]
    [InlineData("Int32")]
    [InlineData("Int64")]
    [InlineData("Single")]
    [InlineData("Double")]
    [InlineData("Decimal")]
    public void ASignedTypeWhoseDeclarationIsNotLoaded_IsStillNegatable(string type)
    {
        var operand = new HandBuiltModel().Type(type);

        Assert.Null(OperatorDecision.Unary(UnaryOperatorKind.Negate, operand));
        Assert.Same(operand, OperatorAdmissibility.UnaryResultType(UnaryOperatorKind.Negate, operand));
    }

    [Fact]
    [Trait("Tier", "1")]
    public void ADeclaredNegation_AnswersForItsType()
    {
        var integer = new HandBuiltModel().Type("Int32");
        integer.OperatorsList.Add(new Operator(OperatorKind.Minus, integer, [new Parameter("a", integer)], new MemberModifiers { IsStatic = true }));

        Assert.Null(OperatorDecision.Unary(UnaryOperatorKind.Negate, integer));
    }

    [Theory]
    [Trait("Tier", "1")]
    [InlineData(true, "class")]
    [InlineData(false, "class")]
    [InlineData(false, "Int32")]
    [InlineData(true, "Boolean")]
    [InlineData(true, "String")]
    [InlineData(false, "String")]
    [InlineData(true, "error")]
    [InlineData(false, "error")]
    public void AnUnsupportedOperand_HasNoUnaryResult(bool negate, string type)
    {
        var kind = negate ? UnaryOperatorKind.Negate : UnaryOperatorKind.LogicalNot;
        IMistType operand = type switch
        {
            "class" => HandBuiltModel.Class("Thing"),
            "error" => ErrorType.Instance,
            _ => new HandBuiltModel().Type(type),
        };

        Assert.Null(OperatorAdmissibility.UnaryResultType(kind, operand));
        Assert.Equal("MIST0200", OperatorDecision.Unary(kind, operand)?.Number.Value);
    }

    [Fact]
    [Trait("Tier", "2")]
    public void ANegationTheBinderCouldNotType_IsReportedAgainstItsOperand()
    {
        var fragment = new DeclarationFragments();
        var unsigned = fragment.Built.Type("UInt32");
        fragment.Root = new UnaryExpression(
            HandBuiltModel.Node(30), UnaryOperatorKind.Negate, HandBuiltModel.Literal(unsigned, 5U, 31), ErrorType.Instance);

        var finding = Assert.Single(fragment.Run(new OperatorAnalyzer()));

        Assert.Equal("MIST0200", finding.Number.Value);
    }

    [Theory]
    [Trait("Tier", "1")]
    [InlineData(true)]
    [InlineData(false)]
    public void Unknown_HasNoUnaryOperators(bool negate)
    {
        var kind = negate ? UnaryOperatorKind.Negate : UnaryOperatorKind.LogicalNot;

        Assert.Null(OperatorAdmissibility.UnaryResultType(kind, UnknownType.Instance));
        Assert.Equal("MIST0201", OperatorDecision.Unary(kind, UnknownType.Instance)?.Number.Value);
    }

    [Theory]
    [Trait("Tier", "1")]
    [InlineData(true, "named")]
    [InlineData(true, "anonymous")]
    [InlineData(true, "alias")]
    [InlineData(true, "lifted")]
    [InlineData(true, "unknown-value")]
    [InlineData(false, "named")]
    [InlineData(false, "anonymous")]
    [InlineData(false, "alias")]
    [InlineData(false, "lifted")]
    [InlineData(false, "unknown-value")]
    public void AnIntrinsicUnaryResult_DoesNotKeepItsOperandsValuePromise(bool negate, string wrapper)
    {
        var baseType = new HandBuiltModel().Type(negate ? "Int32" : "Boolean");
        IMistType operand = wrapper switch
        {
            "named" => HandBuiltModel.Refined(baseType, "Promise"),
            "anonymous" => HandBuiltModel.Refined(baseType),
            "alias" => new DeclarationFragments().Alias("Promise", HandBuiltModel.Refined(baseType, "Refinement")),
            "lifted" => new LiftedType(HandBuiltModel.Refined(baseType), negate ? 2L : true),
            _ => new UnknownValueType(HandBuiltModel.Refined(baseType)),
        };
        var kind = negate ? UnaryOperatorKind.Negate : UnaryOperatorKind.LogicalNot;

        Assert.Same(baseType, OperatorAdmissibility.UnaryResultType(kind, operand));
        Assert.Null(OperatorDecision.Unary(kind, operand));
    }

    [Fact]
    [Trait("Tier", "1")]
    public void ABooleanShell_AdmitsLogicalNot()
    {
        var boolean = new HandBuiltModel().Type("Boolean");

        Assert.Same(boolean, OperatorAdmissibility.UnaryResultType(UnaryOperatorKind.LogicalNot, boolean));
        Assert.Null(OperatorDecision.Unary(UnaryOperatorKind.LogicalNot, boolean));
    }

    [Theory]
    [Trait("Tier", "1")]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void ADeclaredUnaryResult_IsExactlyItsDeclaredContract(bool negate, bool refinedReturn)
    {
        var built = new HandBuiltModel();
        var operandType = HandBuiltModel.Class("Thing");
        var baseReturn = built.Type(negate ? "Int32" : "Boolean");
        IMistType returnType = refinedReturn ? HandBuiltModel.Refined(baseReturn, "ResultPromise") : baseReturn;
        var kind = negate ? UnaryOperatorKind.Negate : UnaryOperatorKind.LogicalNot;
        var declared = new Operator(negate ? OperatorKind.Minus : OperatorKind.Exclamation, returnType,
            [new Parameter("value", operandType)], new MemberModifiers { IsStatic = true });
        operandType.OperatorsList.Add(declared);
        var operand = HandBuiltModel.Refined(operandType, "InputPromise");

        Assert.Same(declared, OperatorAdmissibility.Unary(kind, operand).Selected?.Declaration);
        Assert.Same(returnType, OperatorAdmissibility.UnaryResultType(kind, operand));
        Assert.Null(OperatorDecision.Unary(kind, operand));
    }

    [Theory]
    [Trait("Tier", "1")]
    [InlineData(true)]
    [InlineData(false)]
    public void ADeclaredReturnMatchingTheOperandBase_DoesNotRetainTheOperandRefinement(bool negate)
    {
        var baseType = new HandBuiltModel().Type(negate ? "Int32" : "Boolean");
        var declaredReturn = new HandBuiltModel().Type(baseType.Name);
        baseType.OperatorsList.Add(new Operator(negate ? OperatorKind.Minus : OperatorKind.Exclamation,
            declaredReturn, [new Parameter("value", baseType)], new MemberModifiers { IsStatic = true }));
        var operand = HandBuiltModel.Refined(baseType, "Promise");

        Assert.Same(declaredReturn,
            OperatorAdmissibility.UnaryResultType(negate ? UnaryOperatorKind.Negate : UnaryOperatorKind.LogicalNot, operand));
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData(true, "class", "MIST0200")]
    [InlineData(false, "class", "MIST0200")]
    [InlineData(false, "Int32", "MIST0200")]
    [InlineData(true, "unknown", "MIST0201")]
    [InlineData(false, "unknown", "MIST0201")]
    public void AnUnsupportedUnary_IsRefusedAgainstItsOperand(bool negate, string type, string diagnosticId)
    {
        var built = new HandBuiltModel();
        IMistType operandType = type switch
        {
            "class" => HandBuiltModel.Class("Thing"),
            "unknown" => UnknownType.Instance,
            _ => built.Type(type),
        };
        var operand = new LocalReferenceExpression(HandBuiltModel.Node(31), new LocalVariable("value", operandType));
        var unary = new UnaryExpression(HandBuiltModel.Node(30),
            negate ? UnaryOperatorKind.Negate : UnaryOperatorKind.LogicalNot, operand, ErrorType.Instance);

        var diagnostic = Assert.Single(built.Analyze(new OperatorAnalyzer(), unary));

        Assert.Equal(diagnosticId, diagnostic.Number.Value);
        Assert.Equal("hand-built.mist", diagnostic.Location.Path);
        Assert.Equal(31, diagnostic.Position().Start.Column);
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData(true, "error-expression")]
    [InlineData(false, "error-expression")]
    [InlineData(true, "failed-local")]
    [InlineData(false, "failed-local")]
    [InlineData(true, "failed-sound-local")]
    [InlineData(false, "failed-sound-local")]
    [InlineData(true, "nested-unary")]
    [InlineData(false, "nested-unary")]
    public void AnOperandWithAPriorReportedFailure_IsNotRefusedAgain(bool negate, string shape)
    {
        Expression failed = shape switch
        {
            "failed-local" => new LocalReferenceExpression(HandBuiltModel.Node(31),
                new LocalVariable("failed", ErrorType.Instance) { IsFailed = true }),
            "failed-sound-local" => new LocalReferenceExpression(HandBuiltModel.Node(31),
                new LocalVariable("failed", HandBuiltModel.Class("Thing")) { IsFailed = true }),
            "nested-unary" => new UnaryExpression(HandBuiltModel.Node(31), UnaryOperatorKind.Negate,
                new ErrorExpression(HandBuiltModel.Node(32)), ErrorType.Instance),
            _ => new ErrorExpression(HandBuiltModel.Node(31)),
        };
        var unary = new UnaryExpression(HandBuiltModel.Node(30),
            negate ? UnaryOperatorKind.Negate : UnaryOperatorKind.LogicalNot, failed, ErrorType.Instance);

        Assert.Empty(new HandBuiltModel().Analyze(new OperatorAnalyzer(), unary));
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData(true, "unresolved")]
    [InlineData(false, "unresolved")]
    [InlineData(true, "local")]
    [InlineData(false, "local")]
    public void AnUnreportedErrorSentinel_IsStillRefused(bool negate, string shape)
    {
        Expression operand = shape == "local"
            ? new LocalReferenceExpression(HandBuiltModel.Node(31), new LocalVariable("value", ErrorType.Instance))
            : new UnresolvedExpression(HandBuiltModel.Node(31));
        var unary = new UnaryExpression(HandBuiltModel.Node(30),
            negate ? UnaryOperatorKind.Negate : UnaryOperatorKind.LogicalNot, operand, ErrorType.Instance);

        Assert.Equal("MIST0200",
            Assert.Single(new HandBuiltModel().Analyze(new OperatorAnalyzer(), unary)).Number.Value);
    }
}