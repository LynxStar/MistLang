// Generator for multi-namespace Mist source files.
// Produces syntactically valid source strings with 0–3 namespace declarations
// (file-scoped, block, or mixed) interleaved with type declarations.

using FsCheck;
using FsCheck.Fluent;

namespace Mist.Tests.Generators;

/// <summary>
/// Wrapper type for FsCheck Arbitrary registration — multi-namespace source files.
/// </summary>
public record MultiNamespaceSource(string Source);

/// <summary>
/// Wrapper type for mixed namespace source files.
/// </summary>
public record MixedNamespaceSource(string Source);

/// <summary>
/// FsCheck generators for multi-namespace Mist source files.
/// Used by Property 5: Multi-Namespace Parsing.
/// </summary>
public static class MistMultiNamespaceGenerator
{
    /// <summary>
    /// Generates a valid Mist identifier (simple, no keywords).
    /// </summary>
    private static Gen<string> GenIdentifier()
    {
        return Gen.Elements(
            "Alpha", "Beta", "Gamma", "Delta", "Epsilon",
            "Foo", "Bar", "Baz", "Qux", "Quux",
            "Widget", "Service", "Handler", "Manager");
    }

    /// <summary>
    /// Generates a valid qualified namespace name (1–3 segments).
    /// </summary>
    private static Gen<string> GenNamespaceName()
    {
        return from segCount in Gen.Choose(1, 3)
               from segments in Gen.ArrayOf(GenIdentifier(), segCount)
               select string.Join(".", segments);
    }

    /// <summary>
    /// Generates a simple class declaration.
    /// </summary>
    private static Gen<string> GenClassDecl()
    {
        return from name in GenIdentifier()
               select $"class {name} {{ }}";
    }

    /// <summary>
    /// Generates a simple struct declaration.
    /// </summary>
    private static Gen<string> GenStructDecl()
    {
        return from name in GenIdentifier()
               select $"struct {name} {{ }}";
    }

    /// <summary>
    /// Generates a simple interface declaration.
    /// </summary>
    private static Gen<string> GenInterfaceDecl()
    {
        return from name in GenIdentifier()
               select $"interface I{name} {{ }}";
    }

    /// <summary>
    /// Generates a random type declaration (class, struct, or interface).
    /// </summary>
    private static Gen<string> GenTypeDecl()
    {
        return Gen.OneOf(GenClassDecl(), GenStructDecl(), GenInterfaceDecl());
    }

    /// <summary>
    /// Generates a file-scoped namespace declaration with 0–2 type declarations.
    /// </summary>
    private static Gen<string> GenFileScopedNamespace()
    {
        return from nsName in GenNamespaceName()
               from typeCount in Gen.Choose(0, 2)
               from types in Gen.ArrayOf(GenTypeDecl(), typeCount)
               select $"namespace {nsName};\n{string.Join("\n", types)}";
    }

    /// <summary>
    /// Generates a block namespace declaration with 0–2 type declarations.
    /// </summary>
    private static Gen<string> GenBlockNamespace()
    {
        return from nsName in GenNamespaceName()
               from typeCount in Gen.Choose(0, 2)
               from types in Gen.ArrayOf(GenTypeDecl(), typeCount)
               let body = string.Join("\n    ", types)
               select $"namespace {nsName} {{\n    {body}\n}}";
    }

    /// <summary>
    /// Generates a namespace declaration (file-scoped or block).
    /// </summary>
    private static Gen<string> GenNamespaceDecl()
    {
        return Gen.OneOf(GenFileScopedNamespace(), GenBlockNamespace());
    }

    /// <summary>
    /// Generates a top-level item: either a namespace declaration or a type declaration.
    /// </summary>
    private static Gen<string> GenTopLevelItem()
    {
        return Gen.Frequency(
            (2, GenNamespaceDecl()),
            (3, GenTypeDecl()));
    }

    /// <summary>
    /// Generates a complete Mist source file with 0–5 top-level items
    /// (namespaces and/or type declarations).
    /// </summary>
    public static Gen<string> GenMultiNamespaceSource()
    {
        return from itemCount in Gen.Choose(0, 5)
               from items in Gen.ArrayOf(GenTopLevelItem(), itemCount)
               select string.Join("\n\n", items);
    }

    /// <summary>
    /// Generates a source file with exactly the specified number of namespace declarations
    /// plus 0–2 additional type declarations.
    /// </summary>
    public static Gen<string> GenSourceWithNamespaceCount(int nsCount)
    {
        return from namespaces in Gen.ArrayOf(GenNamespaceDecl(), nsCount)
               from typeCount in Gen.Choose(0, 2)
               from types in Gen.ArrayOf(GenTypeDecl(), typeCount)
               let allItems = namespaces.Concat(types)
               select string.Join("\n\n", allItems);
    }

    /// <summary>
    /// Generates a source file with mixed file-scoped and block namespaces.
    /// </summary>
    public static Gen<string> GenMixedNamespaceSource()
    {
        return from fileScopedCount in Gen.Choose(1, 2)
               from blockCount in Gen.Choose(1, 2)
               from fileScoped in Gen.ArrayOf(GenFileScopedNamespace(), fileScopedCount)
               from blocks in Gen.ArrayOf(GenBlockNamespace(), blockCount)
               from typeCount in Gen.Choose(0, 2)
               from types in Gen.ArrayOf(GenTypeDecl(), typeCount)
               let allItems = fileScoped.Concat(blocks).Concat(types)
               select string.Join("\n\n", allItems);
    }

    /// <summary>
    /// FsCheck Arbitrary for multi-namespace source files.
    /// </summary>
    public static Arbitrary<MultiNamespaceSource> ArbitraryMultiNamespaceSource()
    {
        return GenMultiNamespaceSource()
            .Select(s => new MultiNamespaceSource(s))
            .ToArbitrary();
    }

    /// <summary>
    /// FsCheck Arbitrary for mixed namespace source files.
    /// </summary>
    public static Arbitrary<MixedNamespaceSource> ArbitraryMixedNamespaceSource()
    {
        return GenMixedNamespaceSource()
            .Select(s => new MixedNamespaceSource(s))
            .ToArbitrary();
    }
}
