// Feature: ownership-v2, Property 11: Round-trip parsing
// **Validates: Requirements 17.1, 17.2, 17.3**

using FsCheck;
using FsCheck.Fluent;

namespace Mist.Tests.Generators;

public sealed class OwnershipV2SourceScenario
{
    public string Source { get; }

    public string Description { get; }

    public OwnershipV2SourceScenario(string source, string description)
    {
        Source = source;
        Description = description;
    }

    public override string ToString()
    {
        return Description;
    }
}

/// Fields never carry the owned modifier, which v2 syntax does not have (Requirement 11).
public static class MistOwnershipV2Generator
{
    private static readonly string[] TypeNames =
        ["Foo", "Bar", "Baz", "Widget", "Node", "Item"];

    private static readonly string[] VarNames =
        ["x", "y", "z", "item", "node", "value"];

    private static readonly string[] FieldNames =
        ["Data", "Child", "Next", "Content", "Payload"];

    private static readonly string[] Modifiers =
        ["", "public ", "private "];

    public static Gen<OwnershipV2SourceScenario> GenOwnedClassDeclaration()
    {
        return from modifier in Gen.Elements(Modifiers)
               from className in Gen.Elements(TypeNames)
               from fieldName in Gen.Elements(FieldNames)
               let source =
                   $"{modifier}owned class {className}\n" +
                   "{\n" +
                   $"    public int {fieldName};\n" +
                   "}\n"
               select new OwnershipV2SourceScenario(
                   source, $"owned-class: {modifier}owned class {className}");
    }

    public static Gen<OwnershipV2SourceScenario> GenOwnedStructDeclaration()
    {
        return from modifier in Gen.Elements(Modifiers)
               from structName in Gen.Elements(TypeNames)
               from fieldName in Gen.Elements(FieldNames)
               let source =
                   $"{modifier}owned struct {structName}\n" +
                   "{\n" +
                   $"    public int {fieldName};\n" +
                   "}\n"
               select new OwnershipV2SourceScenario(
                   source, $"owned-struct: {modifier}owned struct {structName}");
    }

    public static Gen<OwnershipV2SourceScenario> GenOwnedInferredBinding()
    {
        return from typeName in Gen.Elements(TypeNames)
               from varName in Gen.Elements(VarNames)
               let source = WrapInClassMethod($"owned var {varName} = new {typeName}();")
               select new OwnershipV2SourceScenario(
                   source, $"owned-binding-inferred: owned var {varName} = new {typeName}()");
    }

    public static Gen<OwnershipV2SourceScenario> GenOwnedTypedBinding()
    {
        return from typeName in Gen.Elements(TypeNames)
               from varName in Gen.Elements(VarNames)
               let source = WrapInClassMethod($"owned {typeName} {varName} = new {typeName}();")
               select new OwnershipV2SourceScenario(
                   source, $"owned-binding-typed: owned {typeName} {varName} = new {typeName}()");
    }

    public static Gen<OwnershipV2SourceScenario> GenOwnedDefaultBinding()
    {
        return from typeName in Gen.Elements(TypeNames)
               from varName in Gen.Elements(VarNames)
               let source = WrapInClassMethod($"owned {typeName} {varName} = default;")
               select new OwnershipV2SourceScenario(
                   source, $"owned-binding-default: owned {typeName} {varName} = default");
    }

    public static Gen<OwnershipV2SourceScenario> GenIClonableImplementation()
    {
        return from className in Gen.Elements(TypeNames)
               from fieldName in Gen.Elements(FieldNames)
               let source =
                   $"public class {className} : IClonable<{className}>\n" +
                   "{\n" +
                   $"    public int {fieldName};\n" +
                   $"    public owned {className} Clone()\n" +
                   "    {\n" +
                   $"        return new {className}();\n" +
                   "    }\n" +
                   "}\n"
               select new OwnershipV2SourceScenario(
                   source, $"iclonable-impl: {className} : IClonable<{className}>");
    }

    public static Gen<OwnershipV2SourceScenario> GenOwnedIClonableImplementation()
    {
        return from className in Gen.Elements(TypeNames)
               from fieldName in Gen.Elements(FieldNames)
               let source =
                   $"public owned class {className} : IClonable<{className}>\n" +
                   "{\n" +
                   $"    public int {fieldName};\n" +
                   $"    public owned {className} Clone()\n" +
                   "    {\n" +
                   $"        return new {className}();\n" +
                   "    }\n" +
                   "}\n"
               select new OwnershipV2SourceScenario(
                   source, $"owned-iclonable-impl: owned {className} : IClonable<{className}>");
    }

    public static Gen<OwnershipV2SourceScenario> GenOwnershipV2Source()
    {
        return Gen.OneOf(
            GenOwnedClassDeclaration(),
            GenOwnedStructDeclaration(),
            GenOwnedInferredBinding(),
            GenOwnedTypedBinding(),
            GenOwnedDefaultBinding(),
            GenIClonableImplementation(),
            GenOwnedIClonableImplementation());
    }

    public static Arbitrary<OwnershipV2SourceScenario> ArbitraryOwnershipV2SourceScenario()
    {
        return GenOwnershipV2Source().ToArbitrary();
    }

    private static string WrapInClassMethod(string statement)
    {
        return "class TestClass\n" +
        "{\n" +
        "    void Run()\n" +
        "    {\n" +
        $"        {statement}\n" +
        "    }\n" +
        "}\n";
    }
}