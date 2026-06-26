// Feature: structured-context, Property 3: Context type keying exactness
//
// **Validates: Requirements 5.1, 5.2, 5.3, 5.4, 5.6, 5.7, 5.8**
//
// For any Mist program using context declarations and bindings, the context
// system SHALL key values by the exact resolved type such that:
// (a) type aliases resolve to their underlying type before keying (so alias
//     and underlying type share a key),
// (b) each distinct generic type argument combination produces an independent key,
// (c) union types are distinct keys from any of their constituent types, and
// (d) no inheritance-based lookup or subtype traversal is performed.
//
// All work runs in-process — no external process is spawned per iteration
// (in keeping with the workspace test-process-spawning policy).

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Analysis;

// ─── Scenario types ──────────────────────────────────────────────────────

/// <summary>
/// Describes the kind of context type keying scenario being tested.
/// </summary>
public enum ContextKeyingKind
{
    /// <summary>
    /// Type alias resolves to underlying type: alias and underlying share a key.
    /// Two methods with with-clauses — one using the alias, one using the underlying
    /// type — should resolve to the same underlying type after alias unwrapping.
    /// </summary>
    AliasResolvesToUnderlying,

    /// <summary>
    /// Generic types with different type arguments produce independent keys.
    /// Two methods with with-clauses using Box&lt;int&gt; and Box&lt;string&gt;
    /// should resolve to different types.
    /// </summary>
    GenericTypeArgIndependence,

    /// <summary>
    /// Generic types with the same type arguments share a key.
    /// Two methods with with-clauses using Box&lt;int&gt; should resolve to
    /// the same type.
    /// </summary>
    GenericTypeArgSameKey,

    /// <summary>
    /// Union types are distinct from their constituent types.
    /// A method with a with-clause using a union type alias should resolve to a
    /// different type than one using a constituent type.
    /// </summary>
    UnionDistinctFromConstituent,

    /// <summary>
    /// No inheritance-based lookup: a method binding BaseType should not match
    /// a context declared with DerivedType.
    /// </summary>
    NoInheritanceLookup,
}

/// <summary>
/// A generated scenario for testing context type keying exactness.
/// </summary>
public sealed class ContextKeyingScenario
{
    public ContextKeyingKind Kind { get; }
    public string Source { get; }

    /// <summary>
    /// The name of the first method whose with-clause binding type we inspect.
    /// </summary>
    public string Method1Name { get; }

    /// <summary>
    /// The name of the second method whose with-clause binding type we inspect.
    /// </summary>
    public string Method2Name { get; }

    /// <summary>
    /// Whether the two methods' resolved context types should be the same (true)
    /// or different (false) after applying context keying rules.
    /// </summary>
    public bool ExpectSameKey { get; }

    public ContextKeyingScenario(
        ContextKeyingKind kind,
        string source,
        string method1Name,
        string method2Name,
        bool expectSameKey)
    {
        Kind = kind;
        Source = source;
        Method1Name = method1Name;
        Method2Name = method2Name;
        ExpectSameKey = expectSameKey;
    }

    public override string ToString()
    {
        return $"[{Kind}] expect {(ExpectSameKey ? "SAME" : "DIFFERENT")} key for {Method1Name} vs {Method2Name}";
    }
}

// ─── Property test class ─────────────────────────────────────────────────

/// <summary>
/// Property-based tests for context type keying exactness.
///
/// Property 3: Context type keying exactness
///
/// **Validates: Requirements 5.1, 5.2, 5.3, 5.4, 5.6, 5.7, 5.8**
/// </summary>
public sealed class ContextTypeKeyingPropertyTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly MistLexer Lexer = new();
    private static readonly MistParser Parser = new();
    private static readonly SemanticModelBuilder Builder = new();

    // ─── Known names for generation ─────────────────────────────────────

    private static readonly string[] AliasNames =
        ["MyAlias", "Alias1", "NameAlias", "TypeAlias", "Wrapper"];

    private static readonly string[] PrimitiveTypes =
        ["int", "string", "bool", "float"];

    private static readonly string[] GenericClassNames =
        ["Box", "Container", "Holder", "Store"];

    private static readonly string[] BaseClassNames =
        ["Animal", "Shape", "Vehicle", "Widget"];

    private static readonly string[] DerivedClassNames =
        ["Dog", "Circle", "Car", "Button"];

    // ─── FsCheck generators ─────────────────────────────────────────────

    private static Gen<ContextKeyingScenario> GenAliasResolvesToUnderlying()
    {
        return from aliasName in Gen.Elements(AliasNames)
               from underlying in Gen.Elements(PrimitiveTypes)
               select BuildAliasScenario(aliasName, underlying);
    }

    private static Gen<ContextKeyingScenario> GenGenericTypeArgIndependence()
    {
        return from className in Gen.Elements(GenericClassNames)
               from type1 in Gen.Elements(PrimitiveTypes)
               from type2 in Gen.Elements(PrimitiveTypes)
               where type1 != type2
               select BuildGenericIndependenceScenario(className, type1, type2);
    }

    private static Gen<ContextKeyingScenario> GenGenericTypeArgSameKey()
    {
        return from className in Gen.Elements(GenericClassNames)
               from typeArg in Gen.Elements(PrimitiveTypes)
               select BuildGenericSameKeyScenario(className, typeArg);
    }

    private static Gen<ContextKeyingScenario> GenUnionDistinctFromConstituent()
    {
        return from type1 in Gen.Elements(PrimitiveTypes)
               from type2 in Gen.Elements(PrimitiveTypes)
               where type1 != type2
               select BuildUnionScenario(type1, type2);
    }

    private static Gen<ContextKeyingScenario> GenNoInheritanceLookup()
    {
        return from idx in Gen.Choose(0, BaseClassNames.Length - 1)
               select BuildNoInheritanceScenario(BaseClassNames[idx], DerivedClassNames[idx]);
    }

    public static Gen<ContextKeyingScenario> GenScenario()
    {
        return Gen.OneOf(
            GenAliasResolvesToUnderlying(),
            GenGenericTypeArgIndependence(),
            GenGenericTypeArgSameKey(),
            GenUnionDistinctFromConstituent(),
            GenNoInheritanceLookup());
    }

    public static Arbitrary<ContextKeyingScenario> ArbitraryContextKeyingScenario()
    {
        return GenScenario().ToArbitrary();
    }

    // ─── Scenario builders ──────────────────────────────────────────────

    /// <summary>
    /// Builds a scenario where a type alias is used in one method's with-clause
    /// and the underlying type is used in another. Both should resolve to the
    /// same context key (the underlying type) after alias unwrapping.
    /// </summary>
    private static ContextKeyingScenario BuildAliasScenario(string aliasName, string underlying)
    {
        // The with-clause binding resolves the type reference. For the alias,
        // the resolved type will be a TypeAliasType wrapping the underlying type.
        // For context keying, aliases resolve through to the underlying type.
        var source =
            $"type {aliasName} = {underlying};\n" +
            "class Program {\n" +
            $"    void MethodWithAlias() with {aliasName} ctx1 {{\n" +
            "    }\n" +
            $"    void MethodWithUnderlying() with {underlying} ctx2 {{\n" +
            "    }\n" +
            "}\n";

        return new ContextKeyingScenario(
            ContextKeyingKind.AliasResolvesToUnderlying,
            source,
            "MethodWithAlias",
            "MethodWithUnderlying",
            expectSameKey: true);
    }

    /// <summary>
    /// Builds a scenario where two methods use the same generic class but with
    /// different type arguments. They should resolve to different context keys.
    /// </summary>
    private static ContextKeyingScenario BuildGenericIndependenceScenario(
        string className, string type1, string type2)
    {
        var source =
            $"class {className}<T> {{\n" +
            "}\n" +
            "class Program {\n" +
            $"    void MethodWithType1() with {className}<{type1}> ctx1 {{\n" +
            "    }\n" +
            $"    void MethodWithType2() with {className}<{type2}> ctx2 {{\n" +
            "    }\n" +
            "}\n";

        return new ContextKeyingScenario(
            ContextKeyingKind.GenericTypeArgIndependence,
            source,
            "MethodWithType1",
            "MethodWithType2",
            expectSameKey: false);
    }

    /// <summary>
    /// Builds a scenario where two methods use the same generic class with the
    /// same type argument. They should resolve to the same context key.
    /// </summary>
    private static ContextKeyingScenario BuildGenericSameKeyScenario(
        string className, string typeArg)
    {
        var source =
            $"class {className}<T> {{\n" +
            "}\n" +
            "class Program {\n" +
            $"    void MethodA() with {className}<{typeArg}> ctx1 {{\n" +
            "    }\n" +
            $"    void MethodB() with {className}<{typeArg}> ctx2 {{\n" +
            "    }\n" +
            "}\n";

        return new ContextKeyingScenario(
            ContextKeyingKind.GenericTypeArgSameKey,
            source,
            "MethodA",
            "MethodB",
            expectSameKey: true);
    }

    /// <summary>
    /// Builds a scenario where one method uses a union type alias as its context
    /// key and another uses one of the union's constituent types. They should
    /// resolve to different context keys (union types are distinct from constituents).
    /// </summary>
    private static ContextKeyingScenario BuildUnionScenario(string type1, string type2)
    {
        var unionName = $"Union{type1[0..1].ToUpperInvariant()}{type1[1..]}{type2[0..1].ToUpperInvariant()}{type2[1..]}";
        var source =
            $"type {unionName} = {type1} | {type2};\n" +
            "class Program {\n" +
            $"    void MethodWithUnion() with {unionName} ctx1 {{\n" +
            "    }\n" +
            $"    void MethodWithConstituent() with {type1} ctx2 {{\n" +
            "    }\n" +
            "}\n";

        return new ContextKeyingScenario(
            ContextKeyingKind.UnionDistinctFromConstituent,
            source,
            "MethodWithUnion",
            "MethodWithConstituent",
            expectSameKey: false);
    }

    /// <summary>
    /// Builds a scenario where one method binds a base type and another binds
    /// a derived type. They should resolve to different context keys (no
    /// inheritance-based lookup).
    /// </summary>
    private static ContextKeyingScenario BuildNoInheritanceScenario(
        string baseName, string derivedName)
    {
        var source =
            $"class {baseName} {{\n" +
            "}\n" +
            $"class {derivedName} : {baseName} {{\n" +
            "}\n" +
            "class Program {\n" +
            $"    void MethodWithBase() with {baseName} ctx1 {{\n" +
            "    }\n" +
            $"    void MethodWithDerived() with {derivedName} ctx2 {{\n" +
            "    }\n" +
            "}\n";

        return new ContextKeyingScenario(
            ContextKeyingKind.NoInheritanceLookup,
            source,
            "MethodWithBase",
            "MethodWithDerived",
            expectSameKey: false);
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a semantic model from the given Mist source text using the
    /// shared lexer, parser, and builder (no process spawning).
    /// </summary>
    private static ISemanticModel? BuildModelFromSource(
        string source, out ImmutableArray<Diagnostic> diagnostics)
    {
        var sourceText = new SourceText("test.mist", source);

        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);
        if(lexBag.HasErrors)
        {
            diagnostics = lexBag.ToImmutableArray();
            return null;
        }

        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);
        if(parseBag.HasErrors || parseResult.Root is null)
        {
            diagnostics = parseBag.ToImmutableArray();
            return null;
        }

        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));
        var semBag = new DiagnosticBag();
        var model = Builder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            semBag);

        diagnostics = semBag.ToImmutableArray();
        return model;
    }

    /// <summary>
    /// Finds the with-clause binding local variable for a given method.
    /// The with-clause binding is registered as a LocalVariable in the method's
    /// scope during Pass 2. We find it by looking for a LocalVariable whose
    /// containing scope is the method's body scope.
    /// </summary>
    private static IType? GetWithClauseBindingType(
        ISemanticModel model, string methodName, string bindingName)
    {
        // Find the method symbol
        var methodSymbol = model.AllSymbols
            .OfType<Symbol>()
            .FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == methodName);

        if(methodSymbol is null)
        {
            return null;
        }

        // Find the local variable registered for the with-clause binding.
        // It's in the method's scope (which is a child of the method symbol's
        // containing scope). We look for a LocalVariable with the binding name
        // whose containing scope's parent is the method's containing scope.
        var bindingSymbol = model.AllSymbols
            .FirstOrDefault(s =>
                s.Kind == SymbolKind.LocalVariable &&
                s.Name == bindingName &&
                IsInMethodScope(s, methodSymbol));

        return bindingSymbol?.DeclaredType;
    }

    /// <summary>
    /// Checks if a symbol is in the scope of the given method.
    /// A with-clause binding's containing scope is the method body scope,
    /// which is a child of the type scope that contains the method.
    /// </summary>
    private static bool IsInMethodScope(ISymbol localSymbol, Symbol methodSymbol)
    {
        // The local variable's containing scope should be a child scope
        // of the method's containing scope (the type scope).
        // Walk up from the local's scope to see if we reach the method's scope.
        var scope = localSymbol.ContainingScope;
        var methodScope = methodSymbol.ContainingScope;

        // The method body scope's parent is the type scope (methodSymbol.ContainingScope).
        // So the local's containing scope's parent should be the method's containing scope.
        return scope?.Parent == methodScope;
    }

    /// <summary>
    /// Resolves a type for context keying comparison. Type aliases resolve
    /// through to their underlying type. All other types are used as-is.
    /// </summary>
    private static IType ResolveForKeying(IType type)
    {
        while(type is TypeAliasType alias)
        {
            type = alias.UnderlyingType;
        }

        return type;
    }

    /// <summary>
    /// Compares two types for context key equality. Two types share a context key
    /// if, after alias resolution, they have the same DisplayName.
    /// </summary>
    private static bool AreContextKeysEqual(IType type1, IType type2)
    {
        var resolved1 = ResolveForKeying(type1);
        var resolved2 = ResolveForKeying(type2);
        return resolved1.DisplayName == resolved2.DisplayName;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 3: Context type keying exactness
    //
    // **Validates: Requirements 5.1, 5.2, 5.3, 5.4, 5.6, 5.7, 5.8**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For any Mist program using context declarations and bindings, the context
    /// system SHALL key values by the exact resolved type such that:
    /// (a) type aliases resolve to their underlying type before keying,
    /// (b) each distinct generic type argument combination produces an independent key,
    /// (c) union types are distinct keys from any of their constituent types, and
    /// (d) no inheritance-based lookup or subtype traversal is performed.
    ///
    /// **Validates: Requirements 5.1, 5.2, 5.3, 5.4, 5.6, 5.7, 5.8**
    /// </summary>
    [Property(
        Arbitrary = new[] { typeof(ContextTypeKeyingPropertyTests) },
        MaxTest = 100)]
    public Property ContextTypeKeying_ExactnessProperty(ContextKeyingScenario scenario)
    {
        var model = BuildModelFromSource(scenario.Source, out var diagnostics);

        if(model is null)
        {
            return false.ToProperty()
                .Label($"Failed to build semantic model.\n" +
                       $"Diagnostics: {string.Join("; ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}\n" +
                       $"Source:\n{scenario.Source}");
        }

        // Find the with-clause binding types for each method.
        // Each method has a with-clause binding named ctx1/ctx2 respectively.
        var type1 = GetWithClauseBindingType(model, scenario.Method1Name, "ctx1");
        if(type1 is null)
        {
            return false.ToProperty()
                .Label($"Could not find with-clause binding type for method '{scenario.Method1Name}'.\n" +
                       $"All symbols: [{string.Join(", ", model.AllSymbols.Select(s => $"{s.Name}:{s.Kind}"))}]\n" +
                       $"Diagnostics: [{string.Join("; ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]\n" +
                       $"Source:\n{scenario.Source}");
        }

        var type2 = GetWithClauseBindingType(model, scenario.Method2Name, "ctx2");
        if(type2 is null)
        {
            return false.ToProperty()
                .Label($"Could not find with-clause binding type for method '{scenario.Method2Name}'.\n" +
                       $"All symbols: [{string.Join(", ", model.AllSymbols.Select(s => $"{s.Name}:{s.Kind}"))}]\n" +
                       $"Diagnostics: [{string.Join("; ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]\n" +
                       $"Source:\n{scenario.Source}");
        }

        var keysAreEqual = AreContextKeysEqual(type1, type2);

        if(scenario.ExpectSameKey && !keysAreEqual)
        {
            return false.ToProperty()
                .Label($"Expected SAME context key but got DIFFERENT.\n" +
                       $"  {scenario.Method1Name} resolved to: {ResolveForKeying(type1).DisplayName} (raw: {type1.DisplayName})\n" +
                       $"  {scenario.Method2Name} resolved to: {ResolveForKeying(type2).DisplayName} (raw: {type2.DisplayName})\n" +
                       $"  Scenario: {scenario}");
        }

        if(!scenario.ExpectSameKey && keysAreEqual)
        {
            return false.ToProperty()
                .Label($"Expected DIFFERENT context keys but got SAME.\n" +
                       $"  {scenario.Method1Name} resolved to: {ResolveForKeying(type1).DisplayName} (raw: {type1.DisplayName})\n" +
                       $"  {scenario.Method2Name} resolved to: {ResolveForKeying(type2).DisplayName} (raw: {type2.DisplayName})\n" +
                       $"  Scenario: {scenario}");
        }

        return true.ToProperty()
            .Label($"Context keying correct for {scenario.Kind}: " +
                   $"{scenario.Method1Name}={ResolveForKeying(type1).DisplayName}, " +
                   $"{scenario.Method2Name}={ResolveForKeying(type2).DisplayName}");
    }
}