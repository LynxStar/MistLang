using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// Where an extension method Mist bound is called by its carrier rather than on its receiver. C# binds
/// a call written on an instance to any applicable instance member of the receiver's type before an
/// extension, and an intrinsic or projected receiver has CLR members its Mist declaration does not show,
/// so the instance spelling can run a member the program never bound and the analysis never judged.
/// The object's instance ToString, GetHashCode and Equals keep that spelling: every object has them, and
/// the analysis already reads an extension of that shape as the object's member.
/// </summary>
internal static class ExtensionCalls
{
    public static bool NeedsNonNullReceiver(IMistType receiver, Method method)
    {
        // Mist's nonoptional receiver excludes absence even when C# cannot constrain the generic argument.
        return TypeNameEmitter.Closed(receiver) is TypeParameter or PackElementType
            && method.DeclaringBlock?.ResolvedReceiverType is UnknownType;
    }

    public static string? Carrier(Method? method)
    {
        if(method is not { DeclaringBlock: { DeclarationNode: not null } block }
            || (!method.Modifiers.IsStatic
                && (method.Name, method.ParametersList.Count) is ("ToString", 0) or ("GetHashCode", 0) or ("Equals", 1)))
        {
            return null;
        }

        return string.IsNullOrEmpty(block.NamespaceName) ? block.CarrierName : $"{IdentifierSpelling.Qualified(block.NamespaceName)}.{block.CarrierName}";
    }
}