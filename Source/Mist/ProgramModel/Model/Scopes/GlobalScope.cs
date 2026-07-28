using Mist.Common.Diagnostics;

namespace Mist.ProgramModel.Model.Scopes;

/// <summary>
/// The project's implicit usings. Reached only when a file's own usings did not answer, which is
/// what makes those shadow these.
/// </summary>
internal sealed class GlobalScope(ProgramScope parent, ProgramModel programModel, DiagnosticBag diagnostics)
    : ImportingScope(parent, programModel, diagnostics)
{
    protected override LookupResult LookupSelf(LookupRequest request)
    {
        return LookupImports(request);
    }
}