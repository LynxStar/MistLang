// Generator for nested scoping property tests.
// Produces type kind combinations and nested type source strings for
// verifying parser nesting universality across all 25 container x nested kind pairs.

using FsCheck;
using FsCheck.Fluent;

namespace Mist.Tests.Generators;

/// <summary>
/// Represents a type kind that supports MemberDeclaration (class, struct).
/// These are the container types that can hold nested types via the NestedTypeMember rule.
/// </summary>
public enum NestableTypeKind
{
    Class,
    Struct,
}

/// <summary>
/// Represents any of the four type kinds in the Mist language.
/// </summary>
public enum AllTypeKind
{
    Class,
    Struct,
    Abstract,
    Enum,
}

/// <summary>
/// Represents a subset of modifiers that can be applied to a nested type.
/// Each flag indicates whether the modifier is present.
/// </summary>
public record ModifierSubset(bool IsPublic, bool IsPrivate, bool IsStatic)
{
    public override string ToString()
    {
        var parts = new List<string>();
        if(IsPublic) parts.Add("public");
        if(IsPrivate) parts.Add("private");
        if(IsStatic) parts.Add("static");
        return parts.Count == 0 ? "(none)" : string.Join(" ", parts);
    }
}

/// <summary>
/// Wrapper type for FsCheck Arbitrary registration - a complete nested type source string.
/// </summary>
public record NestedTypeSource(string Source, AllTypeKind ContainerKind, AllTypeKind NestedKind)
{
    public override string ToString() => $"{ContainerKind} containing {NestedKind}";
}

/// <summary>
/// Wrapper type for FsCheck Arbitrary registration - a nested type source with a specific depth.
/// Used by Property 2: Nesting depth acceptance.
/// </summary>
public record NestedTypeDepthSource(string Source, int Depth)
{
    public override string ToString() => $"Depth={Depth}";
}

/// <summary>
/// Wrapper type for FsCheck Arbitrary registration - a source with multiple same-name block
/// namespaces where types should be cross-resolvable.
/// Used by Property 10: Same-name namespace scope sharing.
/// </summary>
public record SameNameNamespaceSource(string Source, string NamespaceName, int BlockCount)
{
    public override string ToString() => $"namespace {NamespaceName} x{BlockCount}";
}

/// <summary>
/// Wrapper type for FsCheck Arbitrary registration - a nested type inside generic ancestors.
/// Used by Property 8: Ancestor type parameter visibility.
/// The source contains generic parent types with a nested type that uses ancestor type parameters.
/// </summary>
public record AncestorTypeParamSource(string Source, int Depth)
{
    public override string ToString() => $"GenericDepth={Depth}";
}

/// <summary>
/// FsCheck generators for nested scoping property tests.
/// </summary>
public static class MistNestedScopingGenerator
{
    private static readonly AllTypeKind[] AllKinds =
    [
        AllTypeKind.Class,
        AllTypeKind.Struct,
        AllTypeKind.Abstract,
        AllTypeKind.Enum,
    ];

    /// <summary>
    /// Generates a random type kind from all five kinds.
    /// </summary>
    public static Gen<AllTypeKind> GenAllTypeKind()
    {
        return Gen.Elements(AllKinds);
    }

    /// <summary>
    /// Generates a complete Mist source string with a container type containing a nested type.
    /// Container kinds are limited to class and struct (which use MemberDeclaration and
    /// therefore support the NestedTypeMember alternative). Nested kinds can be any of the five.
    /// </summary>
    public static Gen<NestedTypeSource> GenNestedTypeSource()
    {
        // Only class and struct use MemberDeclaration which has the NestedTypeMember alternative
        var containerKinds = new[] { AllTypeKind.Class, AllTypeKind.Struct };

        return from containerKind in Gen.Elements(containerKinds)
               from nestedKind in GenAllTypeKind()
               let containerKeyword = AllTypeKindKeyword(containerKind)
               let nestedKeyword = AllTypeKindKeyword(nestedKind)
               let nestedBody = nestedKind == AllTypeKind.Enum ? "{ A, B }" : "{ }"
               let source = $"{containerKeyword} Container {{\n    {nestedKeyword} Nested {nestedBody}\n}}"
               select new NestedTypeSource(source, containerKind, nestedKind);
    }

    /// <summary>
    /// Generates a random modifier subset (any combination of public, private, static).
    /// Note: public and private are mutually exclusive in practice, but we generate
    /// all combinations to test parser behavior.
    /// </summary>
    public static Gen<ModifierSubset> GenModifierSubset()
    {
        return from isPublic in Gen.Elements(true, false)
               from isPrivate in Gen.Elements(true, false)
               from isStatic in Gen.Elements(true, false)
                   // Exclude public+private (mutually exclusive)
               where !(isPublic && isPrivate)
               select new ModifierSubset(isPublic, isPrivate, isStatic);
    }

    /// <summary>
    /// Returns the Mist keyword for a given type kind.
    /// </summary>
    public static string AllTypeKindKeyword(AllTypeKind kind) => kind switch
    {
        AllTypeKind.Class => "class",
        AllTypeKind.Struct => "struct",
        AllTypeKind.Abstract => "abstract",
        AllTypeKind.Enum => "enum",
        _ => "class"
    };

    /// <summary>
    /// Converts a ModifierSubset to its source code representation (e.g., "public static ").
    /// Returns empty string if no modifiers are set.
    /// </summary>
    public static string ModifierSetToSource(ModifierSubset mods)
    {
        var parts = new List<string>();
        if(mods.IsPublic) parts.Add("public");
        if(mods.IsPrivate) parts.Add("private");
        if(mods.IsStatic) parts.Add("static");
        return parts.Count == 0 ? "" : string.Join(" ", parts) + " ";
    }

    /// <summary>
    /// Returns the expected number of modifier nodes for a given ModifierSubset.
    /// </summary>
    public static int ModifierCount(ModifierSubset mods)
    {
        var count = 0;
        if(mods.IsPublic) count++;
        if(mods.IsPrivate) count++;
        if(mods.IsStatic) count++;
        return count;
    }

    /// <summary>
    /// Returns the expected set of modifier node types for a given ModifierSubset.
    /// </summary>
    public static HashSet<Type> ExpectedModifierNodeTypes(ModifierSubset mods)
    {
        var types = new HashSet<Type>();
        if(mods.IsPublic) types.Add(typeof(Mist.Generated.PublicModifierModifierNode));
        if(mods.IsPrivate) types.Add(typeof(Mist.Generated.PrivateModifierModifierNode));
        if(mods.IsStatic) types.Add(typeof(Mist.Generated.StaticModifierModifierNode));
        return types;
    }

    /// <summary>
    /// FsCheck Arbitrary for nested type source strings.
    /// </summary>
    public static Arbitrary<NestedTypeSource> ArbNestedTypeSource()
    {
        return GenNestedTypeSource().ToArbitrary();
    }

    /// <summary>
    /// FsCheck Arbitrary for type kinds.
    /// </summary>
    public static Arbitrary<AllTypeKind> ArbAllTypeKind()
    {
        return GenAllTypeKind().ToArbitrary();
    }

    /// <summary>
    /// FsCheck Arbitrary for modifier subsets.
    /// </summary>
    public static Arbitrary<ModifierSubset> ArbModifierSubset()
    {
        return GenModifierSubset().ToArbitrary();
    }

    /// <summary>
    /// Generates a nested type source with a specific nesting depth (1-8).
    /// Each level uses a class container with a nested class inside.
    /// </summary>
    public static Gen<NestedTypeDepthSource> GenNestedTypeDepthSource()
    {
        return from depth in Gen.Choose(1, 8)
               from kinds in Gen.ArrayOf(Gen.Elements(AllTypeKind.Class, AllTypeKind.Struct), depth)
               let source = BuildNestedSourceAtDepth(kinds)
               select new NestedTypeDepthSource(source, depth);
    }

    /// <summary>
    /// Builds a Mist source string with nested types at the specified depth.
    /// </summary>
    private static string BuildNestedSourceAtDepth(AllTypeKind[] kinds)
    {
        var sb = new System.Text.StringBuilder();
        for(var i = 0; i < kinds.Length; i++)
        {
            var indent = new string(' ', i * 4);
            var keyword = AllTypeKindKeyword(kinds[i]);
            var name = $"Level{i}";
            sb.AppendLine($"{indent}{keyword} {name} {{");
        }

        // Close all braces in reverse order
        for(var i = kinds.Length - 1; i >= 0; i--)
        {
            var indent = new string(' ', i * 4);
            sb.AppendLine($"{indent}}}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// FsCheck Arbitrary for nested type depth source strings.
    /// </summary>
    public static Arbitrary<NestedTypeDepthSource> ArbNestedTypeDepthSource()
    {
        return GenNestedTypeDepthSource().ToArbitrary();
    }

    // ─── Namespace Chain (Property 5) ──────────────────────────────────────

    /// <summary>
    /// Generates a valid Mist identifier (starts with uppercase letter, followed by lowercase letters).
    /// Avoids Mist keywords.
    /// </summary>
    private static Gen<string> GenIdentifier()
    {
        return from upper in Gen.Elements('A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J', 'K', 'L', 'M', 'N', 'O', 'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z')
               from len in Gen.Choose(2, 5)
               from rest in Gen.ArrayOf(Gen.Elements('a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j', 'k', 'l', 'm', 'n', 'o', 'p', 'q', 'r', 's', 't', 'u', 'v', 'w', 'x', 'y', 'z'), len)
               let name = upper + new string(rest)
               where name != "Class" && name != "Struct" && name != "Abstract"
                  && name != "Enum" && name != "Namespace" && name != "Public" && name != "Private"
                  && name != "Static" && name != "True" && name != "False" && name != "Null"
               select name;
    }

    /// <summary>
    /// Generates a chain of namespace names (2–5 segments) for nested block namespaces (no file-scoped prefix).
    /// </summary>
    public static Gen<NamespaceChainSource> GenNamespaceChainBlockOnly()
    {
        return from segmentCount in Gen.Choose(2, 5)
               from names in Gen.ArrayOf<string>(GenIdentifier(), segmentCount)
               let distinctNames = names.Select((n, i) => n + i).ToArray()
               let source = BuildNestedBlockNamespaceSource(distinctNames, fileScopedPrefix: null)
               let expectedFqns = ComputeExpectedFqns(distinctNames, fileScopedPrefix: null)
               select new NamespaceChainSource(source, distinctNames, expectedFqns, FileScopedPrefix: null);
    }

    /// <summary>
    /// Generates a chain of namespace names with a file-scoped namespace prefix.
    /// </summary>
    public static Gen<NamespaceChainSource> GenNamespaceChainWithFileScoped()
    {
        return from fileScopedName in GenIdentifier()
               from segmentCount in Gen.Choose(1, 4)
               from names in Gen.ArrayOf<string>(GenIdentifier(), segmentCount)
               let distinctNames = names.Select((n, i) => n + i).ToArray()
               let source = BuildNestedBlockNamespaceSource(distinctNames, fileScopedPrefix: fileScopedName)
               let expectedFqns = ComputeExpectedFqns(distinctNames, fileScopedPrefix: fileScopedName)
               select new NamespaceChainSource(source, distinctNames, expectedFqns, FileScopedPrefix: fileScopedName);
    }

    /// <summary>
    /// Builds Mist source with nested block namespaces and a marker class in the innermost.
    /// E.g., for names ["A0", "B1", "C2"]:
    ///   namespace A0 { namespace B1 { namespace C2 { class Marker { } } } }
    /// </summary>
    internal static string BuildNestedBlockNamespaceSource(string[] names, string? fileScopedPrefix)
    {
        var sb = new System.Text.StringBuilder();

        if(fileScopedPrefix is not null)
        {
            sb.AppendLine($"namespace {fileScopedPrefix};");
            sb.AppendLine();
        }

        for(var i = 0; i < names.Length; i++)
        {
            var indent = new string(' ', i * 4);
            sb.AppendLine($"{indent}namespace {names[i]} {{");
        }

        // Place a marker class in the innermost namespace
        var innerIndent = new string(' ', names.Length * 4);
        sb.AppendLine($"{innerIndent}class Marker {{ }}");

        // Close all braces in reverse order
        for(var i = names.Length - 1; i >= 0; i--)
        {
            var indent = new string(' ', i * 4);
            sb.AppendLine($"{indent}}}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Computes the expected FQNs for each nesting level.
    /// E.g., for names ["A0", "B1", "C2"] with no prefix: ["A0", "A0.B1", "A0.B1.C2"]
    /// For names ["A0", "B1"] with prefix "Root": ["Root.A0", "Root.A0.B1"]
    /// </summary>
    internal static string[] ComputeExpectedFqns(string[] names, string? fileScopedPrefix)
    {
        var fqns = new string[names.Length];
        for(var i = 0; i < names.Length; i++)
        {
            var segments = names.Take(i + 1).ToArray();
            var joined = string.Join(".", segments);
            fqns[i] = fileScopedPrefix is not null
                ? fileScopedPrefix + "." + joined
                : joined;
        }
        return fqns;
    }

    /// <summary>
    /// FsCheck Arbitrary for namespace chain sources (both block-only and file-scoped prefix variants).
    /// </summary>
    public static Arbitrary<NamespaceChainSource> ArbNamespaceChain()
    {
        return Gen.OneOf(GenNamespaceChainBlockOnly(), GenNamespaceChainWithFileScoped()).ToArbitrary();
    }

    // ─── Type Parameter Shadowing (Property 9) ───────────────────────────

    /// <summary>
    /// Available type parameter names for generating shadowing scenarios.
    /// </summary>
    private static readonly string[] TypeParamNames = ["T", "U", "V", "W", "X"];

    /// <summary>
    /// Generates a type parameter shadowing scenario where a nested type re-declares
    /// a parent's type parameter with the same name. The nested type uses the parameter
    /// in a field declaration, verifying that the nested type's own parameter takes precedence.
    /// </summary>
    public static Gen<TypeParamShadowingSource> GenTypeParamShadowingSource()
    {
        // Container must be class or struct (supports MemberDeclaration with nested types)
        var containerKinds = new[] { AllTypeKind.Class, AllTypeKind.Struct };

        return from containerKind in Gen.Elements(containerKinds)
               from nestedKind in Gen.Elements(AllTypeKind.Class, AllTypeKind.Struct)
               from paramName in Gen.Elements(TypeParamNames)
               let source = BuildTypeParamShadowingSource(containerKind, nestedKind, paramName)
               select new TypeParamShadowingSource(source, containerKind, nestedKind, paramName);
    }

    /// <summary>
    /// Builds a Mist source string where a nested type shadows the parent's type parameter.
    /// Example output: class Outer&lt;T&gt; { class Inner&lt;T&gt; { T field; } }
    /// </summary>
    private static string BuildTypeParamShadowingSource(
        AllTypeKind containerKind, AllTypeKind nestedKind, string paramName)
    {
        var containerKeyword = AllTypeKindKeyword(containerKind);
        var nestedKeyword = AllTypeKindKeyword(nestedKind);

        return $"{containerKeyword} Outer<{paramName}> {{\n" +
               $"    {nestedKeyword} Inner<{paramName}> {{\n" +
               $"        {paramName} data;\n" +
               $"    }}\n" +
               $"}}";
    }

    /// <summary>
    /// FsCheck Arbitrary for type parameter shadowing source strings.
    /// </summary>
    public static Arbitrary<TypeParamShadowingSource> ArbTypeParamShadowingSource()
    {
        return GenTypeParamShadowingSource().ToArbitrary();
    }

    // ─── Ancestor Type Parameter Visibility (Property 8) ─────────────────

    /// <summary>
    /// Generates a nested type source where generic parent types declare type parameters
    /// and the innermost nested type uses all ancestor type parameters as field types.
    /// Depth ranges from 1 to 4 (each level is a generic class with one type parameter).
    /// </summary>
    public static Gen<AncestorTypeParamSource> GenAncestorTypeParamSource()
    {
        return from depth in Gen.Choose(1, 4)
               let source = BuildGenericNestedSource(depth)
               select new AncestorTypeParamSource(source, depth);
    }

    /// <summary>
    /// Builds a Mist source string with generic parent types at each level.
    /// Each level declares a unique type parameter (T0, T1, ..., Td-1).
    /// The innermost nested type declares fields using all ancestor type parameters.
    /// </summary>
    private static string BuildGenericNestedSource(int depth)
    {
        var sb = new System.Text.StringBuilder();

        // Open each level: class Level0<T0> { class Level1<T1> { ... class Inner { fields } } }
        for(var i = 0; i < depth; i++)
        {
            var indent = new string(' ', i * 4);
            var name = $"Level{i}";
            var typeParam = $"T{i}";
            sb.AppendLine($"{indent}class {name}<{typeParam}> {{");
        }

        // Innermost nested type uses all ancestor type parameters as field types
        var innerIndent = new string(' ', depth * 4);
        sb.AppendLine($"{innerIndent}class Inner {{");
        for(var i = 0; i < depth; i++)
        {
            var fieldIndent = new string(' ', (depth + 1) * 4);
            sb.AppendLine($"{fieldIndent}public T{i} field{i};");
        }
        sb.AppendLine($"{innerIndent}}}");

        // Close all braces in reverse order
        for(var i = depth - 1; i >= 0; i--)
        {
            var indent = new string(' ', i * 4);
            sb.AppendLine($"{indent}}}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// FsCheck Arbitrary for ancestor type parameter visibility source strings.
    /// </summary>
    public static Arbitrary<AncestorTypeParamSource> ArbAncestorTypeParamSource()
    {
        return GenAncestorTypeParamSource().ToArbitrary();
    }

    // ─── Same-Name Namespace Scope Sharing (Property 10) ─────────────────

    private static readonly string[] SameNameNamespacePool =
    [
        "Alpha", "Beta", "Gamma", "Delta", "Epsilon",
        "Alpha.Inner", "Beta.Core", "Gamma.Utils",
    ];

    /// <summary>
    /// Generates a Mist source with multiple same-name block namespace declarations.
    /// Each block declares a class, and the last block uses a type from the first block
    /// as a field type — proving cross-resolvability within the shared namespace scope.
    /// </summary>
    public static Gen<SameNameNamespaceSource> GenSameNameNamespaceSource()
    {
        return from nsName in Gen.Elements(SameNameNamespacePool)
               from blockCount in Gen.Choose(2, 4)
               let source = BuildSameNameNamespaceSource(nsName, blockCount)
               select new SameNameNamespaceSource(source, nsName, blockCount);
    }

    /// <summary>
    /// Builds a Mist source string with multiple same-name block namespaces.
    /// The first N-1 blocks each declare a class (TypeA, TypeB, ...).
    /// The last block declares a class that has fields referencing types from
    /// the earlier blocks, verifying cross-resolvability.
    /// </summary>
    private static string BuildSameNameNamespaceSource(string nsName, int blockCount)
    {
        var sb = new System.Text.StringBuilder();
        var typeNames = new List<string>();

        // Generate N-1 blocks, each with a simple class
        for(var i = 0; i < blockCount - 1; i++)
        {
            var typeName = $"Type{(char)('A' + i)}";
            typeNames.Add(typeName);
            sb.AppendLine($"namespace {nsName} {{");
            sb.AppendLine($"    class {typeName} {{");
            sb.AppendLine($"    }}");
            sb.AppendLine($"}}");
            sb.AppendLine();
        }

        // Last block declares a class that references all types from earlier blocks
        sb.AppendLine($"namespace {nsName} {{");
        sb.AppendLine($"    class Consumer {{");
        for(var i = 0; i < typeNames.Count; i++)
        {
            sb.AppendLine($"        {typeNames[i]} field{i};");
        }
        sb.AppendLine($"    }}");
        sb.AppendLine($"}}");

        return sb.ToString();
    }

    /// <summary>
    /// FsCheck Arbitrary for same-name namespace source strings.
    /// </summary>
    public static Arbitrary<SameNameNamespaceSource> ArbSameNameNamespaceSource()
    {
        return GenSameNameNamespaceSource().ToArbitrary();
    }

    // ─── Closed Generic FQN for Nested Types (Property 7) ──────────────

    /// <summary>
    /// Generates a scenario with a nested type inside one or more generic ancestor types.
    /// Used to verify that the nested type is registered in the correct scope and that
    /// parent types have the expected type parameters (prerequisite for closed-generic FQN).
    /// </summary>
    public static Gen<GenericNestedFqnSource> GenGenericNestedFqnSource()
    {
        return Gen.OneOf(
            GenSingleGenericParent(),
            GenMultiLevelGenericParents(),
            GenNestedTypeAlsoGeneric());
    }

    /// <summary>
    /// Generates a single generic parent with a non-generic nested type.
    /// Example: class Outer&lt;T&gt; { class Inner { } }
    /// </summary>
    private static Gen<GenericNestedFqnSource> GenSingleGenericParent()
    {
        return from paramCount in Gen.Choose(1, 3)
               let typeParams = string.Join(", ", Enumerable.Range(0, paramCount).Select(i => $"T{i}"))
               let source = $"class Outer<{typeParams}> {{\n    class Inner {{ }}\n}}"
               let expectedParentParams = Enumerable.Range(0, paramCount).Select(i => $"T{i}").ToArray()
               select new GenericNestedFqnSource(
                   source,
                   GenericNestedFqnKind.SingleGenericParent,
                   ParentTypeNames: new[] { "Outer" },
                   ParentTypeParamCounts: new[] { paramCount },
                   NestedTypeName: "Inner",
                   NestedTypeParamCount: 0);
    }

    /// <summary>
    /// Generates multiple levels of generic parents with a non-generic innermost nested type.
    /// Example: class Level0&lt;T0&gt; { class Level1&lt;T1&gt; { class Inner { } } }
    /// </summary>
    private static Gen<GenericNestedFqnSource> GenMultiLevelGenericParents()
    {
        return from depth in Gen.Choose(2, 4)
               from paramCounts in Gen.ArrayOf(Gen.Choose(1, 2), depth)
               let source = BuildMultiLevelGenericSource(depth, paramCounts)
               let parentNames = Enumerable.Range(0, depth).Select(i => $"Level{i}").ToArray()
               select new GenericNestedFqnSource(
                   source,
                   GenericNestedFqnKind.MultipleLevelGenericParents,
                   ParentTypeNames: parentNames,
                   ParentTypeParamCounts: paramCounts,
                   NestedTypeName: "Inner",
                   NestedTypeParamCount: 0);
    }

    /// <summary>
    /// Generates a generic parent with a generic nested type.
    /// Example: class Outer&lt;T&gt; { class Inner&lt;U&gt; { } }
    /// </summary>
    private static Gen<GenericNestedFqnSource> GenNestedTypeAlsoGeneric()
    {
        return from parentParamCount in Gen.Choose(1, 2)
               from nestedParamCount in Gen.Choose(1, 2)
               let parentParams = string.Join(", ", Enumerable.Range(0, parentParamCount).Select(i => $"T{i}"))
               let nestedParams = string.Join(", ", Enumerable.Range(0, nestedParamCount).Select(i => $"U{i}"))
               let source = $"class Outer<{parentParams}> {{\n    class Inner<{nestedParams}> {{ }}\n}}"
               select new GenericNestedFqnSource(
                   source,
                   GenericNestedFqnKind.NestedTypeAlsoGeneric,
                   ParentTypeNames: new[] { "Outer" },
                   ParentTypeParamCounts: new[] { parentParamCount },
                   NestedTypeName: "Inner",
                   NestedTypeParamCount: nestedParamCount);
    }

    /// <summary>
    /// Builds a Mist source string with multiple levels of generic parents.
    /// </summary>
    private static string BuildMultiLevelGenericSource(int depth, int[] paramCounts)
    {
        var sb = new System.Text.StringBuilder();
        var paramOrdinal = 0;

        for(var i = 0; i < depth; i++)
        {
            var indent = new string(' ', i * 4);
            var name = $"Level{i}";
            var typeParams = string.Join(", ", Enumerable.Range(paramOrdinal, paramCounts[i]).Select(j => $"T{j}"));
            paramOrdinal += paramCounts[i];
            sb.AppendLine($"{indent}class {name}<{typeParams}> {{");
        }

        // Innermost nested type (non-generic)
        var innerIndent = new string(' ', depth * 4);
        sb.AppendLine($"{innerIndent}class Inner {{ }}");

        // Close all braces in reverse order
        for(var i = depth - 1; i >= 0; i--)
        {
            var indent = new string(' ', i * 4);
            sb.AppendLine($"{indent}}}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// FsCheck Arbitrary for generic nested FQN source strings.
    /// </summary>
    public static Arbitrary<GenericNestedFqnSource> ArbGenericNestedFqnSource()
    {
        return GenGenericNestedFqnSource().ToArbitrary();
    }

    // ─── Type FQN Construction (Property 6) ─────────────────────────────

    /// <summary>
    /// Represents the style of namespace declaration used in a type FQN test.
    /// </summary>
    public enum NamespaceStyle
    {
        /// <summary>A single-segment block namespace (e.g., <c>namespace Foo { }</c>).</summary>
        SingleSegmentBlock,

        /// <summary>A multi-segment block namespace (e.g., <c>namespace Foo.Bar.Baz { }</c>).</summary>
        MultiSegmentBlock,

        /// <summary>A file-scoped namespace (e.g., <c>namespace Foo.Bar;</c>).</summary>
        FileScoped,
    }

    /// <summary>
    /// Pool of valid single-segment namespace names for generating type FQN scenarios.
    /// </summary>
    private static readonly string[] NsSegmentPool =
    [
        "Alpha", "Beta", "Gamma", "Delta", "Epsilon",
        "Zeta", "Eta", "Theta", "Iota", "Kappa",
    ];

    /// <summary>
    /// Pool of valid type names for generating type FQN scenarios.
    /// </summary>
    private static readonly string[] TypeNamePool =
    [
        "Widget", "Gadget", "Service", "Handler", "Factory",
        "Builder", "Manager", "Provider", "Adapter", "Proxy",
    ];

    /// <summary>
    /// Generates a type-inside-namespace scenario for Property 6.
    /// Produces a Mist source with a type declared inside a namespace (single-segment,
    /// multi-segment, or file-scoped) and the expected FQN.
    /// </summary>
    public static Gen<TypeInNamespaceSource> GenTypeInNamespaceSource()
    {
        return from style in Gen.Elements(
                   NamespaceStyle.SingleSegmentBlock,
                   NamespaceStyle.MultiSegmentBlock,
                   NamespaceStyle.FileScoped)
               from segmentCount in style == NamespaceStyle.SingleSegmentBlock
                   ? Gen.Constant(1)
                   : Gen.Choose(2, 4)
               from segments in Gen.ArrayOf(Gen.Elements(NsSegmentPool), segmentCount)
               from typeName in Gen.Elements(TypeNamePool)
               from typeKind in Gen.Elements(AllTypeKind.Class, AllTypeKind.Struct,
                   AllTypeKind.Abstract, AllTypeKind.Enum)
               let nsPath = string.Join(".", segments)
               let source = BuildTypeInNamespaceSource(style, nsPath, typeName, typeKind)
               let expectedFqn = nsPath + "." + typeName
               select new TypeInNamespaceSource(source, nsPath, typeName, typeKind, expectedFqn, style);
    }

    /// <summary>
    /// Builds a Mist source string with a type declared inside a namespace.
    /// </summary>
    private static string BuildTypeInNamespaceSource(
        NamespaceStyle style, string nsPath, string typeName, AllTypeKind typeKind)
    {
        var keyword = AllTypeKindKeyword(typeKind);
        var body = typeKind == AllTypeKind.Enum ? "{ A, B }" : "{ }";

        return style switch
        {
            NamespaceStyle.FileScoped =>
                $"namespace {nsPath};\n{keyword} {typeName} {body}\n",
            NamespaceStyle.SingleSegmentBlock or NamespaceStyle.MultiSegmentBlock =>
                $"namespace {nsPath} {{\n    {keyword} {typeName} {body}\n}}\n",
            _ => throw new ArgumentOutOfRangeException(nameof(style)),
        };
    }

    /// <summary>
    /// FsCheck Arbitrary for type-in-namespace source strings.
    /// </summary>
    public static Arbitrary<TypeInNamespaceSource> ArbTypeInNamespaceSource()
    {
        return GenTypeInNamespaceSource().ToArbitrary();
    }

    // ─── Different-Name Namespace Isolation (Property 11) ────────────────

    /// <summary>
    /// Pool of valid namespace names for generating different-name namespace pairs.
    /// </summary>
    private static readonly string[] NamespaceNamePool =
    [
        "Alpha", "Beta", "Gamma", "Delta", "Epsilon",
        "Zeta", "Eta", "Theta", "Iota", "Kappa",
    ];

    /// <summary>
    /// Generates a pair of distinct namespace names for testing scope isolation.
    /// </summary>
    public static Gen<DifferentNamespacePair> GenDifferentNamespacePair()
    {
        return from ns1 in Gen.Elements(NamespaceNamePool)
               from ns2 in Gen.Elements(NamespaceNamePool)
               where ns1 != ns2
               select new DifferentNamespacePair(ns1, ns2);
    }

    /// <summary>
    /// FsCheck Arbitrary for different-name namespace pairs.
    /// </summary>
    public static Arbitrary<DifferentNamespacePair> ArbDifferentNamespacePair()
    {
        return GenDifferentNamespacePair().ToArbitrary();
    }

    // ─── C# Emission Nesting Structure (Property 12) ─────────────────────

    /// <summary>
    /// Generates a nested type inside a parent type, optionally with type parameters
    /// on the parent. Used to verify C# emission preserves nesting structure and does
    /// not re-declare parent type parameters on the nested type.
    /// </summary>
    public static Gen<CSharpNestingSource> GenCSharpNestingSource()
    {
        return Gen.OneOf(
            GenCSharpNesting_NonGenericParent(),
            GenCSharpNesting_GenericParent());
    }

    /// <summary>
    /// Generates a non-generic parent with a nested type.
    /// </summary>
    private static Gen<CSharpNestingSource> GenCSharpNesting_NonGenericParent()
    {
        var containerKinds = new[] { AllTypeKind.Class, AllTypeKind.Struct };

        return from containerKind in Gen.Elements(containerKinds)
               from nestedKind in Gen.Elements(AllTypeKind.Class, AllTypeKind.Struct,
                   AllTypeKind.Abstract, AllTypeKind.Enum)
               let containerKeyword = AllTypeKindKeyword(containerKind)
               let nestedKeyword = AllTypeKindKeyword(nestedKind)
               let nestedBody = nestedKind == AllTypeKind.Enum ? "{ A, B }" : "{ }"
               let source = $"{containerKeyword} Outer {{\n    {nestedKeyword} Inner {nestedBody}\n}}\nclass Program {{\n    public static void Main() {{ }}\n}}"
               select new CSharpNestingSource(
                   source, containerKind, nestedKind,
                   ParentName: "Outer", NestedName: "Inner",
                   ParentTypeParams: Array.Empty<string>(),
                   NestedTypeParams: Array.Empty<string>());
    }

    /// <summary>
    /// Generates a generic parent with a nested type (nested type may also be generic).
    /// </summary>
    private static Gen<CSharpNestingSource> GenCSharpNesting_GenericParent()
    {
        var containerKinds = new[] { AllTypeKind.Class, AllTypeKind.Struct };

        return from containerKind in Gen.Elements(containerKinds)
               from nestedKind in Gen.Elements(AllTypeKind.Class, AllTypeKind.Struct,
                   AllTypeKind.Abstract, AllTypeKind.Enum)
               from parentParamCount in Gen.Choose(1, 3)
               from hasNestedParams in Gen.Elements(true, false)
                   // Enums cannot have type parameters
               let effectiveHasNestedParams = nestedKind == AllTypeKind.Enum ? false : hasNestedParams
               from nestedParamCount in effectiveHasNestedParams ? Gen.Choose(1, 2) : Gen.Constant(0)
               let containerKeyword = AllTypeKindKeyword(containerKind)
               let nestedKeyword = AllTypeKindKeyword(nestedKind)
               let parentParams = Enumerable.Range(0, parentParamCount).Select(i => $"T{i}").ToArray()
               let nestedParams = Enumerable.Range(0, nestedParamCount).Select(i => $"U{i}").ToArray()
               let parentParamList = "<" + string.Join(", ", parentParams) + ">"
               let nestedParamList = nestedParamCount > 0 ? "<" + string.Join(", ", nestedParams) + ">" : ""
               let nestedBody = nestedKind == AllTypeKind.Enum ? "{ A, B }" : "{ }"
               let source = $"{containerKeyword} Outer{parentParamList} {{\n    {nestedKeyword} Inner{nestedParamList} {nestedBody}\n}}\nclass Program {{\n    public static void Main() {{ }}\n}}"
               select new CSharpNestingSource(
                   source, containerKind, nestedKind,
                   ParentName: "Outer", NestedName: "Inner",
                   ParentTypeParams: parentParams,
                   NestedTypeParams: nestedParams);
    }

    /// <summary>
    /// FsCheck Arbitrary for C# nesting structure source strings.
    /// </summary>
    public static Arbitrary<CSharpNestingSource> ArbCSharpNestingSource()
    {
        return GenCSharpNestingSource().ToArbitrary();
    }

    // ─── Parse–Print–Parse Round Trip (Property 4) ───────────────────────

    /// <summary>
    /// Generates a valid Mist source string with nested types and/or nested namespaces
    /// for round-trip testing. Produces a variety of scenarios:
    /// - Nested types at various depths (1–4) with modifiers
    /// - Nested namespaces (block namespaces inside block namespaces)
    /// - Types with modifiers inside nested namespaces
    /// </summary>
    public static Gen<RoundTripSource> GenRoundTripSource()
    {
        return Gen.OneOf(
            GenRoundTripNestedTypes(),
            GenRoundTripNestedNamespaces(),
            GenRoundTripNestedTypesInNamespace());
    }

    /// <summary>
    /// Generates nested types at depth 1–4 with optional modifiers.
    /// </summary>
    private static Gen<RoundTripSource> GenRoundTripNestedTypes()
    {
        return from depth in Gen.Choose(1, 4)
               from containerKinds in Gen.ArrayOf(
                   Gen.Elements(AllTypeKind.Class, AllTypeKind.Struct), depth)
               from leafKind in GenAllTypeKind()
               from mods in GenModifierSubset()
               let source = BuildRoundTripNestedTypeSource(containerKinds, leafKind, mods)
               select new RoundTripSource(source, RoundTripScenario.NestedTypes);
    }

    /// <summary>
    /// Builds a Mist source with nested container types and a leaf type at the bottom.
    /// </summary>
    private static string BuildRoundTripNestedTypeSource(
        AllTypeKind[] containerKinds, AllTypeKind leafKind, ModifierSubset mods)
    {
        var sb = new System.Text.StringBuilder();

        for(var i = 0; i < containerKinds.Length; i++)
        {
            var indent = new string(' ', i * 4);
            var keyword = AllTypeKindKeyword(containerKinds[i]);
            var name = $"Container{i}";
            sb.AppendLine($"{indent}{keyword} {name} {{");
        }

        // Leaf type with modifiers
        var leafIndent = new string(' ', containerKinds.Length * 4);
        var modStr = ModifierSetToSource(mods);
        var leafKeyword = AllTypeKindKeyword(leafKind);
        var leafBody = leafKind == AllTypeKind.Enum ? "{ A, B }" : "{ }";
        sb.AppendLine($"{leafIndent}{modStr}{leafKeyword} Leaf {leafBody}");

        // Close all braces in reverse order
        for(var i = containerKinds.Length - 1; i >= 0; i--)
        {
            var indent = new string(' ', i * 4);
            sb.AppendLine($"{indent}}}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Generates nested block namespaces at depth 2–4.
    /// </summary>
    private static Gen<RoundTripSource> GenRoundTripNestedNamespaces()
    {
        return from depth in Gen.Choose(2, 4)
               from names in Gen.ArrayOf(GenIdentifier(), depth)
               let distinctNames = names.Select((n, i) => n + i).ToArray()
               let source = BuildRoundTripNestedNamespaceSource(distinctNames)
               select new RoundTripSource(source, RoundTripScenario.NestedNamespaces);
    }

    /// <summary>
    /// Builds a Mist source with nested block namespaces and a class in the innermost.
    /// </summary>
    private static string BuildRoundTripNestedNamespaceSource(string[] names)
    {
        var sb = new System.Text.StringBuilder();

        for(var i = 0; i < names.Length; i++)
        {
            var indent = new string(' ', i * 4);
            sb.AppendLine($"{indent}namespace {names[i]} {{");
        }

        var innerIndent = new string(' ', names.Length * 4);
        sb.AppendLine($"{innerIndent}class Marker {{ }}");

        for(var i = names.Length - 1; i >= 0; i--)
        {
            var indent = new string(' ', i * 4);
            sb.AppendLine($"{indent}}}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Generates nested types inside a namespace (combining both features).
    /// </summary>
    private static Gen<RoundTripSource> GenRoundTripNestedTypesInNamespace()
    {
        return from nsName in GenIdentifier()
               from containerKind in Gen.Elements(AllTypeKind.Class, AllTypeKind.Struct)
               from nestedKind in GenAllTypeKind()
               from mods in GenModifierSubset()
               let source = BuildRoundTripTypesInNamespaceSource(
                   nsName, containerKind, nestedKind, mods)
               select new RoundTripSource(source, RoundTripScenario.NestedTypesInNamespace);
    }

    /// <summary>
    /// Builds a Mist source with a block namespace containing a type with a nested type.
    /// </summary>
    private static string BuildRoundTripTypesInNamespaceSource(
        string nsName, AllTypeKind containerKind, AllTypeKind nestedKind, ModifierSubset mods)
    {
        var containerKeyword = AllTypeKindKeyword(containerKind);
        var nestedKeyword = AllTypeKindKeyword(nestedKind);
        var modStr = ModifierSetToSource(mods);
        var nestedBody = nestedKind == AllTypeKind.Enum ? "{ A, B }" : "{ }";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"namespace {nsName} {{");
        sb.AppendLine($"    {containerKeyword} Outer {{");
        sb.AppendLine($"        {modStr}{nestedKeyword} Inner {nestedBody}");
        sb.AppendLine($"    }}");
        sb.AppendLine($"}}");

        return sb.ToString();
    }

    /// <summary>
    /// FsCheck Arbitrary for round-trip source strings.
    /// </summary>
    public static Arbitrary<RoundTripSource> ArbRoundTripSource()
    {
        return GenRoundTripSource().ToArbitrary();
    }
}

/// <summary>
/// Wrapper type for FsCheck Arbitrary registration - a type parameter shadowing scenario.
/// Used by Property 9: Type parameter shadowing.
/// </summary>
public record TypeParamShadowingSource(
    string Source,
    AllTypeKind ContainerKind,
    AllTypeKind NestedKind,
    string ShadowedParamName)
{
    public override string ToString() =>
        $"{ContainerKind}<{ShadowedParamName}> containing {NestedKind}<{ShadowedParamName}>";
}

/// <summary>
/// Wrapper type for FsCheck Arbitrary registration — a namespace chain source with expected FQNs.
/// Used by Property 5: Namespace FQN concatenation.
/// </summary>
public sealed class NamespaceChainSource
{
    public string Source { get; }
    public IReadOnlyList<string> Names { get; }
    public IReadOnlyList<string> ExpectedFqns { get; }
    public string? FileScopedPrefix { get; }

    public NamespaceChainSource(string source, string[] names, string[] expectedFqns, string? FileScopedPrefix)
    {
        Source = source;
        Names = names;
        ExpectedFqns = expectedFqns;
        this.FileScopedPrefix = FileScopedPrefix;
    }

    public override string ToString()
    {
        var prefix = FileScopedPrefix is not null ? $"{FileScopedPrefix}." : "";
        return $"{prefix}{string.Join(".", Names)}";
    }
}

/// <summary>
/// Wrapper type for FsCheck Arbitrary registration - a pair of distinct namespace names.
/// Used by Property 11: Different-name namespace scope isolation.
/// </summary>
public record DifferentNamespacePair(string Namespace1, string Namespace2)
{
    public override string ToString() => $"namespace {Namespace1} vs namespace {Namespace2}";
}

/// <summary>
/// Classifies the kind of generic nesting scenario for Property 7.
/// </summary>
public enum GenericNestedFqnKind
{
    SingleGenericParent,
    MultipleLevelGenericParents,
    NestedTypeAlsoGeneric,
}

/// <summary>
/// Wrapper type for FsCheck Arbitrary registration - a nested type inside generic ancestors.
/// Used by Property 7: Closed generic FQN for nested types.
/// Contains the source and metadata about the expected parent type parameters and nested type.
/// </summary>
public record GenericNestedFqnSource(
    string Source,
    GenericNestedFqnKind Kind,
    IReadOnlyList<string> ParentTypeNames,
    IReadOnlyList<int> ParentTypeParamCounts,
    string NestedTypeName,
    int NestedTypeParamCount)
{
    public override string ToString() => Kind switch
    {
        GenericNestedFqnKind.SingleGenericParent =>
            $"Single generic parent ({ParentTypeParamCounts[0]} params) → {NestedTypeName}",
        GenericNestedFqnKind.MultipleLevelGenericParents =>
            $"Multi-level ({ParentTypeNames.Count} levels) → {NestedTypeName}",
        GenericNestedFqnKind.NestedTypeAlsoGeneric =>
            $"Parent({ParentTypeParamCounts[0]} params) → {NestedTypeName}({NestedTypeParamCount} params)",
        _ => Source,
    };
}

/// <summary>
/// Wrapper type for FsCheck Arbitrary registration - a type declared inside a namespace.
/// Used by Property 6: Type FQN construction.
/// </summary>
public record TypeInNamespaceSource(
    string Source,
    string NamespacePath,
    string TypeName,
    AllTypeKind TypeKind,
    string ExpectedFqn,
    MistNestedScopingGenerator.NamespaceStyle Style)
{
    public override string ToString() => $"{Style}: {ExpectedFqn}";
}

/// <summary>
/// Wrapper type for FsCheck Arbitrary registration - a nested type inside a parent type
/// with optional type parameters on both parent and nested type.
/// Used by Property 12: C# emission preserves nesting structure.
/// </summary>
public record CSharpNestingSource(
    string Source,
    AllTypeKind ContainerKind,
    AllTypeKind NestedKind,
    string ParentName,
    string NestedName,
    IReadOnlyList<string> ParentTypeParams,
    IReadOnlyList<string> NestedTypeParams)
{
    public override string ToString()
    {
        var parentParams = ParentTypeParams.Count > 0
            ? $"<{string.Join(", ", ParentTypeParams)}>"
            : "";
        var nestedParams = NestedTypeParams.Count > 0
            ? $"<{string.Join(", ", NestedTypeParams)}>"
            : "";
        return $"{ContainerKind}{parentParams} containing {NestedKind}{nestedParams}";
    }
}

/// <summary>
/// Classifies the kind of round-trip scenario for Property 4.
/// </summary>
public enum RoundTripScenario
{
    /// <summary>Nested types at various depths with modifiers.</summary>
    NestedTypes,

    /// <summary>Nested block namespaces.</summary>
    NestedNamespaces,

    /// <summary>Nested types inside a block namespace.</summary>
    NestedTypesInNamespace,
}

/// <summary>
/// Wrapper type for FsCheck Arbitrary registration - a valid Mist source with nested
/// types and/or nested namespaces for parse–print–parse round-trip testing.
/// Used by Property 4: Parse–print–parse round trip.
/// </summary>
public record RoundTripSource(string Source, RoundTripScenario Scenario)
{
    public override string ToString() => Scenario.ToString();
}