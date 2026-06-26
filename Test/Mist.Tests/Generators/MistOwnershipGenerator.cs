// Generators for Mist ownership-annotated declaration source strings.
// Produces random but structurally valid ownership-annotated source text for property-based testing.
// Feature: ownership-system, Property 1: Ownership-annotated round-trip parsing
// **Validates: Requirements 20.1, 20.2, 20.3, 2.1, 2.2, 2.3, 2.4, 2.5**

using FsCheck;
using FsCheck.Fluent;

namespace Mist.Tests.Generators;

/// <summary>
/// Metadata about a generated ownership-annotated source string for round-trip testing.
/// Carries the source text along with a human-readable description.
/// </summary>
public sealed class OwnershipSourceScenario
{
    /// <summary>The complete ownership-annotated declaration source string.</summary>
    public string Source { get; }

    /// <summary>Human-readable description of the scenario kind.</summary>
    public string Description { get; }

    public OwnershipSourceScenario(string source, string description)
    {
        Source = source;
        Description = description;
    }

    public override string ToString() => Description;
}

/// <summary>
/// FsCheck generators for Mist ownership-annotated declaration source strings.
/// Generates random valid declarations covering:
/// <list type="bullet">
///   <item>Ownership specifiers: owned, borrowed, readonly ref, frozen</item>
///   <item>Variable declarations with ownership specifiers</item>
///   <item>Parameters with ownership specifiers</item>
///   <item>Method return types with ownership specifiers (owned only)</item>
///   <item>Field declarations with ownership specifiers</item>
///   <item>Owned type declarations (owned class, owned struct)</item>
/// </list>
/// </summary>
public static class MistOwnershipGenerator
{
    // ─── Name pools ─────────────────────────────────────────────────────────

    private static readonly string[] TypeNames =
        ["Foo", "Bar", "Baz", "Widget", "Node", "Item"];

    private static readonly string[] VarNames =
        ["x", "y", "z", "item", "node", "value"];

    private static readonly string[] FieldNames =
        ["data", "child", "next", "content", "payload"];

    private static readonly string[] MethodNames =
        ["Process", "Transform", "Create", "Build", "Get"];

    private static readonly string[] ParamNames =
        ["input", "source", "target", "arg", "param"];

    private static readonly string[] ReferenceTypes =
        ["Foo", "Bar", "Widget", "Node"];

    private static readonly string[] Modifiers =
        ["", "public ", "private "];

    // ─── Ownership specifier generators ─────────────────────────────────────

    /// <summary>Generates a random ownership specifier string.</summary>
    public static Gen<string> GenOwnershipSpecifier() =>
        Gen.Elements("owned ", "borrowed ", "readonly ref ", "frozen ");

    /// <summary>Generates an optional ownership specifier (may be empty).</summary>
    public static Gen<string> GenOptionalOwnershipSpecifier() =>
        Gen.Elements("", "owned ", "borrowed ", "readonly ref ", "frozen ");

    // ─── Variable declaration generators ────────────────────────────────────

    /// <summary>
    /// Generates a typed variable declaration with an ownership specifier:
    /// <c>owned Foo x = new Foo();</c>
    /// </summary>
    public static Gen<OwnershipSourceScenario> GenOwnershipAnnotatedVariableDeclaration()
    {
        return from specifier in GenOwnershipSpecifier()
               from typeName in Gen.Elements(ReferenceTypes)
               from varName in Gen.Elements(VarNames)
               let source = WrapInClassMethod($"{specifier}{typeName} {varName} = new {typeName}();")
               select new OwnershipSourceScenario(source, $"var-decl: {specifier}{typeName} {varName}");
    }

    /// <summary>
    /// Generates an inferred variable declaration with an ownership specifier:
    /// <c>owned var x = new Foo();</c>
    /// </summary>
    public static Gen<OwnershipSourceScenario> GenOwnershipAnnotatedInferredVariableDeclaration()
    {
        return from specifier in GenOwnershipSpecifier()
               from typeName in Gen.Elements(ReferenceTypes)
               from varName in Gen.Elements(VarNames)
               let source = WrapInClassMethod($"{specifier}var {varName} = new {typeName}();")
               select new OwnershipSourceScenario(source, $"var-inferred-decl: {specifier}var {varName}");
    }

    // ─── Parameter declaration generators ───────────────────────────────────

    /// <summary>
    /// Generates a method with ownership-annotated parameters:
    /// <c>void Process(owned Foo input) { }</c>
    /// </summary>
    public static Gen<OwnershipSourceScenario> GenOwnershipAnnotatedParameterDeclaration()
    {
        return from specifier in GenOwnershipSpecifier()
               from typeName in Gen.Elements(ReferenceTypes)
               from paramName in Gen.Elements(ParamNames)
               from methodName in Gen.Elements(MethodNames)
               let source = WrapInClass($"void {methodName}({specifier}{typeName} {paramName}) {{\n    }}")
               select new OwnershipSourceScenario(source, $"param: {specifier}{typeName} {paramName}");
    }

    /// <summary>
    /// Generates a method with multiple parameters, some with ownership specifiers:
    /// <c>void Process(owned Foo input, readonly ref Bar source) { }</c>
    /// </summary>
    public static Gen<OwnershipSourceScenario> GenMultiParamOwnershipDeclaration()
    {
        return from spec1 in GenOwnershipSpecifier()
               from spec2 in GenOwnershipSpecifier()
               from type1 in Gen.Elements(ReferenceTypes)
               from type2 in Gen.Elements(ReferenceTypes)
               from methodName in Gen.Elements(MethodNames)
               let source = WrapInClass($"void {methodName}({spec1}{type1} first, {spec2}{type2} second) {{\n    }}")
               select new OwnershipSourceScenario(source, $"multi-param: {spec1}{type1}, {spec2}{type2}");
    }

    // ─── Method return type generators ──────────────────────────────────────

    /// <summary>
    /// Generates a method with an owned return type:
    /// <c>owned Foo Create() { }</c>
    /// </summary>
    public static Gen<OwnershipSourceScenario> GenOwnedReturnTypeDeclaration()
    {
        return from typeName in Gen.Elements(ReferenceTypes)
               from methodName in Gen.Elements(MethodNames)
               let source = WrapInClass($"owned {typeName} {methodName}() {{\n        return new {typeName}();\n    }}")
               select new OwnershipSourceScenario(source, $"return-type: owned {typeName} {methodName}()");
    }

    // ─── Field declaration generators ───────────────────────────────────────

    /// <summary>
    /// Generates a field declaration with an ownership specifier:
    /// <c>owned Foo data;</c>
    /// </summary>
    public static Gen<OwnershipSourceScenario> GenOwnershipAnnotatedFieldDeclaration()
    {
        return from specifier in GenOwnershipSpecifier()
               from typeName in Gen.Elements(ReferenceTypes)
               from fieldName in Gen.Elements(FieldNames)
               from modifier in Gen.Elements(Modifiers)
               let source = WrapInClass($"{modifier}{specifier}{typeName} {fieldName};")
               select new OwnershipSourceScenario(source, $"field: {modifier}{specifier}{typeName} {fieldName}");
    }

    // ─── Owned type declaration generators ──────────────────────────────────

    /// <summary>
    /// Generates an owned class declaration:
    /// <c>owned class Foo { owned Bar data; }</c>
    /// </summary>
    public static Gen<OwnershipSourceScenario> GenOwnedClassDeclaration()
    {
        return from modifier in Gen.Elements(Modifiers)
               from className in Gen.Elements(TypeNames)
               from fieldType in Gen.Elements(ReferenceTypes)
               from fieldName in Gen.Elements(FieldNames)
               let source = $"{modifier}owned class {className} {{\n    owned {fieldType} {fieldName};\n}}"
               select new OwnershipSourceScenario(source, $"owned-class: {modifier}owned class {className}");
    }

    /// <summary>
    /// Generates an owned struct declaration:
    /// <c>owned struct Widget { owned Node child; }</c>
    /// </summary>
    public static Gen<OwnershipSourceScenario> GenOwnedStructDeclaration()
    {
        return from modifier in Gen.Elements(Modifiers)
               from structName in Gen.Elements(TypeNames)
               from fieldType in Gen.Elements(ReferenceTypes)
               from fieldName in Gen.Elements(FieldNames)
               let source = $"{modifier}owned struct {structName} {{\n    owned {fieldType} {fieldName};\n}}"
               select new OwnershipSourceScenario(source, $"owned-struct: {modifier}owned struct {structName}");
    }

    // ─── Combined generators ────────────────────────────────────────────────

    /// <summary>
    /// Generates a random ownership-annotated source scenario covering all variants.
    /// </summary>
    public static Gen<OwnershipSourceScenario> GenOwnershipSource()
    {
        return Gen.OneOf(
            GenOwnershipAnnotatedVariableDeclaration(),
            GenOwnershipAnnotatedInferredVariableDeclaration(),
            GenOwnershipAnnotatedParameterDeclaration(),
            GenMultiParamOwnershipDeclaration(),
            GenOwnedReturnTypeDeclaration(),
            GenOwnershipAnnotatedFieldDeclaration(),
            GenOwnedClassDeclaration(),
            GenOwnedStructDeclaration());
    }

    // ─── FsCheck Arbitrary registration ─────────────────────────────────────

    /// <summary>
    /// FsCheck Arbitrary for <see cref="OwnershipSourceScenario"/>.
    /// Register with <c>[Property(Arbitrary = new[] { typeof(MistOwnershipGenerator) })]</c>.
    /// </summary>
    public static Arbitrary<OwnershipSourceScenario> ArbitraryOwnershipSourceScenario() =>
        GenOwnershipSource().ToArbitrary();

    // ─── Helpers ────────────────────────────────────────────────────────────

    /// <summary>Wraps a statement inside a class with a method body.</summary>
    private static string WrapInClassMethod(string statement) =>
        $"class TestClass {{\n    void Run() {{\n        {statement}\n    }}\n}}";

    /// <summary>Wraps a member declaration inside a class body.</summary>
    private static string WrapInClass(string member) =>
        $"class TestClass {{\n    {member}\n}}";
}