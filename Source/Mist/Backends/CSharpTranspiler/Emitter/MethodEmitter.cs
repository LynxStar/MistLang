using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Emits a method declaration: modifiers, return type, name, parameters, and body.
/// </summary>
internal static class MethodEmitter
{
    public static void Emit(Method method, CSharpWriter writer)
    {
        var modifiers = BuildModifiers(method);
        var returnType = TypeNameEmitter.Emit(method.ReturnType);
        var parameters = BuildParameters(method);

        writer.WriteLine($"{modifiers}{returnType} {method.Name}({parameters})");
        using(writer.Block())
        {
            if(method.Body is not null)
            {
                BodyEmitter.EmitBlock(method.Body, writer);
            }
        }
    }

    private static string BuildModifiers(Method method)
    {
        var parts = new List<string>();

        if(method.Modifiers.Accessibility == Accessibility.Public)
        {
            parts.Add("public");
        }
        else if(method.Modifiers.Accessibility == Accessibility.Private)
        {
            parts.Add("private");
        }
        else if(method.Modifiers.Accessibility == Accessibility.Protected)
        {
            parts.Add("protected");
        }
        else if(method.Modifiers.Accessibility == Accessibility.Internal)
        {
            parts.Add("internal");
        }

        if(method.Modifiers.IsStatic)
        {
            parts.Add("static");
        }

        return parts.Count > 0 ? string.Join(" ", parts) + " " : "";
    }

    private static string BuildParameters(Method method)
    {
        if(method.ParametersList.Count == 0)
        {
            return "";
        }

        var parts = new List<string>();
        foreach(var param in method.ParametersList)
        {
            var typeName = TypeNameEmitter.Emit(param.Type);
            parts.Add($"{typeName} {param.Name}");
        }

        return string.Join(", ", parts);
    }
}