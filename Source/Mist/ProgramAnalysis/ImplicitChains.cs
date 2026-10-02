using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis;

/// <summary>
/// What a constructor that writes no chain runs first, which purity judges and class-inheritance 8.3
/// refuses the absence of: the base's constructor that can be called with nothing -- no parameter, or a
/// single params one -- and is accessible from the derived type, as C# picks it for an implicit
/// <c>base()</c>, or the base itself when it declares no instance constructor. Only a Mist class base is
/// refused for having none: an Ambient one's projection drops constructors it cannot type and knows no
/// optional or params parameter, so Roslyn stays its judge, and a derived struct's is struct-inheritance
/// 7.4, a refusal of its own.
/// </summary>
internal static class ImplicitChains
{
    public static object? Of(ConcreteType? derived)
    {
        if(derived?.BaseType is not { } baseType || PurityJudge.DefinitionOf(baseType) is not ConcreteType { IsIntrinsic: false } type)
        {
            return null;
        }

        var declared = type.ConstructorsList.FindAll(candidate => !candidate.Modifiers.IsStatic);

        return declared.Count == 0 ? type : CalledWithNothing(declared, derived);
    }

    public static Constructor? CalledWithNothing(List<Constructor> candidates, MistType from)
    {
        var reachable = candidates.FindAll(candidate => candidate.DeclaringType is not { } declaring
            || AccessDomains.Admits(from, from.Assembly, candidate.Modifiers.Accessibility, declaring));

        return reachable.Find(candidate => candidate.ParametersList.Count == 0)
            ?? reachable.Find(candidate => candidate.ParametersList is [{ IsFlattened: true }]);
    }

    public static MistType? UnsatisfiedBase(Constructor constructor)
    {
        return constructor is { ChainKind: ConstructorChainKind.None, Modifiers.IsStatic: false, DeclaringType: ClassType { BaseType: { } baseType } derived }
            && PurityJudge.DefinitionOf(baseType) is ClassType { IsIntrinsic: false, Assembly.Kind: not MistAssemblyKind.Ambient }
            && Of(derived) is null
            ? baseType
            : null;
    }
}