using System.Text;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model.Types;

namespace Mist.Backends.CSharpTranspiler;

/// <summary>
/// Generates C# helper structs for Mist union types. Each distinct union type
/// produces a discriminated wrapper struct with a tag field, Is*/As* properties,
/// and implicit conversion operators from each constituent type.
/// </summary>
internal static class UnionHelperGenerator
{
    /// <summary>
    /// Computes the C# helper type name for a <see cref="UnionType"/>.
    /// Format: <c>Union_{Type1}_{Type2}</c> where type names are the C# CLR names
    /// sorted alphabetically (e.g., <c>Union_Int32_String</c>).
    /// </summary>
    public static string GetHelperTypeName(UnionType unionType)
    {
        var names = unionType.Constituents
            .Select(GetConstituentCSharpName)
            .OrderBy(n => n, StringComparer.Ordinal);
        return "Union_" + string.Join("_", names);
    }

    /// <summary>
    /// Returns the C# CLR name for a constituent type, suitable for use in the
    /// helper type name. Uses the type's DisplayName which is already the CLR
    /// name for stdlib types (e.g., "Int32", "String", "Boolean").
    /// For array types, appends "Array" (e.g., "Int32Array").
    /// For constructed generic types, includes arity (e.g., "List_1_String").
    /// </summary>
    internal static string GetConstituentCSharpName(IType type) => type switch
    {
        RefinementType r => GetConstituentCSharpName(r.BaseType),
        ArrayType a => $"{GetConstituentCSharpName(a.ElementType)}Array",
        ConstructedType ct => $"{ct.GenericDefinition.DisplayName}_{ct.TypeArguments.Count}_{string.Join("_", ct.TypeArguments.Select(GetConstituentCSharpName))}",
        _ => type.DisplayName,
    };

    /// <summary>
    /// Generates the complete <c>UnionHelpers.cs</c> file content containing all
    /// helper structs for the given set of distinct union types.
    /// Skips two-constituent void unions (<c>T | void</c>) which are emitted as
    /// C# <c>Nullable&lt;T&gt;</c> or plain <c>T</c> instead.
    /// </summary>
    public static string GenerateUnionHelpersFile(IReadOnlyCollection<UnionType> unionTypes)
    {
        var sb = new StringBuilder();
        _ = sb.AppendLine("#nullable enable");
        _ = sb.AppendLine("using System;");
        _ = sb.AppendLine();

        var emitted = new Dictionary<string, bool>(StringComparer.Ordinal);

        foreach(var unionType in unionTypes)
        {
            // Skip two-constituent void unions — these use Nullable<T> or null
            // instead of a generated helper struct (Requirement 7.1, 7.8).
            if(CSharpTypeMap.IsOptionalUnion(unionType))
            {
                continue;
            }

            var helperName = GetHelperTypeName(unionType);
            if(emitted.ContainsKey(helperName))
            {
                continue;
            }

            emitted[helperName] = true;
            GenerateHelperStruct(sb, unionType, helperName);
            _ = sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Generates a single helper struct for the given union type.
    /// </summary>
    private static void GenerateHelperStruct(StringBuilder sb, UnionType unionType, string helperName)
    {
        _ = sb.AppendLine("[System.Runtime.CompilerServices.CompilerGenerated]");
        _ = sb.AppendLine($"public struct {helperName}");
        _ = sb.AppendLine("{");

        // Fields
        _ = sb.AppendLine("    private readonly byte _tag;");
        _ = sb.AppendLine("    private readonly object _value;");
        _ = sb.AppendLine();

        // Private constructor
        _ = sb.AppendLine($"    private {helperName}(byte tag, object value)");
        _ = sb.AppendLine("    {");
        _ = sb.AppendLine("        _tag = tag;");
        _ = sb.AppendLine("        _value = value;");
        _ = sb.AppendLine("    }");

        // Is* properties and As* properties
        for(var i = 0; i < unionType.Constituents.Count; i++)
        {
            var constituent = unionType.Constituents[i];
            var csharpName = GetConstituentCSharpName(constituent);
            var csharpTypeName = CSharpTypeMap.GetTypeName(constituent);
            var tag = (byte)(i + 1);

            _ = sb.AppendLine();
            _ = sb.AppendLine($"    public bool Is{csharpName} => _tag == {tag};");
            _ = sb.AppendLine();
            _ = sb.AppendLine($"    public {csharpTypeName} As{csharpName} => _tag == {tag}");
            _ = sb.AppendLine($"        ? ({csharpTypeName})_value");
            _ = sb.AppendLine($"        : throw new InvalidOperationException(\"Union does not hold {csharpName}\");");
        }

        // Implicit conversion operators
        for(var i = 0; i < unionType.Constituents.Count; i++)
        {
            var constituent = unionType.Constituents[i];
            var csharpTypeName = CSharpTypeMap.GetTypeName(constituent);
            var tag = (byte)(i + 1);

            _ = sb.AppendLine();
            _ = sb.AppendLine($"    public static implicit operator {helperName}({csharpTypeName} value)");
            _ = sb.AppendLine($"        => new {helperName}({tag}, value);");
        }

        _ = sb.AppendLine("}");
    }

    /// <summary>
    /// Collects all distinct <see cref="UnionType"/> instances from the semantic model
    /// by walking all symbols and their declared types.
    /// </summary>
    public static List<UnionType> CollectUnionTypes(ISemanticModel model)
    {
        var seen = new Dictionary<string, UnionType>(StringComparer.Ordinal);

        foreach(var symbol in model.AllSymbols)
        {
            if(symbol.DeclaredType is not null)
            {
                CollectFromType(symbol.DeclaredType, seen);
            }
        }

        return seen.Values.ToList();
    }

    /// <summary>
    /// Recursively inspects a type for <see cref="UnionType"/> instances,
    /// including those nested inside arrays, constructed generics, and tuples.
    /// </summary>
    private static void CollectFromType(IType type, Dictionary<string, UnionType> seen)
    {
        switch(type)
        {
            case UnionType ut:
                var name = GetHelperTypeName(ut);
                if(!seen.ContainsKey(name))
                {
                    seen[name] = ut;
                }
                // Also collect from constituents (in case they contain nested unions)
                foreach(var constituent in ut.Constituents)
                {
                    CollectFromType(constituent, seen);
                }
                break;

            case ArrayType a:
                CollectFromType(a.ElementType, seen);
                break;

            case ConstructedType ct:
                CollectFromType(ct.GenericDefinition, seen);
                foreach(var arg in ct.TypeArguments)
                {
                    CollectFromType(arg, seen);
                }
                break;

            case TupleType tt:
                foreach(var elementType in tt.ElementTypes)
                {
                    CollectFromType(elementType, seen);
                }
                break;

            case TypeAliasType alias:
                CollectFromType(alias.UnderlyingType, seen);
                break;

            case EnumType enumType:
                if(enumType.UnderlyingType is not null)
                {
                    CollectFromType(enumType.UnderlyingType, seen);
                }
                break;
        }
    }
}