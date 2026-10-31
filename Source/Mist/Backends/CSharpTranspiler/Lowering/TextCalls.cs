using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

internal static class TextCalls
{
    [ThreadStatic]
    private static bool searches;

    [ThreadStatic]
    private static bool sequences;

    private enum Adapter
    {
        None,
        Search,
        Comparison,
        Sequence,
        Format,
    }

    public static void Reset()
    {
        searches = false;
        sequences = false;
    }

    public static bool Respells(Method? method)
    {
        return Classify(method) != Adapter.None;
    }

    public static string Rewrite(Method method, string? receiver, IReadOnlyList<string> arguments, bool conditional)
    {
        var access = conditional ? "?." : ".";
        var joined = string.Join(", ", arguments);

        switch(Classify(method))
        {
            case Adapter.Search:
                searches = true;
                return $"MistText.Index({receiver}{access}{method.Name}({joined}))";
            case Adapter.Comparison:
                return $"{receiver}{access}{method.Name}({joined}, System.StringComparison.InvariantCulture)";
            case Adapter.Sequence:
                sequences = true;
                var values = $"MistText.Strings({arguments[^1]})";
                return method.Name == "Join" ? $"string.Join({arguments[0]}, {values})" : $"string.Concat({values})";
            case Adapter.Format:
                return $"string.Format({arguments[0]} ?? {InvariantCulture.Provider}, {string.Join(", ", arguments.Skip(1))})";
            default:
                throw new InvalidOperationException($"ICE: '{method.Name}' has no text adapter.");
        }
    }

    public static void EmitFile(string outputDir, List<string> emittedFiles)
    {
        if(!searches && !sequences)
        {
            return;
        }

        var writer = new CSharpWriter();
        Emit(writer);
        var path = Path.Combine(outputDir, "__Text.g.cs");
        File.WriteAllText(path, writer.ToString());
        emittedFiles.Add(path);
    }

    internal static void Emit(CSharpWriter writer)
    {
        writer.WriteLine("internal static class MistText");
        using(writer.Block())
        {
            if(searches)
            {
                writer.WriteLine("public static int? Index(int? index)");
                using(writer.Block())
                {
                    writer.WriteLine("return index >= 0 ? index : null;");
                }
            }

            if(sequences)
            {
                writer.WriteLine("public static System.Collections.Generic.IEnumerable<string> Strings<T>(System.Collections.Generic.IEnumerable<T> values)");
                using(writer.Block())
                {
                    writer.WriteLine($"return System.Linq.Enumerable.Select(values, value => {InvariantCulture.Text("value", UnknownType.Instance)});");
                }
            }
        }
    }

    private static Adapter Classify(Method? method)
    {
        if(method is not { Modifiers.IsIntrinsic: true, DeclaringType.FullyQualifiedName: "Standard.String" })
        {
            return Adapter.None;
        }

        var parameters = method.ParametersList.Select(parameter => parameter.Type).ToArray();
        if(!method.Modifiers.IsStatic)
        {
            if(method.Name is "IndexOf" or "LastIndexOf" or "IndexOfAny"
                && OptionalUnion.InnerType(method.ReturnType)?.FullyQualifiedName == WellKnownTypes.Int32.FullyQualifiedName
                && SearchParameters(method.Name, parameters))
            {
                return Adapter.Search;
            }

            return method.Name is "StartsWith" or "EndsWith" && parameters is [var value] && Is(value, "Standard.String")
                && Is(method.ReturnType, "Standard.Boolean")
                ? Adapter.Comparison
                : Adapter.None;
        }

        if(method.Name is "Join" or "Concat" && method.TypeParametersList.Count == 1
            && Is(method.ReturnType, "Standard.String")
            && parameters.Length == (method.Name == "Join" ? 2 : 1)
            && parameters[^1] is ConstructedType { Definition.FullyQualifiedName: "Standard.IEnumerable<>", TypeArguments: [var element] }
            && ReferenceEquals(element, method.TypeParametersList[0])
            && (method.Name == "Concat" || Is(OptionalUnion.InnerType(parameters[0]) ?? parameters[0], "Standard.String") || Is(parameters[0], "Standard.Char")))
        {
            return Adapter.Sequence;
        }

        return method.Name == "Format" && parameters is [var provider, var format, var args]
            && Is(method.ReturnType, "Standard.String")
            && Is(OptionalUnion.InnerType(provider) ?? provider, "System.IFormatProvider")
            && Is(format, "Standard.String")
            && args is ConstructedType { Definition.FullyQualifiedName: "Standard.Array<,>", TypeArguments: [UnknownType, _] }
            ? Adapter.Format
            : Adapter.None;
    }

    private static bool SearchParameters(string name, IMistType[] parameters)
    {
        if(name == "IndexOfAny")
        {
            return parameters.Length is 1 or 2
                && parameters[0] is ConstructedType { Definition.FullyQualifiedName: "Standard.Array<,>", TypeArguments: [var element, _] }
                && Is(element, "Standard.Char")
                && (parameters.Length == 1 || Is(parameters[1], "Standard.Int32"));
        }

        return parameters switch
        {
            [var value] => Is(value, "Standard.Char"),
            [var value, var option] => (Is(value, "Standard.Char") || Is(value, "Standard.String"))
                && (Is(option, "System.StringComparison") || (name == "IndexOf" && Is(value, "Standard.Char") && Is(option, "Standard.Int32"))),
            _ => false,
        };
    }

    private static bool Is(IMistType type, string name)
    {
        return type.FullyQualifiedName == name;
    }
}