using Mist.Common.Diagnostics;

namespace Mist.ProgramModel.Model.Scopes;

/// <summary>
/// One assembly's implicit usings, reached only when a file's own usings did not answer, which is
/// what makes those shadow these.
/// <para>
/// One per assembly rather than one per compilation, because the setting is the project's: two
/// projects compiled together may declare different implicit usings, and a file must see only its
/// own assembly's. The names come from <see cref="MistAssembly.GlobalUsings"/>, which is where they
/// are recorded, so nothing has to be declared into this scope from outside.
/// </para>
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