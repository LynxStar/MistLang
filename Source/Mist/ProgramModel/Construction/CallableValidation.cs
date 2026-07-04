using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Shared validation logic for all callable members (methods, constructors,
/// operators, cast operators). Ensures that rules which must apply uniformly
/// are defined in exactly one place.
/// </summary>
internal static class CallableValidation
{
    /// <summary>
    /// Resolves a <see cref="CallableBodyNode"/> into an optional <see cref="Block"/>.
    /// </summary>
    public static Block? ResolveBody(CallableBodyNode bodyNode)
    {
        if(bodyNode is CallableBlockBodyCallableBodyNode blockBody)
        {
            return new Block(blockBody.Block);
        }

        return null;
    }

    /// <summary>
    /// Validates intrinsic rules: no body, must be on an intrinsic type, cannot be on an abstract.
    /// </summary>
    public static void ValidateIntrinsic(
        SyntaxNode locationNode,
        MemberType memberType,
        string memberName,
        Block? body,
        DiagnosticBag diagnostics)
    {
        if(memberType is AbstractType)
        {
            diagnostics.ReportAtNode(ProgramModelDiagnostics.IntrinsicNotValidOnMember, locationNode, null, "abstract members");
            return;
        }

        if(body is not null)
        {
            diagnostics.ReportAtNode(ProgramModelDiagnostics.IntrinsicMemberCannotHaveBody, locationNode, null, memberName);
        }

        var typeIsIntrinsic = memberType is ConcreteType concrete && concrete.IsIntrinsic;

        if(!typeIsIntrinsic)
        {
            diagnostics.ReportAtNode(ProgramModelDiagnostics.IntrinsicRequiresIntrinsicType, locationNode, null, memberType.Name);
        }
    }

    /// <summary>
    /// Validates that a non-intrinsic callable on a concrete type has a body.
    /// </summary>
    public static void ValidateBodyRequired(
        SyntaxNode locationNode,
        MemberType memberType,
        string memberName,
        Block? body,
        DiagnosticBag diagnostics)
    {
        if(body is null && memberType is not AbstractType)
        {
            diagnostics.ReportAtNode(ProgramModelDiagnostics.MethodMustHaveBody, locationNode, null, memberName);
        }
    }

    /// <summary>
    /// Runs the standard intrinsic-or-body-required check. Call this after resolving body.
    /// </summary>
    public static void ValidateIntrinsicOrBody(
        SyntaxNode locationNode,
        MemberType memberType,
        string memberName,
        MemberModifiers modifiers,
        Block? body,
        DiagnosticBag diagnostics)
    {
        if(modifiers.IsIntrinsic)
        {
            ValidateIntrinsic(locationNode, memberType, memberName, body, diagnostics);
        }
        else
        {
            ValidateBodyRequired(locationNode, memberType, memberName, body, diagnostics);
        }
    }
}