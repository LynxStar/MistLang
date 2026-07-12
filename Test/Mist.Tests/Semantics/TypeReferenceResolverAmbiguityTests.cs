using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Semantics;

/// <summary>
/// Unit tests for TypeReferenceResolver ambiguity detection.
/// Verifies that when the same type name is visible from multiple imported
/// namespaces at the same scope level, an ambiguity diagnostic is emitted
/// rather than silently picking the first match.
/// </summary>
public sealed class TypeReferenceResolverAmbiguityTests
{
    /// <summary>
    /// Creates a minimal BuilderContext with the given ProgramModel.
    /// </summary>
    private static (BuilderContext Context, DiagnosticBag Diagnostics) CreateContext()
    {
        var programModel = new ProgramModelClass();
        var diagnostics = new DiagnosticBag();
        var globalScope = new Scope(parent: null);
        var context = new BuilderContext(globalScope, programModel, diagnostics, NullAmbientTypeProvider.Instance);
        return (context, diagnostics);
    }

    /// <summary>
    /// Registers a type in the program model under the given namespace.
    /// </summary>
    private static MistType RegisterType(ProgramModelClass programModel, string namespaceName, string typeName)
    {
        _ = programModel.GetOrAddNamespace(namespaceName);
        var scope = new Scope(null) { Namespace = namespaceName };
        var type = new ClassType(typeName, namespaceName, scope, null, null);
        _ = programModel.RegisterType(type);
        return type;
    }

    /// <summary>
    /// Creates a dummy AST node that ResolveNamedType needs for diagnostic reporting.
    /// Uses a generated node type since SyntaxNode is abstract.
    /// </summary>
    private static Storm.Runtime.SyntaxNode MakeDummyNode()
    {
        return new CompilationUnitNode
        {
            Items = ImmutableArray<TopLevelItemNode>.Empty,
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test: Two imported namespaces with same type name → ambiguity error
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// When two imported namespaces both contain a type with the same name,
    /// resolving that name should produce an ambiguity diagnostic.
    /// This is the core bug: the current implementation returns the first match.
    /// </summary>
    [Fact]
    public void TwoImportedNamespaces_SameTypeName_ReportsAmbiguity()
    {
        var (context, diagnostics) = CreateContext();

        // Register "Foo" in both NsA and NsB.
        _ = RegisterType(context.ProgramModel, "NsA", "Foo");
        _ = RegisterType(context.ProgramModel, "NsB", "Foo");

        // Create a scope that imports both namespaces.
        var scope = new Scope(context.GlobalScope) { Namespace = "Consumer" };
        scope.AddImportedNamespace("NsA");
        scope.AddImportedNamespace("NsB");

        var resolver = new TypeReferenceResolver(context);
        var result = resolver.ResolveNamedType("Foo", scope, MakeDummyNode());

        // Should be ErrorType (ambiguous) and a diagnostic should be emitted.
        var diags = diagnostics.ToImmutableArray();
        Assert.True(
            result is ErrorType || diags.Any(d => d.Id.Value == "MIST_PM_AMBIGUOUS_TYPE"),
            "Expected an ambiguity error when the same type name exists in two imported namespaces, " +
            $"but got {result.GetType().Name} with {diags.Length} diagnostics.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test: Current namespace shadows imports (correct behavior)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// When the current namespace and an imported namespace both contain a type
    /// with the same name, the current namespace should win without ambiguity.
    /// This matches C# behavior where own namespace shadows imports.
    /// </summary>
    [Fact]
    public void CurrentNamespace_ShadowsImport_NoAmbiguity()
    {
        var (context, diagnostics) = CreateContext();

        // Register "Foo" in both the current namespace and an imported one.
        var ownFoo = RegisterType(context.ProgramModel, "MyNamespace", "Foo");
        _ = RegisterType(context.ProgramModel, "OtherNs", "Foo");

        // Scope is in MyNamespace and imports OtherNs.
        var scope = new Scope(context.GlobalScope) { Namespace = "MyNamespace" };
        scope.AddImportedNamespace("OtherNs");

        var resolver = new TypeReferenceResolver(context);
        var result = resolver.ResolveNamedType("Foo", scope, MakeDummyNode());

        // Should resolve to MyNamespace.Foo without any diagnostic.
        var diags = diagnostics.ToImmutableArray();
        Assert.Same(ownFoo, result);
        Assert.Empty(diags);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test: Local type shadows everything (correct behavior)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A type parameter or nested type registered directly in the scope's LocalTypes
    /// should shadow any namespace-level types with the same name.
    /// </summary>
    [Fact]
    public void LocalType_ShadowsImportsAndCurrentNamespace_NoAmbiguity()
    {
        var (context, diagnostics) = CreateContext();

        // Register "T" in an imported namespace.
        _ = RegisterType(context.ProgramModel, "NsA", "T");

        // Create a scope with "T" as a local type (e.g., a type parameter).
        var scope = new Scope(context.GlobalScope) { Namespace = "Consumer" };
        scope.AddImportedNamespace("NsA");
        var localT = new TypeParameter("T", 0, scope, null);
        scope.AddLocalType("T", localT);

        var resolver = new TypeReferenceResolver(context);
        var result = resolver.ResolveNamedType("T", scope, MakeDummyNode());

        // Should resolve to the local type parameter, not the namespace type.
        var diags = diagnostics.ToImmutableArray();
        Assert.Same(localT, result);
        Assert.Empty(diags);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test: Single imported namespace — no ambiguity
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// When only one imported namespace contains the type, resolution should
    /// succeed without any ambiguity diagnostic.
    /// </summary>
    [Fact]
    public void SingleImportedNamespace_ResolvesCleanly()
    {
        var (context, diagnostics) = CreateContext();

        var foo = RegisterType(context.ProgramModel, "NsA", "Foo");
        _ = RegisterType(context.ProgramModel, "NsB", "Bar"); // different name

        var scope = new Scope(context.GlobalScope) { Namespace = "Consumer" };
        scope.AddImportedNamespace("NsA");
        scope.AddImportedNamespace("NsB");

        var resolver = new TypeReferenceResolver(context);
        var result = resolver.ResolveNamedType("Foo", scope, MakeDummyNode());

        var diags = diagnostics.ToImmutableArray();
        Assert.Same(foo, result);
        Assert.Empty(diags);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test: Imports at different scope levels accumulate — ambiguity detected
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Namespace imports do NOT shadow each other across scope levels — they
    /// accumulate into a flat pool. When an inner scope imports NsA (containing
    /// Foo) and an outer scope imports NsB (also containing Foo), both are
    /// visible and the result is ambiguous.
    ///
    /// This is unlike local types which DO shadow across levels.
    /// </summary>
    [Fact]
    public void ImportsAtDifferentScopeLevels_Accumulate_ReportsAmbiguity()
    {
        var (context, diagnostics) = CreateContext();

        _ = RegisterType(context.ProgramModel, "NsA", "Foo");
        _ = RegisterType(context.ProgramModel, "NsB", "Foo");

        // Outer scope imports NsB, inner scope imports NsA.
        var outerScope = new Scope(context.GlobalScope) { Namespace = "Consumer" };
        outerScope.AddImportedNamespace("NsB");

        var innerScope = new Scope(outerScope) { Namespace = "Consumer" };
        innerScope.AddImportedNamespace("NsA");

        var resolver = new TypeReferenceResolver(context);
        var result = resolver.ResolveNamedType("Foo", innerScope, MakeDummyNode());

        // Namespaces accumulate — both NsA and NsB are visible, so this is ambiguous.
        var diags = diagnostics.ToImmutableArray();
        Assert.True(
            result is ErrorType || diags.Any(d => d.Id.Value == "MIST_PM_AMBIGUOUS_TYPE"),
            "Expected ambiguity when imports at different scope levels both provide the same type name. " +
            $"Got {result.GetType().Name} with {diags.Length} diagnostics.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test: Global using + file using both have same type → ambiguity
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Simulates global usings (on the global scope) and file-level usings (on
    /// the file scope) both importing a namespace that contains the same type.
    /// Since namespace imports accumulate across all scope levels, this must
    /// be detected as ambiguous.
    /// </summary>
    [Fact]
    public void GlobalUsing_And_FileUsing_SameType_ReportsAmbiguity()
    {
        var (context, diagnostics) = CreateContext();

        _ = RegisterType(context.ProgramModel, "System", "Foo");
        _ = RegisterType(context.ProgramModel, "MyLib", "Foo");

        // Global scope imports System.
        context.GlobalScope.AddImportedNamespace("System");

        // File scope imports MyLib.
        var fileScope = new Scope(context.GlobalScope) { Namespace = "MyApp" };
        fileScope.AddImportedNamespace("MyLib");

        var resolver = new TypeReferenceResolver(context);
        var result = resolver.ResolveNamedType("Foo", fileScope, MakeDummyNode());

        var diags = diagnostics.ToImmutableArray();
        Assert.True(
            result is ErrorType || diags.Any(d => d.Id.Value == "MIST_PM_AMBIGUOUS_TYPE"),
            "Expected ambiguity when a global using and a file using both provide the same type name. " +
            $"Got {result.GetType().Name} with {diags.Length} diagnostics.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test: Three imported namespaces, two have same name → ambiguity
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Even when there are many imports, if exactly two (or more) of them at the
    /// same scope level contain the same type name, it should be ambiguous.
    /// </summary>
    [Fact]
    public void ThreeImports_TwoHaveSameName_ReportsAmbiguity()
    {
        var (context, diagnostics) = CreateContext();

        _ = RegisterType(context.ProgramModel, "NsA", "Foo");
        _ = RegisterType(context.ProgramModel, "NsB", "Bar");
        _ = RegisterType(context.ProgramModel, "NsC", "Foo");

        var scope = new Scope(context.GlobalScope) { Namespace = "Consumer" };
        scope.AddImportedNamespace("NsA");
        scope.AddImportedNamespace("NsB");
        scope.AddImportedNamespace("NsC");

        var resolver = new TypeReferenceResolver(context);
        var result = resolver.ResolveNamedType("Foo", scope, MakeDummyNode());

        var diags = diagnostics.ToImmutableArray();
        Assert.True(
            result is ErrorType || diags.Any(d => d.Id.Value == "MIST_PM_AMBIGUOUS_TYPE"),
            "Expected an ambiguity error when two of three imported namespaces have the same type name.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test: Nested qualified name ambiguity in LookupNestedQualifiedName
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// When a nested type key (e.g., "Outer.Inner") exists in two imported
    /// namespaces, resolving that name should produce an ambiguity error.
    /// This tests that the fix addresses LookupNestedQualifiedName too.
    ///
    /// Note: ResolveNamedType handles simple names; the nested/qualified path
    /// is exercised through ResolveNamed when dots are present. This test
    /// validates the simple-name path with a composite key registered in
    /// namespace dictionaries, confirming the same first-match-wins pattern
    /// exists for any namespace-level lookup.
    /// </summary>
    [Fact]
    public void TwoImportedNamespaces_SameNestedKey_ReportsAmbiguity()
    {
        var (context, diagnostics) = CreateContext();

        // Register a type with an arity name in two namespaces.
        // This simulates how generic types are registered (e.g., "List<>").
        _ = context.ProgramModel.GetOrAddNamespace("NsA");
        var scopeA = new Scope(null) { Namespace = "NsA" };
        var listA = new ClassType("List<>", "NsA", scopeA, null, null);
        _ = context.ProgramModel.RegisterType(listA);

        _ = context.ProgramModel.GetOrAddNamespace("NsB");
        var scopeB = new Scope(null) { Namespace = "NsB" };
        var listB = new ClassType("List<>", "NsB", scopeB, null, null);
        _ = context.ProgramModel.RegisterType(listB);

        var scope = new Scope(context.GlobalScope) { Namespace = "Consumer" };
        scope.AddImportedNamespace("NsA");
        scope.AddImportedNamespace("NsB");

        var resolver = new TypeReferenceResolver(context);
        var result = resolver.ResolveNamedType("List<>", scope, MakeDummyNode());

        var diags = diagnostics.ToImmutableArray();
        Assert.True(
            result is ErrorType || diags.Any(d => d.Id.Value == "MIST_PM_AMBIGUOUS_TYPE"),
            "Expected an ambiguity error when two imported namespaces have the same type (arity-bearing name).");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test: Local type at inner scope shadows namespace imports from all levels
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A local type (type parameter, nested type) at an inner scope level shadows
    /// all namespace imports regardless of scope level. This is the ONLY thing
    /// that shadows namespace imports.
    /// </summary>
    [Fact]
    public void LocalType_ShadowsAccumulatedNamespaceImports()
    {
        var (context, diagnostics) = CreateContext();

        _ = RegisterType(context.ProgramModel, "NsA", "T");
        _ = RegisterType(context.ProgramModel, "NsB", "T");

        // Global and file scopes both import namespaces with "T".
        context.GlobalScope.AddImportedNamespace("NsA");
        var fileScope = new Scope(context.GlobalScope) { Namespace = "Consumer" };
        fileScope.AddImportedNamespace("NsB");

        // Type scope has a local type parameter "T" — this shadows everything.
        var typeScope = new Scope(fileScope) { Namespace = "Consumer" };
        var localT = new TypeParameter("T", 0, typeScope, null);
        typeScope.AddLocalType("T", localT);

        var resolver = new TypeReferenceResolver(context);
        var result = resolver.ResolveNamedType("T", typeScope, MakeDummyNode());

        var diags = diagnostics.ToImmutableArray();
        Assert.Same(localT, result);
        Assert.Empty(diags);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test: Same type reference in both imports resolves to same object → no ambiguity
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// If the same type object is registered in two namespaces (e.g., type forwarding),
    /// it should not be considered ambiguous — it's the same type.
    /// </summary>
    [Fact]
    public void SameTypeObject_InTwoNamespaces_NoAmbiguity()
    {
        var (context, diagnostics) = CreateContext();

        // Register Foo in NsA. With FQN-keyed storage, type forwarding (same object
        // under two namespace keys) is no longer naturally representable. Instead, we
        // test the ReferenceEquals deduplication by importing the same namespace at
        // multiple scope levels — the resolver finds the same object twice and should
        // not report ambiguity.
        var scope = new Scope(null) { Namespace = "NsA" };
        var sharedFoo = new ClassType("Foo", "NsA", scope, null, null);

        _ = context.ProgramModel.GetOrAddNamespace("NsA");
        _ = context.ProgramModel.RegisterType(sharedFoo);

        // Import NsA from both outer and inner scope — resolver sees it twice.
        var outerScope = new Scope(context.GlobalScope) { Namespace = "Consumer" };
        outerScope.AddImportedNamespace("NsA");

        var innerScope = new Scope(outerScope) { Namespace = "Consumer" };
        innerScope.AddImportedNamespace("NsA");

        var resolver = new TypeReferenceResolver(context);
        var result = resolver.ResolveNamedType("Foo", innerScope, MakeDummyNode());

        // Same object found via duplicate imports — should resolve cleanly.
        var diags = diagnostics.ToImmutableArray();
        Assert.Same(sharedFoo, result);
        Assert.Empty(diags);
    }
}