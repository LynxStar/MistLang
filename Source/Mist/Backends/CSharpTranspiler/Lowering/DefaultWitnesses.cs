using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// How one factory serves every instantiation of a generic class or struct. C# emits the generic type
/// once, and its <c>default(T)</c> is a null where Mist's zero of a string or a class is not, so each type
/// parameter whose zero the default needs (<see cref="Defaultability.ZeroNeeded"/>) becomes a factory
/// parameter the use fills from its argument, or leaves null where the argument has no default to pass.
/// <see cref="DefaultConstraintWitness"/> lowers <c>where T : default</c> the same way, under the same
/// names.
/// </summary>
internal static class DefaultWitnesses
{
    [ThreadStatic]
    private static HashSet<TypeParameter>? witnessed;

    public static IReadOnlyList<TypeParameter> Of(ConcreteType declaration)
    {
        return Defaultability.ZeroNeeded(declaration);
    }

    public static List<string> Parameters(ConcreteType declaration)
    {
        return Of(declaration).Select(DefaultConstraintWitness.Parameter).ToList();
    }

    public static IDisposable Witnessing(ConcreteType declaration)
    {
        var enclosing = witnessed;
        witnessed = new HashSet<TypeParameter>(Of(declaration), ReferenceEqualityComparer.Instance);
        return new Scope(enclosing);
    }

    public static bool IsWitnessed(TypeParameter parameter)
    {
        return DefaultConstraintWitness.Constrains(parameter) || witnessed?.Contains(parameter) == true;
    }

    public static List<string>? Arguments(ConcreteType declaration, IMistType type)
    {
        var substitution = TypeSubstitution.ForReceiver(type as ConstructedType);
        var arguments = new List<string>();

        foreach(var parameter in Of(declaration))
        {
            if(Argument(substitution.Apply(parameter, interner: null)) is not { } argument)
            {
                return null;
            }

            arguments.Add(argument);
        }

        return arguments;
    }

    public static List<string> Defaults(ConcreteType declaration)
    {
        return Of(declaration).Select(parameter => $"() => default({DependentCarrierNames.Parameter(parameter)})").ToList();
    }

    private static string? Argument(IMistType type)
    {
        type = TypeNameEmitter.Closed(type);

        if(type is TypeParameter parameter && IsWitnessed(parameter))
        {
            return DefaultConstraintWitness.Name(parameter);
        }

        return DefaultSynthesis.Expression(type) is { } value ? $"() => {value}" : null;
    }

    private sealed class Scope(HashSet<TypeParameter>? enclosing) : IDisposable
    {
        public void Dispose()
        {
            witnessed = enclosing;
        }
    }
}