using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// What a <c>Clone</c> body means when it is bound as a conversion (newtypes spec Requirement 10.4). A
/// field converts only where the body itself clones it, and there the clone IS the conversion: the call
/// is replaced rather than wrapped, so the value crosses once. A test naming the result type would be
/// asking whether an operand-typed value is already the newtype, which is never true and would leave a
/// deep copy silently truncating, so it tests for the operand instead. A field the body shares reaches
/// neither rule and fails to bind, which is what refuses the cast and names it.
/// </summary>
internal static class CloneConversionBody
{
    public static Expression? Convert(
        SyntaxNode node, MethodGroupExpression group, Method selected, BindingContext ctx, BuilderContext context)
    {
        if(ConversionScope.Around(ctx.Scope) is not { ClonesThrough: { } written } conversion
            || selected.DeclarationNode is not { } called
            || !ReferenceEquals(called, written.DeclarationNode))
        {
            return null;
        }

        var interner = context.ProgramModel.TypeInterner;
        var operand = conversion.SourceParameter.Type;
        var receiver = group.Receiver ?? new ThisExpression(node, operand);
        var held = group.IsConditional ? OptionalLifting.NonVoidPortion(receiver.Type, interner) : receiver.Type;

        if(!ConversionRelation.IsIdentity(held, operand))
        {
            return null;
        }

        var result = group.IsConditional
            ? OptionalLifting.Lift(conversion.TargetType, context.Resolver.LookupWellKnown(WellKnownTypes.Void), interner)
            : conversion.TargetType
            ;

        return new CastExpression(node, receiver, conversion.TargetType, [new ConversionFunction(conversion, operand, conversion.TargetType)], result)
        {
            IsConditional = group.IsConditional,
        };
    }

    public static IMistType Tested(IMistType tested, IMistType scrutinee, Scope scope)
    {
        return ConversionScope.Around(scope) is { ClonesThrough: not null } conversion
            && ConversionRelation.IsIdentity(tested, conversion.TargetType)
            && Holds(scrutinee, conversion.SourceParameter.Type)
            ? conversion.SourceParameter.Type
            : tested
            ;
    }

    private static bool Holds(IMistType scrutinee, IMistType operand)
    {
        return ConversionRelation.IsIdentity(scrutinee, operand)
            || (scrutinee is UnionType union && union.Constituents.Any(constituent => ConversionRelation.IsIdentity(constituent, operand)));
    }
}