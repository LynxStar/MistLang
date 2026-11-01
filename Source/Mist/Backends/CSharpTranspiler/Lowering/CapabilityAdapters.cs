using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramModel;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

internal static class CapabilityAdapters
{
    private static string RuntimeNamespace { get; } = "Mist.Backends.CSharpTranspiler.Runtime";

    [ThreadStatic]
    private static HashSet<string>? used;

    public static void Reset()
    {
        used = null;
    }

    public static bool Supplies(Method method)
    {
        return Parsing(method) || ArrayMember(method);
    }

    private static bool Parsing(Method method)
    {
        if(method is not { Name: "Parse", Modifiers.IsStatic: true, DeclaringType: { } owner }
            || OptionalUnion.InnerType(method.ReturnType)?.FullyQualifiedName != owner.FullyQualifiedName)
        {
            return false;
        }

        var parameters = method.ParametersList.Select(parameter => parameter.Type).ToArray();
        if(owner.FullyQualifiedName == "Standard.Int32")
        {
            return parameters switch
            {
                [var text] => OptionalString(text) || (text is ConstructedType span
                    && span.Definition.FullyQualifiedName == WellKnownTypes.ReadOnlySpan.FullyQualifiedName
                    && span.TypeArguments[0].FullyQualifiedName == WellKnownTypes.Char.FullyQualifiedName),
                [var text, var provider] => OptionalString(text) && Provider(provider),
                _ => false,
            };
        }

        return owner.FullyQualifiedName is "Standard.UInt64" or "Standard.Single" or "Standard.Double" or "Standard.Decimal"
            && parameters is [var input, var style, var formatProvider]
            && OptionalString(input)
            && style.FullyQualifiedName == "System.Globalization.NumberStyles"
            && Provider(formatProvider);
    }

    private static bool OptionalString(IMistType type)
    {
        return OptionalUnion.InnerType(type)?.FullyQualifiedName == WellKnownTypes.String.FullyQualifiedName;
    }

    private static bool Provider(IMistType type)
    {
        return OptionalUnion.InnerType(type)?.FullyQualifiedName == "System.IFormatProvider";
    }

    private static bool ArrayMember(Method method)
    {
        if(method.Modifiers.IsStatic
            || method.DeclaringType?.FullyQualifiedName != WellKnownTypes.Array.FullyQualifiedName)
        {
            return false;
        }

        if(method is { Name: "Any", ParametersList.Count: 0 })
        {
            return method.ReturnType.FullyQualifiedName == WellKnownTypes.Boolean.FullyQualifiedName;
        }

        if(method.ParametersList is not [var parameter])
        {
            return false;
        }

        var element = method.DeclaringType.TypeParametersList[0];
        return method.Name switch
        {
            "IndexOf" => parameter.Type == element
                && OptionalUnion.InnerType(method.ReturnType)?.FullyQualifiedName == WellKnownTypes.Int32.FullyQualifiedName,
            "Contains" => parameter.Type == element && method.ReturnType.FullyQualifiedName == WellKnownTypes.Boolean.FullyQualifiedName,
            "Sort" => parameter.Type is ConstructedType comparer
                && comparer.Definition.FullyQualifiedName == "System.Collections.Generic.IComparer<>"
                && comparer.TypeArguments is [var compared] && compared == element
                && OptionalUnion.IsVoid(method.ReturnType),
            "Find" or "Any" => parameter.Type is IFunctionType { ParameterTypes: [var input], ReturnType: var result }
                && input == element && result.FullyQualifiedName == WellKnownTypes.Boolean.FullyQualifiedName
                && (method.Name == "Find" ? OptionalUnion.InnerType(method.ReturnType) == element
                    : method.ReturnType.FullyQualifiedName == WellKnownTypes.Boolean.FullyQualifiedName),
            _ => false,
        };
    }

    public static string Rewrite(Method method, string? receiver, IMistType? receiverType, IReadOnlyList<string> arguments, bool conditional = false)
    {
        if(Parsing(method))
        {
            Use("NumericParsing");
            return $"{RuntimeNamespace}.NumericParsing.Parse{method.DeclaringType!.Name}({string.Join(", ", arguments)})";
        }

        Use("ArrayOperations");
        var closed = TypeNameEmitter.Closed(receiverType!);
        var array = (OptionalUnion.InnerType(closed) ?? closed) as ConstructedType
            ?? throw new InvalidOperationException("ICE: array search has no constructed receiver.");
        var target = receiver ?? throw new InvalidOperationException("ICE: array operation has no receiver.");
        var argument = conditional ? BodyEmitter.TemporaryName() : target;
        var call = $"{RuntimeNamespace}.ArrayOperations.{method.Name}({string.Join(", ", arguments.Prepend(argument))})";
        if(conditional)
        {
            var helper = OptionalUnion.IsVoid(method.ReturnType) ? "IfPresentAction" : "IfPresent";
            call = $"{RuntimeNamespace}.ArrayOperations.{helper}({receiver}, {argument} => {call})";
        }

        if(method.Name == "Find")
        {
            var result = TypeSubstitution.ForReceiver(array).Apply(method.ReturnType, interner: null);
            var spelling = TypeNameEmitter.Emit(result);
            if(UnknownUnions.Carrier(array.TypeArguments[0]) is { } union)
            {
                var bound = BodyEmitter.TemporaryName();
                return $"({call} is {TypeNameEmitter.Emit(union)} {bound} ? ({spelling}){bound} : ({spelling})Standard.Void.Instance)";
            }

            return $"(({spelling}){call})";
        }

        if(conditional && !OptionalUnion.IsVoid(method.ReturnType))
        {
            var result = OptionalUnion.InnerType(method.ReturnType) ?? method.ReturnType;
            return $"(({TypeNameEmitter.EmitOptional(result)}){call})";
        }

        return call;
    }

    public static void EmitFiles(string outputDir, List<string> emittedFiles)
    {
        foreach(var name in used ?? [])
        {
            using var stream = typeof(CapabilityAdapters).Assembly.GetManifestResourceStream($"Mist.Runtime.{name}.cs")!;
            using var reader = new StreamReader(stream);
            var path = Path.Combine(outputDir, $"__{name}.g.cs");
            File.WriteAllText(path, "#nullable enable\nusing System;\nusing System.Collections.Generic;\n" + reader.ReadToEnd());
            emittedFiles.Add(path);
        }
    }

    private static void Use(string name)
    {
        _ = (used ??= new HashSet<string>(StringComparer.Ordinal)).Add(name);
    }
}