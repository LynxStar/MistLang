using System.Globalization;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramAnalysis.Refinements;

internal sealed record LiteralValue(object Value, string TypeName)
{
    public static LiteralValue? Of(Expression? expression, Func<LocalVariable, LiteralValue?>? held = null)
    {
        return expression switch
        {
            LiteralExpression { Value: string body, Type: var type } => Decoded(body) is { } text ? new LiteralValue(text, type.FullyQualifiedName) : null,
            LiteralExpression { Value: float number, Type: var type } when float.IsFinite(number) => new LiteralValue(number, type.FullyQualifiedName),
            LiteralExpression { Value: double number, Type: var type } when double.IsFinite(number) => new LiteralValue(number, type.FullyQualifiedName),
            LiteralExpression { Value: decimal number, Type: var type } => new LiteralValue(number, type.FullyQualifiedName),
            LiteralExpression { Value: var value, Type: var type } when IntegralConstant.Of(value) is { } number
                => new LiteralValue(Integral(number, type), type.FullyQualifiedName),
            LocalReferenceExpression local => held?.Invoke(local.Local),
            CloneExpression clone => Of(clone.Source, held),
            UnaryExpression { OperatorKind: UnaryOperatorKind.Negate, Operator: null or { Modifiers.IsIntrinsic: true }, Operand: var operand } => Of(operand, held)?.Negated(),
            _ => null,
        };
    }

    private static object Integral(Int128 value, IMistType type)
    {
        return type.FullyQualifiedName == WellKnownTypes.UInt64.FullyQualifiedName ? (ulong)value : (long)value;
    }

    public static string? Decoded(string body)
    {
        var syntax = SyntaxFactory.ParseExpression($"\"{body}\"");
        return syntax is LiteralExpressionSyntax literal && !literal.ContainsDiagnostics && literal.Token.Value is string text ? text : null;
    }

    public LiteralValue? ConvertedTo(IMistType target)
    {
        var name = Requirement.Unrefined(target).FullyQualifiedName;

        if(name == TypeName)
        {
            return this;
        }

        if(Value is string || (TypeName == WellKnownTypes.Decimal.FullyQualifiedName
            && (name == WellKnownTypes.Single.FullyQualifiedName || name == WellKnownTypes.Double.FullyQualifiedName)))
        {
            return null;
        }

        try
        {
            if(name == WellKnownTypes.Double.FullyQualifiedName)
            {
                var number = Convert.ToDouble(Value, CultureInfo.InvariantCulture);
                return double.IsFinite(number) ? new LiteralValue(number, name) : null;
            }

            if(name == WellKnownTypes.Single.FullyQualifiedName && Value is not double)
            {
                var number = Convert.ToSingle(Value, CultureInfo.InvariantCulture);
                return float.IsFinite(number) ? new LiteralValue(number, name) : null;
            }

            if(name == WellKnownTypes.Decimal.FullyQualifiedName && Value is not float and not double)
            {
                return new LiteralValue(Convert.ToDecimal(Value, CultureInfo.InvariantCulture), name);
            }

            return Requirement.IsIntegral(Requirement.Unrefined(target)) && Value is long or int ? new LiteralValue(Value, name) : null;
        }
        catch(OverflowException)
        {
            return null;
        }
    }

    private LiteralValue? Negated()
    {
        if(TypeName == WellKnownTypes.UInt32.FullyQualifiedName || TypeName == WellKnownTypes.UInt64.FullyQualifiedName)
        {
            return null;
        }

        return Value switch
        {
            long number when number != long.MinValue => new LiteralValue(-number, TypeName),
            int number when number != int.MinValue => new LiteralValue(-number, TypeName),
            float number => new LiteralValue(-number, TypeName),
            double number => new LiteralValue(-number, TypeName),
            decimal number => new LiteralValue(-number, TypeName),
            _ => null,
        };
    }

    public override string ToString()
    {
        return Value is string text ? $"\"{text}\"" : Convert.ToString(Value, CultureInfo.InvariantCulture) ?? "";
    }
}