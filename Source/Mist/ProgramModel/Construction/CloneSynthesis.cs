using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Gives each type <see cref="CloneDerivation"/> derives the <c>Clone</c> and the <c>IClonable</c> conformance a
/// declared one would have (ownership-v2 Requirement 8.13), before bodies bind, so a call, a constraint and an
/// implicit clone all find them. The <c>Clone</c> is kept off the declared methods, which have bodies or are slots.
/// Every decision is taken before any member is added, so none depends on the order the types are visited in.
/// </summary>
internal static class CloneSynthesis
{
    public static void Run(IEnumerable<IMistType> sourceTypes, Model.ProgramModel model)
    {
        var types = WithNested(sourceTypes.OfType<MistType>()).Distinct().ToList();
        var clones = new CloneDerivation(types);
        model.Clones = clones;

        var clonable = model.TypesByFQN.GetValueOrDefault(WellKnownTypes.IClonable.FullyQualifiedName) as AbstractType;
        var deriving = types.OfType<ConcreteType>().Where(clones.Derives).ToList();

        foreach(var type in deriving)
        {
            IMistType itself = type.TypeParametersList.Count == 0 ? type : model.TypeInterner.GetOrCreateConstructedType(type, type.TypeParametersList);
            var modifiers = new MemberModifiers { Accessibility = Accessibility.Public, IsOwned = true, IsReadonly = true };
            var clone = new Method("Clone", itself, [], modifiers)
            {
                DeclaringType = type,
                Scope = new MethodScope(type.Scope),
                IsDerivedClone = true,
            };

            type.DerivedClone = clone;

            if(clonable is not null && !type.BaseAbstractTypes.Exists(declared => declared is ConstructedType { TypeArguments: [var argument] } constructed
                && ReferenceEquals(constructed.Definition, clonable) && argument.FullyQualifiedName == itself.FullyQualifiedName))
            {
                var conformance = model.TypeInterner.GetOrCreateConstructedType(clonable, [itself]);
                type.BaseAbstractTypes.Add(conformance);
                type.DerivedCloneConformance = conformance;
            }
        }
    }

    private static IEnumerable<MistType> WithNested(IEnumerable<MistType> types)
    {
        foreach(var type in types)
        {
            yield return type;

            if(type is MemberType member)
            {
                foreach(var nested in WithNested(member.NestedTypes.OfType<MistType>()))
                {
                    yield return nested;
                }
            }
        }
    }
}