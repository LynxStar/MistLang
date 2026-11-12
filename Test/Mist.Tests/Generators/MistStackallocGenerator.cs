// Feature: stack-only-types, Property 2: Stackalloc parsing structure and round-trip
// **Validates: Requirements 3.2, 3.6**

using FsCheck;
using FsCheck.Fluent;

namespace Mist.Tests.Generators;

public sealed class StackallocSourceScenario
{
    public string Source { get; }

    public string Description { get; }

    public StackallocSourceScenario(string source, string description)
    {
        Source = source;
        Description = description;
    }

    public override string ToString()
    {
        return Description;
    }
}

/// Programs only need to parse, so runtime sizes may name undeclared identifiers.
public static class MistStackallocGenerator
{
    private static readonly string[] ElementTypes =
        ["int", "bool", "char", "float", "double", "long", "byte", "short", "uint"];

    private static readonly string[] LiteralSizes =
        ["0", "1", "2", "3", "4", "8", "16", "42", "100"];

    private static readonly string[] RuntimeSizes =
        ["n", "count", "len", "n + 1", "count * 2"];

    private static Gen<string> GenElementType()
    {
        return Gen.Elements(ElementTypes);
    }

    private static Gen<string> GenSize()
    {
        return Gen.OneOf(Gen.Elements(LiteralSizes), Gen.Elements(RuntimeSizes));
    }

    private static Gen<string> GenStackallocExpr()
    {
        return from type in GenElementType()
               from size in GenSize()
               select $"stackalloc {type}[{size}]";
    }

    private static Gen<StackallocSourceScenario> GenLocalInitializer()
    {
        return from expr in GenStackallocExpr()
               let body = $"var s = {expr};"
               select new StackallocSourceScenario(Wrap(body), $"local-initializer: {expr}");
    }

    private static Gen<StackallocSourceScenario> GenCallArgument()
    {
        return from expr in GenStackallocExpr()
               let body = $"Consume({expr});"
               select new StackallocSourceScenario(Wrap(body), $"call-argument: {expr}");
    }

    private static Gen<StackallocSourceScenario> GenAssignmentRhs()
    {
        return from expr in GenStackallocExpr()
               let body = $"var dst = seed;\n        dst = {expr};"
               select new StackallocSourceScenario(Wrap(body), $"assignment-rhs: {expr}");
    }

    public static Gen<StackallocSourceScenario> GenStackallocSource()
    {
        return Gen.OneOf(
            GenLocalInitializer(),
            GenCallArgument(),
            GenAssignmentRhs());
    }

    private static string Wrap(string body)
    {
        return "namespace TestNs;\n\npublic class Program\n{\n    public static void Run(int seed)\n    {\n        "
        + body + "\n    }\n}";
    }

    public static Arbitrary<StackallocSourceScenario> ArbitraryStackallocSourceScenario()
    {
        return GenStackallocSource().ToArbitrary();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Feature: stack-only-types, Property 3: Stackalloc missing/empty size is
    // rejected and recovered. **Validates: Requirements 3.4, 3.5**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Element-type references exercised by the missing/empty-size scenarios. Includes
    /// primitives, simple and qualified identifiers, and generic forms so the parser's
    /// recovery scan is exercised across the full shape of an <c>ElementTypeRef</c>.
    /// </summary>
    private static readonly string[] MalformedElementTypes =
        ["int", "bool", "char", "Foo", "Bar", "My.Ns.Widget", "List<int>", "Dictionary<int, string>"];

    private static Gen<string> GenMalformedElementType()
    {
        return Gen.Elements(MalformedElementTypes);
    }

    private static Gen<(string Expr, string Kind)> GenMalformedStackallocExpr()
    {
        return from type in GenMalformedElementType()
               from emptyBrackets in Gen.Elements(false, true)
               select emptyBrackets
                   ? ($"stackalloc {type}[]", "empty-brackets")
                   : ($"stackalloc {type}", "no-brackets");
    }

    private static Gen<StackallocSourceScenario> GenMalformedLocalInitializer()
    {
        return from m in GenMalformedStackallocExpr()
               let body = $"var s = {m.Expr};"
               select new StackallocSourceScenario(Wrap(body), $"{m.Kind} local-initializer: {m.Expr}");
    }

    private static Gen<StackallocSourceScenario> GenMalformedCallArgument()
    {
        return from m in GenMalformedStackallocExpr()
               let body = $"Consume({m.Expr});"
               select new StackallocSourceScenario(Wrap(body), $"{m.Kind} call-argument: {m.Expr}");
    }

    private static Gen<StackallocSourceScenario> GenMalformedAssignmentRhs()
    {
        return from m in GenMalformedStackallocExpr()
               let body = $"var dst = seed;\n        dst = {m.Expr};"
               select new StackallocSourceScenario(Wrap(body), $"{m.Kind} assignment-rhs: {m.Expr}");
    }

    public static Gen<StackallocSourceScenario> GenMissingOrEmptySizeSource()
    {
        return Gen.OneOf(
            GenMalformedLocalInitializer(),
            GenMalformedCallArgument(),
            GenMalformedAssignmentRhs());
    }
}