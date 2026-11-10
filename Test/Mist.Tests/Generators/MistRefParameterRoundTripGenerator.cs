// Feature: ref-parameters, Property 1: Pretty Printer Round-Trip
// **Validates: Requirements 11.1, 11.2, 11.3, 11.4, 11.5, 11.6**

using FsCheck;
using FsCheck.Fluent;

namespace Mist.Tests.Generators;

public sealed class RefParameterRoundTripScenario
{
    public string Source { get; }

    public string Description { get; }

    public RefParameterRoundTripScenario(string source, string description)
    {
        Source = source;
        Description = description;
    }

    public override string ToString()
    {
        return Description;
    }
}

public static class MistRefParameterRoundTripGenerator
{
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

    private static Gen<string> GenTypeName()
    {
        return Gen.Elements(PrimitiveTypes);
    }

    private static Gen<string> GenVarName()
    {
        return Gen.Elements(VarNames);
    }

    private static Gen<string> GenParamName()
    {
        return Gen.Elements(ParamNames);
    }

    private static Gen<string> GenMethodName()
    {
        return Gen.Elements(MethodNames);
    }

    private static Gen<string> GenFieldName()
    {
        return Gen.Elements(FieldNames);
    }

    private static Gen<string> GenClassName()
    {
        return Gen.Elements(TypeNames);
    }

    private static Gen<RefParameterRoundTripScenario> GenRefParameterMethod()
    {
        return from methodName in GenMethodName()
               from typeName in GenTypeName()
               from paramName in GenParamName()
               let source = WrapInClass($"void {methodName}(ref {typeName} {paramName}) {{\n    }}")
               select new RefParameterRoundTripScenario(source,
                   $"ref-param: void {methodName}(ref {typeName} {paramName})");
    }

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

    private static Gen<RefParameterRoundTripScenario> GenRefParameterConstructor()
    {
        return from className in GenClassName()
               from typeName in GenTypeName()
               from paramName in GenParamName()
               let source = $"class {className} {{\n    {className}(ref {typeName} {paramName}) {{\n    }}\n}}"
               select new RefParameterRoundTripScenario(source,
                   $"ref-param-ctor: {className}(ref {typeName} {paramName})");
    }

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

    private static Gen<RefParameterRoundTripScenario> GenRefLocalInferred()
    {
        return from varName in GenVarName()
               from initName in Gen.Elements("a", "b", "c")
               let stmt = $"ref var {varName} = {initName};"
               let source = WrapInClassMethodWithIntLocal(initName, stmt)
               select new RefParameterRoundTripScenario(source,
                   $"ref-local-inferred: ref var {varName}");
    }

    private static Gen<RefParameterRoundTripScenario> GenReadonlyRefLocalInferred()
    {
        return from varName in GenVarName()
               from initName in Gen.Elements("a", "b", "c")
               let stmt = $"readonly ref var {varName} = {initName};"
               let source = WrapInClassMethodWithIntLocal(initName, stmt)
               select new RefParameterRoundTripScenario(source,
                   $"readonly-ref-local-inferred: readonly ref var {varName}");
    }

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

    public static Arbitrary<RefParameterRoundTripScenario> ArbitraryRefParameterRoundTripScenario()
    {
        return GenScenario().ToArbitrary();
    }

    private static string WrapInClass(string member)
    {
        return $"class TestClass {{\n    {member}\n}}";
    }

    private static string WrapInClassWithField(string fieldType, string fieldName, string member)
    {
        return $"class TestClass {{\n    {fieldType} {fieldName};\n\n    {member}\n}}";
    }

    private static string WrapInClassMethodWithLocal(string typeName, string localName, string statement)
    {
        return $"class TestClass {{\n    void Run() {{\n        {typeName} {localName} = 0;\n        {statement}\n    }}\n}}";
    }

    private static string WrapInClassMethodWithIntLocal(string localName, string statement)
    {
        return $"class TestClass {{\n    void Run() {{\n        int {localName} = 0;\n        {statement}\n    }}\n}}";
    }

    private static string WrapInClassWithRefMethod(string typeName, string paramName, string methodName, string callStatement)
    {
        return $"class TestClass {{\n    void {methodName}(ref {typeName} {paramName}) {{\n    }}\n\n    void Run() {{\n        {callStatement}\n    }}\n}}";
    }

    private static string WrapInClassWithMixedMethod(string type1, string type2, string methodName, string callStatement)
    {
        return $"class TestClass {{\n    void {methodName}(ref {type1} first, {type2} second) {{\n    }}\n\n    void Run() {{\n        {callStatement}\n    }}\n}}";
    }
}