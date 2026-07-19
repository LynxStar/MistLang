// Feature: variadics, task 12.4: unit tests for binding edge cases over the CLASSIC (legacy
// Mist.Semantics) pipeline. Requirements: 4.5, 4.6.
//
// These dedicated unit tests exercise the shared flat-to-bracketed binding rule (task 12.1)
// on the specific edge cases the design calls out, complementing the broader task 12.1 example
// tests (VariadicsSemanticModelBindingRuleTests) and the task 12.2/12.3 property tests. Each
// drives the classic SemanticModelBuilder entirely in-process via the shared
// VariadicsCompilerFixture (parse -> build semantic model -> inspect the ConstructedType
// resolved for a field). The rule lives in TypeCheckerAnalyzer.BindPackSlots and is invoked by
// SemanticModelBuilder while resolving a variadic family's use-site type-argument list; a bound
// pack rides on a DependentValueArgument at the pack slot of the ConstructedType
// (DependentArguments[i].PackElementTypes), and a too-short flat list is reported as
// MIST_VAR_0005. There are ZERO external process spawns: the builder runs directly and no
// backend or compiled program is ever invoked.
//
// This is the CLASSIC-track counterpart of the ProgramModel-track edge-case suite
// (VariadicsBindingEdgeCaseTests, task 7.4). It uses the design's exact primitive keyword
// spellings (`int`, `string`, `bool`, `double`, `float`), which the classic model resolves
// through the legacy primitive fallback WITHOUT a loaded standard library (their DisplayName is
// the keyword spelling itself, e.g. `int`); the design's lowercase carrier operands (a, b, c,
// x, y, z) are declared source classes. The task 12.1 example tests deliberately used carrier
// classes A/B/C/D for the same shapes; this file locks the requirement's precise primitive
// examples and adds the arity-5 two-bracketed-pack construction, which the example tests do
// not cover.

using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Variadics;

/// <summary>
/// Binding-rule edge-case unit tests over the classic (legacy <c>Mist.Semantics</c>) pipeline
/// (Requirements 4.5, 4.6). Each builds the semantic model in-process and asserts on the
/// constructed variadic type resolved for a field:
/// <list type="bullet">
///   <item>the design's arity-5 construction
///     <c>Foo&lt;int, [a, b], x, y, z, [c], float&gt;</c> binds fixed scalars from the ends, the
///     two bracketed <c>Type[]:</c> packs from their bracketed groups, and the single
///     unbracketed <c>params</c> pack from the contiguous middle (4.5);</item>
///   <item>the design's <c>Between&lt;TFirst, params TMid, TLast&gt;</c> example at
///     <c>Between&lt;int, string, bool, double&gt;</c> binds <c>TFirst = int</c>,
///     <c>TMid = [string, bool]</c>, <c>TLast = double</c> (4.5);</item>
///   <item>a flat argument list too short to fill the fixed and bracketed slots raises
///     <c>MIST_VAR_0005</c>, using a case distinct from the task 12.1 example tests (4.6).</item>
/// </list>
/// </summary>
public sealed class VariadicsSemanticModelBindingEdgeCaseTests
{
    private const string TooFewArgumentsId = "MIST_VAR_0005";
    private const string ArityMismatchId = "MIST_ARITY_MISMATCH";

    // =====================================================================
    // Requirement 4.5: the design's arity-5 middle-and-bracketed construction.
    //   class Foo<TVal, Type[]:TArgs, params CtorArgs, Type[]:Args3, TRes>
    //   Foo<int, [a, b], x, y, z, [c], float>
    //   -> TVal = int, TArgs = [a, b], CtorArgs = [x, y, z], Args3 = [c], TRes = float
    // =====================================================================

    [Fact]
    public void ArityFiveConstruction_BindsFixedScalarsBracketedPacksAndMiddlePack()
    {
        var (model, diagnostics) = BuildModel(
            "public class a { }\n"
            + "public class b { }\n"
            + "public class c { }\n"
            + "public class x { }\n"
            + "public class y { }\n"
            + "public class z { }\n"
            + "public class Foo<TVal, Type[]:TArgs, params CtorArgs, Type[]:Args3, TRes> { }\n"
            + "public class Holder\n"
            + "{\n"
            + "    public Foo<int, [a, b], x, y, z, [c], float> Value;\n"
            + "}");

        AssertNoErrors(diagnostics);

        // The construction folds seven flat use-site arguments into the five declared slots:
        // the fixed ends bind inward, the two bracketed packs take their bracketed groups, and
        // the single unbracketed pack (CtorArgs) absorbs the contiguous middle x, y, z.
        var foo = FieldConstructed(model, "Value", argumentCount: 5);

        // TVal (fixed) binds int from the left end.
        AssertFixedSlot(foo, index: 0, "int");

        // TArgs (bracketed Type[]:) binds its bracketed group [a, b], in order.
        AssertPackSlot(foo, index: 1, "a", "b");

        // CtorArgs (the lone params pack) absorbs the contiguous scalar middle [x, y, z].
        AssertPackSlot(foo, index: 2, "x", "y", "z");

        // Args3 (bracketed Type[]:) binds its bracketed group [c].
        AssertPackSlot(foo, index: 3, "c");

        // TRes (fixed) binds float from the right end.
        AssertFixedSlot(foo, index: 4, "float");
    }

    // =====================================================================
    // Requirement 4.5: the design's Between example, exact spelling.
    //   class Between<TFirst, params TMid, TLast>
    //   Between<int, string, bool, double>
    //   -> TFirst = int, TMid = [string, bool], TLast = double
    // =====================================================================

    [Fact]
    public void BetweenExample_BindsFirstAndLastInward_AndMiddlePackInOrder()
    {
        var (model, diagnostics) = BuildModel(
            "public class Between<TFirst, params TMid, TLast> { }\n"
            + "public class Holder\n"
            + "{\n"
            + "    public Between<int, string, bool, double> Value;\n"
            + "}");

        AssertNoErrors(diagnostics);

        // Requirement 4.5 verbatim: TFirst binds the left end (int), TLast the right end
        // (double), and the pack TMid absorbs the contiguous middle [string, bool], in order.
        var between = FieldConstructed(model, "Value", argumentCount: 3);
        AssertFixedSlot(between, index: 0, "int");
        AssertPackSlot(between, index: 1, "string", "bool");
        AssertFixedSlot(between, index: 2, "double");
    }

    // =====================================================================
    // Requirement 4.6: too few arguments to fill the fixed and bracketed slots.
    // Distinct from the task 12.1 example test's Between<A> case: here the arity-5 family
    // (two bracketed Type[]: packs plus two fixed slots plus one params pack) is supplied
    // three arguments, one short of the four fixed-and-bracketed slots that must bind while
    // the params pack absorbs zero.
    // =====================================================================

    [Fact]
    public void ArityFiveFamily_TooFewArgumentsForFixedAndBracketedSlots_EmitsMistVar0005()
    {
        var (_, diagnostics) = BuildModel(
            "public class A { }\n"
            + "public class B { }\n"
            + "public class C { }\n"
            + "public class D { }\n"
            + "public class Foo<TVal, Type[]:TArgs, params CtorArgs, Type[]:Args3, TRes> { }\n"
            + "public class Holder\n"
            + "{\n"
            + "    public Foo<A, [B, C], D> TooFew;\n"
            + "}");

        // Foo has four fixed-and-bracketed slots (TVal, TArgs, Args3, TRes) that must bind
        // while CtorArgs absorbs zero, but only three arguments are supplied (A, [B, C], D),
        // so binding fails with MIST_VAR_0005 (Requirement 4.6).
        var tooFew = Assert.Single(diagnostics, d => d.Id.Value == TooFewArgumentsId);
        Assert.Equal(DiagnosticSeverity.Error, tooFew.Severity);

        // The pre-existing fixed-arity mismatch must not fire for a variadic family: the
        // binding rule superseded it during construction.
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == ArityMismatchId);
    }

    // =====================================================================
    // Helpers (mirror the task 12.1 example tests; ZERO external process spawns).
    // =====================================================================

    private static void AssertNoErrors(ImmutableArray<Diagnostic> diagnostics)
    {
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(
            errors.Count == 0,
            $"Unexpected errors: {string.Join("; ", errors.Select(d => $"{d.Id.Value} {d.Message}"))}");
    }

    /// <summary>
    /// Returns the constructed variadic type resolved for the field named
    /// <paramref name="fieldName"/> and asserts its declared type-argument count (one per
    /// declared slot; each pack is exactly one slot).
    /// </summary>
    private static ConstructedType FieldConstructed(ISemanticModel model, string fieldName, int argumentCount)
    {
        var field = model.AllSymbols.Single(s => s.Kind == SymbolKind.Field && s.Name == fieldName);
        var constructed = Assert.IsType<ConstructedType>(field.DeclaredType);
        Assert.Equal(argumentCount, constructed.TypeArguments.Count);
        return constructed;
    }

    /// <summary>
    /// Asserts slot <paramref name="index"/> is a pack whose lifted comptime <c>Type[]</c>
    /// element types match <paramref name="expectedElementNames"/> in order (empty means the
    /// empty pack). The pack rides on a <see cref="DependentValueArgument"/> at that slot.
    /// </summary>
    private static void AssertPackSlot(ConstructedType constructed, int index, params string[] expectedElementNames)
    {
        Assert.NotNull(constructed.DependentArguments);
        var dependent = constructed.DependentArguments![index];
        Assert.NotNull(dependent);
        Assert.NotNull(dependent!.PackElementTypes);
        Assert.Equal(
            expectedElementNames,
            dependent.PackElementTypes!.Select(e => e.DisplayName).ToArray());
    }

    /// <summary>
    /// Asserts slot <paramref name="index"/> is a fixed scalar slot bound to the type named
    /// <paramref name="expectedTypeName"/>, carrying no pack dependent argument.
    /// </summary>
    private static void AssertFixedSlot(ConstructedType constructed, int index, string expectedTypeName)
    {
        Assert.Equal(expectedTypeName, constructed.TypeArguments[index].DisplayName);
        if(constructed.DependentArguments is not null)
        {
            Assert.Null(constructed.DependentArguments[index]);
        }
    }

    private static (ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics) BuildModel(string source)
    {
        var parse = VariadicsCompilerFixture.Parse(source);
        Assert.NotNull(parse.Root);
        Assert.True(
            VariadicsCompilerFixture.ParsedCleanly(parse),
            $"Edge-case test source is expected to parse without lex/parse errors: <{source}>");

        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(new SourceText("test.mist", source))));
        var diagnostics = new DiagnosticBag();
        var builder = new SemanticModelBuilder();

        var model = builder.Build(
            compilation,
            ImmutableArray.Create(parse.Root!),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            diagnostics);

        return (model, diagnostics.ToImmutableArray());
    }
}