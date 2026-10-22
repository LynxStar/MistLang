using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding.Expressions;

/// <summary>
/// A type named by a whole postfix expression, with no member read or call after it, is a value: the
/// <c>Type</c> it names (type-literals Requirements 1.3 and 3.3), as <c>var t = int;</c> is. Only a
/// finished postfix can tell, because inside one the same name is the receiver of a static member.
/// A pack and a dependent parameter keep their reference, each being a value of its own already --
/// the pack's <c>Type[]</c> and the dependent parameter's domain value.
/// </summary>
internal static class TypeNameValues
{
    public static Expression AsValue(Expression bound, BuilderContext context)
    {
        return bound is TypeReferenceExpression { ReferencedType: not (TypeParameter { IsPack: true } or TypeParameter { IsDependent: true } or LiftedType) } reference
            ? new TypeLiteralExpression(reference.Node, reference.ReferencedType, context.Resolver.LookupWellKnown(WellKnownTypes.Type))
            : bound;
    }
}