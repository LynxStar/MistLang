using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// Phase 3 for an enum's variants: binds every written value and SYNTHESIZES the omitted ones, so
/// each variant leaves with an expression of its own. An enum with no annotation whose variants write
/// values takes its base from them first -- their one type, or the union of their types (enums spec,
/// Requirement 3.1 and 3.2) -- so the written values bind with no target, the base being what they decide.
/// <para>
/// Synthesizing rather than leaving the gap for emission to fill is what keeps the answer in one
/// place. A backend that re-derived an omitted value would be reimplementing the inference rules,
/// and a backend that re-emitted a WRITTEN value from the syntax would be reading a source the model
/// has already resolved -- so neither is asked to.
/// </para>
/// </summary>
internal static class EnumMemberBinder
{
    public static void BindAll(EnumType enumType, BuilderContext context)
    {
        var underlying = enumType.UnderlyingType ??= InferredBase(enumType, context);

        if(underlying is ErrorType)
        {
            return;
        }

        // Counting on is relative to whatever the previous variant ended up with, written or
        // inferred -- NOT to the last explicit value, which repeats after two omissions in a row.
        // No previous variant counts from zero; a previous variant whose constant cannot be folded
        // is a third state, and the one that must not be guessed at.
        long previous = -1;
        var countable = true;

        foreach(var member in enumType.MembersList)
        {
            member.Value = member.Value switch
            {
                null => Infer(member, enumType, underlying, previous, countable, context),
                UnresolvedExpression written => BodyBinder.BindInitializer(written, underlying, enumType.Scope, context),
                var bound => bound,
            };

            if(Fold(member.Value) is { } constant)
            {
                previous = constant;
                countable = true;
            }
            else
            {
                countable = false;
            }
        }
    }

    private static IMistType InferredBase(EnumType enumType, BuilderContext context)
    {
        var written = new List<IMistType>();

        foreach(var member in enumType.MembersList)
        {
            member.Value = BodyBinder.BindInitializer(member.Value, null, enumType.Scope, context);

            if(member.Value?.Type is { } type and not ErrorType)
            {
                written.Add(type);
            }
        }

        return written.Count == 0 ? ErrorType.Instance : context.ProgramModel.TypeInterner.GetOrCreateUnionType(written);
    }

    private static Expression? Infer(
        EnumMember member,
        EnumType enumType,
        IMistType underlying,
        long previous,
        bool countable,
        BuilderContext context)
    {
        var node = (Storm.Runtime.SyntaxNode?)member.DeclarationNode ?? enumType.DeclarationNode!;

        if(underlying.FullyQualifiedName == WellKnownTypes.String.FullyQualifiedName)
        {
            return new LiteralExpression(node, member.Name, underlying);
        }

        if(!IntegralRanges.TryGetValue(underlying.FullyQualifiedName, out var range))
        {
            context.Diagnostics.ReportAtNode(
                ProgramModelDiagnostics.EnumVariantValueNotInferable, node, null,
                member.Name, TypeDisplay.Mist(underlying));
            return null;
        }

        if(!countable)
        {
            context.Diagnostics.ReportAtNode(
                ProgramModelDiagnostics.EnumVariantValueNotCountable, node, null,
                member.Name);
            return null;
        }

        var value = previous + 1;

        if(value < range.Min || value > range.Max)
        {
            context.Diagnostics.ReportAtNode(
                ProgramModelDiagnostics.EnumVariantValueOutOfRange, node, null,
                member.Name, value, TypeDisplay.Mist(underlying));
            return null;
        }

        return new LiteralExpression(node, value, underlying);
    }

    /// <summary>
    /// The compile-time constant of a bound value, folding the one shape a written integral commonly
    /// takes beyond a bare literal: negation. Anything else -- a const reference, arithmetic -- is
    /// not a constant this phase can see, and the caller treats the variant as uncountable rather
    /// than guessing.
    /// </summary>
    private static long? Fold(Expression? expression)
    {
        return expression switch
        {
            LiteralExpression { Value: long constant } => constant,
            UnaryExpression { OperatorKind: UnaryOperatorKind.Negate, Operand: LiteralExpression { Value: long constant } }
                => -constant,
            _ => null,
        };
    }

    /// <summary>
    /// The bases that HAVE a successor, with the bounds a synthesized value must stay inside. A
    /// struct or class base leaves an omitted value unanswered instead: there is no next instance of
    /// a user type to reach for. UInt64's ceiling is capped at long.MaxValue because literal
    /// constants are carried as long, so nothing beyond it can be counted from anyway.
    /// </summary>
    private static readonly Dictionary<string, (long Min, long Max)> IntegralRanges = new(StringComparer.Ordinal)
    {
        [WellKnownTypes.Byte.FullyQualifiedName] = (byte.MinValue, byte.MaxValue),
        [WellKnownTypes.SByte.FullyQualifiedName] = (sbyte.MinValue, sbyte.MaxValue),
        [WellKnownTypes.Int16.FullyQualifiedName] = (short.MinValue, short.MaxValue),
        [WellKnownTypes.UInt16.FullyQualifiedName] = (ushort.MinValue, ushort.MaxValue),
        [WellKnownTypes.Int32.FullyQualifiedName] = (int.MinValue, int.MaxValue),
        [WellKnownTypes.UInt32.FullyQualifiedName] = (uint.MinValue, uint.MaxValue),
        [WellKnownTypes.Int64.FullyQualifiedName] = (long.MinValue, long.MaxValue),
        [WellKnownTypes.UInt64.FullyQualifiedName] = (0L, long.MaxValue),
    };
}