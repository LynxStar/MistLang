using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.HandBuiltModel;
using static Mist.Tests.Helpers.HandBuiltRefinements;

namespace Mist.Tests.ProgramAnalysis;

internal sealed class RealFlowBody
{
    public HandBuiltRefinements Values { get; } = new();
    private readonly Dictionary<string, IMistType> types;
    private readonly Operator booleanNot;

    public RealFlowBody()
    {
        var h = Values;
        var boolean = h.Built.Type("Boolean");
        booleanNot = new Operator(OperatorKind.Exclamation, boolean, [new Parameter("a", boolean)],
            new MemberModifiers { IsIntrinsic = true, IsStatic = true, IsPure = true, IsReadonly = true, Accessibility = Accessibility.Public })
        { DeclaringType = boolean };
        var real = h.Double;
        var single = h.Built.Type("Single");
        var amount = h.Built.Type("Decimal");
        var unit = h.Between(real, 0.0, 1.0, "Unit");
        types = new(StringComparer.Ordinal)
        {
            ["double"] = real,
            ["float"] = single,
            ["decimal"] = amount,
            ["Positive"] = h.Refine(real, ComparisonKind.GreaterThan, 0.0, "Positive"),
            ["NonNegative"] = h.Refine(real, ComparisonKind.GreaterThanOrEqual, 0.0, "NonNegative"),
            ["NonPositive"] = h.Refine(real, ComparisonKind.LessThanOrEqual, 0.0, "NonPositive"),
            ["Unit"] = unit,
            ["PositiveUnit"] = h.Refine(unit, ComparisonKind.GreaterThan, 0.0, "PositiveUnit"),
            ["StrictUnit"] = RefinePattern(real, new ConjunctionPattern(Node(), real,
                [h.Compare(real, ComparisonKind.GreaterThan, 0.0), h.Compare(real, ComparisonKind.LessThan, 1.0)]), "StrictUnit"),
            ["Outside"] = RefinePattern(real, new DisjunctionPattern(Node(), real,
                [h.Compare(real, ComparisonKind.LessThan, 0.0), h.Compare(real, ComparisonKind.GreaterThan, 1.0)]), "Outside"),
            ["NotZero"] = RefinePattern(real, new NegatedPattern(Node(), real, h.Compare(real, ComparisonKind.Equal, 0.0)), "NotZero"),
            ["NotPositive"] = RefinePattern(real, new NegatedPattern(Node(), real, h.Compare(real, ComparisonKind.GreaterThan, 0.0)), "NotPositive"),
            ["FloatUnit"] = h.Between(single, 0.0f, 1.0f, "FloatUnit"),
            ["Tenth"] = h.Refine(real, ComparisonKind.LessThanOrEqual, 0.1, "Tenth"),
            ["DecimalUnit"] = h.Between(amount, 0.0m, 1.0m, "DecimalUnit"),
            ["DecimalNonNegative"] = h.Refine(amount, ComparisonKind.GreaterThanOrEqual, 0.0m, "DecimalNonNegative"),
            ["DecimalNonPositive"] = h.Refine(amount, ComparisonKind.LessThanOrEqual, 0.0m, "DecimalNonPositive"),
            ["DecimalPositive"] = h.Refine(amount, ComparisonKind.GreaterThan, 0.0m, "DecimalPositive"),
        };
    }

    public IMistType Type(string name)
    {
        return types[name];
    }

    public Expression Number(string domain, double value)
    {
        return domain switch
        {
            "double" => Values.Literal(value),
            "float" => Values.Literal((float)value),
            "decimal" => Values.Literal((decimal)value),
            _ => throw new ArgumentOutOfRangeException(nameof(domain)),
        };
    }

    public Expression Positive(Expression input)
    {
        return Values.Binary(input, BinaryOperatorKind.GreaterThan, Values.Literal(0.0));
    }

    public Expression Guard(string domain, Expression input, string guard)
    {
        var h = Values;
        var zero = Number(domain, 0);
        var one = Number(domain, 1);

        return guard switch
        {
            "> 0" => h.Binary(input, BinaryOperatorKind.GreaterThan, zero),
            "0 <" => h.Binary(zero, BinaryOperatorKind.LessThan, input),
            ">= 0" => h.Binary(input, BinaryOperatorKind.GreaterThanOrEqual, zero),
            "<= 0" => h.Binary(input, BinaryOperatorKind.LessThanOrEqual, zero),
            "== 0" => h.Binary(input, BinaryOperatorKind.Equal, zero),
            "0 ==" => h.Binary(zero, BinaryOperatorKind.Equal, input),
            "!= 0" => h.Binary(input, BinaryOperatorKind.NotEqual, zero),
            "<= double tenth" => h.Binary(input, BinaryOperatorKind.LessThanOrEqual, h.Literal(0.1)),
            "<= float tenth" => h.Binary(input, BinaryOperatorKind.LessThanOrEqual, h.Literal(0.1f)),
            "not positive" => Not(h.Binary(input, BinaryOperatorKind.GreaterThan, zero)),
            "not nonpositive" => Not(h.Binary(input, BinaryOperatorKind.LessThanOrEqual, zero)),
            "double negation" => Not(Not(h.Binary(input, BinaryOperatorKind.GreaterThan, zero))),
            "between" => h.And(h.Binary(input, BinaryOperatorKind.GreaterThanOrEqual, zero), h.Binary(input, BinaryOperatorKind.LessThanOrEqual, one)),
            "between reversed" => h.And(h.Binary(zero, BinaryOperatorKind.LessThanOrEqual, input), h.Binary(one, BinaryOperatorKind.GreaterThanOrEqual, input)),
            "negated between" => Not(h.And(h.Binary(input, BinaryOperatorKind.GreaterThanOrEqual, zero), h.Binary(input, BinaryOperatorKind.LessThanOrEqual, one))),
            "outside" => h.Binary(h.Binary(input, BinaryOperatorKind.LessThan, zero), BinaryOperatorKind.LogicalOr, h.Binary(input, BinaryOperatorKind.GreaterThan, one)),
            "outside reversed" => h.Binary(h.Binary(zero, BinaryOperatorKind.GreaterThan, input), BinaryOperatorKind.LogicalOr, h.Binary(one, BinaryOperatorKind.LessThan, input)),
            "neither outside comparison" => h.And(Not(h.Binary(input, BinaryOperatorKind.LessThan, zero)), Not(h.Binary(input, BinaryOperatorKind.GreaterThan, one))),
            "above another parameter" => h.Binary(input, BinaryOperatorKind.GreaterThan, Local(h.Double, "limit")),
            _ => throw new ArgumentOutOfRangeException(nameof(guard)),
        };
    }

    private UnaryExpression Not(Expression operand)
    {
        return new UnaryExpression(Node(), UnaryOperatorKind.LogicalNot, operand, booleanNot.ReturnType)
        { Operator = booleanNot, ParameterTypes = [booleanNot.ParametersList[0].Type] };
    }

    public static Statement Set(Expression target, Expression value)
    {
        return Evaluate(Assign(target, value));
    }

    public static Statement Guarded(Expression guard, Statement use, bool earlyReturn)
    {
        return earlyReturn ? Block(If(guard, new ReturnStatement(null, null)), use) : If(guard, use);
    }

    public static CastExpression Cast(Expression value, IMistType target)
    {
        return new CastExpression(Node(), value, target, [], target);
    }

    public static InvocationExpression Invoke(Method method, params Expression[] arguments)
    {
        return new InvocationExpression(Node(), new MethodReferenceExpression(Node(), method), arguments, method.ReturnType, method,
            [.. method.ParametersList.Select(parameter => parameter.Type)]);
    }

    public void Check(string? expected, params Statement[] statements)
    {
        var findings = Values.Built.Analyze(new RefinementAnalyzer(), Block(statements));
        if(expected is null)
        {
            Assert.Empty(findings);
        }
        else
        {
            Assert.Equal(expected, Assert.Single(findings).Number.Value);
        }
    }
}