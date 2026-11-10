// Feature: tuple-pattern-matching, Property 1: Pretty-print round-trip
// **Validates: Requirements 1.1, 1.3, 1.4, 5.1, 6.1, 6.2, 6.3**

using FsCheck;
using FsCheck.Fluent;

namespace Mist.Tests.Generators;

public sealed class TuplePositionalPatternSourceScenario
{
    public string Source { get; }

    public string Description { get; }

    public TuplePositionalPatternSourceScenario(string source, string description)
    {
        Source = source;
        Description = description;
    }

    public override string ToString()
    {
        return Description;
    }
}

public static class MistTuplePositionalPatternGenerator
{
    private static readonly string[] BindingNames = ["a", "b", "c", "d", "e", "f"];

    private static Gen<string> GenIntConstantPattern()
    {
        return Gen.Choose(0, 99).Select(n => n.ToString());
    }

    private static Gen<string> GenRelationalPattern()
    {
        return from op in Gen.Elements(">", "<", ">=", "<=")
               from value in Gen.Choose(0, 99)
               select $"{op} {value}";
    }

    private static Gen<string> GenLogicalAndPattern()
    {
        return from lo in Gen.Choose(0, 49)
               from hi in Gen.Choose(50, 99)
               select $">= {lo} and <= {hi}";
    }

    private static Gen<string> GenVarPattern()
    {
        return Gen.Elements(BindingNames).Select(name => $"var {name}");
    }

    private static Gen<string> GenDiscardPattern()
    {
        return Gen.Constant("_");
    }

    private static Gen<string> GenTypePattern()
    {
        return from typeName in Gen.Elements("int", "string")
               from bindName in Gen.Elements(BindingNames)
               select $"{typeName} {bindName}";
    }

    private static Gen<string> GenLeafPattern()
    {
        return Gen.OneOf(
            GenIntConstantPattern(),
            GenRelationalPattern(),
            GenLogicalAndPattern(),
            GenVarPattern(),
            GenDiscardPattern(),
            GenTypePattern());
    }

    private static Gen<string> GenSubpattern(int depth)
    {
        if(depth <= 0)
        {
            return GenLeafPattern();
        }

        return Gen.Frequency(
            (4, GenLeafPattern()),
            (1, GenTuplePatternSource(depth - 1)));
    }

    private static Gen<string> GenTuplePatternSource(int depth)
    {
        var arity2 = from p1 in GenSubpattern(depth)
                     from p2 in GenSubpattern(depth)
                     select $"({p1}, {p2})";

        var arity3 = from p1 in GenSubpattern(depth)
                     from p2 in GenSubpattern(depth)
                     from p3 in GenSubpattern(depth)
                     select $"({p1}, {p2}, {p3})";

        var arity4 = from p1 in GenSubpattern(depth)
                     from p2 in GenSubpattern(depth)
                     from p3 in GenSubpattern(depth)
                     from p4 in GenSubpattern(depth)
                     select $"({p1}, {p2}, {p3}, {p4})";

        var arity5 = from p1 in GenSubpattern(depth)
                     from p2 in GenSubpattern(depth)
                     from p3 in GenSubpattern(depth)
                     from p4 in GenSubpattern(depth)
                     from p5 in GenSubpattern(depth)
                     select $"({p1}, {p2}, {p3}, {p4}, {p5})";

        return Gen.Frequency(
            (4, arity2),
            (3, arity3),
            (2, arity4),
            (1, arity5));
    }

    public static Gen<TuplePositionalPatternSourceScenario> GenTuplePatternIsExpression()
    {
        return from depth in Gen.Choose(0, 2)
               from pattern in GenTuplePatternSource(depth)
               let scrutinee = GenerateMatchingScrutinee(pattern)
               let body = $"var x = {scrutinee};\n    if (x is {pattern})\n    {{\n    }}"
               let source = WrapInProgram(body)
               select new TuplePositionalPatternSourceScenario(source, $"is expr depth={depth}: {pattern}");
    }

    public static Gen<TuplePositionalPatternSourceScenario> GenNestedTuplePatternIsExpression()
    {
        return from depth in Gen.Choose(1, 2)
               from pattern in GenTuplePatternSource(depth)
               let scrutinee = GenerateMatchingScrutinee(pattern)
               let body = $"var x = {scrutinee};\n    if (x is {pattern})\n    {{\n    }}"
               let source = WrapInProgram(body)
               select new TuplePositionalPatternSourceScenario(source, $"nested is expr depth={depth}: {pattern}");
    }

    public static Gen<TuplePositionalPatternSourceScenario> GenTuplePositionalPatternSource()
    {
        return Gen.OneOf(
            GenTuplePatternIsExpression(),
            GenNestedTuplePatternIsExpression());
    }

    /// Round-trip only needs the source to parse, so integer literals of matching arity suffice whatever the subpatterns are.
    private static string GenerateMatchingScrutinee(string pattern)
    {
        var arity = CountTopLevelArity(pattern);
        var elements = Enumerable.Range(1, arity).Select(i => i.ToString());
        return $"({string.Join(", ", elements)})";
    }

    private static int CountTopLevelArity(string pattern)
    {
        if(pattern.Length < 2 || pattern[0] != '(')
        {
            return 1;
        }

        var depth = 0;
        var commas = 0;
        for(var i = 1; i < pattern.Length - 1; i++)
        {
            switch(pattern[i])
            {
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    break;
                case ',' when depth == 0:
                    commas++;
                    break;
            }
        }

        return commas + 1;
    }

    private static string WrapInProgram(string body)
    {
        return "namespace TestNs;\n\npublic class Program\n{\n    public static void Main()\n    {\n    "
            + body + "\n    }\n}";
    }

    public static Arbitrary<TuplePositionalPatternSourceScenario> ArbitraryTuplePositionalPatternSourceScenario()
    {
        return GenTuplePositionalPatternSource().ToArbitrary();
    }
}