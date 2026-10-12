using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis;

/// <summary>
/// Whether an expression names storage a reference can be taken to, answered as what it is when it is
/// not, for the diagnostic. Null means a location or a shape this does not decide -- <c>this</c>, a value
/// that already failed -- so a program is refused only for what is certainly a value.
/// </summary>
internal static class StorageLocations
{
    public static string? NotALocation(Expression expression)
    {
        return expression switch
        {
            LocalReferenceExpression or FieldReferenceExpression or BackingFieldReferenceExpression or ThisExpression
                or ElementReadExpression or ErrorExpression or UnresolvedExpression or UnboundLambdaExpression or UnboundContextualExpression => null,
            MemberAccessExpression { Member: FieldResult found } access => found.Field.Modifiers.IsStatic || !ThisTiers.IsValue(Requirement.Unrefined(access.Receiver.Type))
                ? null
                : NotALocation(access.Receiver) is { } holder ? $"a field of {holder}" : null,
            InvocationExpression { Signature.Modifiers.IsRef: true } => null,
            InvocationExpression { Method: { } method } => $"the result of '{method.Name}'",
            InvocationExpression => "the result of a call",
            PropertyReferenceExpression { Property: var property } => property.Modifiers.IsRef ? null : $"property '{property.Name}'",
            MemberAccessExpression { Member: PropertyResult { Property: var property } } => property.Modifiers.IsRef ? null : $"property '{property.Name}'",
            ExtensionPropertyAccessExpression { Property: var property } => $"extension property '{property.Name}'",
            IndexExpression { Indexer: { } indexer } index => indexer.Modifiers.IsRef ? null : $"the indexer of '{TypeDisplay.Mist(index.Receiver.Type)}'",
            IndexExpression index when Indexability.IsRange(index.Index.Type) => "a slice",
            IndexExpression index => HoldsElements(index.Receiver.Type) ? null : $"an element of '{TypeDisplay.Mist(index.Receiver.Type)}'",
            ImplicitConversionExpression { Operator: null } conversion => NotALocation(conversion.Operand),
            LiteralExpression => "a literal",
            _ => "a computed value",
        };
    }

    /// <summary>
    /// What an assignment cannot write through, by the same reckoning: a property or indexer is written by its
    /// setter, which <see cref="Rules.AccessorAnalyzer"/> judges, unless it is reached through a struct that
    /// is itself a value, whose copy the write would be lost on.
    /// </summary>
    public static string? NotAssignable(Expression target)
    {
        return target switch
        {
            PropertyReferenceExpression or ExtensionPropertyAccessExpression or IndexExpression { Indexer.Modifiers.IsRef: true } => null,
            MemberAccessExpression { Member: PropertyResult { Property: var property } } access
                => CopiedHolder(property.Modifiers.IsStatic, access.Receiver) is { } holder ? $"a property of {holder}" : null,
            IndexExpression { Indexer: not null } index => CopiedHolder(isStatic: false, index.Receiver) is { } holder ? $"the indexer of {holder}" : null,
            TypeReferenceExpression { ReferencedType: TypeParameter { IsDependent: true } dependent } => $"dependent parameter '{dependent.Name}'",
            TypeReferenceExpression type => $"the type '{TypeDisplay.Mist(type.ReferencedType)}'",
            TypeLiteralExpression type => $"the type '{TypeDisplay.Mist(type.ReferencedType)}'",
            ThisExpression self when !ThisTiers.IsValue(Requirement.Unrefined(self.Type)) => "'this'",
            TupleLiteralExpression => "a tuple literal",
            _ => NotALocation(target),
        };
    }

    private static string? CopiedHolder(bool isStatic, Expression receiver)
    {
        return !isStatic && ThisTiers.IsValue(Requirement.Unrefined(receiver.Type)) ? NotALocation(receiver) : null;
    }

    private static bool HoldsElements(IMistType receiver)
    {
        return Requirement.Unrefined(receiver) is ConstructedType { Definition.FullyQualifiedName: var name }
            && (name == WellKnownTypes.Array.FullyQualifiedName
                || name == WellKnownTypes.Span.FullyQualifiedName
                || name == WellKnownTypes.ReadOnlySpan.FullyQualifiedName);
    }
}