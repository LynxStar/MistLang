using Mist.Generated;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Generators;
using Mist.Tests.Helpers;

namespace Mist.Tests.Construction;

[Trait("Tier", "2")]
public sealed class ArithmeticBindingTests
{
    [Fact]
    public void APositiveGuardChangesOnlySubsequentExpressionTypesInItsScope()
    {
        var f = new Fixture();
        var divisor = f.Local("d", null);
        var earlier = f.Divide();
        var guarded = new BodyScope(f.Fragments.Scope);
        OperandFacts.Learn(f.Fragments.Binding, guarded, new BinaryExpression(HandBuiltModel.Node(), divisor,
            BinaryOperatorKind.GreaterThan, f.Number(0), f.Fragments.Built.Type("Boolean")), true);
        f.Fragments.Binding.Scope = guarded;

        Assert.Same(f.Integer, f.Divide().Type);
        Assert.NotNull(ResultTypes.Of(earlier.Type));
        f.Fragments.Binding.Scope = f.Fragments.Scope;
        Assert.NotNull(ResultTypes.Of(f.Divide().Type));
    }

    [Fact]
    public void ExcludingTheMinimumProvesDivisionByNegativeOne()
    {
        var f = new Fixture();
        var dividend = f.Local("a", null);
        _ = f.Local("d", -1);
        OperandFacts.Learn(f.Fragments.Binding, f.Fragments.Scope, new BinaryExpression(HandBuiltModel.Node(), dividend,
            BinaryOperatorKind.NotEqual, f.Number(int.MinValue), f.Fragments.Built.Type("Boolean")), true);
        var facts = OperandFacts.For(f.Fragments.Binding, f.Fragments.Scope, dividend);
        Assert.Equal(ArithmeticProof.Verdict.Safe, ArithmeticProof.Judge(BinaryOperatorKind.Divide,
            f.Integer, facts, Admitted.Exactly(-1)));
    }

    [Theory]
    [InlineData("ref-call")]
    [InlineData("ref-alias")]
    [InlineData("inferred-ref-alias")]
    [InlineData("readonly-ref-alias")]
    [InlineData("ref-return")]
    public void ExposedOriginalLocationsCannotSupplyProof(string exposure)
    {
        var f = new Fixture();
        var read = BindingFragments.Expression(BindingFragments.Identifier("d"));
        Storm.Runtime.SyntaxNode syntax = exposure switch
        {
            "ref-call" => new RefArgumentArgumentNode { Expression = read },
            "inferred-ref-alias" => new InferredVariableDeclarationVariableDeclarationStatementNode
            {
                Name = BindingFragments.Token("alias"),
                OwnershipSpecifier = new RefSpecifierOwnershipSpecifierNode(),
                Initializer = read,
            },
            "ref-return" => new ReturnStatementNode { RefKeyword = BindingFragments.Token("ref"), Value = read },
            _ => new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Name = BindingFragments.Token("alias"),
                Type = BindingFragments.TypeName("Int32"),
                OwnershipSpecifier = exposure == "readonly-ref-alias"
                    ? new ReadonlyRefSpecifierOwnershipSpecifierNode() : new RefSpecifierOwnershipSpecifierNode(),
                Initializer = read,
            },
        };
        f.Fragments.Binding.AssignedLocals = OperandFacts.AssignedNames(syntax);
        _ = f.Local("d", 1);

        Assert.NotNull(ResultTypes.Of(f.Divide().Type));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BranchAndLoopWritesCannotRetainTheOriginalBindingProof(bool loop)
    {
        var f = new Fixture();
        var target = (AssignmentFormExpressionNode)BindingFragments.Expression(BindingFragments.Identifier("d"));
        target.Assignment.Group0 = new AssignmentNodeGroup0
        {
            Operator = BindingFragments.Token("="),
            Value = BindingFragments.Expression(new IntegerLiteralExpressionPrimaryNode { IntegerLiteral = BindingFragments.Token("0") }),
        };
        var body = new BlockNode
        {
            Statements = [new ExpressionStatementStatementNode
        {
            ExpressionStatement = new ExpressionStatementNode { Expression = target },
        }]
        };
        var condition = BindingFragments.Expression(new FalseLiteralExpressionPrimaryNode());
        StatementNode statement = loop
            ? new WhileStatementStatementNode { WhileStatement = new WhileStatementNode { Condition = condition, Body = body } }
            : new IfStatementStatementNode { IfStatement = new IfStatementNode { Condition = condition, ThenBranch = body } };
        f.Fragments.Binding.AssignedLocals = OperandFacts.AssignedNames(statement);
        _ = f.Local("d", 1);
        _ = StatementBinder.BindStatement(statement, f.Fragments.Binding, f.Fragments.Builder);
        Assert.NotNull(ResultTypes.Of(f.Divide().Type));
    }

    [Fact]
    public void AnExpectedIntegerDoesNotSupplySafety()
    {
        var f = new Fixture();
        _ = f.Local("d", null);
        f.Fragments.Binding.TargetType = f.Integer;
        Assert.NotNull(ResultTypes.Of(f.Divide().Type));
    }

    [Fact]
    public void AConstantZeroIsRefusedAtBinding()
    {
        var f = new Fixture();
        _ = f.Local("d", 0);
        _ = f.Divide();
        Assert.Contains(f.Fragments.Diagnostics.ToImmutableArray(), diagnostic => diagnostic.Number == Mist.Common.Diagnostics.ProgramModelDiagnostics.InvalidArithmetic.Number);
    }

    [Fact]
    public void FoldedByteWrappingDoesNotManufactureNonzeroProof()
    {
        var f = new Fixture();
        var narrow = f.Fragments.Built.Type("Byte");
        var sum = new BinaryExpression(HandBuiltModel.Node(), HandBuiltModel.Literal(narrow, 255L),
            BinaryOperatorKind.Add, HandBuiltModel.Literal(narrow, 1L), narrow);
        var facts = OperandFacts.For(f.Fragments.Binding, f.Fragments.Scope, sum);
        Assert.Equal(ArithmeticProof.Verdict.Invalid, ArithmeticProof.Judge(
            BinaryOperatorKind.Divide, f.Integer, Admitted.Exactly(20), facts));
    }

    [Fact]
    public void ASignedNarrowNegationRetainsItsWrappingFact()
    {
        var f = new Fixture();
        var narrow = f.Fragments.Built.Type("Int16");
        var negation = new UnaryExpression(HandBuiltModel.Node(), UnaryOperatorKind.Negate,
            HandBuiltModel.Literal(narrow, (long)short.MinValue), narrow);
        Assert.Equal(Admitted.Exactly(short.MinValue), OperandFacts.For(f.Fragments.Binding, f.Fragments.Scope, negation));
    }

    private sealed class Fixture
    {
        public BindingFragments Fragments { get; } = new();
        public ClassType Integer => Fragments.Built.Type("Int32");

        public Fixture()
        {
            var refined = new HandBuiltRefinements();
            Add("Nonzero", new NegatedPattern(HandBuiltModel.Node(), Integer,
                refined.Compare(Integer, ComparisonKind.Equal, 0L)));
            Add("QuotientDividend", new NegatedPattern(HandBuiltModel.Node(), Integer,
                refined.Compare(Integer, ComparisonKind.Equal, (long)int.MinValue)));
            Add("QuotientDivisor", new NegatedPattern(HandBuiltModel.Node(), Integer,
                refined.Compare(Integer, ComparisonKind.Equal, -1L)));
            var result = new ResultModel().Result(Integer, Fragments.Built.Type("DivisionError"));
            var helpers = Fragments.Built.Type("ResultArithmetic");
            Fragments.Register(helpers);
            helpers.MethodsList.Add(new Method("DivideChecked", result,
                [new Parameter("a", Integer), new Parameter("b", Integer)],
                DeclarationFragments.Public with { IsStatic = true })
            { DeclaringType = helpers });
        }

        private void Add(string name, Pattern predicate)
        {
            Integer.NestedTypes.Add(new RefinedType(Integer, HandBuiltModel.Node(), name) { Predicate = predicate });
        }

        public LocalReferenceExpression Local(string name, long? initial)
        {
            var local = (LocalReferenceExpression)Fragments.Local(name, Integer);
            if(initial is { } value)
            {
                OperandFacts.Declared(Fragments.Binding, Fragments.Scope, local.Local, Number(value));
            }

            return local;
        }

        public LiteralExpression Number(long value)
        {
            return HandBuiltModel.Literal(Integer, value);
        }

        public Expression Divide()
        {
            var expression = BindingFragments.Expression(new IntegerLiteralExpressionPrimaryNode { IntegerLiteral = BindingFragments.Token("20") });
            var assignment = ((AssignmentFormExpressionNode)expression).Assignment;
            var additive = ((RelationalComparisonRelationalNode)assignment.Target.Left.Left.Left.Left).Left;
            additive.Left.Group0 = [new MultiplicativeNodeGroup0
            {
                Operator = BindingFragments.Token("/"),
                Right = new NonRangeRangeNode
                {
                    Operand = MistAstGenerator.ExtractUnary(BindingFragments.Expression(BindingFragments.Identifier("d"))),
                },
            }];
            return ExpressionBinder.BindExpression(expression, Fragments.Binding, Fragments.Builder);
        }
    }
}