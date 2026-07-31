using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model.Scopes;

/// <summary>
/// A level that may carry using directives: the project's implicit usings
/// (<see cref="GlobalScope"/>) and a file's own (<see cref="FileScope"/>).
/// <para>
/// Two levels rather than one pool, so a file using shadows a global one instead of conflicting with
/// it. Ambiguity is therefore only ever within a single level.
/// </para>
/// </summary>
internal abstract class ImportingScope(Scope? parent, ProgramModel programModel, DiagnosticBag diagnostics)
    : Scope(parent)
{
    private readonly List<string> imports = [];

    /// <summary>
    /// Declares a using directive. Repeats collapse, so importing a namespace twice cannot make it
    /// ambiguous with itself.
    /// </summary>
    public void DeclareImport(string namespaceName)
    {
        if(!imports.Contains(namespaceName))
        {
            imports.Add(namespaceName);
        }
    }

    /// <summary>
    /// The namespace names brought into scope by using directives on this level.
    /// </summary>
    public IReadOnlyList<string> Imports => imports;

    /// <summary>
    /// The types this level's usings bring in. Compared by identity, so two usings that reach the
    /// same type agree rather than conflict.
    /// </summary>
    protected LookupResult LookupImports(LookupRequest request)
    {
        IMistType? match = null;
        string? matchedNamespace = null;
        string? conflictNamespace = null;

        foreach(var imported in imports)
        {
            var found = programModel.FindType(imported, request.Name);
            if(found is null)
            {
                continue;
            }

            if(match is null)
            {
                match = found;
                matchedNamespace = imported;
            }
            else if(!ReferenceEquals(match, found))
            {
                conflictNamespace = imported;
            }
        }

        if(conflictNamespace is not null)
        {
            // No node means no syntax to blame, as with assembly projection. The error type is
            // returned either way, so the caller can report in its own terms.
            if(request.LocationNode is { } node)
            {
                diagnostics.ReportAtNode(
                    ProgramModelDiagnostics.AmbiguousType, node, null,
                    request.Name, matchedNamespace!, conflictNamespace);
            }

            return new TypeResult(Types.ErrorType.Instance);
        }

        return match is null ? new NotFoundResult() : new TypeResult(match);
    }
}