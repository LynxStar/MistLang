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

    public void WithdrawImport(string namespaceName)
    {
        _ = imports.Remove(namespaceName);
    }

    /// <summary>
    /// The namespace names brought into scope by using directives on this level.
    /// </summary>
    public IReadOnlyList<string> Imports => imports;

    /// <summary>
    /// The types this level's usings bring in, resolved in two tiers: namespaces declared in Mist
    /// source first, and CLR-projected ones only when those find nothing.
    /// <para>
    /// The tiers exist because the standard library is declared ONTO the framework it wraps.
    /// <c>Standard.Console</c> is an <c>intrinsic</c> declaration and <c>System.Console</c> is
    /// metadata for the same thing, so a file importing both reaches two distinct types under one
    /// name, which a single flat pass can only call ambiguous. Preferring the declared one is what
    /// lets <c>using Standard;</c> stand beside <c>using System;</c>, and the two names are not
    /// peers: one is the stdlib and the other is what the stdlib projects onto.
    /// </para>
    /// <para>
    /// Within a tier, candidates are compared by identity, so two usings that reach the same type
    /// agree rather than conflict.
    /// </para>
    /// </summary>
    protected LookupResult LookupImports(LookupRequest request)
    {
        var declared = LookupTier(request, fromMistSource: true);

        return declared is NotFoundResult
            ? LookupTier(request, fromMistSource: false)
            : declared
            ;
    }

    private LookupResult LookupTier(LookupRequest request, bool fromMistSource)
    {
        IMistType? match = null;
        string? matchedNamespace = null;
        string? conflictNamespace = null;

        foreach(var imported in imports)
        {
            var found = programModel.FindType(imported, request.Name);
            if(found is null || found.Assembly.HasMistSource != fromMistSource)
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
                diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.AmbiguousType, node, null, new NameValue(request.Name), new NameValue(matchedNamespace!), new NameValue(conflictNamespace));
            }

            return new TypeResult(Types.ErrorType.Instance);
        }

        return match is null ? new NotFoundResult() : new TypeResult(match);
    }
}