using System.Diagnostics.CodeAnalysis;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

internal sealed partial class AmbientTypeResolver
{
    private AmbientTypeResolver? storageResolver;

    private AmbientTypeResolver(AmbientTypeResolver source)
        : this(new Dictionary<string, DeclaredType>(source.shells), source.wellKnownTypes, source.typeInterner, new DiagnosticBag(), source.declaredTypes)
    {
        storageResolver = this;
        unavailable.UnionWith(source.unavailable);
    }

    public MistType ResolveStorage(Type clrType, Scope scope)
    {
        storageResolver ??= new AmbientTypeResolver(this);
        if(!ReferenceEquals(storageResolver, this))
        {
            return storageResolver.ResolveStorage(clrType, scope);
        }

        PrepareStorageType(clrType);
        var resolved = Resolve(clrType, scope);
        return resolved is ErrorType && clrType.IsGenericType && clrType.DeclaringType is { IsGenericType: true }
            ? ConstructStorageType(clrType, scope)
            : resolved;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2055", Justification = MetadataOnlyReflection.Justification)]
    private MistType ConstructStorageType(Type clrType, Scope scope)
    {
        var definitionClrType = clrType.GetGenericTypeDefinition();
        var definition = Resolve(definitionClrType, scope);
        if(definition is ErrorType or NotProjectedType)
        {
            return definition;
        }

        var clrArguments = clrType.GetGenericArguments();
        var inherited = definitionClrType.DeclaringType?.GetGenericArguments().Length ?? 0;
        var enclosing = inherited == 0
            ? null
            : ConstructStorageType(definitionClrType.DeclaringType!.MakeGenericType(clrArguments[..inherited]), scope) as ConstructedType;
        if(inherited > 0 && enclosing is null)
        {
            return ErrorType.Instance;
        }

        var arguments = new List<IMistType>();
        foreach(var clrArgument in clrArguments[inherited..])
        {
            var argument = ResolveStorage(clrArgument, scope);
            if(argument is ErrorType or NotProjectedType)
            {
                return argument;
            }

            arguments.Add(argument);
        }

        if(definition is IGenericDeclaration { TypeParameters.Count: var declared } && declared > arguments.Count
            && wellKnownTypes.TryGetValue("System.Int32", out var int32))
        {
            while(arguments.Count < declared)
            {
                arguments.Add(typeInterner.GetOrCreateUnknownValue(int32));
            }
        }

        return typeInterner.GetOrCreateConstructedType(definition, arguments, enclosing);
    }

    private void PrepareStorageType(Type clrType)
    {
        if(clrType.IsGenericParameter)
        {
            return;
        }

        if(clrType.HasElementType)
        {
            PrepareStorageType(clrType.GetElementType()!);
            return;
        }

        if(clrType.IsGenericType && !clrType.IsGenericTypeDefinition)
        {
            foreach(var argument in clrType.GetGenericArguments())
            {
                PrepareStorageType(argument);
            }

            if(NullableUnderlying(clrType) is not null || ClrValueTuple.Cardinality(clrType) >= 2)
            {
                return;
            }

            clrType = clrType.GetGenericTypeDefinition();
        }

        var name = BuildFQN(clrType);
        if(shells.ContainsKey(name) || wellKnownTypes.ContainsKey(name) || unavailable.Contains(name))
        {
            return;
        }

        MistType? parent = null;
        if(clrType.DeclaringType is { } declaringType)
        {
            PrepareStorageType(declaringType);
            parent = ResolveByFQN(BuildFQN(declaringType));
        }

        var shell = TypeProjector.CreateShell(clrType, clrType.Namespace ?? "", parent, parent?.Scope);
        shell.Assembly = MistAssembly.Ambient;
        // Storage-only shells stay in this resolver's private dictionary, never in member lookup.
        shells[name] = shell;
        if(shell is not MemberType member || !TypeParameterProjector.Initialize(member, clrType))
        {
            return;
        }

        if(shell is StructType structure)
        {
            StorageProjector.Project(structure, clrType, this);
        }
    }
}