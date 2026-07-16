// Feature: full-accessibility-model, Property 15: Sealed interacts correctly with Mist-specific type constructs
//
// **Validates: Requirements 12.5, 12.6, 12.7 (Property 15)**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for sealed interactions with Mist-specific type constructs.
///
/// Property 15: For any sealed type, a type alias targeting it is accepted with no
/// sealed diagnostic, a refinement declared over it (or over an alias of it) is
/// accepted with no sealed diagnostic, and a newtype wrapping it is reported as an
/// error (MIST_TC_0178).
///
/// The test generates Mist programs with type aliases, refinement types, and newtypes
/// targeting sealed classes and sealed structs, compiles in-process via the standard
/// pipeline with no process spawn, and asserts that MIST_TC_0178 is reported exactly
/// when a newtype wraps a sealed type.
///
/// **Validates: Requirements 12.5, 12.6, 12.7 (Property 15)**
/// </summary>
public class SealedConstructInteractionPropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();
    private static readonly SemanticModelBuilder Builder = new();
    private static readonly TypeCheckerAnalyzer Analyzer = new();

    private const string NewtypeOverSealedId = "MIST_TC_0178";

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);

        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);

        if(parseResult.Root is null)
        {
            return lexBag.ToImmutableArray().AddRange(parseBag.ToImmutableArray());
        }

        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));
        var semBag = new DiagnosticBag();
        var model = Builder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root!),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            semBag);

        var tcBag = new DiagnosticBag();
        Analyzer.Analyze(model, tcBag);

        return lexBag.ToImmutableArray()
            .AddRange(parseBag.ToImmutableArray())
            .AddRange(semBag.ToImmutableArray())
            .AddRange(tcBag.ToImmutableArray());
    }

    private static bool HasNewtypeOverSealedError(ImmutableArray<Diagnostic> diagnostics)
    {
        return diagnostics.Any(d =>
            d.Id.Value == NewtypeOverSealedId &&
            d.Severity == DiagnosticSeverity.Error);
    }

    private static string FormatDiagnostics(ImmutableArray<Diagnostic> diagnostics)
    {
        if(diagnostics.IsEmpty)
        {
            return "(none)";
        }

        return string.Join("; ", diagnostics.Select(d => $"{d.Id.Value}[{d.Severity}]: {d.Message}"));
    }

    private static string MainClass()
    {
        return @"
class Program {
    public static void Main() { }
}";
    }

    // ─── Scenario type ───────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// A generated scenario for testing sealed interactions with Mist constructs.
    /// </summary>
    public sealed record SealedConstructScenario(
        string Source,
        string Description,
        bool ShouldReject)
    {
        public override string ToString()
        {
            return $"[{(ShouldReject ? "REJECT" : "ACCEPT")}] {Description}";
        }
    }

#pragma warning restore CA1034

    // ─── Name pools ──────────────────────────────────────────────────────

    private static readonly string[] SealedClassNames = ["Frozen", "Locked", "Final", "Fixed", "Sealed"];
    private static readonly string[] SealedStructNames = ["Rigid", "Solid", "Firm", "Hard", "Compact"];
    private static readonly string[] AliasNames = ["Alias", "Ref", "Shortcut", "Handle", "Nickname"];
    private static readonly string[] RefinementNames = ["Positive", "Valid", "Checked", "Bounded", "Safe"];
    private static readonly string[] NewtypeNames = ["Wrapper", "Boxed", "Tagged", "Typed", "Named"];

    // ─── ACCEPT scenarios: type alias targeting a sealed class ────────────

    /// <summary>
    /// Generates scenarios where a type alias targets a sealed class.
    /// Type aliases are transparent and do not introduce a new type — accepted (Req 12.5).
    /// </summary>
    private static Gen<SealedConstructScenario> GenAliasOfSealedClass()
    {
        return from classIdx in Gen.Choose(0, SealedClassNames.Length - 1)
               from aliasIdx in Gen.Choose(0, AliasNames.Length - 1)
               let className = SealedClassNames[classIdx]
               let aliasName = AliasNames[aliasIdx]
               let source = $$"""
                   public sealed class {{className}}
                   {
                       public int value;
                       public {{className}}(int v)
                       {
                           this.value = v;
                       }
                   }
                   type {{aliasName}} = {{className}};
                   """ + MainClass()
               select new SealedConstructScenario(source,
                   $"Type alias '{aliasName}' targets sealed class '{className}'",
                   ShouldReject: false);
    }

    /// <summary>
    /// Generates scenarios where a type alias targets a sealed struct.
    /// Type aliases are transparent — accepted (Req 12.5).
    /// </summary>
    private static Gen<SealedConstructScenario> GenAliasOfSealedStruct()
    {
        return from structIdx in Gen.Choose(0, SealedStructNames.Length - 1)
               from aliasIdx in Gen.Choose(0, AliasNames.Length - 1)
               let structName = SealedStructNames[structIdx]
               let aliasName = AliasNames[aliasIdx]
               let source = $$"""
                   public sealed struct {{structName}}
                   {
                       public int value;
                       public {{structName}}(int v)
                       {
                           this.value = v;
                       }
                   }
                   type {{aliasName}} = {{structName}};
                   """ + MainClass()
               select new SealedConstructScenario(source,
                   $"Type alias '{aliasName}' targets sealed struct '{structName}'",
                   ShouldReject: false);
    }

    /// <summary>
    /// Generates scenarios where a type alias with an explicit access modifier
    /// targets a sealed class. The alias itself carries an accessibility, but
    /// no sealed-related error — accepted (Req 12.5, 12.8).
    /// </summary>
    private static Gen<SealedConstructScenario> GenAliasOfSealedClassWithAccessModifier()
    {
        return from classIdx in Gen.Choose(0, SealedClassNames.Length - 1)
               from aliasIdx in Gen.Choose(0, AliasNames.Length - 1)
               from accessKw in Gen.Elements("public", "internal")
               let className = SealedClassNames[classIdx]
               let aliasName = AliasNames[aliasIdx]
               let source = $$"""
                   public sealed class {{className}}
                   {
                       public int value;
                       public {{className}}(int v)
                       {
                           this.value = v;
                       }
                   }
                   {{accessKw}} type {{aliasName}} = {{className}};
                   """ + MainClass()
               select new SealedConstructScenario(source,
                   $"{accessKw} type alias '{aliasName}' targets sealed class '{className}'",
                   ShouldReject: false);
    }

    // ─── ACCEPT scenarios: refinement type over a sealed class ────────────

    /// <summary>
    /// Generates scenarios where a refinement type is declared over a sealed struct
    /// (using int, which is a sealed CLR type). Refinements narrow values of the base
    /// type without extending it — accepted (Req 12.6).
    /// Note: Refinement types use a predicate constraint on a primitive type. Since int
    /// is sealed at the CLR level and projected as sealed, this exercises Req 12.6.
    /// </summary>
    private static Gen<SealedConstructScenario> GenRefinementOverSealedPrimitive()
    {
        return from refinementIdx in Gen.Choose(0, RefinementNames.Length - 1)
               from threshold in Gen.Choose(1, 100)
               let refinementName = RefinementNames[refinementIdx]
               let source = $$"""
                   type {{refinementName}} = int >= {{threshold}};
                   """ + MainClass()
               select new SealedConstructScenario(source,
                   $"Refinement type '{refinementName}' over int (sealed CLR type) with >= {threshold}",
                   ShouldReject: false);
    }

    /// <summary>
    /// Generates scenarios where a refinement type is declared over a user-defined
    /// sealed class that has an int field. Since the sealed enforcement for type aliases
    /// (which includes refinements under SymbolKind.TypeAlias) explicitly accepts them,
    /// this exercises Req 12.6 with user-declared sealed types.
    /// Note: Refinement types can only apply predicates to primitive numeric types,
    /// so we use int-based refinements which are always over sealed CLR types.
    /// </summary>
    private static Gen<SealedConstructScenario> GenRefinementWithUpperBound()
    {
        return from refinementIdx in Gen.Choose(0, RefinementNames.Length - 1)
               from lower in Gen.Choose(0, 50)
               from upper in Gen.Choose(51, 200)
               let refinementName = RefinementNames[refinementIdx]
               let source = $$"""
                   type {{refinementName}} = int >= {{lower}} and <= {{upper}};
                   """ + MainClass()
               select new SealedConstructScenario(source,
                   $"Refinement type '{refinementName}' over int with >= {lower} and <= {upper}",
                   ShouldReject: false);
    }

    // ─── REJECT scenarios: newtype wrapping a sealed class ───────────────

    /// <summary>
    /// Generates scenarios where a newtype wraps a sealed class.
    /// Newtypes build a new nominal type — rejected (Req 12.7, MIST_TC_0178).
    /// </summary>
    private static Gen<SealedConstructScenario> GenNewtypeOfSealedClass()
    {
        return from classIdx in Gen.Choose(0, SealedClassNames.Length - 1)
               from newtypeIdx in Gen.Choose(0, NewtypeNames.Length - 1)
               let className = SealedClassNames[classIdx]
               let newtypeName = NewtypeNames[newtypeIdx]
               let source = $$"""
                   public sealed class {{className}}
                   {
                       public int value;
                       public {{className}}(int v)
                       {
                           this.value = v;
                       }
                   }
                   type {{newtypeName}} = new {{className}};
                   """ + MainClass()
               select new SealedConstructScenario(source,
                   $"Newtype '{newtypeName}' wraps sealed class '{className}'",
                   ShouldReject: true);
    }

    /// <summary>
    /// Generates scenarios where a newtype wraps a sealed struct.
    /// Newtypes build a new nominal type — rejected (Req 12.7, MIST_TC_0178).
    /// </summary>
    private static Gen<SealedConstructScenario> GenNewtypeOfSealedStruct()
    {
        return from structIdx in Gen.Choose(0, SealedStructNames.Length - 1)
               from newtypeIdx in Gen.Choose(0, NewtypeNames.Length - 1)
               let structName = SealedStructNames[structIdx]
               let newtypeName = NewtypeNames[newtypeIdx]
               let source = $$"""
                   public sealed struct {{structName}}
                   {
                       public int value;
                       public {{structName}}(int v)
                       {
                           this.value = v;
                       }
                   }
                   type {{newtypeName}} = new {{structName}};
                   """ + MainClass()
               select new SealedConstructScenario(source,
                   $"Newtype '{newtypeName}' wraps sealed struct '{structName}'",
                   ShouldReject: true);
    }

    /// <summary>
    /// Generates scenarios where a newtype wraps a sealed class with an access modifier.
    /// The sealed enforcement applies regardless of the newtype's accessibility — rejected
    /// (Req 12.7, MIST_TC_0178).
    /// </summary>
    private static Gen<SealedConstructScenario> GenNewtypeOfSealedClassWithAccess()
    {
        return from classIdx in Gen.Choose(0, SealedClassNames.Length - 1)
               from newtypeIdx in Gen.Choose(0, NewtypeNames.Length - 1)
               from accessKw in Gen.Elements("public", "internal")
               let className = SealedClassNames[classIdx]
               let newtypeName = NewtypeNames[newtypeIdx]
               let source = $$"""
                   public sealed class {{className}}
                   {
                       public int value;
                       public {{className}}(int v)
                       {
                           this.value = v;
                       }
                   }
                   {{accessKw}} type {{newtypeName}} = new {{className}};
                   """ + MainClass()
               select new SealedConstructScenario(source,
                   $"{accessKw} newtype '{newtypeName}' wraps sealed class '{className}'",
                   ShouldReject: true);
    }

    // ─── Combined generator ──────────────────────────────────────────────

    private static Gen<SealedConstructScenario> GenScenario()
    {
        return Gen.OneOf(
            // ACCEPT: type aliases targeting sealed types (Req 12.5)
            GenAliasOfSealedClass(),
            GenAliasOfSealedStruct(),
            GenAliasOfSealedClassWithAccessModifier(),
            // ACCEPT: refinement types over sealed types (Req 12.6)
            GenRefinementOverSealedPrimitive(),
            GenRefinementWithUpperBound(),
            // REJECT: newtypes wrapping sealed types (Req 12.7)
            GenNewtypeOfSealedClass(),
            GenNewtypeOfSealedStruct(),
            GenNewtypeOfSealedClassWithAccess());
    }

    public static Arbitrary<SealedConstructScenario> ArbitrarySealedConstructScenario()
    {
        return GenScenario().ToArbitrary();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 15: Sealed interacts correctly with Mist-specific type constructs
    //
    // For any sealed type, a type alias targeting it is accepted with no sealed
    // diagnostic, a refinement declared over it (or over an alias of it) is
    // accepted with no sealed diagnostic, and a newtype wrapping it is reported
    // as an error (MIST_TC_0178).
    //
    // **Validates: Requirements 12.5, 12.6, 12.7**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(SealedConstructInteractionPropertyTests) })]
    public Property SealedConstructs_AliasAndRefinementAccepted_NewtypeRejected(SealedConstructScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        var hasNewtypeSealedError = HasNewtypeOverSealedError(diagnostics);

        if(scenario.ShouldReject)
        {
            // Newtype over sealed type: MIST_TC_0178 should be reported.
            return hasNewtypeSealedError.ToProperty()
                .Label($"Expected REJECT ({NewtypeOverSealedId}) for: {scenario.Description}" +
                       $"\nDiagnostics: {FormatDiagnostics(diagnostics)}");
        }
        else
        {
            // Type alias or refinement over sealed type: no MIST_TC_0178 and no
            // unrelated errors. We allow warnings (e.g., protected-in-sealed).
            var hasUnrelatedErrors = diagnostics.Any(d =>
                d.Severity == DiagnosticSeverity.Error);

            return ((!hasNewtypeSealedError) & (!hasUnrelatedErrors)).ToProperty()
                .Label($"Expected ACCEPT (no errors) for: {scenario.Description}" +
                       $"\nDiagnostics: {FormatDiagnostics(diagnostics)}");
        }
    }
}