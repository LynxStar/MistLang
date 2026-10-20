using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

/// <summary>
/// The tuple names C# records in custom attributes on InterfaceImpl and GenericParamConstraint rows, which
/// reflection does not surface, read from the defining assembly's own metadata. Reflection keeps no link from
/// an interface or constraint back to its row, so a row is matched by its signature; an unreadable file, a
/// signature two rows share, or a name count that disagrees leaves the tuple structural.
/// </summary>
internal sealed class TupleNameRows
{
    private const string AttributeNamespace = "System.Runtime.CompilerServices";
    private const string AttributeName = "TupleElementNamesAttribute";

    private readonly Dictionary<string, MetadataReader?> readers = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PEReader> images = [];
    private readonly Lock gate = new();

    public TupleElementNames? OfInterface(Type implementer, Type iface)
    {
        return Match(implementer, iface, reader => Definition(reader, implementer) is { } definition
            ? reader.GetTypeDefinition(definition).GetInterfaceImplementations()
                .Select(reader.GetInterfaceImplementation)
                .Select(row => (row.Interface, row.GetCustomAttributes()))
            : []);
    }

    public TupleElementNames? OfConstraint(Type parameter, Type constraint)
    {
        return Match(parameter, constraint, reader => Parameters(reader, parameter) is { } parameters
            && parameter.GenericParameterPosition < parameters.Count
            ? reader.GetGenericParameter(parameters[parameter.GenericParameterPosition]).GetConstraints()
                .Select(reader.GetGenericParameterConstraint)
                .Select(row => (row.Type, row.GetCustomAttributes()))
            : []);
    }

    internal static string?[]? Matching(
        string key, IEnumerable<(string Key, string?[]? Names)> rows)
    {
        var matches = rows.Where(row => row.Key == key).Take(2).ToList();
        return matches.Count == 1 ? matches[0].Names : null;
    }

    internal static string Key(Type type)
    {
        if(type.IsGenericParameter)
        {
            return (type.DeclaringMethod is null ? "!" : "!!") + type.GenericParameterPosition;
        }

        if(type.HasElementType)
        {
            var element = Key(type.GetElementType()!);
            return type.IsSZArray ? element + "[]"
                : type.IsArray ? element + "[" + new string(',', type.GetArrayRank() - 1) + "]"
                : type.IsByRef ? element + "&"
                : element + "*";
        }

        if(type.IsGenericType && !type.IsGenericTypeDefinition)
        {
            return Key(type.GetGenericTypeDefinition()) + "<" + string.Join(",", type.GetGenericArguments().Select(Key)) + ">";
        }

        return type.DeclaringType is { } declaring ? Key(declaring) + "+" + type.Name : Qualified(type.Namespace, type.Name);
    }

    internal static string?[]? Names(BlobReader value)
    {
        try
        {
            if(value.Length < 2 || value.ReadUInt16() != 1)
            {
                return null;
            }

            var count = value.ReadInt32();
            if(count < 0 || count > value.RemainingBytes)
            {
                return null;
            }

            var names = new string?[count];
            for(var i = 0; i < count; i++)
            {
                names[i] = value.ReadSerializedString();
            }

            return names;
        }
        catch(BadImageFormatException)
        {
            return null;
        }
    }

    private TupleElementNames? Match(
        Type owner, Type target, Func<MetadataReader, IEnumerable<(EntityHandle Type, CustomAttributeHandleCollection Attributes)>> rows)
    {
        try
        {
            if(ClrValueTuple.NameSlots(target) == 0 || Reader(owner.Module) is not { } reader)
            {
                return null;
            }

            var found = Matching(Key(target), rows(reader).Select(row => (SignatureKey.Of(reader, row.Type), Names(reader, row.Attributes))));
            return TupleElementNames.OfNames(found, target);
        }
        catch(Exception exception) when(GenericConstraintProjector.IsMetadataFailure(exception)
            || exception is InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    private MetadataReader? Reader(Module module)
    {
        var path = module.FullyQualifiedName;
        lock(gate)
        {
            if(!readers.TryGetValue(path, out var reader))
            {
                reader = Open(path);
                readers[path] = reader;
            }

            return reader;
        }
    }

    private MetadataReader? Open(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var image = new PEReader(stream, PEStreamOptions.PrefetchMetadata | PEStreamOptions.LeaveOpen);
            if(!image.HasMetadata)
            {
                image.Dispose();
                return null;
            }

            images.Add(image);
            return image.GetMetadataReader();
        }
        catch(Exception exception) when(exception is IOException or UnauthorizedAccessException
            or BadImageFormatException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static TypeDefinitionHandle? Definition(MetadataReader reader, Type type)
    {
        var handle = MetadataTokens.EntityHandle(type.MetadataToken);
        if(handle.Kind != HandleKind.TypeDefinition || MetadataTokens.GetRowNumber(handle) > reader.TypeDefinitions.Count)
        {
            return null;
        }

        var definition = (TypeDefinitionHandle)handle;
        return reader.StringComparer.Equals(reader.GetTypeDefinition(definition).Name, type.Name) ? definition : null;
    }

    private static GenericParameterHandleCollection? Parameters(MetadataReader reader, Type parameter)
    {
        if(parameter.DeclaringMethod is not { } method)
        {
            return parameter.DeclaringType is { } type && Definition(reader, type) is { } definition
                ? reader.GetTypeDefinition(definition).GetGenericParameters()
                : null;
        }

        var handle = MetadataTokens.EntityHandle(method.MetadataToken);
        if(handle.Kind != HandleKind.MethodDefinition || MetadataTokens.GetRowNumber(handle) > reader.MethodDefinitions.Count)
        {
            return null;
        }

        var definitionOfMethod = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
        return reader.StringComparer.Equals(definitionOfMethod.Name, method.Name) ? definitionOfMethod.GetGenericParameters() : null;
    }

    private static string?[]? Names(MetadataReader reader, CustomAttributeHandleCollection attributes)
    {
        foreach(var handle in attributes)
        {
            var attribute = reader.GetCustomAttribute(handle);
            if(IsTupleElementNames(reader, attribute.Constructor))
            {
                return Names(reader.GetBlobReader(attribute.Value));
            }
        }

        return null;
    }

    private static bool IsTupleElementNames(MetadataReader reader, EntityHandle constructor)
    {
        var owner = constructor.Kind switch
        {
            HandleKind.MemberReference => reader.GetMemberReference((MemberReferenceHandle)constructor).Parent,
            HandleKind.MethodDefinition => reader.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType(),
            _ => default,
        };

        return owner.Kind switch
        {
            HandleKind.TypeReference when reader.GetTypeReference((TypeReferenceHandle)owner) is var reference
                => reader.StringComparer.Equals(reference.Namespace, AttributeNamespace) && reader.StringComparer.Equals(reference.Name, AttributeName),
            HandleKind.TypeDefinition when reader.GetTypeDefinition((TypeDefinitionHandle)owner) is var definition
                => reader.StringComparer.Equals(definition.Namespace, AttributeNamespace) && reader.StringComparer.Equals(definition.Name, AttributeName),
            _ => false,
        };
    }

    private static string Qualified(string? namespaceName, string name)
    {
        return string.IsNullOrEmpty(namespaceName) ? name : namespaceName + "." + name;
    }

    private sealed class SignatureKey : ISignatureTypeProvider<string, object?>
    {
        private static readonly SignatureKey Instance = new();

        public static string Of(MetadataReader reader, EntityHandle handle)
        {
            return handle.Kind switch
            {
                HandleKind.TypeDefinition => Instance.GetTypeFromDefinition(reader, (TypeDefinitionHandle)handle, 0),
                HandleKind.TypeReference => Instance.GetTypeFromReference(reader, (TypeReferenceHandle)handle, 0),
                HandleKind.TypeSpecification => Instance.GetTypeFromSpecification(reader, null, (TypeSpecificationHandle)handle, 0),
                _ => string.Empty,
            };
        }

        public string GetArrayType(string elementType, ArrayShape shape)
        {
            return elementType + "[" + new string(',', shape.Rank - 1) + "]";
        }

        public string GetByReferenceType(string elementType)
        {
            return elementType + "&";
        }

        public string GetFunctionPointerType(MethodSignature<string> signature)
        {
            return string.Empty;
        }

        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
        {
            return genericType + "<" + string.Join(",", typeArguments) + ">";
        }

        public string GetGenericMethodParameter(object? genericContext, int index)
        {
            return "!!" + index;
        }

        public string GetGenericTypeParameter(object? genericContext, int index)
        {
            return "!" + index;
        }

        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired)
        {
            return unmodifiedType;
        }

        public string GetPinnedType(string elementType)
        {
            return elementType;
        }

        public string GetPointerType(string elementType)
        {
            return elementType + "*";
        }

        public string GetPrimitiveType(PrimitiveTypeCode typeCode)
        {
            return "System." + typeCode;
        }

        public string GetSZArrayType(string elementType)
        {
            return elementType + "[]";
        }

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            var definition = reader.GetTypeDefinition(handle);
            var name = reader.GetString(definition.Name);
            var declaring = definition.GetDeclaringType();

            return declaring.IsNil
                ? Qualified(reader.GetString(definition.Namespace), name)
                : GetTypeFromDefinition(reader, declaring, rawTypeKind) + "+" + name;
        }

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            var reference = reader.GetTypeReference(handle);
            var name = reader.GetString(reference.Name);

            return reference.ResolutionScope.Kind == HandleKind.TypeReference
                ? GetTypeFromReference(reader, (TypeReferenceHandle)reference.ResolutionScope, rawTypeKind) + "+" + name
                : Qualified(reader.GetString(reference.Namespace), name);
        }

        public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
        {
            return reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
        }
    }
}