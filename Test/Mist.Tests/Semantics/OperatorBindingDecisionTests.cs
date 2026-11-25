using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Generators;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class OperatorBindingDecisionTests
{
    private readonly BindingFragments fragments = new();

    private ClassType Int32 => fragments.Built.Type("Int32");

    private ClassType Void => fragments.Built.Type("Void");

    [Theory]
    [InlineData("animal", "Dog", "Narrowing", true)]
    [InlineData("choice", "Int32", "UnionExtraction", true)]
    [InlineData("dog", "Animal", "Widening", false)]
    [InlineData("dog", "Dog", "Identity", false)]
    public void OnlyACheckedConversionAnswersAnOptional(string operand, string target, string kind, bool optional)
    {
        DeclareAnimals();
        _ = fragments.Local("choice", fragments.Built.Interner.GetOrCreateUnionType([Int32, fragments.Built.Type("String")]));

        var conversion = Assert.IsType<AsExpression>(Bind(As(operand, target)));

        Assert.Empty(fragments.Diagnostics.ToImmutableArray());
        Assert.Equal(kind, conversion.Kind.ToString());
        Assert.Same(conversion.TargetType, optional ? NonVoid(conversion.Type) : conversion.Type);
    }

    [Theory]
    [InlineData("dog", "Missing")]
    [InlineData("failed", "Dog")]
    public void AnUnresolvedEndIsNotAlsoReportedAsUnrelated(string operand, string target)
    {
        DeclareAnimals();
        _ = fragments.Local("failed", ErrorType.Instance, failed: true);

        _ = Assert.IsType<ErrorExpression>(Bind(As(operand, target)));

        Assert.DoesNotContain(fragments.Diagnostics.ToImmutableArray(),
            diagnostic => diagnostic.Number == ProgramModelDiagnostics.AsConversionNoRelationship.Number);
    }

    [Fact]
    public void AnOptionalRightOperandKeepsTheCoalesceOptional()
    {
        var optional = fragments.Built.Interner.GetOrCreateUnionType([Int32, Void]);
        _ = fragments.Local("first", optional);
        _ = fragments.Local("second", optional);
        _ = fragments.Local("last", Int32);

        var stillAbsent = Assert.IsType<CoalesceExpression>(Bind(Coalesce("first", "second")));
        var settled = Assert.IsType<CoalesceExpression>(Bind(Coalesce("first", "second", "last")));

        Assert.Same(Int32, NonVoid(stillAbsent.Type));
        Assert.Same(Int32, settled.Type);
        Assert.Same(Int32, NonVoid(settled.Left.Type));
    }

    [Fact]
    public void AGenericBinaryOperatorRecordsTheArgumentsItWasClosedWith()
    {
        var results = new GenericResults(fragments);
        var composed = results.Declare(OperatorKind.Plus, "E", "F");
        _ = fragments.Local("left", results.Of(results.Error));
        _ = fragments.Local("right", results.Of(results.Division));

        var sum = Assert.IsType<BinaryExpression>(Bind(Add("left", "right")));

        Assert.Same(composed, sum.Operator);
        Assert.Equal([results.Error, results.Division], RecordedFor(composed));
    }

    [Fact]
    public void AGenericUnaryOperatorRecordsTheArgumentsItWasClosedWith()
    {
        var results = new GenericResults(fragments);
        var negation = results.Declare(OperatorKind.Minus, "E");
        _ = fragments.Local("value", results.Of(results.Division));

        var negated = Assert.IsType<UnaryExpression>(Bind(Negate("value")));

        Assert.Same(negation, negated.Operator);
        Assert.Equal([results.Division], RecordedFor(negation));
    }

    private void DeclareAnimals()
    {
        var animal = HandBuiltModel.Class("Animal");
        fragments.Register(animal);
        var dog = HandBuiltModel.Class("Dog", animal);
        fragments.Register(dog);
        _ = fragments.Local("animal", animal);
        _ = fragments.Local("dog", dog);
    }

    private IMistType NonVoid(IMistType optional)
    {
        var union = Assert.IsType<UnionType>(optional);
        Assert.Contains(Void, union.Constituents);
        return Assert.Single(union.Constituents, constituent => !ReferenceEquals(constituent, Void));
    }

    private IReadOnlyList<IMistType> RecordedFor(Method method)
    {
        return Assert.Single(fragments.Built.Model.GenericCalls.All(), call => ReferenceEquals(call.Method, method)).TypeArguments;
    }

    private Expression Bind(ExpressionNode expression)
    {
        return ExpressionBinder.BindExpression(expression, fragments.Binding, fragments.Builder);
    }

    private static ExpressionNode As(string operand, string target)
    {
        var expression = Read(operand);
        var conversion = BindingFragments.At<AsExpressionRelationalNode>();
        conversion.Left = AdditiveOf(Read(operand));
        conversion.TargetType = BindingFragments.TypeName(target);
        expression.AsAssignment().Target.Left.Left.Left.Left = conversion;
        return expression;
    }

    private static ExpressionNode Coalesce(string first, params string[] rest)
    {
        var expression = Read(first);
        expression.AsAssignment().Target.LogicalOrs = [.. rest.Select(name => Read(name).AsAssignment().Target.Left)];
        return expression;
    }

    private static ExpressionNode Add(string left, string right)
    {
        var expression = Read(left);
        AdditiveOf(expression).Group0 = [new AdditiveNodeGroup0
        {
            Operator = BindingFragments.Token("+"),
            Right = new MultiplicativeNode { Left = new NonRangeRangeNode { Operand = MistAstGenerator.ExtractUnary(Read(right)) } },
        }];
        return expression;
    }

    private static ExpressionNode Negate(string operand)
    {
        var negate = BindingFragments.At<NegateUnaryNode>();
        negate.Operand = MistAstGenerator.ExtractUnary(Read(operand));
        return BindingFragments.Expression(negate);
    }

    private static ExpressionNode Read(string name)
    {
        return BindingFragments.Expression(BindingFragments.Identifier(name));
    }

    private static AdditiveNode AdditiveOf(ExpressionNode expression)
    {
        return ((RelationalComparisonRelationalNode)expression.AsAssignment().Target.Left.Left.Left.Left).Left;
    }

    private sealed class GenericResults
    {
        private readonly BindingFragments fragments;
        private readonly ClassType definition = new("Result<,>", "Standard", null, null, null);

        public GenericResults(BindingFragments fragments)
        {
            this.fragments = fragments;
            definition.TypeParametersList.AddRange([
                new TypeParameter("T", 0, definition.Scope, definition), new TypeParameter("E", 1, definition.Scope, definition)]);
            Error = HandBuiltModel.Class("Error");
            Division = HandBuiltModel.Class("DivisionError", Error);
        }

        public ClassType Error { get; }

        public ClassType Division { get; }

        public ConstructedType Of(IMistType error)
        {
            return fragments.Built.Interner.GetOrCreateConstructedType(definition, [fragments.Built.Type("Int32"), error]);
        }

        public Operator Declare(OperatorKind kind, params string[] errors)
        {
            var owner = fragments.Built.Type("Int32");
            var parameters = errors.Select((name, i) => new TypeParameter(name, i, owner.Scope, null)).ToList();
            var declared = new Operator(kind, Of(parameters[0]), [.. parameters.Select((parameter, i) => new Parameter($"p{i}", Of(parameter)))],
                new MemberModifiers { IsStatic = true })
            {
                DeclaringType = owner,
                DeclarationNode = HandBuiltModel.Node(),
            };
            parameters.ForEach(parameter => parameter.DeclaringMethod = declared);
            declared.TypeParametersList.AddRange(parameters);
            owner.OperatorsList.Add(declared);
            return declared;
        }
    }
}