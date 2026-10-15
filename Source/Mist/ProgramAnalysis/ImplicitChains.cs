using Mist.ProgramModel.Construction.Binding;
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

        return declared.Count == 0 ? type : CalledWithNothing(declared, derived, baseType);
    }

    public static Constructor? CalledWithNothing(List<Constructor> candidates, MistType from, IMistType? target = null)
    {
        var reachable = candidates.FindAll(candidate => candidate.DeclaringType is not { } declaring
            || AccessDomains.Admits(from, from.Assembly, candidate.Modifiers.Accessibility, declaring));
        var closing = TypeSubstitution.ForReceiver(target as ConstructedType);

        return reachable.Find(candidate => candidate.ParametersList.Count == 0)
            ?? reachable.Find(candidate => candidate.ParametersList is [{ IsFlattened: true } sole] && AbsorbsNothing(closing.Apply(sole.Type, interner: null)));
    }

    /// <summary>
    /// Whether a flattened parameter takes an empty argument list once the base clause has closed it.
    /// The homogeneous form always collects an empty array; a value pack takes nothing only when it is
    /// closed to no elements, since one still open stands for every width, and the chain has to hold
    /// at each of them.
    /// </summary>
    private static bool AbsorbsNothing(IMistType closed)
    {
        return closed switch
        {
            TupleType tuple => tuple.ElementTypes.Count == 0,
            NamedTupleType named => named.ElementTypes.Count == 0,
            _ => true,
        };
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