// Tier 4: Boolean pattern lowering crosses the emitter, Roslyn, and runtime without losing predicates.

using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Mist.Backends.CSharpTranspiler;
using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Backend;

namespace Mist.Tests.Integration;

public sealed class PredicatePatternRuntimeBoundaryTests
{
    private static readonly IMistType Text = PropertyPatternEmissionTests.Type("String", "Standard");
    private static readonly IMistType Single = PropertyPatternEmissionTests.Type("Single", "Standard");
    private static readonly IMistType Double = PropertyPatternEmissionTests.Type("Double", "Standard");
    private static readonly IMistType Boolean = PropertyPatternEmissionTests.Type("Boolean", "Standard");
    private static readonly IMistType Integer = PropertyPatternEmissionTests.Type("Int32", "Standard");

    [Fact]
    public void ModelToEmitterToRoslynToRuntime_PreservesComposedPredicatesAndNumericPromotion()
    {
        var letters = Regex("[a-z]+");
        var notLetters = new NegatedPattern(null!, Text, letters);
        var either = new DisjunctionPattern(null!, Text, [letters, Regex("\\\\d+")]);
        var shortCircuit = new DisjunctionPattern(null!, Text,
            [new ComparisonPattern(null!, Text, ComparisonKind.Equal, new LiteralExpression(null!, "abc", Text)), Regex("[")]);
        var refined = new RefinedType(Text, null!, "Words") { Predicate = either };
        var binding = new LocalVariable("word", refined);
        var typed = new TypeTestPattern(null!, refined, binding, refinement: either);
        var bindingGuard = PropertyPatternEmissionTests.Binary(Test(Reference("value", Text), typed), BinaryOperatorKind.LogicalAnd,
            PropertyPatternEmissionTests.Binary(new MemberAccessExpression(null!, new LocalReferenceExpression(null!, binding), "Length", Integer),
                BinaryOperatorKind.GreaterThan, PropertyPatternEmissionTests.Number(1), Boolean), Boolean);
        var switched = new SwitchExpression(null!, Reference("value", Text),
            [new SwitchArm(null!, typed, null, new MemberAccessExpression(null!, new LocalReferenceExpression(null!, binding), "Length", Integer)),
             new SwitchArm(null!, new CatchAllPattern(null!, Text, null), null, PropertyPatternEmissionTests.Number(-1))], Integer);
        var dog = PropertyPatternEmissionTests.Type("Dog");
        var property = new PropertyPattern(null!, dog,
            [new PropertySubpattern(null!, "Name", either, Text, new NotFoundResult())],
            typeTest: new TypeTestPattern(null!, dog, null));
        var tupleType = new TupleType([Text, Integer]);
        var tuple = new TuplePattern(null!, [notLetters, new ComparisonPattern(null!, Integer, ComparisonKind.Equal,
            PropertyPatternEmissionTests.Number(1))], tupleType, null, null);
        var arrayType = PropertyPatternEmissionTests.Type("StringArray");
        var list = new ListPattern(null!, [new ListPatternElement(either, false)], arrayType);
        var negatedProperty = new NegatedPattern(null!, dog, property);
        var union = new UnionType([Text, dog]);
        var unionTyped = new TypeTestPattern(null!, refined, binding, Text, either);
        var unionSwitch = new SwitchExpression(null!, Reference("value", union),
            [new SwitchArm(null!, unionTyped, null, new MemberAccessExpression(null!, new LocalReferenceExpression(null!, binding), "Length", Integer)),
             new SwitchArm(null!, new CatchAllPattern(null!, union, null), null, PropertyPatternEmissionTests.Number(-1))], Integer);
        var next = new InvocationExpression(null!, Reference("next", new FunctionType([], Text)), [], Text, null, []);
        var comparisons = EmitSequence(Enum.GetValues<ComparisonKind>().Select(kind =>
            Test(Reference("value", Single), new ComparisonPattern(null!, Single, kind, new LiteralExpression(null!, 0.1, Double)))));
        var floatBound = new ComparisonPattern(null!, Single, ComparisonKind.LessThanOrEqual, new LiteralExpression(null!, 0.1, Double));
        var floatRefined = new RefinedType(Single, null!, "Tenth") { Predicate = floatBound };
        var floatBinding = new LocalVariable("number", floatRefined);
        var floatTyped = new TypeTestPattern(null!, floatRefined, floatBinding, refinement: floatBound);
        var floatNot = new NegatedPattern(null!, Single, floatBound);
        var floatEither = new DisjunctionPattern(null!, Single,
            [floatBound, new ComparisonPattern(null!, Single, ComparisonKind.GreaterThan, new LiteralExpression(null!, 1.0f, Single))]);
        var scoped = Scoped(typed, binding);
        var source = $$"""
            using Int32 = System.Int32;
            using String = System.String;
            using Single = System.Single;
            using Double = System.Double;
            public sealed class Dog
            {
                public static int Reads;
                private readonly string name;
                public Dog(string name) { this.name = name; }
                public string Name { get { Reads++; return name; } }
            }
            public sealed class Carrier
            {
                public Carrier(string value) { AsStringOrNull = value; }
                public string AsStringOrNull { get; }
            }
            public static class Probe
            {
                private static bool NotLetters(string value) { return {{Emit(Test(Reference("value", Text), notLetters))}}; }
                private static bool Either(string value) { return {{Emit(Test(Reference("value", Text), either))}}; }
                private static bool ShortCircuit(string value) { return {{Emit(Test(Reference("value", Text), shortCircuit))}}; }
                private static bool Bound(string value) { return {{Emit(bindingGuard)}}; }
                private static int Switched(string value) { return {{Emit(switched)}}; }
                private static bool Property(object value) { return {{Emit(Test(Reference("value", dog), property))}}; }
                private static bool NegatedProperty(object value) { return {{Emit(Test(Reference("value", dog), negatedProperty))}}; }
                private static bool Tuple((string, int) value) { return {{Emit(Test(Reference("value", tupleType), tuple))}}; }
                private static bool List(string[] value) { return {{Emit(Test(Reference("value", arrayType), list))}}; }
                private static bool Union(Carrier value) { return {{Emit(Test(Reference("value", union), unionTyped))}}; }
                private static int UnionSwitch(Carrier value) { return {{Emit(unionSwitch)}}; }
                private static bool Once(System.Func<string> next) { return {{Emit(Test(next, either))}}; }
                private static bool Commented(string value) { return {{Emit(Test(Reference("value", Text), Regex("(?x)abc #c")))}}; }
                private static int Scoped(string value) { {{scoped}} }
                private static bool TypedFloat(object value) { return {{Emit(Test(Reference("value", Single), floatTyped))}}; }
                private static bool NotFloat(float value) { return {{Emit(Test(Reference("value", Single), floatNot))}}; }
                private static bool EitherFloat(float value) { return {{Emit(Test(Reference("value", Single), floatEither))}}; }
                private static bool[] Floats(float value) { return [{{comparisons}}]; }
                private static bool[] Oracle(float value) { return [value == 0.1, value < 0.1, value > 0.1, value <= 0.1, value >= 0.1]; }
                public static int[] Run()
                {
                    var results = new System.Collections.Generic.List<int>();
                    results.Add(NotLetters("123") ? 1 : 0);
                    results.Add(NotLetters("abc") ? 1 : 0);
                    results.Add(Either("abc") ? 1 : 0);
                    results.Add(Either("123") ? 1 : 0);
                    results.Add(Either("abc1") ? 1 : 0);
                    results.Add(Either("abc\n") ? 1 : 0);
                    results.Add(ShortCircuit("abc") ? 1 : 0);
                    results.Add(Bound("abc") ? 1 : 0);
                    results.Add(Bound("a") ? 1 : 0);
                    results.Add(Switched("123"));
                    results.Add(Switched("?"));
                    Dog.Reads = 0;
                    results.Add(Property(null) ? 1 : 0);
                    results.Add(Property(new object()) ? 1 : 0);
                    results.Add(Dog.Reads);
                    results.Add(Property(new Dog("abc")) ? 1 : 0);
                    results.Add(Property(new Dog("?")) ? 1 : 0);
                    results.Add(Dog.Reads);
                    results.Add(NegatedProperty(new object()) ? 1 : 0);
                    results.Add(NegatedProperty(new Dog("abc")) ? 1 : 0);
                    results.Add(Tuple(("123", 1)) ? 1 : 0);
                    results.Add(Tuple(("abc", 1)) ? 1 : 0);
                    results.Add(List(["123"]) ? 1 : 0);
                    results.Add(List(["?"]) ? 1 : 0);
                    results.Add(Union(new Carrier("abc")) ? 1 : 0);
                    results.Add(UnionSwitch(new Carrier("123")));
                    results.Add(UnionSwitch(new Carrier("?")));
                    var calls = 0;
                    results.Add(Once(() => { calls++; return "123"; }) ? 1 : 0);
                    results.Add(calls);
                    results.Add(Scoped("abc"));
                    results.Add(Scoped("?"));
                    foreach(var value in new[] { 0.1f, 0.0f, -0.0f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                    {
                        var actual = Floats(value);
                        var oracle = Oracle(value);
                        for(var index = 0; index < actual.Length; index++)
                        {
                            results.Add(actual[index] == oracle[index] ? 1 : 0);
                        }
                    }
                    results.Add(Floats(0.1f)[3] ? 1 : 0);
                    results.Add(TypedFloat(0.0f) ? 1 : 0);
                    results.Add(TypedFloat(0.1f) ? 1 : 0);
                    results.Add(TypedFloat(0.0) ? 1 : 0);
                    results.Add(NotFloat(0.1f) ? 1 : 0);
                    results.Add(EitherFloat(0.1f) ? 1 : 0);
                    results.Add(EitherFloat(2.0f) ? 1 : 0);
                    results.Add(Commented("abc") ? 1 : 0);
                    results.Add(Commented("abc\n") ? 1 : 0);
                    return results.ToArray();
                }
            }
            """;
        var compilation = CSharpCompilation.Create("PredicatePatternRuntime_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview))],
            CSharpCompilationDriver.CoreReferences, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics.Select(diagnostic => diagnostic.ToString())));
        Assert.DoesNotContain(emitted.Diagnostics, diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Warning);
        stream.Position = 0;
        var context = new AssemblyLoadContext("PredicatePatternRuntime", isCollectible: true);

        try
        {
            var assembly = context.LoadFromStream(stream);
            var results = Assert.IsType<int[]>(assembly.GetType("Probe")!.GetMethod("Run")!.Invoke(null, null));
            int[] expected = [1, 0, 1, 1, 0, 0, 1, 1, 0, 3, -1, 0, 0, 0, 1, 0, 2, 1, 0, 1, 0, 1, 0, 1, 3, -1, 1, 1, 3, -1];
            Assert.Equal(expected.Concat(Enumerable.Repeat(1, 30)).Concat([0, 1, 0, 0, 1, 0, 1, 1, 0]), results);
        }
        finally
        {
            context.Unload();
        }
    }

    private static string Scoped(TypeTestPattern pattern, LocalVariable binding)
    {
        var later = new LocalVariable("word", Integer);
        var body = new Block(null!)
        {
            Statements = [new IfStatement(Test(Reference("value", Text), pattern),
                new Block(null!) { Statements = [new ReturnStatement(new MemberAccessExpression(null!, new LocalReferenceExpression(null!, binding), "Length", Integer), Integer)] },
                null, conditionBinds: true),
                new LocalDeclarationStatement(later, PropertyPatternEmissionTests.Number(-1)),
                new ReturnStatement(new LocalReferenceExpression(null!, later), Integer)],
        };
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();
        BodyEmitter.EmitBlock(body, ["value"], writer, diagnostics);
        Assert.False(diagnostics.HasErrors);
        return writer.ToString();
    }

    private static IsPatternExpression Test(Expression value, Pattern pattern)
    {
        return new IsPatternExpression(null!, value, pattern, Boolean);
    }

    private static LocalReferenceExpression Reference(string name, IMistType type)
    {
        return PropertyPatternEmissionTests.Reference(name, type);
    }

    private static RegexPattern Regex(string pattern)
    {
        return new RegexPattern(null!, Text, pattern);
    }

    private static string EmitSequence(IEnumerable<Expression> expressions)
    {
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();
        BodyEmitter.ResetTemporaries();
        var first = true;

        foreach(var expression in expressions)
        {
            if(!first)
            {
                writer.Append(", ");
            }

            BodyEmitter.EmitExpression(expression, writer, diagnostics);
            first = false;
        }

        Assert.False(diagnostics.HasErrors);
        return writer.ToString();
    }

    private static string Emit(Expression expression)
    {
        return PropertyPatternEmissionTests.Emit(expression);
    }
}