using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis;

/// <summary>
/// What a constructor that writes no chain runs first: an accessible base constructor callable with
/// nothing, or the base itself when it declares no instance constructor. Class and struct refusals
/// share this decision; an Ambient class base stays with Roslyn because its projection can omit
/// constructors and optional or params parameters.
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

    public static MistType? UnsatisfiedStructBase(Constructor constructor)
    {
        return constructor is { ChainKind: ConstructorChainKind.None, Modifiers.IsStatic: false, DeclaringType: StructType { BaseType: { } baseType } derived }
            && PurityJudge.DefinitionOf(baseType) is StructType { IsIntrinsic: false }
            && Of(derived) is null
            ? baseType
            : null;
    }
}