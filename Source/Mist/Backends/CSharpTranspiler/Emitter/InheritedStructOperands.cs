using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// An operand of a derived struct handed to an operator its base struct declares. Mist selects the operator
/// through the derived-to-base conversion (struct-inheritance Requirement 4.2), but flattening leaves the two
/// structs unrelated in C#, joined only by the implicit operator of Requirement 6.2, and C# never applies a
/// user-defined conversion to find an operator. So the operand is converted to the parameter explicitly.
/// </summary>
internal static class InheritedStructOperands
{
    public static string? Conversion(IMistType operand, IMistType parameter)
    {
        if(Definition(TypeNameEmitter.Closed(operand)) is not StructType derived
            || Definition(TypeNameEmitter.Closed(parameter)) is not StructType ancestor)
        {
            return null;
        }

        for(var level = derived.BaseType; level is not null; level = Definition(level) is StructType inner ? inner.BaseType : null)
        {
            if(ReferenceEquals(Definition(level), ancestor))
            {
                return TypeNameEmitter.Emit(parameter);
            }
        }

        return null;
    }

    private static MemberType? Definition(IMistType type)
    {
        return (type is ConstructedType constructed ? constructed.Definition : type) as MemberType;
    }
}