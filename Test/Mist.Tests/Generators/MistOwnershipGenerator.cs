// Feature: ownership-system, Property 1: Ownership-annotated round-trip parsing
// **Validates: Requirements 20.1, 20.2, 20.3, 2.1, 2.2, 2.3, 2.4, 2.5**

using FsCheck;
using FsCheck.Fluent;

namespace Mist.Tests.Generators;

public sealed class OwnershipSourceScenario
{
    public string Source { get; }

    public string Description { get; }

    public OwnershipSourceScenario(string source, string description)
    {
        Source = source;
        Description = description;
    }

    public override string ToString()
    {
        return Description;
    }
}

public static class MistOwnershipGenerator
{
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

    public static Gen<string> GenOwnershipSpecifier()
    {
        return Gen.Elements("owned ", "borrowed ", "readonly ref ", "frozen ");
    }

    public static Gen<string> GenOptionalOwnershipSpecifier()
    {
        return Gen.Elements("", "owned ", "borrowed ", "readonly ref ", "frozen ");
    }

    public static Gen<OwnershipSourceScenario> GenOwnershipAnnotatedVariableDeclaration()
    {
        return from specifier in GenOwnershipSpecifier()
               from typeName in Gen.Elements(ReferenceTypes)
               from varName in Gen.Elements(VarNames)
               let source = WrapInClassMethod($"{specifier}{typeName} {varName} = new {typeName}();")
               select new OwnershipSourceScenario(source, $"var-decl: {specifier}{typeName} {varName}");
    }

    public static Gen<OwnershipSourceScenario> GenOwnershipAnnotatedInferredVariableDeclaration()
    {
        return from specifier in GenOwnershipSpecifier()
               from typeName in Gen.Elements(ReferenceTypes)
               from varName in Gen.Elements(VarNames)
               let source = WrapInClassMethod($"{specifier}var {varName} = new {typeName}();")
               select new OwnershipSourceScenario(source, $"var-inferred-decl: {specifier}var {varName}");
    }

    public static Gen<OwnershipSourceScenario> GenOwnershipAnnotatedParameterDeclaration()
    {
        return from specifier in GenOwnershipSpecifier()
               from typeName in Gen.Elements(ReferenceTypes)
               from paramName in Gen.Elements(ParamNames)
               from methodName in Gen.Elements(MethodNames)
               let source = WrapInClass($"void {methodName}({specifier}{typeName} {paramName}) {{\n    }}")
               select new OwnershipSourceScenario(source, $"param: {specifier}{typeName} {paramName}");
    }

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

    public static Gen<OwnershipSourceScenario> GenOwnedReturnTypeDeclaration()
    {
        return from typeName in Gen.Elements(ReferenceTypes)
               from methodName in Gen.Elements(MethodNames)
               let source = WrapInClass($"owned {typeName} {methodName}() {{\n        return new {typeName}();\n    }}")
               select new OwnershipSourceScenario(source, $"return-type: owned {typeName} {methodName}()");
    }

    public static Gen<OwnershipSourceScenario> GenOwnershipAnnotatedFieldDeclaration()
    {
        return from specifier in GenOwnershipSpecifier()
               from typeName in Gen.Elements(ReferenceTypes)
               from fieldName in Gen.Elements(FieldNames)
               from modifier in Gen.Elements(Modifiers)
               let source = WrapInClass($"{modifier}{specifier}{typeName} {fieldName};")
               select new OwnershipSourceScenario(source, $"field: {modifier}{specifier}{typeName} {fieldName}");
    }

    public static Gen<OwnershipSourceScenario> GenOwnedClassDeclaration()
    {
        return from modifier in Gen.Elements(Modifiers)
               from className in Gen.Elements(TypeNames)
               from fieldType in Gen.Elements(ReferenceTypes)
               from fieldName in Gen.Elements(FieldNames)
               let source = $"{modifier}owned class {className} {{\n    owned {fieldType} {fieldName};\n}}"
               select new OwnershipSourceScenario(source, $"owned-class: {modifier}owned class {className}");
    }

    public static Gen<OwnershipSourceScenario> GenOwnedStructDeclaration()
    {
        return from modifier in Gen.Elements(Modifiers)
               from structName in Gen.Elements(TypeNames)
               from fieldType in Gen.Elements(ReferenceTypes)
               from fieldName in Gen.Elements(FieldNames)
               let source = $"{modifier}owned struct {structName} {{\n    owned {fieldType} {fieldName};\n}}"
               select new OwnershipSourceScenario(source, $"owned-struct: {modifier}owned struct {structName}");
    }

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

    public static Arbitrary<OwnershipSourceScenario> ArbitraryOwnershipSourceScenario()
    {
        return GenOwnershipSource().ToArbitrary();
    }

    private static string WrapInClassMethod(string statement)
    {
        return $"class TestClass {{\n    void Run() {{\n        {statement}\n    }}\n}}";
    }

    private static string WrapInClass(string member)
    {
        return $"class TestClass {{\n    {member}\n}}";
    }
}