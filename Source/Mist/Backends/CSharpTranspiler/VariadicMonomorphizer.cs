using System.Text;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Backends.CSharpTranspiler;

/// <summary>
/// Scans the semantic model for constructed variadic types (types whose generic
/// definition has a pack type parameter and whose <see cref="ConstructedType.DependentArguments"/>
/// carry a <see cref="DependentValueArgument"/> with <see cref="DependentValueArgument.IsPack"/>
/// set) and emits per-arity monomorphized C# definitions.
/// <para>
/// For each observed arity of a variadic type/method, a distinct C# generic class/method
/// is emitted with that many type parameters. The pack's value tuple <c>(TArgs...)</c> maps
/// to <c>System.ValueTuple&lt;T1, ..., TN&gt;</c> (nested for N &gt; 7), and spread-generated
/// member projections map to the corresponding <c>ValueTuple</c> element accessors.
/// </para>
/// <para>
/// Requirements: 15.1, 15.2, 15.6, 15.7, 16.1, 16.2, 16.3.
/// </para>
/// </summary>
internal static class VariadicMonomorphizer
{
    /// <summary>
    /// Represents a single variadic construction observed in the semantic model:
    /// the generic definition (the variadic family) together with the pack's concrete
    /// element types at that usage site.
    /// </summary>
    private readonly record struct ObservedConstruction(
        IType GenericDefinition,
        IReadOnlyList<IType> PackElementTypes,
        int PackParameterOrdinal,
        IReadOnlyList<IType> AllTypeArguments);

    /// <summary>
    /// Collects all distinct variadic constructions from the semantic model and generates
    /// per-arity C# source files for each variadic family. Returns a dictionary mapping
    /// relative file paths to their emitted C# content (same shape as
    /// <see cref="CSharpEmitter.EmitToMemory"/>).
    /// </summary>
    public static Dictionary<string, string> EmitMonomorphizedDefinitions(ISemanticModel model)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);

        var constructions = CollectVariadicConstructions(model);
        if(constructions.Count == 0)
        {
            return files;
        }

        // Group by generic definition (the variadic family).
        var grouped = new Dictionary<IType, List<ObservedConstruction>>();
        foreach(var construction in constructions)
        {
            if(!grouped.TryGetValue(construction.GenericDefinition, out var list))
            {
                list = new List<ObservedConstruction>();
                grouped[construction.GenericDefinition] = list;
            }

            list.Add(construction);
        }

        // For each family, deduplicate by arity and emit one C# definition per arity.
        foreach(var (definition, usages) in grouped)
        {
            // Skip intrinsic backend-backed types (Func, Action) — they already map
            // to System.Func/System.Action via CSharpTypeMap and do not need
            // monomorphized class definitions emitted by the backend.
            if(IsBackendBacked(definition))
            {
                continue;
            }

            var familyName = definition.DisplayName;
            var sb = new StringBuilder();

            // Collect the containing namespace for the emitted definitions.
            var ns = GetContainingNamespace(definition);
            if(ns is not null)
            {
                _ = sb.AppendLine($"namespace {ns};");
                _ = sb.AppendLine();
            }

            // Deduplicate by arity (pack element count).
            var seenArities = new HashSet<int>();
            foreach(var usage in usages)
            {
                var arity = usage.PackElementTypes.Count;
                if(!seenArities.Add(arity))
                {
                    continue;
                }

                EmitMonomorphizedType(sb, definition, usage, arity);
                _ = sb.AppendLine();
            }

            if(seenArities.Count > 0)
            {
                files[$"Variadic_{familyName}.g.cs"] = sb.ToString();
            }
        }

        return files;
    }

    /// <summary>
    /// Scans the semantic model for all <see cref="ConstructedType"/> instances whose
    /// generic definition has a pack type parameter (identified by
    /// <see cref="TypeParameterType.IsPack"/>) and whose dependent arguments carry a
    /// pack element list.
    /// </summary>
    private static List<ObservedConstruction> CollectVariadicConstructions(ISemanticModel model)
    {
        var result = new List<ObservedConstruction>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach(var symbol in model.AllSymbols)
        {
            CollectFromType(symbol.DeclaredType, result, seen);

            // Also scan type arguments of methods and properties whose resolved type
            // may be a variadic construction.
            if(symbol is Symbol sym)
            {
                CollectFromType(sym.EffectiveReturnType, result, seen);
                foreach(var param in sym.Parameters)
                {
                    CollectFromType(param.DeclaredType, result, seen);
                }
            }
        }

        return result;
    }

    private static void CollectFromType(IType? type, List<ObservedConstruction> result, HashSet<string> seen)
    {
        if(type is null)
        {
            return;
        }

        if(type is ConstructedType ct)
        {
            if(ct.DependentArguments is not null)
            {
                for(var i = 0; i < ct.DependentArguments.Count; i++)
                {
                    var depArg = ct.DependentArguments[i];
                    if(depArg is not null && depArg.IsPack && depArg.PackElementTypes is not null)
                    {
                        // Build a dedup key: definition name + arity.
                        var key = $"{ct.GenericDefinition.DisplayName}:{depArg.PackElementTypes.Count}";
                        if(seen.Add(key))
                        {
                            result.Add(new ObservedConstruction(
                                ct.GenericDefinition,
                                depArg.PackElementTypes,
                                i,
                                ct.TypeArguments));
                        }
                    }
                }
            }

            // Recurse into type arguments to find nested variadic constructions.
            foreach(var arg in ct.TypeArguments)
            {
                CollectFromType(arg, result, seen);
            }
        }
        else if(type is ArrayType arrType)
        {
            CollectFromType(arrType.ElementType, result, seen);
        }
    }

    /// <summary>
    /// Emits a single monomorphized C# type definition for the given variadic family
    /// at the observed arity. The pack type parameter is expanded to N positional type
    /// parameters (T1, T2, ..., TN), and value pack members are mapped to ValueTuple.
    /// </summary>
    private static void EmitMonomorphizedType(
        StringBuilder sb,
        IType definition,
        ObservedConstruction usage,
        int arity)
    {
        var familyName = definition.DisplayName;
        var typeParams = GetTypeParametersFromDefinition(definition);

        // Build the C# type parameter list: replace the pack parameter with N positional params,
        // and keep the other type parameters as-is.
        var csharpTypeParams = BuildMonomorphizedTypeParamList(typeParams, usage.PackParameterOrdinal, arity);
        var typeParamStr = csharpTypeParams.Count > 0
            ? $"<{string.Join(", ", csharpTypeParams)}>"
            : "";

        // Determine type kind (class or struct).
        var typeKind = definition switch
        {
            ClassType => "class",
            StructType => "struct",
            AbstractType => "interface",
            _ => "class",
        };

        // Emit the accessibility modifier.
        var accessibility = definition switch
        {
            ClassType ct => ct.Accessibility,
            StructType st => st.Accessibility,
            AbstractType => Accessibility.Public,
            _ => Accessibility.Public,
        };
        var accessMod = accessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            Accessibility.Private => "private",
            _ => "public",
        };

        _ = sb.AppendLine($"{accessMod} {typeKind} {familyName}{typeParamStr}");
        _ = sb.AppendLine("{");

        // Emit the value pack tuple field/property mapped to ValueTuple.
        if(arity > 0)
        {
            var valueTupleType = BuildValueTupleTypeName(csharpTypeParams, usage.PackParameterOrdinal, arity);
            _ = sb.AppendLine($"    public {valueTupleType} PackValue;");
            _ = sb.AppendLine();

            // Emit spread-generated member projections (Item1..ItemN) as properties
            // that access the corresponding ValueTuple element.
            for(var i = 0; i < arity; i++)
            {
                var itemIndex = i + 1;
                var paramName = GetPackPositionalParamName(i);
                var accessor = GetValueTupleAccessor(i);
                _ = sb.AppendLine($"    public {paramName} Item{itemIndex} => PackValue.{accessor};");
            }
        }

        _ = sb.AppendLine("}");
    }

    /// <summary>
    /// Builds the expanded type parameter list for a monomorphized definition.
    /// The pack parameter at <paramref name="packOrdinal"/> is replaced with N positional
    /// type parameters (T1, T2, ..., TN); other parameters are preserved by name.
    /// </summary>
    private static List<string> BuildMonomorphizedTypeParamList(
        IReadOnlyList<IType> typeParams,
        int packOrdinal,
        int arity)
    {
        var result = new List<string>();
        for(var i = 0; i < typeParams.Count; i++)
        {
            if(i == packOrdinal)
            {
                // Replace the pack parameter with N positional parameters.
                for(var j = 0; j < arity; j++)
                {
                    result.Add(GetPackPositionalParamName(j));
                }
            }
            else
            {
                result.Add(typeParams[i].DisplayName);
            }
        }

        return result;
    }

    /// <summary>
    /// Returns the positional type parameter name for index <paramref name="index"/>
    /// in the expanded pack (T1, T2, ..., TN).
    /// </summary>
    private static string GetPackPositionalParamName(int index)
    {
        return $"T{index + 1}";
    }

    /// <summary>
    /// Builds the <c>System.ValueTuple&lt;T1, ..., TN&gt;</c> type name for a value pack
    /// of the given arity. For N &gt; 7, produces the nested form:
    /// <c>System.ValueTuple&lt;T1, ..., T7, System.ValueTuple&lt;T8, ...&gt;&gt;</c>.
    /// </summary>
    public static string BuildValueTupleTypeName(int arity)
    {
        if(arity == 0)
        {
            return "System.ValueTuple";
        }

        var names = new List<string>(arity);
        for(var i = 0; i < arity; i++)
        {
            names.Add(GetPackPositionalParamName(i));
        }

        return BuildNestedValueTuple(names, 0);
    }

    /// <summary>
    /// Builds the ValueTuple type name from the expanded type parameter list,
    /// extracting only the parameters that correspond to the pack's positional slots.
    /// </summary>
    private static string BuildValueTupleTypeName(
        List<string> allTypeParams,
        int packOrdinal,
        int arity)
    {
        if(arity == 0)
        {
            return "System.ValueTuple";
        }

        // Find the pack's positional parameters in the expanded list.
        // The pack parameters start at the position where the original pack was,
        // but in the expanded list they occupy `arity` consecutive slots.
        var packStartIndex = 0;
        for(var i = 0; i < packOrdinal; i++)
        {
            packStartIndex++;
        }

        var packParams = allTypeParams.GetRange(packStartIndex, arity);
        return BuildNestedValueTuple(packParams, 0);
    }

    /// <summary>
    /// Recursively builds a (possibly nested) <c>System.ValueTuple</c> type string.
    /// For up to 7 elements, produces <c>System.ValueTuple&lt;T1, ..., TN&gt;</c>.
    /// For more than 7, produces <c>System.ValueTuple&lt;T1, ..., T7, ValueTuple&lt;T8, ...&gt;&gt;</c>.
    /// </summary>
    private static string BuildNestedValueTuple(List<string> elements, int startIndex)
    {
        var remaining = elements.Count - startIndex;

        if(remaining <= 7)
        {
            var slice = elements.GetRange(startIndex, remaining);
            return $"System.ValueTuple<{string.Join(", ", slice)}>";
        }

        // Take the first 7 and nest the rest.
        var first7 = elements.GetRange(startIndex, 7);
        var nested = BuildNestedValueTuple(elements, startIndex + 7);
        return $"System.ValueTuple<{string.Join(", ", first7)}, {nested}>";
    }

    /// <summary>
    /// Returns the ValueTuple accessor path for the element at the given zero-based index.
    /// For indices 0-6, returns <c>Item1</c> through <c>Item7</c>.
    /// For index 7+, returns a nested path like <c>Rest.Item1</c>, <c>Rest.Rest.Item1</c>, etc.
    /// </summary>
    public static string GetValueTupleAccessor(int index)
    {
        if(index < 7)
        {
            return $"Item{index + 1}";
        }

        // Nested: calculate how deep we go and which item within that level.
        var sb = new StringBuilder();
        var remaining = index;
        while(remaining >= 7)
        {
            _ = sb.Append("Rest.");
            remaining -= 7;
        }

        _ = sb.Append($"Item{remaining + 1}");
        return sb.ToString();
    }

    /// <summary>
    /// Maps a pack element type list to its C# <c>ValueTuple</c> type name using
    /// concrete type names from <see cref="CSharpTypeMap"/>. Used by the emitter
    /// when a value pack parameter <c>(TArgs...)</c> appears in a concrete construction.
    /// </summary>
    public static string MapPackToValueTuple(IReadOnlyList<IType> elementTypes)
    {
        if(elementTypes.Count == 0)
        {
            return "System.ValueTuple";
        }

        var names = elementTypes.Select(t => CSharpTypeMap.GetTypeName(t, isValueContext: true)).ToList();
        return BuildNestedValueTuple(names, 0);
    }

    private static bool IsBackendBacked(IType definition)
    {
        return definition switch
        {
            ClassType ct => ct.IsBackendBacked,
            StructType st => st.IsBackendBacked,
            _ => false,
        };
    }

    private static string? GetContainingNamespace(IType definition)
    {
        return definition switch
        {
            ClassType ct => ct.ContainingNamespace,
            StructType st => st.ContainingNamespace,
            AbstractType at => at.ContainingNamespace,
            _ => null,
        };
    }

    private static IReadOnlyList<IType> GetTypeParametersFromDefinition(IType definition)
    {
        return definition switch
        {
            ClassType ct => ct.TypeParameters,
            StructType st => st.TypeParameters,
            AbstractType at => at.TypeParameters,
            _ => Array.Empty<IType>(),
        };
    }
}