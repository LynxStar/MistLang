using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.ProgramModel.Construction.Resolution;

/// <summary>
/// A pack named where one type is required (variadics Requirement 6.3). A pack is a list of types, so
/// the name alone denotes no type; the reference fails here, as an unresolved name does, and takes
/// <see cref="ErrorType"/> so that nothing built on it is judged or emitted as though it had one. The
/// one place the name IS a type is the pack's own constraint, see <see cref="PackConstraintScope"/>.
/// </summary>
internal static class PackPosition
{
    public static IMistType AsSingleType(IMistType named, Scope scope, SyntaxNode node, DiagnosticBag diagnostics, Action? reportFailure = null)
    {
        if(named is not TypeParameter { IsPack: true } pack || PackConstraintScope.Constrains(scope, pack))
        {
            return named;
        }

        diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.PackInTypePosition, node, null, new NameValue(named.Name));
        reportFailure?.Invoke();

        return ErrorType.Instance;
    }
}