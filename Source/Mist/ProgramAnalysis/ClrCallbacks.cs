using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis;

/// <summary>
/// The CLR interfaces through which the target's own code calls back into a Mist type -- a foreach
/// disposing its enumerator, a formatter asking for text, a comparer asking for equality or order. A Mist
/// type conforms to one through <c>using System;</c>, and the member answering it then runs from code
/// the program never shows the analysis, so a rule judging what a scope sets off must find it here.
/// </summary>
internal static class ClrCallbacks
{
    public static bool Implements(IMistType type, string contract)
    {
        var visited = new HashSet<IMistType>(ReferenceEqualityComparer.Instance);

        for(var current = Concrete(type); current is not null; current = current.BaseType is { } baseType ? Concrete(baseType) : null)
        {
            var reached = current.BaseAbstractTypes.Cast<IMistType>().Concat(current.ConformancesList.Select(conformance => conformance.AbstractType));

            if(reached.Any(abstractType => Reaches(abstractType, contract, visited)))
            {
                return true;
            }
        }

        return false;
    }

    public static Method? Answering(IMistType type, string name, int arity)
    {
        for(var current = Concrete(type); current is not null; current = current.BaseType is { } baseType ? Concrete(baseType) : null)
        {
            var mapped = current.ConformancesList.SelectMany(conformance => conformance.Mappings).OfType<MethodMapping>()
                .FirstOrDefault(mapping => mapping.TargetName == name && mapping.Source.ParametersList.Count == arity);

            if((mapped?.Source ?? current.MethodsList.Find(method => method.Name == name && method.ParametersList.Count == arity && !method.Modifiers.IsStatic)) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    public static Method? DisposeOf(ForEachStatement loop)
    {
        return loop.GetEnumerator is { ReturnType: var enumerator } && Implements(enumerator, "System.IDisposable")
            ? Answering(enumerator, "Dispose", 0)
            : null;
    }

    public static bool IsUnrecorded(ForEachStatement loop)
    {
        return loop.GetEnumerator is null
            && Indexability.ArrayElement(loop.Collection.Type) is null
            && PurityJudge.DefinitionOf(loop.Collection.Type) is ConcreteType { IsIntrinsic: false } or AbstractType;
    }

    private static ConcreteType? Concrete(IMistType type)
    {
        return PurityJudge.DefinitionOf(type) is ConcreteType { IsIntrinsic: false } concrete ? concrete : null;
    }

    private static bool Reaches(IMistType abstractType, string contract, HashSet<IMistType> visited)
    {
        if(PurityJudge.DefinitionOf(abstractType) is not AbstractType definition || !visited.Add(definition))
        {
            return false;
        }

        return MistType.StripArity(definition.FullyQualifiedName) == contract
            || definition.BaseAbstractTypes.Any(inherited => Reaches(inherited, contract, visited));
    }
}