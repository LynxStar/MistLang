using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model.Scopes;

/// <summary>
/// One compilation unit: its using directives, and the types it declared at top level. Neither is a
/// namespace level -- the file's own namespace is a <see cref="NamespaceScope"/> below this one.
/// <para>
/// Top-level types are held here because they are nameable from the namespace blocks written in the
/// same file, while the rest of their namespace is not. A using would bring in the whole namespace,
/// so this is deliberately narrower than one.
/// </para>
/// </summary>
internal sealed class FileScope(Scope parent, ProgramModel programModel, DiagnosticBag diagnostics)
    : ImportingScope(parent, programModel, diagnostics)
{
    private readonly Dictionary<string, IMistType> topLevelTypes = [];

    public void DeclareTopLevelType(string name, IMistType type)
    {
        topLevelTypes[name] = type;
    }

    protected override LookupResult LookupSelf(LookupRequest request)
    {
        // Own declarations before usings: what is written here outranks what is merely named here.
        var candidates = new CandidateSet();
        candidates.AddTypeNames(topLevelTypes, request);

        if(!candidates.IsEmpty)
        {
            return candidates.Resolve();
        }

        return LookupImports(request);
    }
}