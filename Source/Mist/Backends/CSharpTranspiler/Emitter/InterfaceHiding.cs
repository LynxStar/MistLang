using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Whether an abstract's member hides one an interface it reaches declares, which C# wants marked
/// <c>new</c>. Property hiding depends on the name alone. For methods, C# judges hiding by name,
/// arity and parameters as spelled, ignoring the return type,
/// so the parameters are compared by their C# spelling rather than by
/// <see cref="ProgramAnalysis.SlotSignatures"/>, which lets an abstract's own type parameter agree
/// with anything and would mark a member that hides nothing.
/// </summary>
internal static class InterfaceHiding
{
    public static bool Hides(Property property, AbstractType owner)
    {
        return AbstractInheritance.Reached(owner.BaseAbstractTypes, interner: null).Any(
            reached => reached.Definition.PropertiesList.Any(inherited => inherited.Name == property.Name));
    }

    public static bool Hides(Method method, AbstractType owner)
    {
        return AbstractInheritance.Reached(owner.BaseAbstractTypes, interner: null).Any(
            reached => reached.Definition.MethodsList.Any(inherited => SameSignature(inherited, reached.Substitution, method))
                || IteratorBoilerplateEmitter.InheritsUndeclared(reached.Definition, method));
    }

    private static bool SameSignature(Method inherited, TypeSubstitution through, Method method)
    {
        if(inherited.Name != method.Name
            || inherited.TypeParametersList.Count != method.TypeParametersList.Count
            || inherited.ParametersList.Count != method.ParametersList.Count)
        {
            return false;
        }

        var closing = through.Extend(TypeSubstitution.ForParameters(inherited.TypeParametersList, [.. method.TypeParametersList]));

        return inherited.ParametersList.Zip(method.ParametersList).All(pair =>
            pair.First.Modifier.Direction == pair.Second.Modifier.Direction
            && TypeNameEmitter.EmitUnannotated(closing.Apply(pair.First.Type, interner: null)) == TypeNameEmitter.EmitUnannotated(pair.Second.Type));
    }
}