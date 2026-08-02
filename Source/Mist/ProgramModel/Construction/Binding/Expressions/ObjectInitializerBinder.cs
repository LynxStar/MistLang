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
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.CollectionInitializerNotSupported, assignment, null);

            return;
        }

        var memberName = named.Name.Lexeme;
        var target = ResolveTargetType(type, memberName, named, context);

        Expression value;

        using(ctx.PushTargetType(target))
        {
            value = ExpressionBinder.BindExpression(named.Value, ctx, context);
        }

        bound.Add(new ObjectInitializerAssignment(named, memberName, value));
    }

    private static IMistType? ResolveTargetType(IMistType type, string memberName, SyntaxNode node, BuilderContext context)
    {
        var (found, constructed) = MemberResolution.Resolve(type, memberName, MemberVisibility.InstanceOnly, node, context);

        var memberType = found switch
        {
            FieldResult field => field.Field.Type,
            PropertyResult property => property.Property.Type,
            _ => null,
        };

        if(memberType is null)
        {
            ReportNotStorage(found, type, memberName, node, context);
            return null;
        }

        return TypeSubstitution
            .ForReceiver(constructed)
            .Apply(memberType, context.ProgramModel.TypeInterner);
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
        if(found is MethodGroupResult or TypeResult or EnumMemberResult)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.InitializerTargetNotAssignable, node, null, type.Name, memberName);
        }
    }
}