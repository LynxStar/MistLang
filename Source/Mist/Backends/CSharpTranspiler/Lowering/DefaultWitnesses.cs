using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramAnalysis;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
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
        return Defaultability.ZeroNeeded(declaration).Concat(Stored(declaration))
            .DistinctBy(DefaultConstraintWitness.Name).ToList();
    }

    public static IReadOnlyList<TypeParameter> Stored(ConcreteType declaration)
    {
        var enclosing = declaration.DeclaringType is ConcreteType outer ? Stored(outer) : [];
        return enclosing.Concat(declaration.TypeParametersList.Where(DefaultConstraintWitness.Constrains)
            .SelectMany<TypeParameter, TypeParameter>(parameter => parameter.IsPack && parameter.PackLength is { } length
                ? Enumerable.Range(0, length).Select(parameter.ElementAt)
                : new[] { parameter })).ToList();
    }

    public static IReadOnlyList<TypeParameter> StaticParameters(Method method)
    {
        return method is { Modifiers.IsStatic: true, DeclaringType: ConcreteType owner } ? Stored(owner) : [];
    }

    public static List<string?> MethodArguments(Method method, IReadOnlyList<IMistType> typeArguments, IMistType? receiverType)
    {
        var closed = method.DeclaringType is { } owner && receiverType is not null ? UnsafeReach.ClosedOwner(owner, receiverType) : receiverType;
        var arguments = (closed is null ? [] : Arguments(StaticParameters(method), closed) ?? [])
            .Select(argument => (string?)argument).ToList();
        arguments.AddRange(DefaultConstraintWitness.Arguments(method.TypeParametersList, typeArguments));
        arguments.AddRange(closed is not null && method.Modifiers.IsStatic && method.DeclaringType is ConcreteType declaring
            ? ConstructorWitnesses.Arguments(ConstructorWitnesses.Stored(declaring), closed) : []);
        if(method.DeclaringType?.Assembly.Kind != MistAssemblyKind.Ambient && !method.Modifiers.IsIntrinsic)
        {
            arguments.AddRange(ConstructorWitnesses.Arguments(method.TypeParametersList, typeArguments));
        }

        return arguments;
    }

    public static IReadOnlyList<TypeParameter> ConstructionOf(ConcreteType declaration)
    {
        return declaration.Assembly.Kind == MistAssemblyKind.Ambient ? []
            : DefaultSynthesis.NeedsSentinel(declaration) ? Stored(declaration) : Of(declaration);
    }

    public static List<string> FactoryParameters(ConcreteType declaration)
    {
        return Parameters(declaration).Concat(ConstructorWitnesses.Parameters(ConstructorWitnesses.Stored(declaration))).ToList();
    }

    public static List<string> FactoryNames(ConcreteType declaration)
    {
        return Of(declaration).Select(DefaultConstraintWitness.Name).Concat(ConstructorWitnesses.Names(ConstructorWitnesses.Stored(declaration))).ToList();
    }

    public static List<string>? FactoryArguments(ConcreteType declaration, IMistType type)
    {
        return Arguments(declaration, type) is { } defaults
            ? defaults.Concat(ConstructorWitnesses.Arguments(ConstructorWitnesses.Stored(declaration), type)).ToList()
            : null;
    }

    public static List<string>? ConstructionArguments(ConcreteType declaration, IMistType type)
    {
        return Arguments(ConstructionOf(declaration), type) is { } defaults
            ? defaults.Concat(ConstructorWitnesses.Arguments(ConstructorWitnesses.Stored(declaration), type)).ToList()
            : null;
    }

    public static List<string> CompatibilityArguments(ConcreteType declaration, bool factory)
    {
        // CLR callers supply no semantic witness; a null CLR default cannot satisfy Func<T>'s result contract.
        return (factory ? Of(declaration) : Stored(declaration))
            .Select(CompatibilityArgument)
            .Concat(ConstructorWitnesses.DefaultArguments(ConstructorWitnesses.Stored(declaration))).ToList();
    }

    private static string CompatibilityArgument(TypeParameter parameter)
    {
        var value = $"default({DependentCarrierNames.Parameter(parameter)})";
        return TypeNameEmitter.IsValueKind(parameter)
            ? $"() => {value}"
            : $"() => {value} ?? throw new System.InvalidOperationException(\"A semantic default witness is required when the CLR default is null.\")";
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
        return Arguments(Of(declaration), type);
    }

    public static List<string>? Arguments(IReadOnlyList<TypeParameter> parameters, IMistType type, string? witnessReceiver = null)
    {
        var substitution = TypeSubstitution.ForReceiver(type as ConstructedType);
        var arguments = new List<string>();

        foreach(var parameter in parameters)
        {
            if(Argument(substitution.Apply(parameter, interner: null), witnessReceiver) is not { } argument)
            {
                return null;
            }

            arguments.Add(argument);
        }

        return arguments;
    }

    public static bool Deferred(Expression expression)
    {
        return BodyWalker.DescendantsAndSelf(expression).OfType<Expression>().Any(node => node switch
        {
            DefaultExpression { Type: TypeParameter parameter } => IsWitnessed(parameter),
            NewExpression { Type: TypeParameter parameter } allocation => ConstructorWitnesses.For(parameter, allocation.Arguments.Select(argument => argument.Type).ToList()) is not null,
            NewExpression allocation when Defaultability.DeclarationOf(TypeNameEmitter.Closed(allocation.Type)) is { } declaration
                => ConstructionOf(declaration).Count > 0 && TypeParameterReach.Collect(TypeNameEmitter.Closed(allocation.Type)).Any(IsWitnessed),
            MethodReferenceExpression reference => reference.TypeArguments.Any(argument => TypeParameterReach.Collect(argument)
                .Any(parameter => IsWitnessed(parameter) || ConstructorWitnesses.Of([parameter]).Count > 0)),
            MemberAccessExpression access => access.TypeArguments.Any(argument => TypeParameterReach.Collect(argument)
                .Any(parameter => IsWitnessed(parameter) || ConstructorWitnesses.Of([parameter]).Count > 0)),
            _ => false,
        });
    }

    public static void EmitInitializers(ConcreteType declaration, CSharpWriter writer, Common.Diagnostics.DiagnosticBag diagnostics)
    {
        foreach(var assignment in DefaultSynthesis.FlattenedAssignments(declaration))
        {
            using var through = TypeEmitter.Substituting(assignment.Path);

            if(assignment.Storage.Initializer is { } initializer && Deferred(initializer))
            {
                BodyEmitter.EmitSequenced(initializer, $"this.{assignment.Storage.Name} = ", writer, diagnostics);
            }
            else if(assignment.Storage.Initializer is null && RefinedZero.IsAdmitted(TypeNameEmitter.Closed(assignment.Storage.Type))
                && DefaultSynthesis.StorageZero(assignment.Storage.Type) is { } zero)
            {
                writer.WriteLine($"this.{assignment.Storage.Name} = {zero};");
            }
            else if(declaration is StructType && assignment.Storage.Initializer is null
                && Defaultability.IsUndecided(TypeNameEmitter.Closed(assignment.Storage.Type))
                && (assignment.Storage.Field is not null || (assignment.Storage.Property is { Getter.IsAuto: true } property
                    && AccessorEmitter.IsBodiless(property.Setter))))
            {
                writer.WriteLine($"this.{assignment.Storage.Name} = default({TypeEmitter.EmitTypeName(declaration)}).{assignment.Storage.Name};");
            }
        }
    }

    public static void EmitStorage(ConcreteType declaration, CSharpWriter writer)
    {
        foreach(var parameter in Stored(declaration))
        {
            writer.WriteLine($"private readonly {DefaultConstraintWitness.Parameter(parameter)};");
        }

        foreach(var witness in ConstructorWitnesses.Stored(declaration))
        {
            writer.WriteLine($"private readonly {ConstructorWitnesses.Parameter(witness)};");
        }
    }

    public static void EmitAssignments(ConcreteType declaration, CSharpWriter writer)
    {
        foreach(var parameter in Stored(declaration))
        {
            var name = DefaultConstraintWitness.Name(parameter);
            writer.WriteLine($"this.{name} = {name};");
        }

        foreach(var witness in ConstructorWitnesses.Stored(declaration))
        {
            var name = ConstructorWitnesses.Name(witness);
            writer.WriteLine($"this.{name} = {name};");
        }
    }

    private static string? Argument(IMistType type, string? witnessReceiver = null)
    {
        type = TypeNameEmitter.Closed(type);

        if(type is TypeParameter parameter && IsWitnessed(parameter))
        {
            var name = DefaultConstraintWitness.Name(parameter);
            return witnessReceiver is null ? name : $"{witnessReceiver}.{name}";
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