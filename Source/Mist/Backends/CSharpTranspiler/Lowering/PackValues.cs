using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramModel.Contracts.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

internal static class PackValues
{
    public static string? Read(IMistType type)
    {
        var elements = MonomorphReference.Elements(TypeNameEmitter.Closed(type));
        return elements is null
            ? null
            : $"new System.Type[] {{ {string.Join(", ", elements.Select(element => $"typeof({TypeNameEmitter.Emit(element)})"))} }}";
    }
}