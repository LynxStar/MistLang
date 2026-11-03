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

        Assert.Same(f.Numeric, f.Divide().Type);
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
            f.Numeric, facts, Admitted.Exactly(-1)));
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
        f.Fragments.Binding.TargetType = f.Numeric;
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
            BinaryOperatorKind.Divide, f.Numeric, Admitted.Exactly(20), facts));
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

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void WrappedComparisonBoundsCannotManufactureSafeIntegerDivision(bool mirrored, bool holds)
    {
        var f = new Fixture();
        var dividend = f.Local("a", null);
        _ = f.Local("d", -1);
        var bound = WrappedMinimum(f.Numeric);
        var condition = new BinaryExpression(HandBuiltModel.Node(), mirrored ? bound : dividend,
            holds ? BinaryOperatorKind.Equal : BinaryOperatorKind.NotEqual, mirrored ? dividend : bound,
            f.Fragments.Built.Type("Boolean"));
        var guarded = new BodyScope(f.Fragments.Scope);
        OperandFacts.Learn(f.Fragments.Binding, guarded, condition, holds);
        f.Fragments.Binding.Scope = guarded;

        _ = f.Divide("a");

        Assert.Contains(f.Fragments.Diagnostics.ToImmutableArray(), diagnostic => diagnostic.Number == Mist.Common.Diagnostics.ProgramModelDiagnostics.InvalidArithmetic.Number);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void WrappedIntegralBoundsCannotManufactureSafeDecimalSubtraction(bool mirrored, bool holds)
    {
        var f = new Fixture("Decimal");
        var dividend = f.Local("a", null);
        var bound = WrappedMinimum(f.Fragments.Built.Type("Int32"));
        var condition = new BinaryExpression(HandBuiltModel.Node(), mirrored ? bound : dividend,
            holds ? BinaryOperatorKind.Equal : BinaryOperatorKind.NotEqual, mirrored ? dividend : bound,
            f.Fragments.Built.Type("Boolean"));
        var guarded = new BodyScope(f.Fragments.Scope);
        OperandFacts.Learn(f.Fragments.Binding, guarded, condition, holds);
        f.Fragments.Binding.Scope = guarded;

        _ = f.Binary(BindingFragments.Identifier("a"), "-",
            new FloatLiteralExpressionPrimaryNode { FloatLiteral = BindingFragments.Token("79228162514264337593543950335m") });

        Assert.Contains(f.Fragments.Diagnostics.ToImmutableArray(), diagnostic => diagnostic.Number == Mist.Common.Diagnostics.ProgramModelDiagnostics.InvalidArithmetic.Number);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UpperUnsignedConstantsRetainThePlainNumericDivisionType(bool local)
    {
        var f = new Fixture("UInt64");
        PrimaryNode right = new IntegerLiteralExpressionPrimaryNode { IntegerLiteral = BindingFragments.Token("18446744073709551615UL") };
        if(local)
        {
            var reference = (LocalReferenceExpression)f.Fragments.Local("d", f.Numeric);
            OperandFacts.Declared(f.Fragments.Binding, f.Fragments.Scope, reference.Local, HandBuiltModel.Literal(f.Numeric, ulong.MaxValue));
            right = BindingFragments.Identifier("d");
        }

        Assert.Same(f.Numeric, f.Binary(new IntegerLiteralExpressionPrimaryNode { IntegerLiteral = BindingFragments.Token("1UL") }, "/", right).Type);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FoldedUnsignedConstantsPreserveNonzeroAndZeroDecisions(bool zero)
    {
        var f = new Fixture("UInt64");
        var local = (LocalReferenceExpression)f.Fragments.Local("d", f.Numeric);
        var folded = new BinaryExpression(HandBuiltModel.Node(), HandBuiltModel.Literal(f.Numeric, zero ? ulong.MaxValue : ulong.MaxValue - 1),
            BinaryOperatorKind.Add, HandBuiltModel.Literal(f.Numeric, 1UL), f.Numeric);
        OperandFacts.Declared(f.Fragments.Binding, f.Fragments.Scope, local.Local, folded);
        var division = f.Binary(new IntegerLiteralExpressionPrimaryNode { IntegerLiteral = BindingFragments.Token("1UL") }, "/", BindingFragments.Identifier("d"));

        if(zero)
        {
            Assert.Contains(f.Fragments.Diagnostics.ToImmutableArray(), diagnostic => diagnostic.Number == Mist.Common.Diagnostics.ProgramModelDiagnostics.InvalidArithmetic.Number);
        }
        else
        {
            Assert.Same(f.Numeric, division.Type);
            Assert.Empty(f.Fragments.Diagnostics.ToImmutableArray());
        }
    }

    [Fact]
    public void FullWidthFactsReachNestedContextsButNeverMutableNames()
    {
        var f = new Fixture("UInt64");
        var local = (LocalReferenceExpression)f.Fragments.Local("d", f.Numeric);
        OperandFacts.Declared(f.Fragments.Binding, f.Fragments.Scope, local.Local, HandBuiltModel.Literal(f.Numeric, ulong.MaxValue));
        var nested = new BindingContext(new BodyScope(f.Fragments.Scope), null, f.Fragments.Binding);
        Assert.Equal((Int128?)ulong.MaxValue, IntegerOperandValue.Exact(nested, nested.Scope, local));

        nested.AssignedLocals = new HashSet<string> { "d" };
        Assert.Null(IntegerOperandValue.Exact(nested, nested.Scope, local));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OnlyIntrinsicConversionsPreserveIntegralGuardValues(bool intrinsic)
    {
        var f = new Fixture("Decimal");
        var integer = f.Fragments.Built.Type("Int32");
        var conversion = HandBuiltModel.Conversion(integer, integer, f.Numeric, isIntrinsic: intrinsic);
        var converted = new ImplicitConversionExpression(HandBuiltModel.Node(), WrappedMinimum(integer), f.Numeric,
            [new ConversionFunction(conversion, integer, f.Numeric)]);
        var subject = f.Local("a", null);
        OperandFacts.Learn(f.Fragments.Binding, f.Fragments.Scope, new BinaryExpression(HandBuiltModel.Node(), subject,
            BinaryOperatorKind.Equal, converted, f.Fragments.Built.Type("Boolean")), true);
        var values = DecimalOperandFacts.For(f.Fragments.Binding, f.Fragments.Scope, subject);

        Assert.Equal(intrinsic ? -2147483648m : null, DecimalArithmeticProof.Exact(values));
    }

    private static Expression WrappedMinimum(ClassType type)
    {
        return new UnaryExpression(HandBuiltModel.Node(), UnaryOperatorKind.Negate,
            HandBuiltModel.Literal(type, (long)int.MinValue), type);
    }

    private sealed class Fixture
    {
        public BindingFragments Fragments { get; } = new();
        public ClassType Numeric { get; }

        public Fixture(string name = "Int32")
        {
            Numeric = Fragments.Built.Type(name);
            Fragments.Register(Numeric);
            var refined = new HandBuiltRefinements();
            Add("Nonzero", new NegatedPattern(HandBuiltModel.Node(), Numeric,
                refined.Compare(Numeric, ComparisonKind.Equal, 0L)));
            if(name == "Int32")
            {
                Add("QuotientDividend", new NegatedPattern(HandBuiltModel.Node(), Numeric,
                    refined.Compare(Numeric, ComparisonKind.Equal, (long)int.MinValue)));
                Add("QuotientDivisor", new NegatedPattern(HandBuiltModel.Node(), Numeric,
                    refined.Compare(Numeric, ComparisonKind.Equal, -1L)));
            }

            var result = new ResultModel().Result(Numeric, Fragments.Built.Type("DivisionError"));
            var helpers = Fragments.Built.Type("ResultArithmetic");
            Fragments.Register(helpers);
            helpers.MethodsList.Add(new Method("DivideChecked" + (name == "Int32" ? "" : name), result,
                [new Parameter("a", Numeric), new Parameter("b", Numeric)],
                DeclarationFragments.Public with { IsStatic = true })
            { DeclaringType = helpers });
        }

        private void Add(string name, Pattern predicate)
        {
            Numeric.NestedTypes.Add(new TypeAlias(name, "Standard", Numeric.Scope, Numeric, new TypeAliasDeclarationNode())
            {
                AliasedType = new RefinedType(Numeric, HandBuiltModel.Node(), name) { Predicate = predicate },
            });
        }

        public LocalReferenceExpression Local(string name, long? initial)
        {
            var local = (LocalReferenceExpression)Fragments.Local(name, Numeric);
            if(initial is { } value)
            {
                OperandFacts.Declared(Fragments.Binding, Fragments.Scope, local.Local, Number(value));
            }

            return local;
        }

        public LiteralExpression Number(long value)
        {
            return HandBuiltModel.Literal(Numeric, value);
        }

        public Expression Divide(string? left = null)
        {
            return Binary(left is null ? new IntegerLiteralExpressionPrimaryNode { IntegerLiteral = BindingFragments.Token("20") }
                : BindingFragments.Identifier(left), "/", BindingFragments.Identifier("d"));
        }

        public Expression Binary(PrimaryNode left, string op, PrimaryNode right)
        {
            var expression = BindingFragments.Expression(left);
            var assignment = ((AssignmentFormExpressionNode)expression).Assignment;
            var additive = ((RelationalComparisonRelationalNode)assignment.Target.Left.Left.Left.Left).Left;
            if(op == "-")
            {
                additive.Group0 = [new AdditiveNodeGroup0
                {
                    Operator = BindingFragments.Token(op),
                    Right = new MultiplicativeNode { Left = new NonRangeRangeNode { Operand = MistAstGenerator.ExtractUnary(BindingFragments.Expression(right)) } },
                }];
                return ExpressionBinder.BindExpression(expression, Fragments.Binding, Fragments.Builder);
            }

            additive.Left.Group0 = [new MultiplicativeNodeGroup0
            {
                Operator = BindingFragments.Token(op),
                Right = new NonRangeRangeNode
                {
                    Operand = MistAstGenerator.ExtractUnary(BindingFragments.Expression(right)),
                },
            }];
            return ExpressionBinder.BindExpression(expression, Fragments.Binding, Fragments.Builder);
        }
    }
}