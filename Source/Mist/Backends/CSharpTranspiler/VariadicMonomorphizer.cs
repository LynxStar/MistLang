using System.Collections.Immutable;
using System.Text;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Generated;
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
    public static Dictionary<string, string> EmitMonomorphizedDefinitions(ISemanticModel model, DiagnosticBag diagnostics)
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

            // Resolve the family's declaration AST and its source once per family. These
            // drive per-arity method-member emission (task 27.4): the body walker needs the
            // declaring TypeBodyDeclarationNode and the exact SourceText it came from so that
            // span-based emission fallbacks resolve against the correct file.
            var declaration = FindTypeDeclaration(model, definition);

            // Deduplicate by arity (pack element count).
            var seenArities = new HashSet<int>();
            foreach(var usage in usages)
            {
                var arity = usage.PackElementTypes.Count;
                if(!seenArities.Add(arity))
                {
                    continue;
                }

                EmitMonomorphizedType(sb, definition, usage, arity, model, diagnostics, declaration);
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

        // Scan object-creation expressions for variadic constructions reached only through a
        // `new` at a use site. A variadic type such as `Tup<params TArgs>` whose only reference
        // is `new Tup<int, string, bool>()` bound to an abstract-typed local
        // (`ICountable<int, string, bool> counter = new Tup<...>()`) is otherwise never observed:
        // the local's declared type is the abstract, not the concrete class. Resolving the
        // creation expression's type (Requirement 22.2 -- a variadic class implementing a
        // variadic abstract type) surfaces the concrete `Tup` construction so its monomorphized
        // definition and base list are emitted.
        foreach(var ast in model.Asts)
        {
            CollectFromObjectCreations(model, ast, result, seen);
        }

        return result;
    }

    /// <summary>
    /// Walks <paramref name="node"/> and its descendants, resolving the type of every
    /// object-creation expression (<c>new T&lt;...&gt;(...)</c>) through the semantic model and
    /// collecting any variadic construction found. This reaches variadic types that appear only
    /// in a <c>new</c> expression (never as a declared member, parameter, or return type).
    /// </summary>
    private static void CollectFromObjectCreations(
        ISemanticModel model,
        Storm.Runtime.SyntaxNode node,
        List<ObservedConstruction> result,
        HashSet<string> seen)
    {
        if(node is ObjectCreationExpressionPrimaryNode or ObjectCreationExpressionNoArgsPrimaryNode)
        {
            CollectFromType(model.GetTypeOf(node), result, seen);
        }

        foreach(var child in node.Children())
        {
            // Children() yields null entries for absent optional slots; skip them.
            if(child is null)
            {
                continue;
            }

            CollectFromObjectCreations(model, child, result, seen);
        }
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
        int arity,
        ISemanticModel model,
        DiagnosticBag diagnostics,
        (TypeBodyDeclarationNode Node, SourceText Source)? declaration)
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

        // The pack type parameter's declared name (e.g. "TArgs"). It seeds the pack-arity
        // substitution context shared by base-list spread expansion (task 27.9) and the
        // body-level pack operations of the class/struct method members (tasks 27.4/27.5).
        var packName = usage.PackParameterOrdinal >= 0 && usage.PackParameterOrdinal < typeParams.Count
            ? typeParams[usage.PackParameterOrdinal].DisplayName
            : null;
        var packArities = packName is not null
            ? new Dictionary<string, int>(StringComparer.Ordinal) { [packName] = arity }
            : null;

        // Emit the base list monomorphized to the concrete arity (task 27.9, Requirements 22.2,
        // 9.3). A variadic implementer that spreads its own pack into the base-type list
        // (`Tup<params TArgs> : ICountable<TArgs...>`) must expand the spread into the concrete
        // positional type arguments (`ICountable<T1, T2, T3>`), NOT collapse the pack to a single
        // `object` argument. The base list is read from the declaration AST because the semantic
        // model leaves the base-list spread unexpanded (a single error-typed argument that would
        // otherwise emit as `ICountable<object>`).
        var baseClause = "";
        if(declaration is { } baseDecl && packArities is not null)
        {
            var baseWriter = new CSharpSourceWriter(
                model,
                ImmutableArray<string>.Empty,
                baseDecl.Source,
                diagnostics);

            var baseList = baseWriter.EmitMonomorphizedBaseList(baseDecl.Node, packArities);
            if(baseList.Length > 0)
            {
                baseClause = $" : {baseList}";
            }
        }

        // Propagate a whole-pack constraint (for example `where TShapes : IShape`) onto every
        // expanded positional type parameter (T1..TN), so a monomorphized member can call the
        // constrained interface's methods on each element (task 27.11, Requirement 15.7). A
        // non-pack type parameter keeps its own constraint under its own name.
        var whereClause = BuildMonomorphizedWhereClauses(typeParams, usage.PackParameterOrdinal, arity);

        _ = sb.AppendLine($"{accessMod} {typeKind} {familyName}{typeParamStr}{baseClause}{whereClause}");
        _ = sb.AppendLine("{");

        // A variadic abstract type emits as a C# interface at the concrete arity. It carries its
        // declared members (abstract methods, default methods, and properties) but NO instance
        // fields: interfaces cannot declare fields, and the spread-generated aggregate/projection
        // members belong only to the concrete class/struct implementers (task 27.9,
        // Requirement 22.2). Emit its members and return before the field/zip/method paths below,
        // which are exclusive to the class/struct kinds.
        if(typeKind is "interface")
        {
            if(declaration is { } ifaceDecl && packArities is not null)
            {
                var ifaceWriter = new CSharpSourceWriter(
                    model,
                    ImmutableArray<string>.Empty,
                    ifaceDecl.Source,
                    diagnostics);

                var members = ifaceWriter.EmitMonomorphizedInterfaceMembers(ifaceDecl.Node, packArities);
                if(members.Length > 0)
                {
                    _ = sb.Append(members);
                }
            }

            _ = sb.AppendLine("}");
            return;
        }

        // Emit the spread-generated members and the zip constructor (Requirement 10,
        // task 27.2). Each `public TArgs... Stem;` declaration surfaces the aggregate
        // member `Stem` of the value-pack tuple type System.ValueTuple<T1..TN> plus the
        // 1-based element projections Stem1..StemN that read the corresponding ValueTuple
        // element.
        var spreadMembers = GetSpreadMembers(definition);
        if(spreadMembers.Count > 0)
        {
            // The value-pack tuple type shared by every family's aggregate. All spread
            // families on a type reference the type's single pack, so the tuple type is the
            // same for each (Requirement 10.8: correlated spreads align at the same arity).
            var valueTupleType = BuildValueTupleTypeName(csharpTypeParams, usage.PackParameterOrdinal, arity);

            for(var f = 0; f < spreadMembers.Count; f++)
            {
                if(f > 0)
                {
                    _ = sb.AppendLine();
                }

                EmitSpreadMemberFamily(sb, spreadMembers[f], valueTupleType, arity);
            }

            // Zip constructor body: `Stem... = values...` (Requirements 10.9, 8.5). The
            // value-pack parameter `params TArgs values` is emitted as a
            // System.ValueTuple<...> parameter (its parameter emission is task 27.8), so the
            // per-element zip reduces to assigning that tuple into each aggregate member.
            _ = sb.AppendLine();
            EmitZipConstructor(sb, familyName, spreadMembers, valueTupleType);
        }

        // Emit the type's user-declared method members at this concrete arity (task 27.4).
        // Method bodies are produced by the shared C# emitter with a pack-arity substitution
        // context, so a size-agnostic pack operation such as `TArgs.Length` emits as the
        // compile-time integer literal N (Requirements 6.1, 6.2).
        if(declaration is { } decl && packArities is not null)
        {
            // A dedicated writer per emission: its output buffer is read verbatim.
            var writer = new CSharpSourceWriter(
                model,
                ImmutableArray<string>.Empty,
                decl.Source,
                diagnostics);

            var methods = writer.EmitMonomorphizedMethodMembers(
                decl.Node,
                insideStructType: definition is StructType,
                enclosingClassType: definition as ClassType,
                packArities);

            if(methods.Length > 0)
            {
                _ = sb.AppendLine();
                _ = sb.Append(methods);
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
    /// Builds the C# <c>where</c> constraint clauses for a monomorphized variadic type at a
    /// concrete arity (task 27.11), returned as a single string prefixed with a leading space
    /// (for example <c>" where T1 : IShape where T2 : IShape"</c>) or an empty string when no
    /// type parameter carries a constraint. A whole-pack constraint declared on the pack type
    /// parameter (for example <c>where TShapes : IShape</c>) is applied to each expanded
    /// positional parameter <c>T1..TN</c>, which is what makes the constrained interface's
    /// methods callable on every spread-generated element member. A non-pack type parameter's
    /// constraint is emitted once under its own declared name.
    /// </summary>
    private static string BuildMonomorphizedWhereClauses(
        IReadOnlyList<IType> typeParams,
        int packOrdinal,
        int arity)
    {
        var clauses = new List<string>();
        for(var i = 0; i < typeParams.Count; i++)
        {
            if(typeParams[i] is not TypeParameterType { Constraints: { } constraints } tpt)
            {
                continue;
            }

            var parts = BuildConstraintParts(constraints);
            if(parts.Count == 0)
            {
                continue;
            }

            var joined = string.Join(", ", parts);
            if(i == packOrdinal)
            {
                for(var k = 0; k < arity; k++)
                {
                    clauses.Add($"where {GetPackPositionalParamName(k)} : {joined}");
                }
            }
            else
            {
                clauses.Add($"where {tpt.DisplayName} : {joined}");
            }
        }

        return clauses.Count > 0 ? " " + string.Join(" ", clauses) : "";
    }

    /// <summary>
    /// Renders the C# constraint parts (class/struct, base class, abstract-type/interface
    /// constraints, and a parameterless <c>new()</c>) for a single type parameter's constraint
    /// set, in the order C# requires. Mirrors the scalar constraint emission in the main C#
    /// emitter so a monomorphized pack element carries the same constraint its declaring pack
    /// did. Substructural and default constraints have no direct C# clause and are omitted here.
    /// </summary>
    private static List<string> BuildConstraintParts(TypeParameterConstraints constraints)
    {
        var parts = new List<string>();

        if(constraints.HasClassConstraint)
        {
            parts.Add("class");
        }
        else if(constraints.HasStructConstraint)
        {
            parts.Add("struct");
        }

        if(constraints.BaseClassConstraint is not null)
        {
            parts.Add(CSharpTypeMap.GetTypeName(constraints.BaseClassConstraint));
        }

        foreach(var abs in constraints.AbstractConstraints)
        {
            parts.Add(CSharpTypeMap.GetTypeName(abs));
        }

        if(constraints.ConstructorConstraints.Any(c => c.Count == 0))
        {
            parts.Add("new()");
        }

        return parts;
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

    /// <summary>
    /// Locates the declaration AST node and originating <see cref="SourceText"/> for a
    /// variadic family's generic definition. Used by per-arity method-member emission
    /// (task 27.4): the shared emitter needs the declaring
    /// <see cref="TypeBodyDeclarationNode"/> to walk method bodies and the exact source it
    /// came from so span-based emission fallbacks resolve against the correct file. Returns
    /// <c>null</c> when no matching declaration is found (for example a synthesized type).
    /// </summary>
    private static (TypeBodyDeclarationNode Node, SourceText Source)? FindTypeDeclaration(
        ISemanticModel model,
        IType definition)
    {
        for(var i = 0; i < model.Units.Count && i < model.Asts.Length; i++)
        {
            var node = FindTypeBodyForDefinition(model, model.Asts[i], definition);
            if(node is not null)
            {
                return (node, model.Units[i].Source);
            }
        }

        return null;
    }

    /// <summary>
    /// Recursively searches <paramref name="node"/> for a
    /// <see cref="TypeBodyDeclarationNode"/> whose declared symbol resolves to the same type
    /// as <paramref name="definition"/> (the variadic family's generic definition).
    /// </summary>
    private static TypeBodyDeclarationNode? FindTypeBodyForDefinition(
        ISemanticModel model,
        Storm.Runtime.SyntaxNode node,
        IType definition)
    {
        if(node is TypeBodyDeclarationNode typeBody)
        {
            var declaredType = model.GetDeclaredSymbol(typeBody)?.DeclaredType;
            if(declaredType is not null && TypeMatches(declaredType, definition))
            {
                return typeBody;
            }
        }

        foreach(var child in node.Children())
        {
            // Children() yields null entries for absent optional slots; skip them.
            if(child is null)
            {
                continue;
            }

            var found = FindTypeBodyForDefinition(model, child, definition);
            if(found is not null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns <c>true</c> when two types denote the same declared type. Prefers reference
    /// equality (the constructed type's generic definition is the same instance as the
    /// declaring symbol's declared type) and falls back to matching the display name and
    /// containing namespace.
    /// </summary>
    private static bool TypeMatches(IType a, IType b)
    {
        if(ReferenceEquals(a, b))
        {
            return true;
        }

        return a.DisplayName == b.DisplayName
            && GetContainingNamespace(a) == GetContainingNamespace(b);
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

    /// <summary>
    /// Returns the spread-generated member families (Requirement 10) recorded on the
    /// given variadic type definition -- one per <c>public TArgs... Stem;</c> or
    /// <c>public TArgs... Stem { get; }</c> declaration. Empty when the type declares no
    /// member spreads.
    /// </summary>
    private static IReadOnlyList<SemanticSpreadMember> GetSpreadMembers(IType definition)
    {
        return definition switch
        {
            ClassType ct => (IReadOnlyList<SemanticSpreadMember>)ct.SpreadMembers,
            StructType st => (IReadOnlyList<SemanticSpreadMember>)st.SpreadMembers,
            AbstractType at => (IReadOnlyList<SemanticSpreadMember>)at.SpreadMembers,
            _ => Array.Empty<SemanticSpreadMember>(),
        };
    }

    /// <summary>
    /// Emits a single spread-generated member family for a monomorphized type at a concrete
    /// arity: the aggregate member named by the <see cref="SemanticSpreadMember.Stem"/> typed
    /// as <paramref name="valueTupleType"/> (the whole <c>System.ValueTuple&lt;T1..TN&gt;</c>),
    /// followed by the 1-based element projections <c>Stem1 .. StemN</c>. Each projection reads
    /// the corresponding <c>ValueTuple</c> element (<c>Stem.Item{k}</c>) and is typed with the
    /// pack's positional type parameter (<c>T{k}</c>). The member kind (field vs property) is
    /// honored: a field spread emits the aggregate as a field and get/set projections; a property
    /// spread emits the aggregate as a get-only property and get-only projections (Requirement 10.1).
    /// </summary>
    private static void EmitSpreadMemberFamily(
        StringBuilder sb,
        SemanticSpreadMember family,
        string valueTupleType,
        int arity)
    {
        var stem = family.Stem;
        var isProperty = family.Kind == SpreadMemberKind.Property;

        // Aggregate member `Stem : (TArgs...)` -- the whole ValueTuple.
        _ = sb.AppendLine(isProperty
            ? $"    public {valueTupleType} {stem} {{ get; }}"
            : $"    public {valueTupleType} {stem};");

        // Element projections Stem1..StemN. Each projects the k-th ValueTuple element of the
        // aggregate (Requirements 10.1, 10.7). A property-kind family surfaces get-only
        // projections; a field-kind family surfaces get/set projections (both read/write the
        // shared aggregate, so no duplicate storage is introduced).
        for(var i = 0; i < arity; i++)
        {
            var projectionName = family.ProjectionName(i + 1);
            var elementTypeName = GetPackPositionalParamName(i);
            var accessor = GetValueTupleAccessor(i);
            _ = sb.AppendLine(isProperty
                ? $"    public {elementTypeName} {projectionName} => {stem}.{accessor};"
                : $"    public {elementTypeName} {projectionName} {{ get => {stem}.{accessor}; set => {stem}.{accessor} = value; }}");
        }
    }

    /// <summary>
    /// Emits the zip constructor for a monomorphized variadic type
    /// (Requirements 10.9, 8.5). The source constructor body <c>Stem... = values...</c> zips the
    /// value pack into the spread-generated aggregate(s); because the value pack
    /// <c>params TArgs values</c> is emitted as a <c>System.ValueTuple&lt;T1..TN&gt;</c> parameter
    /// (parameter emission is task 27.8) and each aggregate is that same value-pack tuple, the
    /// per-element zip reduces to assigning the parameter tuple into every aggregate member.
    /// </summary>
    private static void EmitZipConstructor(
        StringBuilder sb,
        string familyName,
        IReadOnlyList<SemanticSpreadMember> spreadMembers,
        string valueTupleType)
    {
        _ = sb.AppendLine($"    public {familyName}({valueTupleType} values)");
        _ = sb.AppendLine("    {");
        foreach(var family in spreadMembers)
        {
            _ = sb.AppendLine($"        {family.Stem} = values;");
        }

        _ = sb.AppendLine("    }");
    }

    /// <summary>
    /// Maximum number of input parameter types supported by <c>System.Func</c> and
    /// <c>System.Action</c> in the CLR. <c>System.Func</c> supports up to 16 inputs
    /// plus 1 return type (17 type parameters total); <c>System.Action</c> supports
    /// up to 16 inputs.
    /// </summary>
    private const int FuncActionInputCeiling = 16;

    /// <summary>
    /// Validates all variadic constructions against the target CLR type capacity and
    /// emits <see cref="VariadicsDiagnostics.ArityCeilingExceeded"/> (MIST_VAR_0010)
    /// for any construction that exceeds:
    /// <list type="bullet">
    ///   <item><c>System.Func</c> / <c>System.Action</c>: over 16 input parameter types.</item>
    /// </list>
    /// <c>System.ValueTuple</c> over 7 elements is represented via nesting and does NOT
    /// trigger the diagnostic (Requirement 16.3).
    /// <para>
    /// Requirements: 16.1, 16.2, 16.4, 16.5.
    /// </para>
    /// </summary>
    public static void ValidateArityCeiling(ISemanticModel model, DiagnosticBag diagnostics)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach(var symbol in model.AllSymbols)
        {
            CheckTypeForArityCeiling(symbol.DeclaredType, diagnostics, seen);

            if(symbol is Symbol sym)
            {
                CheckTypeForArityCeiling(sym.EffectiveReturnType, diagnostics, seen);
                foreach(var param in sym.Parameters)
                {
                    CheckTypeForArityCeiling(param.DeclaredType, diagnostics, seen);
                }
            }
        }
    }

    private static void CheckTypeForArityCeiling(IType? type, DiagnosticBag diagnostics, HashSet<string> seen)
    {
        if(type is null)
        {
            return;
        }

        if(type is ConstructedType ct)
        {
            // Check if this is a Standard.Func construction that exceeds the ceiling.
            if(ct.GenericDefinition is ClassType { DisplayName: "Func", ContainingNamespace: "Standard", IsBackendBacked: true })
            {
                // Func<TArgs..., TResult>: the last type argument is the return type.
                // The input parameter count is typeArgs.Count - 1.
                var inputCount = ct.TypeArguments.Count - 1;

                // For Action (Func with void return), the input count is the same calculation:
                // Action<T1, ..., TN> resolves to Func<T1, ..., TN, void> which has N inputs.
                if(inputCount > FuncActionInputCeiling)
                {
                    var key = $"FuncCeiling:{inputCount}";
                    if(seen.Add(key))
                    {
                        var isAction = ct.TypeArguments[ct.TypeArguments.Count - 1].IsVoid();
                        var familyName = isAction ? "System.Action" : "System.Func";
                        diagnostics.Report(
                            VariadicsDiagnostics.ArityCeilingExceeded,
                            default,
                            string.Empty,
                            $"{familyName} with {inputCount} input parameter types exceeds the CLR limit of {FuncActionInputCeiling}");
                    }
                }
            }

            // Recurse into type arguments.
            foreach(var arg in ct.TypeArguments)
            {
                CheckTypeForArityCeiling(arg, diagnostics, seen);
            }
        }
        else if(type is ArrayType arrType)
        {
            CheckTypeForArityCeiling(arrType.ElementType, diagnostics, seen);
        }
    }
}