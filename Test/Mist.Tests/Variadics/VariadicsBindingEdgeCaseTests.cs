// Feature: variadics - Unit tests for binding edge cases (Task 7.4)
// Requirements: 4.5, 4.6
//
// These dedicated unit tests exercise the shared flat-to-bracketed binding rule on the
// specific edge cases called out by the design, complementing the broader task 7.1 smoke
// tests (VariadicsBindingRuleSmokeTests) and the task 7.2/7.3 property tests. Each test
// drives program-model construction entirely in-process via ProgramModelBuilder.Build
// (parse -> build program model -> inspect the interned ConstructedType resolved for a
// field). The binding rule lives in PackBinder, invoked from TypeReferenceResolver's
// pack-aware resolution path, which lifts each pack slot's ordered element list into a
// comptime Type[] (a LiftedType whose Value is an IMistType[]). There are ZERO external
// process spawns: the builder runs directly and emit is never invoked.
//
// Because the design's worked examples use the primitive keyword spellings (`int`,
// `string`, `bool`, `double`, `float`), and those keywords resolve through the Standard
// namespace (for example `int` looks up Standard.Int32), a minimal Standard-primitive
// prelude compilation unit is prepended so the keywords resolve in-process without loading
// the full standard library. The prelude declares only the empty carrier types the binding
// rule needs as opaque operands; the rule under test is exercised for real. The smoke test
// deliberately used source classes A/B/C/D instead; this file uses the design's exact
// spelling to lock the requirement's precise examples.

using System.Collections.Immutable;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Variadics;

/// <summary>
/// Binding-rule edge-case unit tests (Requirements 4.5, 4.6). Each builds the program model
/// in-process and asserts on the constructed variadic type resolved for a field:
/// <list type="bullet">
///   <item>the design's arity-5 construction
///     <c>Foo&lt;int, [a, b], x, y, z, [c], float&gt;</c> binds fixed scalars from the ends,
///     the two bracketed <c>Type[]:</c> packs from their bracketed groups, and the single
///     unbracketed <c>params</c> pack from the contiguous middle (4.5);</item>
///   <item>the design's <c>Between&lt;TFirst, params TMid, TLast&gt;</c> example at
///     <c>Between&lt;int, string, bool, double&gt;</c> binds <c>TFirst = int</c>,
///     <c>TMid = [string, bool]</c>, <c>TLast = double</c> (4.5);</item>
///   <item>a flat argument list too short to fill the fixed and bracketed slots raises
///     <c>MIST_VAR_0005</c>, using a case distinct from the smoke test (4.6).</item>
/// </list>
/// </summary>
public sealed class VariadicsBindingEdgeCaseTests
{
    private const string TooFewArgumentsId = "MIST_VAR_0005";

    /// <summary>
    /// A minimal Standard-namespace prelude declaring only the primitive carrier types the
    /// design's worked examples name. A file-scoped <c>namespace Standard;</c> registers each
    /// type at its absolute FQN (for example <c>Standard.Int32</c>), which is exactly where
    /// the primitive keyword aliases resolve (<c>int</c> -&gt; <c>Standard.Int32</c>,
    /// <c>string</c> -&gt; <c>Standard.String</c>, <c>bool</c> -&gt; <c>Standard.Boolean</c>,
    /// <c>double</c> -&gt; <c>Standard.Double</c>, <c>float</c> -&gt; <c>Standard.Single</c>).
    /// The binding rule treats these as opaque named operands, so empty carriers suffice.
    /// </summary>
    private const string StandardPrimitivesPrelude =
        """
        namespace Standard;

        public class Int32 { }
        public class String { }
        public class Boolean { }
        public class Double { }
        public class Single { }
        """;

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 4.5: the design's arity-5 middle-and-bracketed construction.
    //   class Foo<TVal, Type[]:TArgs, params CtorArgs, Type[]:Args3, TRes>
    //   Foo<int, [a, b], x, y, z, [c], float>
    //   -> TVal = int, TArgs = [a, b], CtorArgs = [x, y, z], Args3 = [c], TRes = float
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ArityFiveConstruction_BindsFixedScalarsBracketedPacksAndMiddlePack()
    {
        var (model, diagnostics) = BuildModel(
            """
            namespace TestProject;

            public class a { }
            public class b { }
            public class c { }
            public class x { }
            public class y { }
            public class z { }

            public class Foo<TVal, Type[]:TArgs, params CtorArgs, Type[]:Args3, TRes> { }

            public class Holder
            {
                public Foo<int, [a, b], x, y, z, [c], float> Value;
            }
            """);

        AssertNoErrors(diagnostics);

        var holder = FindConcreteType(model, "Holder");

        // The construction folds seven flat use-site arguments into the five declared slots:
        // the fixed ends bind inward, the two bracketed packs take their bracketed groups, and
        // the single unbracketed pack (CtorArgs) absorbs the contiguous middle x, y, z.
        var foo = AssertConstructed(holder, "Value", argumentCount: 5);

        // TVal (fixed) binds int from the left end.
        Assert.Equal("Int32", foo.TypeArguments[0].Name);

        // TArgs (bracketed Type[]:) binds its bracketed group [a, b], in order.
        AssertPackElements(foo.TypeArguments[1], "a", "b");

        // CtorArgs (the lone params pack) absorbs the contiguous scalar middle [x, y, z].
        AssertPackElements(foo.TypeArguments[2], "x", "y", "z");

        // Args3 (bracketed Type[]:) binds its bracketed group [c].
        AssertPackElements(foo.TypeArguments[3], "c");

        // TRes (fixed) binds float from the right end (the Single carrier).
        Assert.Equal("Single", foo.TypeArguments[4].Name);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 4.5: the design's Between example, exact spelling.
    //   class Between<TFirst, params TMid, TLast>
    //   Between<int, string, bool, double>
    //   -> TFirst = int, TMid = [string, bool], TLast = double
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void BetweenExample_BindsFirstAndLastInward_AndMiddlePackInOrder()
    {
        var (model, diagnostics) = BuildModel(
            """
            namespace TestProject;

            public class Between<TFirst, params TMid, TLast> { }

            public class Holder
            {
                public Between<int, string, bool, double> Value;
            }
            """);

        AssertNoErrors(diagnostics);

        var holder = FindConcreteType(model, "Holder");

        // Requirement 4.5 verbatim: TFirst binds the left end (int), TLast the right end
        // (double), and the pack TMid absorbs the contiguous middle [string, bool], in order.
        var between = AssertConstructed(holder, "Value", argumentCount: 3);
        Assert.Equal("Int32", between.TypeArguments[0].Name);
        AssertPackElements(between.TypeArguments[1], "String", "Boolean");
        Assert.Equal("Double", between.TypeArguments[2].Name);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 4.6: too few arguments to fill the fixed and bracketed slots.
    // Distinct from the smoke test's Between<A> case: here the arity-5 family (two
    // bracketed Type[]: packs plus two fixed slots plus one params pack) is supplied
    // three arguments, one short of the four fixed-and-bracketed slots that must bind
    // while the params pack absorbs zero.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ArityFiveFamily_TooFewArgumentsForFixedAndBracketedSlots_EmitsMistVar0005()
    {
        var (_, diagnostics) = BuildModel(
            """
            namespace TestProject;

            public class A { }
            public class B { }
            public class C { }
            public class D { }

            public class Foo<TVal, Type[]:TArgs, params CtorArgs, Type[]:Args3, TRes> { }

            public class Holder
            {
                public Foo<A, [B, C], D> TooFew;
            }
            """);

        // Foo has four fixed-and-bracketed slots (TVal, TArgs, Args3, TRes) that must bind
        // while CtorArgs absorbs zero, but only three arguments are supplied (A, [B, C], D),
        // so binding fails with MIST_VAR_0005.
        var tooFew = Assert.Single(diagnostics, d => d.Id.Value == TooFewArgumentsId);
        Assert.Equal(DiagnosticSeverity.Error, tooFew.Severity);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    private static void AssertNoErrors(ImmutableArray<Diagnostic> diagnostics)
    {
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(
            errors.Count == 0,
            $"Unexpected errors: {string.Join("; ", errors.Select(d => $"{d.Id.Value} {d.Message}"))}");
    }

    /// <summary>
    /// Asserts the named field of <paramref name="type"/> resolves to a constructed type with
    /// the expected number of type arguments and returns it for per-slot inspection.
    /// </summary>
    private static ConstructedType AssertConstructed(ConcreteType type, string fieldName, int argumentCount)
    {
        var constructed = Assert.IsType<ConstructedType>(FieldType(type, fieldName));
        Assert.Equal(argumentCount, constructed.TypeArguments.Count);
        return constructed;
    }

    /// <summary>
    /// Asserts a pack slot argument is a lifted comptime <c>Type[]</c> whose element type
    /// names match <paramref name="expectedElementNames"/> in order (empty means the empty
    /// pack).
    /// </summary>
    private static void AssertPackElements(IMistType packArgument, params string[] expectedElementNames)
    {
        var lifted = Assert.IsType<LiftedType>(packArgument);
        var elements = Assert.IsType<IMistType[]>(lifted.Value);
        Assert.Equal(expectedElementNames, elements.Select(e => e.Name).ToArray());
    }

    private static IMistType FieldType(ConcreteType type, string fieldName)
    {
        var field = type.FieldsList.Single(f => f.Name == fieldName);
        return field.Type;
    }

    private static ConcreteType FindConcreteType(IProgramModel model, string simpleName)
    {
        var matches = model.TypesByFQN.Values
            .OfType<ConcreteType>()
            .Where(t => t.Origin == TypeOrigin.Source && StripArity(t.Name) == simpleName)
            .ToList();

        _ = Assert.Single(matches);
        return matches[0];
    }

    private static string StripArity(string typeName)
    {
        var angle = typeName.IndexOf('<', StringComparison.Ordinal);
        return angle < 0 ? typeName : typeName[..angle];
    }

    /// <summary>
    /// Parses the Standard-primitive prelude and <paramref name="source"/> and builds the
    /// program model entirely in-process, returning the model together with every diagnostic
    /// collected during construction. The prelude and the user source are supplied as two
    /// index-aligned compilation units so the primitive keyword aliases resolve.
    /// </summary>
    private static (IProgramModel Model, ImmutableArray<Diagnostic> Diagnostics) BuildModel(string source)
    {
        var lexer = new MistLexer();
        var parser = new MistParser();
        var parseDiagnostics = new DiagnosticBag();

        var preludeSource = new SourceText("Standard.mist", StandardPrimitivesPrelude);
        var userSource = new SourceText("test.mist", source);

        var preludeRoot = ParseUnit(lexer, parser, preludeSource, parseDiagnostics);
        var userRoot = ParseUnit(lexer, parser, userSource, parseDiagnostics);

        var parseErrors = parseDiagnostics.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(
            parseErrors.Count == 0,
            $"Unexpected parse errors: {string.Join("; ", parseErrors.Select(d => $"{d.Id.Value} {d.Message}"))}");

        var compilation = new MistCompilation(ImmutableArray.Create(
            new CompilationUnit(preludeSource),
            new CompilationUnit(userSource)));

        var options = new CompilerOptions(
            ProjectFolder: ".",
            SourcePaths: ImmutableArray.Create("Standard.mist", "test.mist"),
            Backend: new BackendId("csharp"),
            OutputDirectory: ".",
            BinDirectory: "bin",
            ProjectName: "TestProject",
            NamespaceRoot: "TestProject",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty);

        var buildDiagnostics = new DiagnosticBag();
        var model = ProgramModelBuilder.Build(
            compilation,
            ImmutableArray.Create(preludeRoot, userRoot),
            options,
            buildDiagnostics,
            NullAmbientTypeProvider.Instance);

        Assert.NotNull(model);
        return (model!, buildDiagnostics.ToImmutableArray());
    }

    private static Storm.Runtime.SyntaxNode ParseUnit(
        MistLexer lexer, MistParser parser, SourceText source, DiagnosticBag diagnostics)
    {
        var tokens = lexer.Lex(source, diagnostics);
        var parseResult = parser.Parse(tokens, diagnostics);
        Assert.NotNull(parseResult.Root);
        return parseResult.Root!;
    }
}