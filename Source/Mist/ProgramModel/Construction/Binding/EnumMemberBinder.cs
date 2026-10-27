using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
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
        Int128 previous = -1;
        var countable = true;

        foreach(var member in enumType.MembersList)
        {
            member.Value = member.Value switch
            {
                null => Infer(member, enumType, underlying, previous, countable, context),
                UnresolvedExpression written => BodyBinder.BindInitializer(written, underlying, enumType.Scope, context),
                var bound => bound,
            };

            if(IntegralConstant.Fold(member.Value) is { } constant)
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
        Int128 previous,
        bool countable,
        BuilderContext context)
    {
        var node = (Storm.Runtime.SyntaxNode?)member.DeclarationNode ?? enumType.DeclarationNode!;

        if(underlying.FullyQualifiedName == WellKnownTypes.String.FullyQualifiedName)
        {
            return new LiteralExpression(node, member.Name, underlying);
        }

        if(!IntegralConstant.IsIntegral(underlying))
        {
            context.Diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.EnumVariantValueNotInferable, node, null, new NameValue(member.Name), DiagnosticSnapshot.Type(underlying));
            return null;
        }

        if(!countable)
        {
            context.Diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.EnumVariantValueNotCountable, node, null, new NameValue(member.Name));
            return null;
        }

        var value = previous + 1;

        if(IntegralConstant.Box(value, underlying) is not { } boxed)
        {
            context.Diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.EnumVariantValueOutOfRange, node, null, new NameValue(member.Name), new QuantityValue(value), DiagnosticSnapshot.Type(underlying));
            return null;
        }

        return new LiteralExpression(node, boxed, underlying);
    }
}