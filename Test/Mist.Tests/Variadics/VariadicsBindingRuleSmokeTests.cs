// Feature: variadics - Smoke tests for the flat-to-bracketed binding rule (Task 7.1)
// Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6
//
// These smoke tests drive the binding rule end-to-end in-process via
// ProgramModelBuilder.Build: source declaring variadic families is parsed, the program
// model is built, and the constructed types resolved for fields of those families are
// inspected. The binding rule lives in TypeReferenceResolver (the pack-aware ProgramModel
// resolution path), where a flat use-site type-argument list is bound to a definition's
// declared slots and the pack's ordered element list is lifted into a comptime Type[] via
// TypeInterner.GetOrCreateLiftedTypeArray. There are ZERO external process spawns: the
// builder runs directly and emit is never invoked.
//
// The dedicated property tests (Tasks 7.2/7.3) and the full binding unit tests (Task 7.4)
// exercise the rule more broadly; this file is the minimal in-process verification for 7.1.

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
/// Smoke tests for the shared binding rule (Requirement 4). Each builds the program model
/// in-process and asserts on the constructed variadic types resolved for fields:
/// <list type="bullet">
///   <item>the flat and bracketed forms of the same pack denote the same interned type (4.4);</item>
///   <item>a trailing fixed slot binds inward, the leading pack takes the rest, and the empty
///     pack is well-formed (4.2, 4.3);</item>
///   <item>a middle pack binds first/last inward from the ends (4.1, 4.2, 4.5);</item>
///   <item>too few arguments to fill the fixed slots raises <c>MIST_VAR_0005</c> (4.6).</item>
/// </list>
/// </summary>
public sealed class VariadicsBindingRuleSmokeTests
{
    private const string TooFewArgumentsId = "MIST_VAR_0005";

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 4.4: flat and bracketed forms denote the SAME interned type
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void FlatAndBracketedForms_DenoteTheSameConstructedType()
    {
        var (model, diagnostics) = BuildModel(
            """
            namespace TestProject;

            public class A { }
            public class B { }

            public class Foo<params TArgs> { }

            public class Holder
            {
                public Foo<A, B> Flat;
                public Foo<[A, B]> Bracketed;
            }
            """);

        AssertNoErrors(diagnostics);

        var holder = FindConcreteType(model, "Holder");
        var flat = FieldType(holder, "Flat");
        var bracketed = FieldType(holder, "Bracketed");

        // Requirement 4.4: the flat construction and the bracketed canonical form intern to
        // the very same constructed type instance.
        Assert.Same(flat, bracketed);

        // ...and both bind the single pack to the ordered Type[] [A, B].
        AssertPackElements(SinglePackArgument(flat), "A", "B");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirements 4.2, 4.3: trailing fixed slot binds inward; leading pack takes
    // the remaining middle, including the empty pack
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void LeadingPackWithTrailingFixed_BindsInwardAndAllowsEmptyPack()
    {
        var (model, diagnostics) = BuildModel(
            """
            namespace TestProject;

            public class A { }
            public class B { }

            public class Fn<params TArgs, TResult> { }

            public class Holder
            {
                public Fn<A, B> OneArg;
                public Fn<A> Empty;
            }
            """);

        AssertNoErrors(diagnostics);

        var holder = FindConcreteType(model, "Holder");

        // Fn<A, B>: the trailing TResult binds B from the right; the pack takes [A].
        var oneArg = AssertConstructed(FieldType(holder, "OneArg"), argumentCount: 2);
        AssertPackElements(oneArg.TypeArguments[0], "A");
        Assert.Equal("B", oneArg.TypeArguments[1].Name);

        // Fn<A>: the trailing TResult binds A; the pack is empty (Requirement 4.3).
        var empty = AssertConstructed(FieldType(holder, "Empty"), argumentCount: 2);
        AssertPackElements(empty.TypeArguments[0]);
        Assert.Equal("A", empty.TypeArguments[1].Name);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirements 4.1, 4.2, 4.5: a middle pack binds first/last inward from both ends
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MiddlePack_BindsEndsInwardAndCollectsTheMiddle()
    {
        var (model, diagnostics) = BuildModel(
            """
            namespace TestProject;

            public class A { }
            public class B { }
            public class C { }
            public class D { }

            public class Between<TFirst, params TMid, TLast> { }

            public class Holder
            {
                public Between<A, B, C, D> Value;
            }
            """);

        AssertNoErrors(diagnostics);

        var holder = FindConcreteType(model, "Holder");

        // Requirement 4.5: TFirst binds the left end (A), TLast the right end (D), and the
        // pack TMid absorbs the contiguous middle [B, C].
        var between = AssertConstructed(FieldType(holder, "Value"), argumentCount: 3);
        Assert.Equal("A", between.TypeArguments[0].Name);
        AssertPackElements(between.TypeArguments[1], "B", "C");
        Assert.Equal("D", between.TypeArguments[2].Name);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 4.6: too few arguments to fill the fixed slots -> MIST_VAR_0005
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TooFewArgumentsForFixedSlots_EmitsMistVar0005()
    {
        var (_, diagnostics) = BuildModel(
            """
            namespace TestProject;

            public class A { }

            public class Between<TFirst, params TMid, TLast> { }

            public class Holder
            {
                public Between<A> TooFew;
            }
            """);

        // Only one argument is supplied, but TFirst and TLast are two fixed slots the pack
        // cannot cover, so binding fails with MIST_VAR_0005.
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
    /// Asserts the type is a constructed type with the expected number of type arguments and
    /// returns it for further inspection.
    /// </summary>
    private static ConstructedType AssertConstructed(IMistType type, int argumentCount)
    {
        var constructed = Assert.IsType<ConstructedType>(type);
        Assert.Equal(argumentCount, constructed.TypeArguments.Count);
        return constructed;
    }

    /// <summary>
    /// Asserts the constructed type has exactly one type argument (the lone pack) and returns
    /// that argument.
    /// </summary>
    private static IMistType SinglePackArgument(IMistType type)
    {
        var constructed = Assert.IsType<ConstructedType>(type);
        return Assert.Single(constructed.TypeArguments);
    }

    /// <summary>
    /// Asserts a pack slot argument is a lifted comptime <c>Type[]</c> whose element types
    /// match <paramref name="expectedElementNames"/> in order (empty means the empty pack).
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
    /// Parses <paramref name="source"/> and builds the program model entirely in-process,
    /// returning the model together with every diagnostic collected during construction.
    /// </summary>
    private static (IProgramModel Model, ImmutableArray<Diagnostic> Diagnostics) BuildModel(string source)
    {
        var lexer = new MistLexer();
        var parser = new MistParser();
        var parseDiagnostics = new DiagnosticBag();

        var sourceText = new SourceText("test.mist", source);
        var tokens = lexer.Lex(sourceText, parseDiagnostics);
        var parseResult = parser.Parse(tokens, parseDiagnostics);

        Assert.NotNull(parseResult.Root);

        var parseErrors = parseDiagnostics.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(
            parseErrors.Count == 0,
            $"Unexpected parse errors: {string.Join("; ", parseErrors.Select(d => $"{d.Id.Value} {d.Message}"))}");

        var unit = new CompilationUnit(sourceText);
        var compilation = new MistCompilation(ImmutableArray.Create(unit));

        var options = new CompilerOptions(
            ProjectFolder: ".",
            SourcePaths: ImmutableArray.Create("test.mist"),
            Backend: new BackendId("csharp"),
            OutputDirectory: ".",
            BinDirectory: "bin",
            ProjectName: "TestProject",
            NamespaceRoot: "TestProject",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty);

        var buildDiagnostics = new DiagnosticBag();
        var model = ProgramModelBuilder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root!),
            options,
            buildDiagnostics,
            NullAmbientTypeProvider.Instance);

        Assert.NotNull(model);
        return (model!, buildDiagnostics.ToImmutableArray());
    }
}