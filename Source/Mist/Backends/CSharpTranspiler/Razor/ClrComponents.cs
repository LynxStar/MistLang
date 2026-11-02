using System.Collections.Immutable;
using System.Reflection;
using Mist.Driver.Contracts;

namespace Mist.Backends.CSharpTranspiler.Razor;

internal sealed class ClrComponents(IAmbientTypeSource ambientTypes)
{
    private const string ComponentInterface = "Microsoft.AspNetCore.Components.IComponent";
    private const string EventHandlersType = "Microsoft.AspNetCore.Components.Web.EventHandlers";
    private const string EventHandlerAttribute = "Microsoft.AspNetCore.Components.EventHandlerAttribute";

    private static readonly string[] ParameterAttributes =
        ["Microsoft.AspNetCore.Components.ParameterAttribute", "Microsoft.AspNetCore.Components.CascadingParameterAttribute"];

    private readonly Dictionary<string, Type?> types = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ComponentFacts?> components = new(StringComparer.Ordinal);

    public ComponentFacts? Find(string fullName)
    {
        if(!components.TryGetValue(fullName, out var facts))
        {
            facts = Lookup(fullName) is { } type ? Read(type) : null;
            components[fullName] = facts;
        }

        return facts;
    }

    public bool IsComponentBase(string fullName)
    {
        return Find(fullName) is not null || Lookup(fullName)?.FullName == ComponentInterface;
    }

    public ComponentFacts? FindBase(string fullName, ImmutableArray<string> arguments)
    {
        if(arguments.IsEmpty)
        {
            return Find(fullName);
        }

        var type = CatalogClrTypeLookup.Find(fullName, ambientTypes.FindClrType, arguments.Length);
        return type is not null && Read(type) is { } facts
            ? facts with { Parameters = CatalogTypeText.Substitute(facts.Parameters, type.GetGenericArguments().Select(argument => argument.Name), arguments) }
            : null;
    }

    public ImmutableArray<EventFacts> Events()
    {
        if(ambientTypes.FindClrType(EventHandlersType) is not { } handlers)
        {
            return [];
        }

        return
        [
            .. handlers.GetCustomAttributesData()
                .Where(attribute => attribute.AttributeType.FullName == EventHandlerAttribute)
                .Select(attribute => attribute.ConstructorArguments)
                .Where(arguments => arguments.Count >= 2 && arguments[0].Value is string && arguments[1].Value is Type)
                .Select(arguments => new EventFacts((string)arguments[0].Value!, ClrTypeText.Name((Type)arguments[1].Value!))),
        ];
    }

    public static ComponentFacts? Read(Type type)
    {
        if(!type.GetInterfaces().Any(contract => contract.FullName == ComponentInterface))
        {
            return null;
        }

        var parameters = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetCustomAttributesData().Any(attribute => ParameterAttributes.Contains(attribute.AttributeType.FullName)))
            .DistinctBy(property => property.Name)
            .Select(property => ParameterFacts.Declared(property.Name, ClrTypeText.Of(property.PropertyType)));
        var arity = type.IsGenericTypeDefinition ? type.GetGenericArguments().Length : 0;
        return new ComponentFacts(ClrTypeText.Name(type), [.. parameters], arity, IsMist: false);
    }

    public Type? Lookup(string fullName)
    {
        if(!types.TryGetValue(fullName, out var type))
        {
            type = CatalogClrTypeLookup.Find(fullName, ambientTypes.FindClrType);
            types[fullName] = type;
        }

        return type;
    }
}