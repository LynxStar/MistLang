using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Scopes;
using Storm.Runtime;

namespace Mist.ProgramModel.Construction.Binding.Expressions;

internal static class ObjectInitializerBinder
{
    /// <summary>
    /// Empty for a block that assigns nothing AND for no block at all. The two are not
    /// distinguished because C# reads them identically, so a third state would buy the model
    /// nothing that anything downstream could act on.
    /// </summary>
    internal static IReadOnlyList<ObjectInitializerAssignment> Bind(
        PropertyInitializerBlockNode? block,
        IMistType type,
        BindingContext ctx,
        BuilderContext context)
    {
        if(block?.Assignments is not { } assignments)
        {
            return [];
        }

        var bound = new List<ObjectInitializerAssignment>(1 + assignments.PropertyAssignments.Length);

        Add(assignments.PropertyAssignment, type, bound, ctx, context);

        foreach(var assignment in assignments.PropertyAssignments)
        {
            Add(assignment, type, bound, ctx, context);
        }

        return bound;
    }

    private static void Add(
        PropertyAssignmentNode assignment,
        IMistType type,
        List<ObjectInitializerAssignment> bound,
        BindingContext ctx,
        BuilderContext context)
    {
        if(assignment is not NamedPropertyAssignmentPropertyAssignmentNode named)
        {
            AddElement(assignment, type, bound, ctx, context);
            return;
        }

        var memberName = named.Name.Lexeme;
        var (target, found) = ResolveTarget(type, memberName, named, context);

        Expression value;

        using(ctx.PushTargetType(target))
        {
            value = ExpressionBinder.BindExpression(named.Value, ctx, context);
        }

        value = ImplicitConversions.At(
            NewtypeLiterals.Admit(value, target, context.ProgramModel.TypeInterner)!, target, context.ProgramModel.TypeInterner);

        bound.Add(new ObjectInitializerAssignment(named, memberName, value, target, member: found));
    }

    /// <summary>
    /// A COLLECTION element: <c>{ 1, 2 }</c>, which names no member and stands for a call to
    /// <c>Add</c>. The overload is resolved HERE even though emission writes the element back as C#
    /// initializer syntax and lets Roslyn resolve Add again -- otherwise the model would have chosen
    /// nothing, and an element matching no Add would reach the generated file to fail there.
    /// </summary>
    private static void AddElement(
        PropertyAssignmentNode assignment,
        IMistType type,
        List<ObjectInitializerAssignment> bound,
        BindingContext ctx,
        BuilderContext context)
    {
        if(Expression(assignment) is not { } expressionNode)
        {
            context.Diagnostics.ReportAtNode(
                ProgramModelDiagnostics.CollectionInitializerNotSupported, assignment, null);

            return;
        }

        var value = ExpressionBinder.BindExpression(expressionNode, ctx, context);
        var (found, constructed, _) = MemberResolution.Resolve(
            type, "Add", MemberVisibility.InstanceOnly, assignment, context, ctx.Scope);

        if(found is not MethodGroupResult { Methods.Count: > 0 } group)
        {
            return;
        }

        var choice = OverloadResolution.Select(
            group.Methods, [value], constructed, null, context.ProgramModel.TypeInterner, [false], group.Paths);

        if(choice.Selected is null)
        {
            var failure = OverloadFailure.Retain(
                choice, group.Methods, [value], "method 'Add'", assignment, context.Diagnostics);
            bound.Add(new ObjectInitializerAssignment(assignment, memberName: null, failure, targetType: null));
            return;
        }

        var interner = context.ProgramModel.TypeInterner;
        var parameterTypes = choice.Selected.ParametersList.ConvertAll(parameter => choice.Substitution.Apply(parameter.Type, interner));

        bound.Add(new ObjectInitializerAssignment(
            assignment, memberName: null, ImplicitConversions.AtParameters([value], choice.Selected, parameterTypes, interner)[0], targetType: null, choice.Selected));
    }

    private static ExpressionNode? Expression(PropertyAssignmentNode assignment)
    {
        return assignment is CollectionElementAssignmentPropertyAssignmentNode element
            ? element.Value
            : null;
    }

    private static MemberResolution.StorageLookup ResolveTarget(IMistType type, string memberName, SyntaxNode node, BuilderContext context)
    {
        var lookup = MemberResolution.ResolveStorage(type, memberName, node, context);

        if(lookup.Type is null)
        {
            ReportNotStorage(lookup.Found, type, memberName, node, context);
        }

        return lookup;
    }

    /// <summary>
    /// Tested for the kinds that DO exist and cannot be assigned, rather than for the absence of the
    /// two that can. Resolving to nothing and resolving ambiguously are reported by
    /// <see cref="MemberResolution"/> already, and a negative test would claim a member is not
    /// storage when the real complaint is that there are two of it.
    /// </summary>
    private static void ReportNotStorage(
        LookupResult found, IMistType type, string memberName, SyntaxNode node, BuilderContext context)
    {
        if(found is MethodGroupResult or TypeResult)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.InitializerTargetNotAssignable, node, null, type.Name, memberName);
        }
    }
}