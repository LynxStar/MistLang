// Feature: newtypes, Property 8: Pretty-printer round-trip
// **Validates: Requirements 4.1, 4.2**

using FsCheck;
using FsCheck.Fluent;

namespace Mist.Tests.Generators;

public sealed class NewtypeSourceScenario
{
    public string Source { get; }

    public string NewtypeName { get; }

    public string SourceTypeText { get; }

    public string Description { get; }

    public NewtypeSourceScenario(
        string source,
        string newtypeName,
        string sourceTypeText,
        string description)
    {
        Source = source;
        NewtypeName = newtypeName;
        SourceTypeText = sourceTypeText;
        Description = description;
    }

    public override string ToString()
    {
        return Description;
    }
}

public static class MistNewtypeGenerator
{
    private static readonly string[] NewtypeNames =
        ["UserId", "OrderId", "Email", "Amount", "Score", "Weight", "Height", "Age", "Count", "Index"];

    private static readonly string[] PrimitiveTypes =
        ["int", "string", "bool", "float", "double", "long", "char", "byte", "decimal", "short"];

    private static readonly string[] NamedTypes =
        ["EntityBase", "Point", "Vector", "Color", "Config", "Settings", "Record", "Entry"];

    public static Gen<NewtypeSourceScenario> GenPrimitiveNewtype()
    {
        return from name in Gen.Elements(NewtypeNames)
               from sourceType in Gen.Elements(PrimitiveTypes)
               let source = $"type {name} = new {sourceType};"
               select new NewtypeSourceScenario(
                   source: source,
                   newtypeName: name,
                   sourceTypeText: sourceType,
                   description: $"primitive: {source}");
    }

    public static Gen<NewtypeSourceScenario> GenNamedNewtype()
    {
        return from name in Gen.Elements(NewtypeNames)
               from sourceType in Gen.Elements(NamedTypes)
               let source = $"type {name} = new {sourceType};"
               select new NewtypeSourceScenario(
                   source: source,
                   newtypeName: name,
                   sourceTypeText: sourceType,
                   description: $"named: {source}");
    }

    public static Gen<NewtypeSourceScenario> GenNewtypeSource()
    {
        return Gen.OneOf(
            GenPrimitiveNewtype(),
            GenNamedNewtype());
    }

    public static Arbitrary<NewtypeSourceScenario> ArbitraryNewtypeSourceScenario()
    {
        return GenNewtypeSource().ToArbitrary();
    }
}