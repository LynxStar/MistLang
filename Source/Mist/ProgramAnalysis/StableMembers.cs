using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis;

/// <summary>
/// Whether a member answers the same for one instance for as long as it lives, which is what
/// refinement-types Requirement 3 needs of every member a predicate reads: a value is proven once, by an
/// `is`, and nothing checks it again. A readonly field does. A property does when it has no setter but
/// `init`, which nothing can call once the value is built, and its getter is auto over storage of its own,
/// or has a body that reads only such members, or has no body here to read -- intrinsic, CLR-projected --
/// and is declared by a `readonly` concrete type. An abstract's property never does, `readonly` or not:
/// nothing holds an implementer to the modifier. A body is held to the node kinds listed rather than
/// refused for the ones known to be trouble, so a call, an index, a loop, and any kind added to the model
/// later all answer no until someone decides otherwise.
/// </summary>
internal static class StableMembers
{
    public static bool IsStable(LookupResult member)
    {
        return member switch
        {
            FieldResult { Field: var field } => field.Modifiers.IsReadonly,
            PropertyResult { Property: var property } => IsStable(property, new HashSet<Property>(ReferenceEqualityComparer.Instance)),
            _ => true,
        };
    }

    private static bool IsStable(Property property, HashSet<Property> reading)
    {
        if(property.Setter is { IsInit: false } || property.Getter is not { } getter)
        {
            return false;
        }

        if(((object?)getter.Body ?? getter.ExpressionBody) is not { } body)
        {
            return OwnsItsStorage(property) || (property.DeclaringType is ConcreteType declaring && Declarations.IsReadonly(declaring));
        }

        if(!reading.Add(property))
        {
            return false;
        }

        var stable = BodyWalker.DescendantsAndSelf(body).All(node => ReadsNothingWritable(node, reading));
        _ = reading.Remove(property);
        return stable;
    }

    private static bool OwnsItsStorage(Property property)
    {
        return property is { Getter.IsAuto: true, Modifiers: { IsClrProjected: false, IsIntrinsic: false } }
            && property.DeclaringType is ConcreteType { IsIntrinsic: false, Modifiers.IsClrProjected: false };
    }

    private static bool ReadsNothingWritable(object node, HashSet<Property> reading)
    {
        return node switch
        {
            FieldReferenceExpression { Field: var field } => field.Modifiers.IsReadonly,
            PropertyReferenceExpression { Property: var property } => IsStable(property, reading),
            MemberAccessExpression { Member: FieldResult { Field: var field } } => field.Modifiers.IsReadonly,
            MemberAccessExpression { Member: PropertyResult { Property: var property } } => IsStable(property, reading),
            PropertySubpattern { Member: FieldResult { Field: var field } } => field.Modifiers.IsReadonly,
            PropertySubpattern { Member: PropertyResult { Property: var property } } => IsStable(property, reading),
            BinaryExpression { Operator.Modifiers.IsIntrinsic: false } or UnaryExpression { Operator.Modifiers.IsIntrinsic: false } => false,
            CastExpression { Operator.Modifiers.IsIntrinsic: false } or ImplicitConversionExpression { Operator.Modifiers.IsIntrinsic: false } => false,
            MemberAccessExpression or PropertySubpattern or BinaryExpression or UnaryExpression or CastExpression
                or ImplicitConversionExpression => true,
            LiteralExpression or ThisExpression or LocalReferenceExpression or BackingFieldReferenceExpression
                or DefaultExpression or VoidLiteralExpression or TypeReferenceExpression or NamespaceReferenceExpression
                or CoalesceExpression or AsExpression or IsPatternExpression or SwitchExpression or SwitchArm
                or TupleLiteralExpression or ErrorExpression or UnresolvedExpression => true,
            Block or ReturnStatement or IfStatement or LocalDeclarationStatement or ExpressionStatement => true,
            CatchAllPattern or ComparisonPattern or ConjunctionPattern or DisjunctionPattern or NegatedPattern
                or PropertyPattern or RegexPattern or TuplePattern or TypeTestPattern => true,
            _ => false,
        };
    }
}