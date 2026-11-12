using FsCheck;
using FsCheck.Fluent;

namespace Mist.Tests.Generators;

/// Each source shape has its own type because FsCheck selects an Arbitrary by type.
public record MultiNamespaceSource(string Source);

public record MixedNamespaceSource(string Source);

public static class MistMultiNamespaceGenerator
{
    private static Gen<string> GenIdentifier()
    {
        return Gen.Elements(
            "Alpha", "Beta", "Gamma", "Delta", "Epsilon",
            "Foo", "Bar", "Baz", "Qux", "Quux",
            "Widget", "Service", "Handler", "Manager");
    }

    private static Gen<string> GenNamespaceName()
    {
        return from segCount in Gen.Choose(1, 3)
               from segments in Gen.ArrayOf(GenIdentifier(), segCount)
               select string.Join(".", segments);
    }

    private static Gen<string> GenClassDecl()
    {
        return from name in GenIdentifier()
               select $"class {name} {{ }}";
    }

    private static Gen<string> GenStructDecl()
    {
        return from name in GenIdentifier()
               select $"struct {name} {{ }}";
    }

    private static Gen<string> GenAbstractDecl()
    {
        return from name in GenIdentifier()
               select $"abstract I{name} {{ }}";
    }

    private static Gen<string> GenTypeDecl()
    {
        return Gen.OneOf(GenClassDecl(), GenStructDecl(), GenAbstractDecl());
    }

    private static Gen<string> GenFileScopedNamespace()
    {
        return from nsName in GenNamespaceName()
               from typeCount in Gen.Choose(0, 2)
               from types in Gen.ArrayOf(GenTypeDecl(), typeCount)
               select $"namespace {nsName};\n{string.Join("\n", types)}";
    }

    private static Gen<string> GenBlockNamespace()
    {
        return from nsName in GenNamespaceName()
               from typeCount in Gen.Choose(0, 2)
               from types in Gen.ArrayOf(GenTypeDecl(), typeCount)
               let body = string.Join("\n    ", types)
               select $"namespace {nsName} {{\n    {body}\n}}";
    }

    private static Gen<string> GenNamespaceDecl()
    {
        return Gen.OneOf(GenFileScopedNamespace(), GenBlockNamespace());
    }

    private static Gen<string> GenTopLevelItem()
    {
        return Gen.Frequency(
            (2, GenNamespaceDecl()),
            (3, GenTypeDecl()));
    }

    public static Gen<string> GenMultiNamespaceSource()
    {
        return from itemCount in Gen.Choose(0, 5)
               from items in Gen.ArrayOf(GenTopLevelItem(), itemCount)
               select string.Join("\n\n", items);
    }

    public static Gen<string> GenSourceWithNamespaceCount(int nsCount)
    {
        return from namespaces in Gen.ArrayOf(GenNamespaceDecl(), nsCount)
               from typeCount in Gen.Choose(0, 2)
               from types in Gen.ArrayOf(GenTypeDecl(), typeCount)
               let allItems = namespaces.Concat(types)
               select string.Join("\n\n", allItems);
    }

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

    public static Arbitrary<MultiNamespaceSource> ArbitraryMultiNamespaceSource()
    {
        return GenMultiNamespaceSource()
            .Select(s => new MultiNamespaceSource(s))
            .ToArbitrary();
    }

    public static Arbitrary<MixedNamespaceSource> ArbitraryMixedNamespaceSource()
    {
        return GenMixedNamespaceSource()
            .Select(s => new MixedNamespaceSource(s))
            .ToArbitrary();
    }
}