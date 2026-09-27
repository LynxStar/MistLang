using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Refinements;

internal static class EnumConstants
{
    public static long? Of(MemberAccessExpression access)
    {
        return Read(access, new HashSet<Expression>(ReferenceEqualityComparer.Instance));
    }

    private static long? Read(Expression? expression, HashSet<Expression> visited)
    {
        if(expression is null || !visited.Add(expression))
        {
            return null;
        }

        return expression switch
        {
            MemberAccessExpression
            {
                IsConditional: false,
                IsFailed: false,
                Member: EnumMemberResult
                {
                    Member.Value: var stored,
                    DeclaringType: EnumType { DeclarationNode: not null, UnderlyingType: { } underlying },
                },
            } => Read(stored, visited) is { } storedValue && Fits(underlying, storedValue) ? storedValue : null,

            LiteralExpression literal when Requirement.IsIntegral(Requirement.Unrefined(literal.Type)) => literal.Value switch
            {
                sbyte value => value,
                byte value => value,
                short value => value,
                ushort value => value,
                int value => value,
                uint value => value,
                long value => value,
                ulong value when value <= long.MaxValue => (long)value,
                char value => value,
                _ => null,
            },

            UnaryExpression
            {
                OperatorKind: UnaryOperatorKind.Negate,
                Operator: null or { Modifiers.IsIntrinsic: true },
                Operand: var operand,
            } unary when Requirement.IsIntegral(Requirement.Unrefined(unary.Type))
                => Read(operand, visited) is { } constant && constant != long.MinValue ? -constant : null,

            CloneExpression clone => Read(clone.Source, visited),
            _ => null,
        };
    }

    private static bool Fits(IMistType type, long value)
    {
        var name = Requirement.Unrefined(type).FullyQualifiedName;
        return Ranges.TryGetValue(name, out var range) && value >= range.Low && value <= range.High;
    }

    // The interval domain is signed long; an unsigned constant beyond its ceiling has no proof here.
    private static Dictionary<string, (long Low, long High)> Ranges { get; } = new(StringComparer.Ordinal)
    {
        [WellKnownTypes.SByte.FullyQualifiedName] = (sbyte.MinValue, sbyte.MaxValue),
        [WellKnownTypes.Byte.FullyQualifiedName] = (byte.MinValue, byte.MaxValue),
        [WellKnownTypes.Int16.FullyQualifiedName] = (short.MinValue, short.MaxValue),
        [WellKnownTypes.UInt16.FullyQualifiedName] = (ushort.MinValue, ushort.MaxValue),
        [WellKnownTypes.Int32.FullyQualifiedName] = (int.MinValue, int.MaxValue),
        [WellKnownTypes.UInt32.FullyQualifiedName] = (uint.MinValue, uint.MaxValue),
        [WellKnownTypes.Int64.FullyQualifiedName] = (long.MinValue, long.MaxValue),
        [WellKnownTypes.UInt64.FullyQualifiedName] = (0, long.MaxValue),
        [WellKnownTypes.Char.FullyQualifiedName] = (char.MinValue, char.MaxValue),
    };
}