using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramModel.Construction.Binding.Expressions;

/// <summary>
/// Binds a <c>new { ... }</c> literal. Each member contributes a name and a value, and the names are
/// what the literal's TYPE is built from -- see <see cref="Model.Types.AnonymousType"/>.
/// <para>
/// A PROJECTED member takes its name from the syntax it was written as, never from what that syntax
/// resolved to: <c>new { lang.Name }</c> is named Name because the source says Name, and reading the
/// name off the bound expression instead would have nothing to read for every form that is not a
/// member access. So the name is decided before the value is bound, which is also why an unnameable
/// projection can be refused without binding anything.
/// </para>
/// </summary>
internal static class AnonymousObjectBinder
{
    internal static Expression Bind(
        AnonymousTypeExpressionPrimaryNode node, BindingContext ctx, BuilderContext context)
    {
        var names = new List<string>();
        var types = new List<IMistType>();
        var values = new List<Expression>();

        foreach(var member in Members(node.Properties))
        {
            if(Name(member, context) is not { } name)
            {
                return new ErrorExpression(node);
            }

            // Nothing targets a member: the literal has no declared shape to read one off, which is
            // the whole of what makes it anonymous.
            using(ctx.PushTargetType(null))
            {
                var value = Value(member, ctx, context);

                names.Add(name);
                types.Add(value.Type);
                values.Add(value);
            }
        }

        return new AnonymousObjectExpression(
            node, values, context.ProgramModel.TypeInterner.GetOrCreateAnonymousType(names, types));
    }

    private static IEnumerable<AnonymousPropertyNode> Members(AnonymousPropertyListNode list)
    {
        yield return list.AnonymousProperty;

        foreach(var member in list.AnonymousPropertys)
        {
            yield return member;
        }
    }

    private static Expression Value(
        AnonymousPropertyNode member, BindingContext ctx, BuilderContext context)
    {
        return member is ExplicitAnonymousPropertyAnonymousPropertyNode explicitly
            ? ExpressionBinder.BindExpression(explicitly.Value, ctx, context)
            : PostfixBinder.BindPostfix(
                ((ProjectedAnonymousPropertyAnonymousPropertyNode)member).Projection, ctx, context)
            ;
    }

    private static string? Name(AnonymousPropertyNode member, BuilderContext context)
    {
        if(member is ExplicitAnonymousPropertyAnonymousPropertyNode explicitly)
        {
            return explicitly.Name.Lexeme;
        }

        var projection = ((ProjectedAnonymousPropertyAnonymousPropertyNode)member).Projection;

        if(Tail(projection) is { } name)
        {
            return name;
        }

        context.Diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.AnonymousMemberNotNameable, projection, null);

        return null;
    }

    private static string? Tail(PostfixNode projection)
    {
        if(projection.Suffix.Length > 0)
        {
            return projection.Suffix[^1] switch
            {
                MemberAccessSuffixPostfixSuffixNode access => access.Name.Lexeme,
                OptionalMemberAccessSuffixPostfixSuffixNode optional => optional.Name.Lexeme,
                _ => null,
            };
        }

        return (projection.Target as IdentifierExpressionPrimaryNode)?.Name.Lexeme;
    }
}