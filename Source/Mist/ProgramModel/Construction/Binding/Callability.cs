using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// What a call written on a VALUE rather than on a method group may reach. A value is called through
/// its type, so only a function type or a CLR delegate answers; a field or property of any other type
/// is not a candidate for the call at all, which is what lets an extension method of the same name
/// answer instead, and what makes the call an error where none is in scope.
/// <para>
/// A union does not answer, being uncallable until it is narrowed (first-class-functions Requirement
/// 6.10). An OPTIONAL callable is let through all the same: its call is one the target accepts, so
/// whether absence must be ruled out first is a rule about optionals and not about what can be
/// called, and <c>?.</c> lifts every member it reaches. A callee the binder has already failed on,
/// cannot represent yet, or whose type answers nothing is left as it is found, for the same reason an
/// unresolved member on one is.
/// </para>
/// </summary>
internal static class Callability
{
    public static bool IsCallable(IMistType type, TypeInterner interner)
    {
        var present = OptionalLifting.NonVoidPortion(AliasTransparency.Expand(type, interner), interner);

        if(!ReferenceEquals(present, type))
        {
            return IsCallable(present, interner);
        }

        return type switch
        {
            RefinedType refined => IsCallable(refined.BaseType, interner),
            Newtype { SourceType: { } source } => IsCallable(source, interner),
            _ => DelegateShape.Of(type) is not null,
        };
    }

    public static MethodGroupExpression? ExtensionMethods(Expression callee, BindingContext ctx, BuilderContext context)
    {
        if(callee is not MemberAccessExpression { Member: FieldResult or PropertyResult } access
            || !Judges(access)
            || IsCallable(access.Type, context.ProgramModel.TypeInterner))
        {
            return null;
        }

        var (found, constructed, _) = MemberResolution.ResolveExtensionMethods(
            ReceiverType(access, context), access.MemberName, VisibilityOf(access), context, ctx.Scope);

        return found is MethodGroupResult { Methods.Count: > 0 } group
            ? new MethodGroupExpression(
                access.Node, access.Receiver, access.MemberName, group.Methods, constructed, access.IsConditional)
            {
                Paths = group.Paths,
            }
            : null;
    }

    public static bool Refuses(Expression callee, Storm.Runtime.SyntaxNode call, BuilderContext context)
    {
        if(!Judges(callee) || IsCallable(callee.Type, context.ProgramModel.TypeInterner))
        {
            return false;
        }

        var type = TypeDisplay.Mist(callee is MemberAccessExpression { IsConditional: true }
            ? OptionalLifting.NonVoidPortion(callee.Type, context.ProgramModel.TypeInterner)
            : callee.Type);

        if(NameOf(callee) is not { } name)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.ValueNotCallable, call, null, type);
            return true;
        }

        var offering = callee is MemberAccessExpression access
            ? ExtensionLookup.NamespacesOffering(
                ReceiverType(access, context), name, VisibilityOf(access), context.ProgramModel)
            : [];

        if(offering.Count == 0)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.NotCallable, call, null, name, type);
        }
        else
        {
            context.Diagnostics.ReportAtNode(
                ProgramModelDiagnostics.NotCallableExtensionNotImported, call, null,
                name, type, string.Join(", ", offering.Select(ns => $"'{ns}'")));
        }

        return true;
    }

    private static bool Judges(Expression callee)
    {
        return callee is not (UnresolvedExpression or TypeReferenceExpression or NamespaceReferenceExpression)
            && callee.Type is not (MethodGroupType or UnboundLambdaType)
            && MemberResolution.AnswersMembers(callee.Type)
            && !PriorFailure.Of(callee);
    }

    private static string? NameOf(Expression callee)
    {
        return callee switch
        {
            MemberAccessExpression access => access.MemberName,
            LocalReferenceExpression local => local.Local.Name,
            FieldReferenceExpression field => field.Field.Name,
            PropertyReferenceExpression property => property.Property.Name,
            ExtensionPropertyAccessExpression extension => extension.Property.Name,
            _ => null,
        };
    }

    private static IMistType ReceiverType(MemberAccessExpression access, BuilderContext context)
    {
        return access.Receiver is TypeReferenceExpression named ? named.ReferencedType
            : access.IsConditional ? OptionalLifting.NonVoidPortion(access.Receiver.Type, context.ProgramModel.TypeInterner)
            : access.Receiver.Type;
    }

    private static MemberVisibility VisibilityOf(MemberAccessExpression access)
    {
        return access.Receiver is TypeReferenceExpression ? MemberVisibility.StaticOnly : MemberVisibility.InstanceOnly;
    }
}