using System.Reflection;
using Mist.ProgramModel.Model.Members;

namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

internal static class ClrSequenceLengths
{
    public static SequenceLengthOperation Of(MethodInfo method)
    {
        var host = method.DeclaringType;
        var parameters = method.GetParameters();
        var result = method.ReturnType;

        if(host is null)
        {
            return SequenceLengthOperation.None;
        }

        if(!method.IsStatic && host.IsGenericType && IsSpan(host.GetGenericTypeDefinition()))
        {
            var element = host.GetGenericArguments()[0];

            if(method.Name == "ToArray" && parameters.Length == 0 && IsArray(result, element))
            {
                return SequenceLengthOperation.Preserved;
            }

            if(method.Name == "Slice" && parameters.Length is 1 or 2 && result.IsGenericType
                && result.GetGenericTypeDefinition() == host.GetGenericTypeDefinition() && result.GetGenericArguments()[0] == element
                && parameters.All(parameter => Is(parameter.ParameterType, typeof(int))))
            {
                return SequenceLengthOperation.Slice;
            }
        }

        if(!method.IsStatic && Is(host, typeof(string)) && method.Name == "ToCharArray"
            && parameters.Length == 0 && result.IsSZArray && Is(result.GetElementType()!, typeof(char)))
        {
            return SequenceLengthOperation.Preserved;
        }

        if(method.IsStatic && Is(host, typeof(MemoryExtensions)) && method.Name == "AsSpan"
            && parameters is [var source] && result.IsGenericType)
        {
            var element = result.GetGenericArguments()[0];
            var definition = result.GetGenericTypeDefinition();

            if((Is(definition, typeof(Span<>)) && IsArray(source.ParameterType, element))
                || (Is(definition, typeof(ReadOnlySpan<>)) && Is(source.ParameterType, typeof(string)) && Is(element, typeof(char))))
            {
                return SequenceLengthOperation.Preserved;
            }
        }

        if(method.IsStatic && Is(host, typeof(Enumerable)) && method.Name == "ToArray"
            && parameters is [var enumerable] && enumerable.ParameterType.IsGenericType
            && Is(enumerable.ParameterType.GetGenericTypeDefinition(), typeof(IEnumerable<>))
            && IsArray(result, enumerable.ParameterType.GetGenericArguments()[0]))
        {
            return SequenceLengthOperation.Preserved;
        }

        return SequenceLengthOperation.None;
    }

    private static bool IsSpan(Type type)
    {
        return Is(type, typeof(Span<>)) || Is(type, typeof(ReadOnlySpan<>));
    }

    private static bool IsArray(Type type, Type element)
    {
        return type.IsSZArray && type.GetElementType() == element;
    }

    private static bool Is(Type metadata, Type runtime)
    {
        // MetadataLoadContext types have separate object identity; the assembly is part of CLR type identity.
        return metadata.FullName == runtime.FullName && metadata.Assembly.FullName == runtime.Assembly.FullName;
    }
}