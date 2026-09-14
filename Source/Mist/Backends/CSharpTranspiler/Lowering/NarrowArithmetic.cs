using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// Standard declares arithmetic on byte, sbyte, short and ushort as answering the operand's own type
/// (standard-library spec Requirement 3.4), and C# promotes the same arithmetic to int. The declaration is
/// what the program means, so a result of one of those types is spelled back into it, wrapping as int
/// arithmetic already does; left as C# types it, it fails at every slot of the declared type.
/// </summary>
internal static class NarrowArithmetic
{
    private static readonly HashSet<string> Promoted = new(StringComparer.Ordinal)
    {
        WellKnownTypes.Byte.FullyQualifiedName,
        WellKnownTypes.SByte.FullyQualifiedName,
        WellKnownTypes.Int16.FullyQualifiedName,
        WellKnownTypes.UInt16.FullyQualifiedName,
    };

    public static bool Promotes(IMistType result)
    {
        return Promoted.Contains(result.FullyQualifiedName);
    }

    public static string Spell(IMistType result, string arithmetic)
    {
        return Promotes(result) ? $"(({TypeNameEmitter.Emit(result)})({arithmetic}))" : arithmetic;
    }
}