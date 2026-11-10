using FsCheck;
using FsCheck.Fluent;

namespace Mist.Tests.Generators;

public enum NestableTypeKind
{
    Class,
    Struct,
}

public enum AllTypeKind
{
    Class,
    Struct,
    Abstract,
    Enum,
}

public record ModifierSubset(bool IsPublic, bool IsPrivate, bool IsStatic)
{
    public override string ToString()
    {
        var parts = new List<string>();
        if(IsPublic)
        {
            parts.Add("public");
        }

        if(IsPrivate)
        {
            parts.Add("private");
        }

        if(IsStatic)
        {
            parts.Add("static");
        }

        return parts.Count == 0 ? "(none)" : string.Join(" ", parts);
    }
}

public record NestedTypeSource(string Source, AllTypeKind ContainerKind, AllTypeKind NestedKind)
{
    public override string ToString()
    {
        return $"{ContainerKind} containing {NestedKind}";
    }
}

public record NestedTypeDepthSource(string Source, int Depth)
{
    public override string ToString()
    {
        return $"Depth={Depth}";
    }
}

public record SameNameNamespaceSource(string Source, string NamespaceName, int BlockCount)
{
    public override string ToString()
    {
        return $"namespace {NamespaceName} x{BlockCount}";
    }
}

public record AncestorTypeParamSource(string Source, int Depth)
{
    public override string ToString()
    {
        return $"GenericDepth={Depth}";
    }
}

public static class MistNestedScopingGenerator
{
    private static readonly AllTypeKind[] AllKinds =
    [
        AllTypeKind.Class,
        AllTypeKind.Struct,
        AllTypeKind.Abstract,
        AllTypeKind.Enum,
    ];

    public static Gen<AllTypeKind> GenAllTypeKind()
    {
        return Gen.Elements(AllKinds);
    }

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

    public static Gen<ModifierSubset> GenModifierSubset()
    {
        return from isPublic in Gen.Elements(true, false)
               from isPrivate in Gen.Elements(true, false)
               from isStatic in Gen.Elements(true, false)
                   // Exclude public+private (mutually exclusive)
               where !(isPublic && isPrivate)
               select new ModifierSubset(isPublic, isPrivate, isStatic);
    }

    public static string AllTypeKindKeyword(AllTypeKind kind)
    {
        return kind switch
        {
            AllTypeKind.Class => "class",
            AllTypeKind.Struct => "struct",
            AllTypeKind.Abstract => "abstract",
            AllTypeKind.Enum => "enum",
            _ => "class"
        };
    }

    public static string ModifierSetToSource(ModifierSubset mods)
    {
        var parts = new List<string>();
        if(mods.IsPublic)
        {
            parts.Add("public");
        }

        if(mods.IsPrivate)
        {
            parts.Add("private");
        }

        if(mods.IsStatic)
        {
            parts.Add("static");
        }

        return parts.Count == 0 ? "" : string.Join(" ", parts) + " ";
    }

    public static int ModifierCount(ModifierSubset mods)
    {
        var count = 0;
        if(mods.IsPublic)
        {
            count++;
        }

        if(mods.IsPrivate)
        {
            count++;
        }

        if(mods.IsStatic)
        {
            count++;
        }

        return count;
    }

    public static HashSet<Type> ExpectedModifierNodeTypes(ModifierSubset mods)
    {
        var types = new HashSet<Type>();
        if(mods.IsPublic)
        {
            _ = types.Add(typeof(Generated.PublicModifierModifierNode));
        }

        if(mods.IsPrivate)
        {
            _ = types.Add(typeof(Generated.PrivateModifierModifierNode));
        }

        if(mods.IsStatic)
        {
            _ = types.Add(typeof(Generated.StaticModifierModifierNode));
        }

        return types;
    }

    public static Arbitrary<NestedTypeSource> ArbNestedTypeSource()
    {
        return GenNestedTypeSource().ToArbitrary();
    }

    public static Arbitrary<AllTypeKind> ArbAllTypeKind()
    {
        return GenAllTypeKind().ToArbitrary();
    }

    public static Arbitrary<ModifierSubset> ArbModifierSubset()
    {
        return GenModifierSubset().ToArbitrary();
    }

    public static Gen<NestedTypeDepthSource> GenNestedTypeDepthSource()
    {
        return from depth in Gen.Choose(1, 8)
               from kinds in Gen.ArrayOf(Gen.Elements(AllTypeKind.Class, AllTypeKind.Struct), depth)
               let source = BuildNestedSourceAtDepth(kinds)
               select new NestedTypeDepthSource(source, depth);
    }

    private static string BuildNestedSourceAtDepth(AllTypeKind[] kinds)
    {
        var sb = new System.Text.StringBuilder();
        for(var i = 0; i < kinds.Length; i++)
        {
            var indent = new string(' ', i * 4);
            var keyword = AllTypeKindKeyword(kinds[i]);
            var name = $"Level{i}";
            _ = sb.AppendLine($"{indent}{keyword} {name} {{");
        }

        for(var i = kinds.Length - 1; i >= 0; i--)
        {
            var indent = new string(' ', i * 4);
            _ = sb.AppendLine($"{indent}}}");
        }

        return sb.ToString();
    }

    public static Arbitrary<NestedTypeDepthSource> ArbNestedTypeDepthSource()
    {
        return GenNestedTypeDepthSource().ToArbitrary();
    }

    // ─── Namespace Chain (Property 5) ──────────────────────────────────────

    private static Gen<string> GenIdentifier()
    {
        return from upper in Gen.Elements('A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J', 'K', 'L', 'M', 'N', 'O', 'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z')
               from len in Gen.Choose(2, 5)
               from rest in Gen.ArrayOf(Gen.Elements('a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j', 'k', 'l', 'm', 'n', 'o', 'p', 'q', 'r', 's', 't', 'u', 'v', 'w', 'x', 'y', 'z'), len)
               let name = upper + new string(rest)
               where name is not "Class" and not "Struct" and not "Abstract"
                  and not "Enum" and not "Namespace" and not "Public" and not "Private"
                  and not "Static" and not "True" and not "False" and not "Null"
               select name;
    }

    public static Gen<NamespaceChainSource> GenNamespaceChainBlockOnly()
    {
        return from segmentCount in Gen.Choose(2, 5)
               from names in Gen.ArrayOf(GenIdentifier(), segmentCount)
               let distinctNames = names.Select((n, i) => n + i).ToArray()
               let source = BuildNestedBlockNamespaceSource(distinctNames, fileScopedPrefix: null)
               let expectedFqns = ComputeExpectedFqns(distinctNames, fileScopedPrefix: null)
               select new NamespaceChainSource(source, distinctNames, expectedFqns, FileScopedPrefix: null);
    }

    public static Gen<NamespaceChainSource> GenNamespaceChainWithFileScoped()
    {
        return from fileScopedName in GenIdentifier()
               from segmentCount in Gen.Choose(1, 4)
               from names in Gen.ArrayOf(GenIdentifier(), segmentCount)
               let distinctNames = names.Select((n, i) => n + i).ToArray()
               let source = BuildNestedBlockNamespaceSource(distinctNames, fileScopedPrefix: fileScopedName)
               let expectedFqns = ComputeExpectedFqns(distinctNames, fileScopedPrefix: fileScopedName)
               select new NamespaceChainSource(source, distinctNames, expectedFqns, FileScopedPrefix: fileScopedName);
    }

    internal static string BuildNestedBlockNamespaceSource(string[] names, string? fileScopedPrefix)
    {
        var sb = new System.Text.StringBuilder();

        if(fileScopedPrefix is not null)
        {
            _ = sb.AppendLine($"namespace {fileScopedPrefix};");
            _ = sb.AppendLine();
        }

        for(var i = 0; i < names.Length; i++)
        {
            var indent = new string(' ', i * 4);
            _ = sb.AppendLine($"{indent}namespace {names[i]} {{");
        }

        var innerIndent = new string(' ', names.Length * 4);
        _ = sb.AppendLine($"{innerIndent}class Marker {{ }}");

        for(var i = names.Length - 1; i >= 0; i--)
        {
            var indent = new string(' ', i * 4);
            _ = sb.AppendLine($"{indent}}}");
        }

        return sb.ToString();
    }

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

    public static Arbitrary<NamespaceChainSource> ArbNamespaceChain()
    {
        return Gen.OneOf(GenNamespaceChainBlockOnly(), GenNamespaceChainWithFileScoped()).ToArbitrary();
    }

    // ─── Type Parameter Shadowing (Property 9) ───────────────────────────

    private static readonly string[] TypeParamNames = ["T", "U", "V", "W", "X"];

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

    public static Arbitrary<TypeParamShadowingSource> ArbTypeParamShadowingSource()
    {
        return GenTypeParamShadowingSource().ToArbitrary();
    }

    // ─── Ancestor Type Parameter Visibility (Property 8) ─────────────────

    public static Gen<AncestorTypeParamSource> GenAncestorTypeParamSource()
    {
        return from depth in Gen.Choose(1, 4)
               let source = BuildGenericNestedSource(depth)
               select new AncestorTypeParamSource(source, depth);
    }

    private static string BuildGenericNestedSource(int depth)
    {
        var sb = new System.Text.StringBuilder();

        for(var i = 0; i < depth; i++)
        {
            var indent = new string(' ', i * 4);
            var name = $"Level{i}";
            var typeParam = $"T{i}";
            _ = sb.AppendLine($"{indent}class {name}<{typeParam}> {{");
        }

        var innerIndent = new string(' ', depth * 4);
        _ = sb.AppendLine($"{innerIndent}class Inner {{");
        for(var i = 0; i < depth; i++)
        {
            var fieldIndent = new string(' ', (depth + 1) * 4);
            _ = sb.AppendLine($"{fieldIndent}public T{i} field{i};");
        }

        _ = sb.AppendLine($"{innerIndent}}}");

        for(var i = depth - 1; i >= 0; i--)
        {
            var indent = new string(' ', i * 4);
            _ = sb.AppendLine($"{indent}}}");
        }

        return sb.ToString();
    }

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

    public static Gen<SameNameNamespaceSource> GenSameNameNamespaceSource()
    {
        return from nsName in Gen.Elements(SameNameNamespacePool)
               from blockCount in Gen.Choose(2, 4)
               let source = BuildSameNameNamespaceSource(nsName, blockCount)
               select new SameNameNamespaceSource(source, nsName, blockCount);
    }

    private static string BuildSameNameNamespaceSource(string nsName, int blockCount)
    {
        var sb = new System.Text.StringBuilder();
        var typeNames = new List<string>();

        for(var i = 0; i < blockCount - 1; i++)
        {
            var typeName = $"Type{(char)('A' + i)}";
            typeNames.Add(typeName);
            _ = sb.AppendLine($"namespace {nsName} {{");
            _ = sb.AppendLine($"    class {typeName} {{");
            _ = sb.AppendLine($"    }}");
            _ = sb.AppendLine($"}}");
            _ = sb.AppendLine();
        }

        _ = sb.AppendLine($"namespace {nsName} {{");
        _ = sb.AppendLine($"    class Consumer {{");
        for(var i = 0; i < typeNames.Count; i++)
        {
            _ = sb.AppendLine($"        {typeNames[i]} field{i};");
        }

        _ = sb.AppendLine($"    }}");
        _ = sb.AppendLine($"}}");

        return sb.ToString();
    }

    public static Arbitrary<SameNameNamespaceSource> ArbSameNameNamespaceSource()
    {
        return GenSameNameNamespaceSource().ToArbitrary();
    }

    // ─── Closed Generic FQN for Nested Types (Property 7) ──────────────

    public static Gen<GenericNestedFqnSource> GenGenericNestedFqnSource()
    {
        return Gen.OneOf(
            GenSingleGenericParent(),
            GenMultiLevelGenericParents(),
            GenNestedTypeAlsoGeneric());
    }

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
            _ = sb.AppendLine($"{indent}class {name}<{typeParams}> {{");
        }

        var innerIndent = new string(' ', depth * 4);
        _ = sb.AppendLine($"{innerIndent}class Inner {{ }}");

        for(var i = depth - 1; i >= 0; i--)
        {
            var indent = new string(' ', i * 4);
            _ = sb.AppendLine($"{indent}}}");
        }

        return sb.ToString();
    }

    public static Arbitrary<GenericNestedFqnSource> ArbGenericNestedFqnSource()
    {
        return GenGenericNestedFqnSource().ToArbitrary();
    }

    // ─── Type FQN Construction (Property 6) ─────────────────────────────

    public enum NamespaceStyle
    {
        SingleSegmentBlock,

        MultiSegmentBlock,

        FileScoped,
    }

    private static readonly string[] NsSegmentPool =
    [
        "Alpha", "Beta", "Gamma", "Delta", "Epsilon",
        "Zeta", "Eta", "Theta", "Iota", "Kappa",
    ];

    private static readonly string[] TypeNamePool =
    [
        "Widget", "Gadget", "Service", "Handler", "Factory",
        "Builder", "Manager", "Provider", "Adapter", "Proxy",
    ];

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

    public static Arbitrary<TypeInNamespaceSource> ArbTypeInNamespaceSource()
    {
        return GenTypeInNamespaceSource().ToArbitrary();
    }

    // ─── Different-Name Namespace Isolation (Property 11) ────────────────

    private static readonly string[] NamespaceNamePool =
    [
        "Alpha", "Beta", "Gamma", "Delta", "Epsilon",
        "Zeta", "Eta", "Theta", "Iota", "Kappa",
    ];

    public static Gen<DifferentNamespacePair> GenDifferentNamespacePair()
    {
        return from ns1 in Gen.Elements(NamespaceNamePool)
               from ns2 in Gen.Elements(NamespaceNamePool)
               where ns1 != ns2
               select new DifferentNamespacePair(ns1, ns2);
    }

    public static Arbitrary<DifferentNamespacePair> ArbDifferentNamespacePair()
    {
        return GenDifferentNamespacePair().ToArbitrary();
    }

    // ─── C# Emission Nesting Structure (Property 12) ─────────────────────

    public static Gen<CSharpNestingSource> GenCSharpNestingSource()
    {
        return Gen.OneOf(
            GenCSharpNesting_NonGenericParent(),
            GenCSharpNesting_GenericParent());
    }

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

    private static Gen<CSharpNestingSource> GenCSharpNesting_GenericParent()
    {
        var containerKinds = new[] { AllTypeKind.Class, AllTypeKind.Struct };

        return from containerKind in Gen.Elements(containerKinds)
               from nestedKind in Gen.Elements(AllTypeKind.Class, AllTypeKind.Struct,
                   AllTypeKind.Abstract, AllTypeKind.Enum)
               from parentParamCount in Gen.Choose(1, 3)
               from hasNestedParams in Gen.Elements(true, false)
                   // Enums cannot have type parameters
               let effectiveHasNestedParams = nestedKind != AllTypeKind.Enum && hasNestedParams
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

    public static Arbitrary<CSharpNestingSource> ArbCSharpNestingSource()
    {
        return GenCSharpNestingSource().ToArbitrary();
    }

    // ─── Parse–Print–Parse Round Trip (Property 4) ───────────────────────

    public static Gen<RoundTripSource> GenRoundTripSource()
    {
        return Gen.OneOf(
            GenRoundTripNestedTypes(),
            GenRoundTripNestedNamespaces(),
            GenRoundTripNestedTypesInNamespace());
    }

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

    private static string BuildRoundTripNestedTypeSource(
        AllTypeKind[] containerKinds, AllTypeKind leafKind, ModifierSubset mods)
    {
        var sb = new System.Text.StringBuilder();

        for(var i = 0; i < containerKinds.Length; i++)
        {
            var indent = new string(' ', i * 4);
            var keyword = AllTypeKindKeyword(containerKinds[i]);
            var name = $"Container{i}";
            _ = sb.AppendLine($"{indent}{keyword} {name} {{");
        }

        var leafIndent = new string(' ', containerKinds.Length * 4);
        var modStr = ModifierSetToSource(mods);
        var leafKeyword = AllTypeKindKeyword(leafKind);
        var leafBody = leafKind == AllTypeKind.Enum ? "{ A, B }" : "{ }";
        _ = sb.AppendLine($"{leafIndent}{modStr}{leafKeyword} Leaf {leafBody}");

        for(var i = containerKinds.Length - 1; i >= 0; i--)
        {
            var indent = new string(' ', i * 4);
            _ = sb.AppendLine($"{indent}}}");
        }

        return sb.ToString();
    }

    private static Gen<RoundTripSource> GenRoundTripNestedNamespaces()
    {
        return from depth in Gen.Choose(2, 4)
               from names in Gen.ArrayOf(GenIdentifier(), depth)
               let distinctNames = names.Select((n, i) => n + i).ToArray()
               let source = BuildRoundTripNestedNamespaceSource(distinctNames)
               select new RoundTripSource(source, RoundTripScenario.NestedNamespaces);
    }

    private static string BuildRoundTripNestedNamespaceSource(string[] names)
    {
        var sb = new System.Text.StringBuilder();

        for(var i = 0; i < names.Length; i++)
        {
            var indent = new string(' ', i * 4);
            _ = sb.AppendLine($"{indent}namespace {names[i]} {{");
        }

        var innerIndent = new string(' ', names.Length * 4);
        _ = sb.AppendLine($"{innerIndent}class Marker {{ }}");

        for(var i = names.Length - 1; i >= 0; i--)
        {
            var indent = new string(' ', i * 4);
            _ = sb.AppendLine($"{indent}}}");
        }

        return sb.ToString();
    }

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

    private static string BuildRoundTripTypesInNamespaceSource(
        string nsName, AllTypeKind containerKind, AllTypeKind nestedKind, ModifierSubset mods)
    {
        var containerKeyword = AllTypeKindKeyword(containerKind);
        var nestedKeyword = AllTypeKindKeyword(nestedKind);
        var modStr = ModifierSetToSource(mods);
        var nestedBody = nestedKind == AllTypeKind.Enum ? "{ A, B }" : "{ }";

        var sb = new System.Text.StringBuilder();
        _ = sb.AppendLine($"namespace {nsName} {{");
        _ = sb.AppendLine($"    {containerKeyword} Outer {{");
        _ = sb.AppendLine($"        {modStr}{nestedKeyword} Inner {nestedBody}");
        _ = sb.AppendLine($"    }}");
        _ = sb.AppendLine($"}}");

        return sb.ToString();
    }

    public static Arbitrary<RoundTripSource> ArbRoundTripSource()
    {
        return GenRoundTripSource().ToArbitrary();
    }
}

public record TypeParamShadowingSource(
    string Source,
    AllTypeKind ContainerKind,
    AllTypeKind NestedKind,
    string ShadowedParamName)
{
    public override string ToString()
    {
        return $"{ContainerKind}<{ShadowedParamName}> containing {NestedKind}<{ShadowedParamName}>";
    }
}

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

public record DifferentNamespacePair(string Namespace1, string Namespace2)
{
    public override string ToString()
    {
        return $"namespace {Namespace1} vs namespace {Namespace2}";
    }
}

public enum GenericNestedFqnKind
{
    SingleGenericParent,
    MultipleLevelGenericParents,
    NestedTypeAlsoGeneric,
}

public record GenericNestedFqnSource(
    string Source,
    GenericNestedFqnKind Kind,
    IReadOnlyList<string> ParentTypeNames,
    IReadOnlyList<int> ParentTypeParamCounts,
    string NestedTypeName,
    int NestedTypeParamCount)
{
    public override string ToString()
    {
        return Kind switch
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
}

public record TypeInNamespaceSource(
    string Source,
    string NamespacePath,
    string TypeName,
    AllTypeKind TypeKind,
    string ExpectedFqn,
    MistNestedScopingGenerator.NamespaceStyle Style)
{
    public override string ToString()
    {
        return $"{Style}: {ExpectedFqn}";
    }
}

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

public enum RoundTripScenario
{
    NestedTypes,

    NestedNamespaces,

    NestedTypesInNamespace,
}

public record RoundTripSource(string Source, RoundTripScenario Scenario)
{
    public override string ToString()
    {
        return Scenario.ToString();
    }
}