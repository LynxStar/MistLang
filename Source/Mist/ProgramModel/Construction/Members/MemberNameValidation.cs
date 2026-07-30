using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.ProgramModel.Construction.Members;

internal static class MemberNameValidation
{
    public static void Validate(
        string memberName,
        IMethodHost host,
        SyntaxNode locationNode,
        DiagnosticBag diagnostics)
    {
        // An extension block's name is its receiver type's, and its members are emitted into a
        // separate static class, so sharing that name never produces CS0542.
        if(host is MemberType)
        {
            Validate(memberName, host.Name, locationNode, diagnostics);
        }
    }

    public static void Validate(
        string memberName,
        string enclosingTypeName,
        SyntaxNode locationNode,
        DiagnosticBag diagnostics)
    {
        if(string.Equals(memberName, enclosingTypeName, StringComparison.Ordinal))
        {
            diagnostics.ReportAtNode(
                ProgramModelDiagnostics.MemberNameMatchesEnclosingType, locationNode, null, enclosingTypeName);
        }
    }
}