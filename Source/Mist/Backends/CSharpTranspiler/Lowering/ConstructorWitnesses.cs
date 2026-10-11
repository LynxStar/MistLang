using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

internal static class ConstructorWitnesses
{
    [ThreadStatic]
    private static bool native;

    public static IDisposable NativeScope()
    {
        var enclosing = native;
        native = true;
        return new Scope(enclosing);
    }

    public static IReadOnlyList<ConstructorFactoryWitness> Of(IReadOnlyList<TypeParameter> parameters)
    {
        var factories = new List<ConstructorFactoryWitness>();

        foreach(var declared in parameters)
        {
            IEnumerable<TypeParameter> elements = declared.IsPack && declared.PackLength is { } length
                ? Enumerable.Range(0, length).Select(declared.ElementAt)
                : [declared];

            foreach(var parameter in elements)
            {
                var signatures = parameter.ConstraintsList.OfType<ConstructorConstraint>().Select(constraint => constraint.ParameterTypes).ToList();

                for(var index = 0; index < signatures.Count; index++)
                {
                    factories.Add(new ConstructorFactoryWitness(parameter, signatures[index], index));
                }
            }
        }

        return factories;
    }

    public static IReadOnlyList<ConstructorFactoryWitness> Stored(ConcreteType declaration)
    {
        return declaration.Assembly.Kind == MistAssemblyKind.Ambient ? []
            : (declaration.DeclaringType is ConcreteType enclosing ? Stored(enclosing) : [])
                .Concat(Of(declaration.TypeParametersList)).ToList();
    }

    public static string Name(ConstructorFactoryWitness witness)
    {
        return $"__construct_{DependentCarrierNames.Parameter(witness.Parameter)}_{witness.Index}";
    }

    public static List<string> Names(IReadOnlyList<ConstructorFactoryWitness> witnesses)
    {
        return witnesses.Select(Name).ToList();
    }

    public static string Parameter(ConstructorFactoryWitness witness)
    {
        var inputs = witness.Signature.Select(TypeNameEmitter.Emit).ToList();
        var types = UsesTuple(witness) ? [$"({string.Join(", ", inputs)})", TypeNameEmitter.Emit(witness.Parameter)]
            : inputs.Append(TypeNameEmitter.Emit(witness.Parameter)).ToList();
        return $"System.Func<{string.Join(", ", types)}> {Name(witness)}";
    }

    public static bool UsesTuple(ConstructorFactoryWitness witness)
    {
        return witness.Signature.Count > DelegateCeiling.Inputs;
    }

    public static List<string> Parameters(IReadOnlyList<ConstructorFactoryWitness> witnesses)
    {
        return witnesses.Select(Parameter).ToList();
    }

    public static List<string> Arguments(IReadOnlyList<TypeParameter> parameters, IReadOnlyList<IMistType> typeArguments)
    {
        return Arguments(Of(parameters), TypeSubstitution.ForParameters(parameters, typeArguments), new HashSet<string>(StringComparer.Ordinal), 0);
    }

    public static List<string> Arguments(IReadOnlyList<ConstructorFactoryWitness> witnesses, IMistType receiver, string? witnessReceiver = null)
    {
        return Arguments(witnesses, TypeSubstitution.ForReceiver(TypeNameEmitter.Closed(receiver) as ConstructedType),
            new HashSet<string>(StringComparer.Ordinal), 0, witnessReceiver);
    }

    public static List<string> DefaultArguments(IReadOnlyList<ConstructorFactoryWitness> witnesses)
    {
        return witnesses.Select(witness => Native(witness.Parameter, witness.Signature, 0)).ToList();
    }

    public static ConstructorFactoryWitness? For(TypeParameter parameter, int arity)
    {
        var matching = native ? [] : Of([parameter]).Where(witness => witness.Signature.Count == arity).ToList();
        return matching.Count == 1 ? matching[0] : null;
    }

    public static ConstructorFactoryWitness? For(TypeParameter parameter, IReadOnlyList<IMistType> argumentTypes)
    {
        var matching = native ? [] : Of([parameter]).Where(witness => witness.Signature.Count == argumentTypes.Count).ToList();
        var exact = matching.Where(witness => Same(witness.Signature, argumentTypes)).ToList();

        if(exact.Count == 1)
        {
            return exact[0];
        }

        var applicable = matching.Where(witness => witness.Signature.Zip(argumentTypes)
            .All(pair => ConversionRelation.IsImplicit(TypeNameEmitter.Closed(pair.Second), TypeNameEmitter.Closed(pair.First)))).ToList();
        var preferred = applicable.Where(candidate => applicable.All(other => ReferenceEquals(candidate, other)
            || candidate.Signature.Zip(other.Signature).All(pair => ConversionRelation.IsWithin(pair.First, pair.Second)))).ToList();
        return preferred.Count == 1 ? preferred[0] : null;
    }

    private static List<string> Arguments(
        IReadOnlyList<ConstructorFactoryWitness> witnesses, TypeSubstitution substitution, HashSet<string> visiting, int depth,
        string? witnessReceiver = null)
    {
        var arguments = new List<string>();

        foreach(var witness in witnesses)
        {
            var product = TypeNameEmitter.Closed(substitution.Apply(witness.Parameter, interner: null));
            var signature = witness.Signature.Select(type => TypeNameEmitter.Closed(substitution.Apply(type, interner: null))).ToList();

            if(product is TypeParameter parameter && Of([parameter]).FirstOrDefault(factory => Same(factory.Signature, signature)) is { } forwarded)
            {
                var name = Name(forwarded);
                arguments.Add(witnessReceiver is null ? name : $"{witnessReceiver}.{name}");
            }
            else
            {
                arguments.Add(Factory(product, signature, visiting, depth, witnessReceiver));
            }
        }

        return arguments;
    }

    private static string Factory(IMistType product, IReadOnlyList<IMistType> signature, HashSet<string> visiting, int depth,
        string? witnessReceiver = null)
    {
        var key = $"{product.FullyQualifiedName}({string.Join(",", signature.Select(type => type.FullyQualifiedName))})";

        if(!visiting.Add(key))
        {
            return Native(product, signature, depth);
        }

        var supplied = new List<string>();

        if(Defaultability.DeclarationOf(product) is { } declaration)
        {
            supplied.AddRange(DefaultWitnesses.Arguments(DefaultWitnesses.ConstructionOf(declaration), product, witnessReceiver) ?? []);
            supplied.AddRange(Arguments(Stored(declaration), TypeSubstitution.ForReceiver(product as ConstructedType), visiting, depth + 1, witnessReceiver));
        }

        supplied.AddRange(ArgumentNames(signature, depth));
        _ = visiting.Remove(key);
        return $"({LambdaParameters(signature, depth)}) => new {TypeNameEmitter.Emit(product)}({string.Join(", ", supplied)})";
    }

    private static string Native(IMistType product, IReadOnlyList<IMistType> signature, int depth)
    {
        var arguments = string.Join(", ", ArgumentNames(signature, depth));
        var target = product is TypeParameter && signature.Count > 0
            ? $"{TypeNameEmitter.Emit(product)}.{ConstructorConstraintInterfaces.MemberName}"
            : $"new {TypeNameEmitter.Emit(product)}";
        return $"({LambdaParameters(signature, depth)}) => {target}({arguments})";
    }

    private static IEnumerable<string> ArgumentNames(IReadOnlyList<IMistType> signature, int depth)
    {
        return Enumerable.Range(0, signature.Count).Select(index => signature.Count > DelegateCeiling.Inputs
            ? $"__arguments{depth}.Item{index + 1}"
            : $"__arg{depth}_{index}");
    }

    private static string LambdaParameters(IReadOnlyList<IMistType> signature, int depth)
    {
        return signature.Count > DelegateCeiling.Inputs ? $"__arguments{depth}" : string.Join(", ", ArgumentNames(signature, depth));
    }

    private sealed class Scope(bool enclosing) : IDisposable
    {
        public void Dispose()
        {
            native = enclosing;
        }
    }

    private static bool Same(IReadOnlyList<IMistType> left, IReadOnlyList<IMistType> right)
    {
        return left.Count == right.Count && left.Zip(right)
            .All(pair => ConversionRelation.IsIdentity(TypeNameEmitter.Closed(pair.First), TypeNameEmitter.Closed(pair.Second)));
    }
}

internal sealed record ConstructorFactoryWitness(TypeParameter Parameter, IReadOnlyList<IMistType> Signature, int Index);