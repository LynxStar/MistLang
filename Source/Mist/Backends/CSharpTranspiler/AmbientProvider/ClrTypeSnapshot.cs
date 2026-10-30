using Mist.Common.Diagnostics;

namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

internal static class ClrTypeSnapshot
{
    public static TypeValue Of(Type type)
    {
        return new TypeValue(type.AssemblyQualifiedName ?? type.FullName ?? type.Name, Spelling(type));
    }

    public static string Spelling(Type type)
    {
        if(type.IsByRef)
        {
            return Spelling(type.GetElementType()!);
        }

        if(type.IsPointer)
        {
            return $"{Spelling(type.GetElementType()!)}*";
        }

        if(type.IsArray)
        {
            return $"{Spelling(type.GetElementType()!)}[]";
        }

        if(type.IsFunctionPointer)
        {
            return "delegate*";
        }

        if(type.IsGenericParameter)
        {
            return type.Name;
        }

        var arity = type.Name.IndexOf('`');
        var name = arity < 0 ? type.Name : type.Name[..arity];
        var qualifier = type.IsNested ? $"{Spelling(type.DeclaringType!)}." : type.Namespace is null ? "" : $"{type.Namespace}.";
        var arguments = type.IsGenericType && !type.IsGenericTypeDefinition
            ? $"<{string.Join(", ", type.GetGenericArguments().Select(Spelling))}>"
            : "";

        return qualifier + name + arguments;
    }
}