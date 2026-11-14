using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis;

/// <summary>
/// Refinement-types Requirement 3 proves values once, so predicate members must stay stable
/// for the instance's lifetime. Abstract readonly getters promise no writes, not stable reads.
/// Getter bodies use a node whitelist so new kinds cannot silently qualify; the alternative
/// is a whole-body pure proof from <see cref="PurityInference"/> (purity Requirement 12.2).
/// </summary>
internal static class StableMembers
{
    public static bool IsStable(LookupResult member, PurityInference? purity = null)
    {
        return member switch
        {
            FieldResult { Field: var field } => field.Modifiers.IsReadonly,
            PropertyResult { Property: var property } => IsStable(property, new HashSet<Property>(ReferenceEqualityComparer.Instance), purity),
            _ => true,
        };
    }

    private static bool IsStable(Property property, HashSet<Property> reading, PurityInference? purity)
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

        var stable = BodyWalker.DescendantsAndSelf(body).All(node => ReadsNothingWritable(node, reading, purity))
            || purity?.Of(getter).Pure == true;
        _ = reading.Remove(property);
        return stable;
    }

    /// <summary>
    /// A body's read of a property with no getter is refused at that read by <see cref="Rules.AccessorAnalyzer"/>,
    /// so it does not also make every getter around it unstable: one missing getter, one complaint.
    /// </summary>
    private static bool ReadsStable(Property property, HashSet<Property> reading, PurityInference? purity)
    {
        return property.Getter is null || IsStable(property, reading, purity);
    }

    private static bool OwnsItsStorage(Property property)
    {
        return property is { Getter.IsAuto: true, Modifiers: { IsClrProjected: false, IsIntrinsic: false } }
            && property.DeclaringType is ConcreteType { IsIntrinsic: false, Modifiers.IsClrProjected: false };
    }

    private static bool ReadsNothingWritable(object node, HashSet<Property> reading, PurityInference? purity)
    {
        return node switch
        {
            FieldReferenceExpression { Field: var field } => field.Modifiers.IsReadonly,
            PropertyReferenceExpression { Property: var property } => ReadsStable(property, reading, purity),
            MemberAccessExpression { Member: FieldResult { Field: var field } } => field.Modifiers.IsReadonly,
            MemberAccessExpression { Member: PropertyResult { Property: var property } } => ReadsStable(property, reading, purity),
            PropertySubpattern { IsMemberFailed: false, Member: FieldResult { Field: var field } } => field.Modifiers.IsReadonly,
            PropertySubpattern { IsMemberFailed: false, Member: PropertyResult { Property: var property } } => ReadsStable(property, reading, purity),
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