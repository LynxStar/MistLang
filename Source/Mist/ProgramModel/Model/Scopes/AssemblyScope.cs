using Mist.Common.Diagnostics;

namespace Mist.ProgramModel.Model.Scopes;

/// <summary>
/// One per assembly rather than one per compilation, because implicit usings are the project's
/// setting: two projects compiled together may declare different ones, and a file must see only its
/// own assembly's.
/// </summary>
internal sealed class AssemblyScope : ImportingScope
{
    public AssemblyScope(
        ProgramScope parent,
        MistAssembly assembly,
        ProgramModel programModel,
        DiagnosticBag diagnostics)
        : base(parent, programModel, diagnostics)
    {
        Assembly = assembly;

        foreach(var globalUsing in assembly.GlobalUsings)
        {
            DeclareImport(globalUsing);
        }
    }

    public MistAssembly Assembly { get; }

    protected override LookupResult LookupSelf(LookupRequest request)
    {
        return LookupImports(request);
    }
}