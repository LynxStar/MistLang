using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Emits a single type (class/struct) as a complete C# source file.
/// Handles namespace declaration, type declaration, and method iteration.
/// </summary>
internal static class TypeEmitter
{
    /// <summary>
    /// Emits a type to a C# source string. Returns the file content.
    /// </summary>
    public static string Emit(MemberType type)
    {
        var writer = new CSharpWriter();

        // Always emit using System for access to Console, etc.
        writer.WriteLine("using System;");
        writer.WriteBlankLine();

        writer.WriteLine($"namespace {type.NamespaceName};");
        writer.WriteBlankLine();

        var typeKeyword = type is StructType ? "struct" : "class";
        var accessibility = type.Modifiers.Accessibility == Accessibility.Public ? "public " : "";

        writer.WriteLine($"{accessibility}{typeKeyword} {type.Name}");
        writer.OpenBrace();

        for(var i = 0; i < type.MethodsList.Count; i++)
        {
            if(i > 0)
            {
                writer.WriteBlankLine();
            }

            MethodEmitter.Emit(type.MethodsList[i], writer);
        }

        writer.CloseBrace();

        return writer.ToString();
    }

    /// <summary>
    /// Computes the relative file path for a type based on its namespace.
    /// E.g., namespace "Simpliest" with type "Program" -> "Simpliest/Program.cs"
    /// </summary>
    public static string GetRelativePath(MemberType type)
    {
        var namespacePath = type.NamespaceName.Replace('.', Path.DirectorySeparatorChar);
        return Path.Combine(namespacePath, $"{type.Name}.cs");
    }
}