// Feature: variadics - Classic-pipeline (legacy Mist.Semantics) flat-to-bracketed binding
// rule (Task 12.1). Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 7.4, 7.6.
//
// Task 12.1 implements the shared binding rule for the CLASSIC (emitting) pipeline. The
// rule itself lives in TypeCheckerAnalyzer.BindPackSlots (the classic-track counterpart of
// the ProgramModel PackBinder); SemanticModelBuilder invokes it while resolving a variadic
// family's use-site type-argument list, folding a flat list (Foo<int, string>) into the
// canonical bracketed form (Foo<[int, string]>) and reporting a too-short list as
// MIST_VAR_0005. In the legacy model a bound pack rides on a DependentValueArgument at the
// pack slot of a ConstructedType (DependentValueArgument.ForPack / PackElementTypes), and
// identity is computed structurally by TypeCheckerAnalyzer.TypesEqual.
//
// Every test drives SemanticModelBuilder entirely in-process (ZERO external process spawns:
// no dotnet run, no compiled DLL, no stdlib) using opaque carrier classes as pack operands.
// The type side is checked through the pipeline (field types + diagnostics); the value side
// applies the SAME shared binder to a method's value-pack parameter slots (Requirement 7.4 /
// 7.6). The dedicated property tests (determinism/totality 12.2, flat/bracketed equivalence
// 12.3) are separate tasks; this file locks the representative example cases.

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Variadics;

/// <summary>
/// Example-based tests for the classic-track flat-to-bracketed binding rule (Requirement 4)
/// and its application to value-parameter binding (Requirements 7.4, 7.6). Each builds the
/// legacy semantic model in-process and inspects the constructed variadic type resolved for a
/// field, asserting the leading/middle/trailing pack cases, the empty pack, flat/bracketed
/// equivalence, and the too-few-arguments <c>MIST_VAR_0005</c> diagnostic.
/// </summary>
public sealed class VariadicsSemanticModelBindingRuleTests
{
    private const string TooFewArgumentsId = "MIST_VAR_0005";
    private const string ArityMismatchId = "MIST_ARITY_MISMATCH";

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 4.4: flat and bracketed forms denote the SAME type
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void FlatAndBracketedForms_DenoteTheSameType()
    {
        var (model, diagnostics) = BuildModel(
            "public class A { }\n"
            + "public class B { }\n"
            + "public class Foo<params TArgs> { }\n"
            + "public class Holder\n"
            + "{\n"
            + "    public Foo<A, B> Flat;\n"
            + "    public Foo<[A, B]> Bracketed;\n"
            + "}");

        AssertNoErrors(diagnostics);

        var flat = FieldConstructed(model, "Flat", argumentCount: 1);
        var bracketed = FieldConstructed(model, "Bracketed", argumentCount: 1);

        // Both bind the single pack to the ordered Type[] [A, B].
        AssertPackSlot(flat, index: 0, "A", "B");
        AssertPackSlot(bracketed, index: 0, "A", "B");

        // Requirement 4.4: the flat construction and the bracketed canonical form denote the
        // SAME type under the legacy structural identity relation (the legacy model does not
        // intern, so they are distinct instances that compare equal).
        Assert.True(
            TypeCheckerAnalyzer.TypesEqual(flat, bracketed),
            "Foo<A, B> and Foo<[A, B]> must denote the same type (Requirement 4.4).");
        Assert.True(TypeCheckerAnalyzer.TypesEqual(bracketed, flat), "Flat/bracketed equivalence must be symmetric.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirements 4.1, 4.2, 4.3: leading pack with a trailing fixed slot;
    // the trailing slot binds inward and the pack absorbs the (possibly empty) middle
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void LeadingPack_WithTrailingFixed_BindsInward_AndAllowsEmptyPack()
    {
        var (model, diagnostics) = BuildModel(
            "public class A { }\n"
            + "public class B { }\n"
            + "public class Fn<params TArgs, TResult> { }\n"
            + "public class Holder\n"
            + "{\n"
            + "    public Fn<A, B> OneArg;\n"
            + "    public Fn<A> Empty;\n"
            + "}");

        AssertNoErrors(diagnostics);

        // Fn<A, B>: the trailing TResult binds B from the right; the leading pack takes [A].
        var oneArg = FieldConstructed(model, "OneArg", argumentCount: 2);
        AssertPackSlot(oneArg, index: 0, "A");
        AssertFixedSlot(oneArg, index: 1, "B");

        // Fn<A>: the trailing TResult binds A; the pack is empty (Requirement 4.3).
        var empty = FieldConstructed(model, "Empty", argumentCount: 2);
        AssertPackSlot(empty, index: 0);
        AssertFixedSlot(empty, index: 1, "A");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirements 4.1, 4.2, 4.5: a middle pack binds first/last inward from both ends
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MiddlePack_BindsEndsInward_AndCollectsTheMiddle()
    {
        var (model, diagnostics) = BuildModel(
            "public class A { }\n"
            + "public class B { }\n"
            + "public class C { }\n"
            + "public class D { }\n"
            + "public class Between<TFirst, params TMid, TLast> { }\n"
            + "public class Holder\n"
            + "{\n"
            + "    public Between<A, B, C, D> Value;\n"
            + "}");

        AssertNoErrors(diagnostics);

        // Requirement 4.5: TFirst binds the left end (A), TLast the right end (D), and the pack
        // TMid absorbs the contiguous middle [B, C].
        var between = FieldConstructed(model, "Value", argumentCount: 3);
        AssertFixedSlot(between, index: 0, "A");
        AssertPackSlot(between, index: 1, "B", "C");
        AssertFixedSlot(between, index: 2, "D");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirements 4.1, 4.2, 4.3: a trailing pack binds the leading fixed slots inward
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TrailingPack_BindsLeadingFixed_AndAllowsEmptyPack()
    {
        var (model, diagnostics) = BuildModel(
            "public class A { }\n"
            + "public class B { }\n"
            + "public class C { }\n"
            + "public class Head<TFirst, params TRest> { }\n"
            + "public class Holder\n"
            + "{\n"
            + "    public Head<A, B, C> Two;\n"
            + "    public Head<A> None;\n"
            + "}");

        AssertNoErrors(diagnostics);

        // Head<A, B, C>: TFirst binds A from the left; the trailing pack takes [B, C].
        var two = FieldConstructed(model, "Two", argumentCount: 2);
        AssertFixedSlot(two, index: 0, "A");
        AssertPackSlot(two, index: 1, "B", "C");

        // Head<A>: TFirst binds A; the trailing pack is empty (Requirement 4.3).
        var none = FieldConstructed(model, "None", argumentCount: 2);
        AssertFixedSlot(none, index: 0, "A");
        AssertPackSlot(none, index: 1);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 4.6: too few arguments to fill the fixed slots -> MIST_VAR_0005
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TooFewArgumentsForFixedSlots_EmitsMistVar0005()
    {
        var (_, diagnostics) = BuildModel(
            "public class A { }\n"
            + "public class Between<TFirst, params TMid, TLast> { }\n"
            + "public class Holder\n"
            + "{\n"
            + "    public Between<A> TooFew;\n"
            + "}");

        // TFirst and TLast are two fixed slots the pack cannot cover with a single argument, so
        // binding fails with MIST_VAR_0005 (Requirement 4.6).
        var tooFew = Assert.Single(diagnostics, d => d.Id.Value == TooFewArgumentsId);
        Assert.Equal(DiagnosticSeverity.Error, tooFew.Severity);

        // The pre-existing fixed-arity mismatch must no longer fire for a variadic family.
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == ArityMismatchId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 4.3: an explicit empty bracketed group [] binds the empty pack
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ExplicitEmptyBracketedGroup_BindsEmptyPack()
    {
        var (model, diagnostics) = BuildModel(
            "public class Foo<params TArgs> { }\n"
            + "public class Holder\n"
            + "{\n"
            + "    public Foo<[]> Empty;\n"
            + "}");

        AssertNoErrors(diagnostics);

        var empty = FieldConstructed(model, "Empty", argumentCount: 1);
        AssertPackSlot(empty, index: 0);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirements 7.4, 7.6: the SAME binding rule applies to value-parameter binding —
    // fixed parameters bind inward from both ends and the contiguous middle collects into
    // the value pack (TArgs...); too few arguments to fill the fixed parameters is the
    // MIST_VAR_0005 condition.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ValueParameterBinding_CollectsMiddleIntoValuePack()
    {
        var model = BuildModelOnly(
            "public class Bundle<params TArgs>\n"
            + "{\n"
            + "    public void Mixed(int number, params TArgs args, bool enabled) { }\n"
            + "}");

        // Classify the declared value-parameter slots: a value pack (the flattened tuple slot)
        // is the single unbracketed pack; every other parameter is a fixed slot. This is the
        // value-side counterpart of the type-side slot classification.
        var parameters = ParametersOf(model, "Mixed");
        Assert.Equal(3, parameters.Count);

        var slotKinds = parameters
            .Select(p => p.HasFlattenedCallConvention
                ? TypeCheckerAnalyzer.PackBindingSlotKind.UnbracketedPack
                : TypeCheckerAnalyzer.PackBindingSlotKind.Fixed)
            .ToArray();

        Assert.Equal(
            new[]
            {
                TypeCheckerAnalyzer.PackBindingSlotKind.Fixed,
                TypeCheckerAnalyzer.PackBindingSlotKind.UnbracketedPack,
                TypeCheckerAnalyzer.PackBindingSlotKind.Fixed,
            },
            slotKinds);

        // A flat call Mixed(5, a, b, c, true): the fixed number/enabled bind inward from the
        // ends and the contiguous middle [a, b, c] collects into the value pack (Requirement 7.4).
        var five = MakeArgs(5);
        var bound = TypeCheckerAnalyzer.BindPackSlots(slotKinds, five);

        Assert.Equal(TypeCheckerAnalyzer.PackBindingStatus.Bound, bound.Status);
        Assert.False(bound.Slots[0].IsPack);
        Assert.Same(five[0].Types[0], bound.Slots[0].Elements[0]);

        Assert.True(bound.Slots[1].IsPack);
        Assert.Equal(
            new[] { five[1].Types[0], five[2].Types[0], five[3].Types[0] },
            bound.Slots[1].Elements.ToArray());

        Assert.False(bound.Slots[2].IsPack);
        Assert.Same(five[4].Types[0], bound.Slots[2].Elements[0]);

        // Too few arguments to fill the two fixed parameters is the MIST_VAR_0005 condition
        // (Requirement 7.6): one argument cannot fill number and enabled.
        var one = MakeArgs(1);
        var tooFew = TypeCheckerAnalyzer.BindPackSlots(slotKinds, one);
        Assert.Equal(TypeCheckerAnalyzer.PackBindingStatus.TooFewArguments, tooFew.Status);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // The full classic checking stage (SemanticModelBuilder + TypeCheckerAnalyzer)
    // accepts variadic constructions without crashing or mis-reporting an arity
    // mismatch (the pre-existing fixed-arity check must not fire for a variadic family).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TypeChecker_AcceptsVariadicConstructions_WithoutCrashOrArityMismatch()
    {
        var (model, buildDiagnostics) = BuildModel(
            "public class A { }\n"
            + "public class B { }\n"
            + "public class C { }\n"
            + "public class Foo<params TArgs, TResult> { }\n"
            + "public class Between<TFirst, params TMid, TLast> { }\n"
            + "public class Holder\n"
            + "{\n"
            + "    public Foo<A, B> Flat;\n"
            + "    public Foo<[A, B]> Bracketed;\n"
            + "    public Foo<A, B, C> ThreeFlat;\n"
            + "    public Between<A, B, C, A> Mid;\n"
            + "}");

        // The binding rule superseded the fixed-arity check during construction.
        Assert.DoesNotContain(buildDiagnostics, d => d.Id.Value == ArityMismatchId);

        // Run the classic type checker over the built model in-process (no backend, no process
        // spawn). It must not crash on the variadic constructed types nor re-report arity.
        var analyzerDiagnostics = new DiagnosticBag();
        new TypeCheckerAnalyzer().Analyze(model, analyzerDiagnostics);

        var analyzerIds = analyzerDiagnostics.ToImmutableArray().Select(d => d.Id.Value).ToList();
        Assert.DoesNotContain("MIST_ANALYZER_CRASH", analyzerIds);
        Assert.DoesNotContain(ArityMismatchId, analyzerIds);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Builds N distinct scalar use-site arguments (each a fresh opaque carrier type).</summary>
    private static IReadOnlyList<TypeCheckerAnalyzer.PackUseSiteArgument> MakeArgs(int count)
    {
        var args = new List<TypeCheckerAnalyzer.PackUseSiteArgument>(count);
        for(var i = 0; i < count; i++)
        {
            IType carrier = new ClassType($"Arg{i}", new Scope(null), containingNamespace: null);
            args.Add(TypeCheckerAnalyzer.PackUseSiteArgument.Scalar(carrier));
        }

        return args;
    }

    private static void AssertNoErrors(ImmutableArray<Diagnostic> diagnostics)
    {
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(
            errors.Count == 0,
            $"Unexpected errors: {string.Join("; ", errors.Select(d => $"{d.Id.Value} {d.Message}"))}");
    }

    /// <summary>
    /// Returns the constructed variadic type resolved for the field named
    /// <paramref name="fieldName"/> and asserts its declared type-argument count.
    /// </summary>
    private static ConstructedType FieldConstructed(ISemanticModel model, string fieldName, int argumentCount)
    {
        var field = model.AllSymbols.Single(s => s.Kind == SymbolKind.Field && s.Name == fieldName);
        var constructed = Assert.IsType<ConstructedType>(field.DeclaredType);
        Assert.Equal(argumentCount, constructed.TypeArguments.Count);
        return constructed;
    }

    /// <summary>
    /// Asserts that slot <paramref name="index"/> is a pack whose lifted comptime <c>Type[]</c>
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
    /// Asserts that slot <paramref name="index"/> is a fixed scalar slot bound to the type named
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

    /// <summary>
    /// Returns the declared parameter <see cref="Symbol"/> instances (in declaration order) of
    /// the single source method named <paramref name="methodName"/>, read from its declaring AST.
    /// </summary>
    private static IReadOnlyList<Symbol> ParametersOf(ISemanticModel model, string methodName)
    {
        var methodSymbol = model.AllSymbols.FirstOrDefault(s =>
            s.Name == methodName && s.Kind == SymbolKind.Method);
        Assert.NotNull(methodSymbol);

        var paramList = model.GetDeclaringNode(methodSymbol!) is MethodDeclarationNode method
            ? method.Parameters
            : null;

        var result = new List<Symbol>();
        if(paramList is null)
        {
            return result;
        }

        foreach(var child in paramList.Children())
        {
            if(child is ParameterNode paramNode && model.GetDeclaredSymbol(paramNode) is Symbol sym)
            {
                result.Add(sym);
            }
        }

        return result;
    }

    private static (ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics) BuildModel(string source)
    {
        var parse = VariadicsCompilerFixture.Parse(source);
        Assert.NotNull(parse.Root);
        Assert.True(
            VariadicsCompilerFixture.ParsedCleanly(parse),
            "Binding-rule test source is expected to parse without lex/parse errors.");

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

    private static ISemanticModel BuildModelOnly(string source)
    {
        return BuildModel(source).Model;
    }
}