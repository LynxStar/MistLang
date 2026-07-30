using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
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
    public static string Emit(MemberType type, DiagnosticBag diagnostics)
    {
        var writer = new CSharpWriter();

        // Always emit using System for access to Console, etc.
        writer.WriteLine("using System;");
        writer.WriteBlankLine();

        writer.WriteLine($"namespace {type.NamespaceName};");
        writer.WriteBlankLine();

        var typeKeyword = type switch
        {
            AbstractType => "interface",
            StructType => "struct",
            ClassType => "class",
            _ => throw new InvalidOperationException($"ICE: unexpected MemberType kind '{type.GetType().Name}' in TypeEmitter"),
        };
        var accessibility = type.Modifiers.Accessibility == Accessibility.Public ? "public " : "";
        var typeName = EmitTypeName(type);

        writer.WriteLine($"{accessibility}{typeKeyword} {typeName}{EmitBaseList(type)}");
        using(writer.Block())
        {
            var concrete = type as ConcreteType;
            var anyEmitted = false;

            // Fields first so they read as the type's state before the members that use
            // it, and because C# requires nothing of the order. Each is a single line, so
            // they run together as one block -- a blank line between every one of them
            // reads as noise rather than as separation.
            foreach(var field in concrete?.FieldsList ?? [])
            {
                FieldEmitter.Emit(field, writer, diagnostics);
                anyEmitted = true;
            }

            foreach(var constructor in concrete?.ConstructorsList ?? [])
            {
                SeparateMember(writer, ref anyEmitted);
                ConstructorEmitter.Emit(constructor, type, writer, diagnostics);
            }

            foreach(var method in type.MethodsList)
            {
                SeparateMember(writer, ref anyEmitted);
                if(type is AbstractType)
                {
                    EmitInterfaceMethod(method, writer);
                }
                else
                {
                    MethodEmitter.Emit(method, type.DeclarationNode, writer, diagnostics);
                }
            }
        }

        return writer.ToString();
    }

    /// <summary>
    /// The <c>: Base, IFirst, ISecond</c> clause, or empty when the type has neither a base nor an
    /// abstract to implement.
    /// <para>
    /// The concrete base comes first because C# requires it to. Mist keeps the two apart --
    /// <see cref="ConcreteType.BaseType"/> against <see cref="MemberType.BaseAbstractTypes"/> --
    /// so the ordering does not depend on how the declaration was written.
    /// </para>
    /// </summary>
    private static string EmitBaseList(MemberType type)
    {
        var bases = new List<string>();

        if(type is ConcreteType { BaseType: { } baseType })
        {
            bases.Add(TypeNameEmitter.Emit(baseType));
        }

        foreach(var abstractBase in type.BaseAbstractTypes)
        {
            bases.Add(TypeNameEmitter.Emit(abstractBase));
        }

        return bases.Count > 0 ? $" : {string.Join(", ", bases)}" : "";
    }

    /// <summary>
    /// Writes the blank line that precedes a block-bodied member, unless that member opens
    /// the type body. Called per member rather than per section, so it also supplies the
    /// break between the field block and whatever follows it.
    /// </summary>
    private static void SeparateMember(CSharpWriter writer, ref bool anyEmitted)
    {
        if(anyEmitted)
        {
            writer.WriteBlankLine();
        }

        anyEmitted = true;
    }

    /// <summary>
    /// Interface methods are bodiless declarations terminated by a semicolon, with no
    /// accessibility modifiers (C# interface members are implicitly public).
    /// </summary>
    private static void EmitInterfaceMethod(Method method, CSharpWriter writer)
    {
        var returnType = TypeNameEmitter.Emit(method.ReturnType);
        var parameters = ParameterEmitter.Emit(method.ParametersList);
        writer.WriteLine($"{returnType} {method.Name}({parameters});");
    }

    /// <summary>
    /// Computes the relative file path for a type based on its namespace.
    /// E.g., namespace "Simpliest" with type "Program" -> "Simpliest/Program.cs"
    /// Generic types include their arity to avoid collisions when the same base name
    /// exists at multiple arities: "GenericBox" vs "GenericBox`1" vs "GenericBox`2".
    /// </summary>
    public static string GetRelativePath(MemberType type)
    {
        var namespacePath = type.NamespaceName.Replace('.', Path.DirectorySeparatorChar);
        return Path.Combine(namespacePath, $"{FileName(type)}.cs");
    }

    /// <summary>
    /// The file-safe name for a type. Non-generic types use their bare name.
    /// Generic types append a backtick and arity count to disambiguate:
    /// <c>GenericBox`1</c>, <c>GenericBox`2</c>.
    /// </summary>
    private static string FileName(MemberType type)
    {
        if(type.TypeParametersList.Count == 0)
        {
            return type.Name;
        }

        return $"{CleanName(type)}`{type.TypeParametersList.Count}";
    }

    /// <summary>
    /// The declared name with type parameter names appended. A non-generic type
    /// returns its bare name; a generic one returns e.g. <c>GenericBox&lt;T&gt;</c>.
    /// </summary>
    private static string EmitTypeName(MemberType type)
    {
        if(type.TypeParametersList.Count == 0)
        {
            return type.Name;
        }

        var baseName = CleanName(type);
        var typeParams = string.Join(", ", type.TypeParametersList.Select(tp => tp.Name));
        return $"{baseName}<{typeParams}>";
    }

    /// <summary>
    /// Strips the arity-bearing suffix (e.g., <c>GenericBox&lt;&gt;</c> → <c>GenericBox</c>).
    /// Returns the name unchanged for non-generic types.
    /// </summary>
    private static string CleanName(MemberType type)
    {
        var arityIndex = type.Name.IndexOf('<');
        return arityIndex >= 0 ? type.Name[..arityIndex] : type.Name;
    }
}