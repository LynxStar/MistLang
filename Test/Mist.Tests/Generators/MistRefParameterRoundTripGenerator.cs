// Generators for Mist ref-annotated source strings.
// Produces random but structurally valid ref-annotated source text for property-based testing.
// Feature: ref-parameters, Property 1: Pretty Printer Round-Trip
// **Validates: Requirements 11.1, 11.2, 11.3, 11.4, 11.5, 11.6**

using FsCheck;
using FsCheck.Fluent;

namespace Mist.Tests.Generators;

/// <summary>
/// Metadata about a generated ref-annotated source string for round-trip testing.
/// Carries the source text along with a human-readable description.
/// </summary>
public sealed class RefParameterRoundTripScenario
{
    /// <summary>The complete ref-annotated Mist source string.</summary>
    public string Source { get; }

    /// <summary>Human-readable description of the scenario kind.</summary>
    public string Description { get; }

    public RefParameterRoundTripScenario(string source, string description)
    {
        Source = source;
        Description = description;
    }

    public override string ToString() => Description;
}

/// <summary>
/// FsCheck generators for Mist ref-annotated source strings.
/// Generates random valid programs covering:
/// <list type="bullet">
///   <item>Ref parameters on methods, constructors, indexers, operators, cast operators</item>
///   <item>Ref return types (ref and readonly ref) on methods</item>
///   <item>Ref locals (ref and readonly ref) with typed and inferred declarations</item>
///   <item>Ref arguments at call sites</item>
///   <item>Return ref statements</item>
/// </list>
/// </summary>
public static class MistRefParameterRoundTripGenerator
{
    // ─── Name pools ─────────────────────────────────────────────────────────

    private static readonly string[] TypeNames =
        ["Foo", "Bar", "Baz", "Widget", "Node"];

    private static readonly string[] VarNames =
        ["x", "y", "z", "item", "value"];

    private static readonly string[] FieldNames =
        ["data", "count", "total", "size"];

    private static readonly string[] MethodNames =
        ["Process", "Transform", "Compute", "Update", "Get"];

    private static readonly string[] ParamNames =
        ["input", "source", "target", "arg", "param"];

    private static readonly string[] PrimitiveTypes =
        ["int", "string", "bool", "double"];

    // ─── Helper generators ──────────────────────────────────────────────────

    private static Gen<string> GenTypeName() =>
        Gen.Elements(PrimitiveTypes);

    private static Gen<string> GenVarName() =>
        Gen.Elements(VarNames);

    private static Gen<string> GenParamName() =>
        Gen.Elements(ParamNames);

    private static Gen<string> GenMethodName() =>
        Gen.Elements(MethodNames);

    private static Gen<string> GenFieldName() =>
        Gen.Elements(FieldNames);

    private static Gen<string> GenClassName() =>
        Gen.Elements(TypeNames);

    // ─── Ref parameter generators ───────────────────────────────────────────

    /// <summary>
    /// Generates a method with a ref parameter:
    /// <c>void Process(ref int input) { }</c>
    /// </summary>
    private static Gen<RefParameterRoundTripScenario> GenRefParameterMethod()
    {
        return from methodName in GenMethodName()
               from typeName in GenTypeName()
               from paramName in GenParamName()
               let source = WrapInClass($"void {methodName}(ref {typeName} {paramName}) {{\n    }}")
               select new RefParameterRoundTripScenario(source,
                   $"ref-param: void {methodName}(ref {typeName} {paramName})");
    }

    /// <summary>
    /// Generates a method with multiple parameters, some ref:
    /// <c>void Process(ref int input, int other) { }</c>
    /// </summary>
    private static Gen<RefParameterRoundTripScenario> GenMultiParamWithRef()
    {
        return from methodName in GenMethodName()
               from type1 in GenTypeName()
               from type2 in GenTypeName()
               from param1 in Gen.Elements("first", "left")
               from param2 in Gen.Elements("second", "right")
               from firstIsRef in Gen.Elements(true, false)
               from secondIsRef in Gen.Elements(true, false)
               let p1 = firstIsRef ? $"ref {type1} {param1}" : $"{type1} {param1}"
               let p2 = secondIsRef ? $"ref {type2} {param2}" : $"{type2} {param2}"
               let source = WrapInClass($"void {methodName}({p1}, {p2}) {{\n    }}")
               select new RefParameterRoundTripScenario(source,
                   $"multi-param-ref: {p1}, {p2}");
    }

    /// <summary>
    /// Generates a constructor with a ref parameter:
    /// <c>Foo(ref int input) { }</c>
    /// </summary>
    private static Gen<RefParameterRoundTripScenario> GenRefParameterConstructor()
    {
        return from className in GenClassName()
               from typeName in GenTypeName()
               from paramName in GenParamName()
               let source = $"class {className} {{\n    {className}(ref {typeName} {paramName}) {{\n    }}\n}}"
               select new RefParameterRoundTripScenario(source,
                   $"ref-param-ctor: {className}(ref {typeName} {paramName})");
    }

    // ─── Ref return generators ──────────────────────────────────────────────

    /// <summary>
    /// Generates a method with a ref return type:
    /// <c>ref int Get() { return ref data; }</c>
    /// </summary>
    private static Gen<RefParameterRoundTripScenario> GenRefReturnMethod()
    {
        return from methodName in GenMethodName()
               from typeName in GenTypeName()
               from fieldName in GenFieldName()
               let source = WrapInClassWithField(typeName, fieldName,
                   $"ref {typeName} {methodName}() {{\n        return ref {fieldName};\n    }}")
               select new RefParameterRoundTripScenario(source,
                   $"ref-return: ref {typeName} {methodName}()");
    }

    /// <summary>
    /// Generates a method with a readonly ref return type:
    /// <c>readonly ref int Get() { return ref data; }</c>
    /// </summary>
    private static Gen<RefParameterRoundTripScenario> GenReadonlyRefReturnMethod()
    {
        return from methodName in GenMethodName()
               from typeName in GenTypeName()
               from fieldName in GenFieldName()
               let source = WrapInClassWithField(typeName, fieldName,
                   $"readonly ref {typeName} {methodName}() {{\n        return ref {fieldName};\n    }}")
               select new RefParameterRoundTripScenario(source,
                   $"readonly-ref-return: readonly ref {typeName} {methodName}()");
    }

    // ─── Ref local generators ───────────────────────────────────────────────

    /// <summary>
    /// Generates a typed ref local declaration:
    /// <c>ref int x = y;</c>
    /// </summary>
    private static Gen<RefParameterRoundTripScenario> GenRefLocalTyped()
    {
        return from typeName in GenTypeName()
               from varName in GenVarName()
               from initName in Gen.Elements("a", "b", "c")
               let stmt = $"ref {typeName} {varName} = {initName};"
               let source = WrapInClassMethodWithLocal(typeName, initName, stmt)
               select new RefParameterRoundTripScenario(source,
                   $"ref-local-typed: ref {typeName} {varName}");
    }

    /// <summary>
    /// Generates a readonly ref local declaration:
    /// <c>readonly ref int x = y;</c>
    /// </summary>
    private static Gen<RefParameterRoundTripScenario> GenReadonlyRefLocalTyped()
    {
        return from typeName in GenTypeName()
               from varName in GenVarName()
               from initName in Gen.Elements("a", "b", "c")
               let stmt = $"readonly ref {typeName} {varName} = {initName};"
               let source = WrapInClassMethodWithLocal(typeName, initName, stmt)
               select new RefParameterRoundTripScenario(source,
                   $"readonly-ref-local-typed: readonly ref {typeName} {varName}");
    }

    /// <summary>
    /// Generates an inferred ref local declaration:
    /// <c>ref var x = y;</c>
    /// </summary>
    private static Gen<RefParameterRoundTripScenario> GenRefLocalInferred()
    {
        return from varName in GenVarName()
               from initName in Gen.Elements("a", "b", "c")
               let stmt = $"ref var {varName} = {initName};"
               let source = WrapInClassMethodWithIntLocal(initName, stmt)
               select new RefParameterRoundTripScenario(source,
                   $"ref-local-inferred: ref var {varName}");
    }

    /// <summary>
    /// Generates an inferred readonly ref local declaration:
    /// <c>readonly ref var x = y;</c>
    /// </summary>
    private static Gen<RefParameterRoundTripScenario> GenReadonlyRefLocalInferred()
    {
        return from varName in GenVarName()
               from initName in Gen.Elements("a", "b", "c")
               let stmt = $"readonly ref var {varName} = {initName};"
               let source = WrapInClassMethodWithIntLocal(initName, stmt)
               select new RefParameterRoundTripScenario(source,
                   $"readonly-ref-local-inferred: readonly ref var {varName}");
    }

    // ─── Ref argument generators ────────────────────────────────────────────

    /// <summary>
    /// Generates a method call with ref arguments:
    /// <c>Process(ref x);</c>
    /// </summary>
    private static Gen<RefParameterRoundTripScenario> GenRefArgument()
    {
        return from methodName in GenMethodName()
               from typeName in GenTypeName()
               from paramName in GenParamName()
               from varName in GenVarName()
               let source = WrapInClassWithRefMethod(typeName, paramName, methodName,
                   $"{typeName} {varName} = 0;\n        {methodName}(ref {varName});")
               select new RefParameterRoundTripScenario(source,
                   $"ref-arg: {methodName}(ref {varName})");
    }

    /// <summary>
    /// Generates a method call with mixed ref and value arguments:
    /// <c>Process(ref x, y);</c>
    /// </summary>
    private static Gen<RefParameterRoundTripScenario> GenMixedRefArguments()
    {
        return from methodName in GenMethodName()
               from type1 in GenTypeName()
               from type2 in GenTypeName()
               from var1 in Gen.Elements("a", "b")
               from var2 in Gen.Elements("c", "d")
               let source = WrapInClassWithMixedMethod(type1, type2, methodName,
                   $"{type1} {var1} = 0;\n        {type2} {var2} = 0;\n        {methodName}(ref {var1}, {var2});")
               select new RefParameterRoundTripScenario(source,
                   $"mixed-ref-args: {methodName}(ref {var1}, {var2})");
    }

    // ─── Return ref generators ──────────────────────────────────────────────

    /// <summary>
    /// Generates a return ref statement in a ref-returning method:
    /// <c>ref int Get() { return ref field; }</c>
    /// </summary>
    private static Gen<RefParameterRoundTripScenario> GenReturnRefStatement()
    {
        return from methodName in GenMethodName()
               from typeName in GenTypeName()
               from fieldName in GenFieldName()
               let source = WrapInClassWithField(typeName, fieldName,
                   $"ref {typeName} {methodName}() {{\n        return ref {fieldName};\n    }}")
               select new RefParameterRoundTripScenario(source,
                   $"return-ref: return ref {fieldName}");
    }

    // ─── Abstract type ref method generators ───────────────────────────

    /// <summary>
    /// Generates an abstract type with a ref return method:
    /// <c>abstract IFoo { ref int Get(); }</c>
    /// </summary>
    private static Gen<RefParameterRoundTripScenario> GenAbstractRefMethod()
    {
        return from abstractName in Gen.Elements("IFoo", "IBar", "IService")
               from methodName in GenMethodName()
               from typeName in GenTypeName()
               from paramName in GenParamName()
               from hasRefParam in Gen.Elements(true, false)
               from hasRefReturn in Gen.Elements(true, false)
               let refReturn = hasRefReturn ? "ref " : ""
               let paramDecl = hasRefParam ? $"ref {typeName} {paramName}" : $"{typeName} {paramName}"
               let source = $"abstract {abstractName} {{\n    {refReturn}{typeName} {methodName}({paramDecl});\n}}"
               select new RefParameterRoundTripScenario(source,
                   $"abstract-ref: {abstractName}.{methodName}");
    }

    // ─── Combined generator ─────────────────────────────────────────────────

    /// <summary>
    /// Generates a random ref-annotated source scenario covering all variants.
    /// </summary>
    public static Gen<RefParameterRoundTripScenario> GenScenario()
    {
        return Gen.OneOf(
            GenRefParameterMethod(),
            GenMultiParamWithRef(),
            GenRefParameterConstructor(),
            GenRefReturnMethod(),
            GenReadonlyRefReturnMethod(),
            GenRefLocalTyped(),
            GenReadonlyRefLocalTyped(),
            GenRefLocalInferred(),
            GenReadonlyRefLocalInferred(),
            GenRefArgument(),
            GenMixedRefArguments(),
            GenReturnRefStatement(),
            GenAbstractRefMethod());
    }

    // ─── FsCheck Arbitrary registration ─────────────────────────────────────

    /// <summary>
    /// FsCheck Arbitrary for <see cref="RefParameterRoundTripScenario"/>.
    /// Register with <c>[Property(Arbitrary = new[] { typeof(MistRefParameterRoundTripGenerator) })]</c>.
    /// </summary>
    public static Arbitrary<RefParameterRoundTripScenario> ArbitraryRefParameterRoundTripScenario() =>
        GenScenario().ToArbitrary();

    // ─── Helpers ────────────────────────────────────────────────────────────

    /// <summary>Wraps a member declaration inside a class body.</summary>
    private static string WrapInClass(string member) =>
        $"class TestClass {{\n    {member}\n}}";

    /// <summary>Wraps a member declaration inside a class that also has a field.</summary>
    private static string WrapInClassWithField(string fieldType, string fieldName, string member) =>
        $"class TestClass {{\n    {fieldType} {fieldName};\n\n    {member}\n}}";

    /// <summary>Wraps a statement inside a class method, with a local variable of the given type.</summary>
    private static string WrapInClassMethodWithLocal(string typeName, string localName, string statement) =>
        $"class TestClass {{\n    void Run() {{\n        {typeName} {localName} = 0;\n        {statement}\n    }}\n}}";

    /// <summary>Wraps a statement inside a class method, with an int local variable.</summary>
    private static string WrapInClassMethodWithIntLocal(string localName, string statement) =>
        $"class TestClass {{\n    void Run() {{\n        int {localName} = 0;\n        {statement}\n    }}\n}}";

    /// <summary>Wraps a call statement inside a class that has a ref method.</summary>
    private static string WrapInClassWithRefMethod(string typeName, string paramName, string methodName, string callStatement) =>
        $"class TestClass {{\n    void {methodName}(ref {typeName} {paramName}) {{\n    }}\n\n    void Run() {{\n        {callStatement}\n    }}\n}}";

    /// <summary>Wraps a call statement inside a class that has a method with mixed ref/value params.</summary>
    private static string WrapInClassWithMixedMethod(string type1, string type2, string methodName, string callStatement) =>
        $"class TestClass {{\n    void {methodName}(ref {type1} first, {type2} second) {{\n    }}\n\n    void Run() {{\n        {callStatement}\n    }}\n}}";
}