using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;

namespace Mist.Backends.CSharpTranspiler.Lowering;

internal static class BackendFacts
{
    public static SymbolValue Symbol(IMistType? owner, string name, string? spelling = null)
    {
        var identity = owner is null ? "<synthetic>" : DiagnosticSnapshot.Type(owner).Identity;
        return new SymbolValue($"{identity}.{name}", spelling ?? name);
    }
}