using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// An array's Enumerator, which the CLR does not have. System.Array.GetEnumerator answers the
/// non-generic IEnumerator, whose Current is an object, where Standard declares
/// <c>Enumerator : IEnumerator&lt;TValue&gt;</c> with a Current of TValue -- so a value read off it
/// would not convert to the type the model gave it. The declaration is the contract, as it is for
/// <see cref="TaskCombinators"/>: the backend reads the enumerator as the generic IEnumerator every
/// array supplies through its IEnumerable of the element, and reaches it through that view. The audit
/// in <see cref="IntrinsicSupply"/> checks the members against the same type.
/// </summary>
internal static class ArrayEnumeration
{
    public const string ClrEnumerator = "System.Collections.Generic.IEnumerator";

    private const string ClrEnumerable = "System.Collections.Generic.IEnumerable";

    public static bool IsEnumerator(IMistType type)
    {
        return type is MistType { DeclaringType: { } outer }
            && outer.FullyQualifiedName == WellKnownTypes.Array.FullyQualifiedName
            && MistType.StripArity(type.Name) == "Enumerator";
    }

    public static string? Emit(ConstructedType constructed, Func<IMistType, string> emit)
    {
        if(constructed.Enclosing is not { TypeArguments: [{ } element, ..] } || !IsEnumerator(constructed.Definition))
        {
            return null;
        }

        return $"{ClrEnumerator}<{emit(element)}>";
    }

    public static bool Enumerates(Method callee)
    {
        return callee is { Name: "GetEnumerator", ParametersList.Count: 0 }
            && callee.DeclaringType?.FullyQualifiedName == WellKnownTypes.Array.FullyQualifiedName;
    }

    public static string Rewrite(string receiver, IMistType receiverType)
    {
        var element = receiverType is ConstructedType { TypeArguments: [{ } first, ..] }
            ? TypeNameEmitter.Emit(first)
            : "object";

        return $"(({ClrEnumerable}<{element}>){receiver}).GetEnumerator()";
    }
}