using System.Text;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Backends.CSharpTranspiler;

/// <summary>
/// Generates the <c>DependentTypeHelpers.g.cs</c> file containing const-carrier interfaces
/// for dependent type parameters. Only interfaces for primitive types actually used as
/// dependent parameters in the program are emitted.
/// </summary>
internal static class DependentTypeHelpersGenerator
{
    /// <summary>
    /// Maps a C# keyword to its capitalized form for the interface name.
    /// E.g., "int" → "Int", "bool" → "Bool", "char" → "Char".
    /// </summary>
    private static readonly Dictionary<string, string> KeywordToCapitalized = new(StringComparer.Ordinal)
    {
        ["int"] = "Int",
        ["uint"] = "Uint",
        ["long"] = "Long",
        ["ulong"] = "Ulong",
        ["short"] = "Short",
        ["ushort"] = "Ushort",
        ["byte"] = "Byte",
        ["sbyte"] = "Sbyte",
        ["bool"] = "Bool",
        ["char"] = "Char",
    };

    /// <summary>
    /// Collects the set of C# type keywords used as dependent type parameter annotations
    /// anywhere in the program. Scans all type declarations (classes, structs, abstract types,
    /// type aliases) and method symbols for <see cref="DependentTypeParameterType"/>
    /// instances in their type parameter lists.
    /// </summary>
    public static HashSet<string> CollectUsedDependentValueTypes(ISemanticModel model)
    {
        var usedKeywords = new HashSet<string>(StringComparer.Ordinal);

        foreach(var symbol in model.AllSymbols)
        {
            // Check type-level type parameters
            var typeParams = GetTypeParameters(symbol.DeclaredType);
            if(typeParams is not null)
            {
                CollectFromTypeParameters(typeParams, usedKeywords);
            }

            // Check method-level type parameters
            if(symbol is Symbol { TypeParameters: { Count: > 0 } methodTypeParams })
            {
                CollectFromTypeParameters(methodTypeParams, usedKeywords);
            }
        }

        return usedKeywords;
    }

    /// <summary>
    /// Returns the type parameters for a given type, or null if the type has none.
    /// </summary>
    private static IReadOnlyList<IType>? GetTypeParameters(IType? type) => type switch
    {
        ClassType ct => ct.TypeParameters,
        StructType st => st.TypeParameters,
        AbstractType it => it.TypeParameters,
        TypeAliasType at => at.TypeParameters,
        _ => null,
    };

    /// <summary>
    /// Scans a list of type parameters for <see cref="DependentTypeParameterType"/> instances
    /// and adds their value type keywords to the set.
    /// </summary>
    private static void CollectFromTypeParameters(IReadOnlyList<IType> typeParameters, HashSet<string> usedKeywords)
    {
        foreach(var typeParam in typeParameters)
        {
            if(typeParam is DependentTypeParameterType depParam)
            {
                var keyword = CSharpTypeMap.GetKeyword(depParam.ValueType);
                if(KeywordToCapitalized.ContainsKey(keyword))
                {
                    _ = usedKeywords.Add(keyword);
                }
            }
        }
    }

    /// <summary>
    /// Generates the <c>DependentTypeHelpers.g.cs</c> file content containing one
    /// const-carrier interface per used dependent value type, followed by carrier structs
    /// for each unique (type, value) pair.
    /// </summary>
    public static string GenerateHelpersFile(HashSet<string> usedKeywords, CarrierStructRegistry? carrierRegistry = null)
    {
        var sb = new StringBuilder();
        _ = sb.AppendLine("#nullable enable");
        _ = sb.AppendLine();

        // Emit interfaces in a stable order (alphabetical by keyword).
        foreach(var keyword in usedKeywords.OrderBy(k => k, StringComparer.Ordinal))
        {
            if(KeywordToCapitalized.TryGetValue(keyword, out var capitalized))
            {
                _ = sb.AppendLine("[System.Runtime.CompilerServices.CompilerGenerated]");
                _ = sb.AppendLine($"public interface IConst{capitalized}");
                _ = sb.AppendLine("{");
                _ = sb.AppendLine($"    static abstract {keyword} Value {{ get; }}");
                _ = sb.AppendLine("}");
                _ = sb.AppendLine();
            }
        }

        // Emit carrier structs for each unique (type, value) pair.
        if(carrierRegistry is not null)
        {
            _ = sb.Append(carrierRegistry.GenerateCarrierStructs());
        }

        // Emit DependentTypeAttribute and DependentTypeValueAttribute classes.
        _ = sb.AppendLine("[System.Runtime.CompilerServices.CompilerGenerated]");
        _ = sb.AppendLine("[System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct | System.AttributeTargets.Interface, AllowMultiple = true)]");
        _ = sb.AppendLine("public sealed class DependentTypeAttribute : System.Attribute");
        _ = sb.AppendLine("{");
        _ = sb.AppendLine("    public string ParameterName { get; }");
        _ = sb.AppendLine("    public System.Type ValueType { get; }");
        _ = sb.AppendLine("    public DependentTypeAttribute(string parameterName, System.Type valueType)");
        _ = sb.AppendLine("    {");
        _ = sb.AppendLine("        ParameterName = parameterName;");
        _ = sb.AppendLine("        ValueType = valueType;");
        _ = sb.AppendLine("    }");
        _ = sb.AppendLine("}");
        _ = sb.AppendLine();
        _ = sb.AppendLine("[System.Runtime.CompilerServices.CompilerGenerated]");
        _ = sb.AppendLine("[System.AttributeUsage(System.AttributeTargets.Property)]");
        _ = sb.AppendLine("public sealed class DependentTypeValueAttribute : System.Attribute");
        _ = sb.AppendLine("{");
        _ = sb.AppendLine("    public string ParameterName { get; }");
        _ = sb.AppendLine("    public object Value { get; }");
        _ = sb.AppendLine("    public DependentTypeValueAttribute(string parameterName, object value)");
        _ = sb.AppendLine("    {");
        _ = sb.AppendLine("        ParameterName = parameterName;");
        _ = sb.AppendLine("        Value = value;");
        _ = sb.AppendLine("    }");
        _ = sb.AppendLine("}");
        _ = sb.AppendLine();

        return sb.ToString();
    }
}