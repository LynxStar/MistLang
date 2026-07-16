// Feature: full-accessibility-model, Property 16: Sealed is valid only on class and struct declarations
//
// **Validates: Requirements 12.9, 13.3 (Property 16)**

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
/// Represents a scenario for testing sealed placement validation.
/// </summary>
public sealed class SealedPlacementScenario
{
    public string Source { get; }
    public string Description { get; }
    public bool ShouldReject { get; }

    public SealedPlacementScenario(string source, string description, bool shouldReject)
    {
        Source = source;
        Description = description;
        ShouldReject = shouldReject;
    }

    public override string ToString()
    {
        return $"[{(ShouldReject ? "REJECT" : "ACCEPT")}] {Description}";
    }
}

/// <summary>
/// Property-based tests for sealed placement validation.
///
/// Property 16: For any declaration bearing the <c>sealed</c> modifier that is not a
/// class or struct type declaration -- a type alias, a method, or any other non-type
/// member -- an error (MIST_TC_0179) is reported.
///
/// The test generates Mist programs with <c>sealed</c> placed on:
/// - Type aliases (should produce MIST_TC_0179)
/// - Methods, fields, properties (should produce MIST_TC_0179)
/// - Class declarations (should succeed)
/// - Struct declarations (should succeed)
///
/// It compiles in-process via the standard lex/parse/build/analyze pipeline
/// with no process spawn, min 100 iterations.
///
/// **Validates: Requirements 12.9, 13.3 (Property 16)**
/// </summary>
public class SealedPlacementPropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();
    private static readonly SemanticModelBuilder Builder = new();
    private static readonly TypeCheckerAnalyzer Analyzer = new();

    private const string SealedOnNonTypeId = "MIST_TC_0179";

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static ImmutableArray<Diagnostic> CompileAndAnalyze(string source)
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

    private static bool HasSealedOnNonTypeError(ImmutableArray<Diagnostic> diagnostics)
    {
        return diagnostics.Any(d =>
            d.Id.Value == SealedOnNonTypeId &&
            d.Severity == DiagnosticSeverity.Error);
    }

    // ─── Name pools ──────────────────────────────────────────────────────

    private static readonly string[] TypeNames = ["Widget", "Gadget", "Device", "Component", "Module"];
    private static readonly string[] AliasNames = ["MyAlias", "TypeRef", "ShortName", "Handle", "Ref"];
    private static readonly string[] FieldNames = ["data", "state", "count", "level", "score"];
    private static readonly string[] MethodNames = ["Compute", "Process", "Transform", "Evaluate", "Calculate"];
    private static readonly string[] PropertyNames = ["Value", "Status", "Name", "Size", "Index"];

    // ─── Generators: REJECT scenarios (sealed on non-type) ───────────────

    /// <summary>
    /// Generates a scenario where <c>sealed</c> is placed on a type alias.
    /// This should produce MIST_TC_0179 (Req 12.9).
    /// </summary>
    private static Gen<SealedPlacementScenario> GenSealedOnTypeAlias()
    {
        return from aliasIdx in Gen.Choose(0, AliasNames.Length - 1)
               from typeIdx in Gen.Choose(0, TypeNames.Length - 1)
               let aliasName = AliasNames[aliasIdx]
               let typeName = TypeNames[typeIdx]
               let source = $$"""
                   public class {{typeName}}
                   {
                   }
                   public sealed type {{aliasName}} = {{typeName}};
                   class Program
                   {
                       static void Main()
                       {
                       }
                   }
                   """
               select new SealedPlacementScenario(source,
                   $"Sealed on type alias '{aliasName}' targeting '{typeName}'",
                   shouldReject: true);
    }

    /// <summary>
    /// Generates a scenario where <c>sealed</c> is placed on a method.
    /// This should produce MIST_TC_0179 (Req 13.3).
    /// </summary>
    private static Gen<SealedPlacementScenario> GenSealedOnMethod()
    {
        return from typeIdx in Gen.Choose(0, TypeNames.Length - 1)
               from methodIdx in Gen.Choose(0, MethodNames.Length - 1)
               let typeName = TypeNames[typeIdx]
               let methodName = MethodNames[methodIdx]
               let source = $$"""
                   public class {{typeName}}
                   {
                       sealed int {{methodName}}()
                       {
                           return 42;
                       }
                   }
                   class Program
                   {
                       static void Main()
                       {
                       }
                   }
                   """
               select new SealedPlacementScenario(source,
                   $"Sealed on method '{methodName}' in class '{typeName}'",
                   shouldReject: true);
    }

    /// <summary>
    /// Generates a scenario where <c>sealed</c> is placed on a field.
    /// This should produce MIST_TC_0179 (Req 13.3).
    /// </summary>
    private static Gen<SealedPlacementScenario> GenSealedOnField()
    {
        return from typeIdx in Gen.Choose(0, TypeNames.Length - 1)
               from fieldIdx in Gen.Choose(0, FieldNames.Length - 1)
               let typeName = TypeNames[typeIdx]
               let fieldName = FieldNames[fieldIdx]
               let source = $$"""
                   public class {{typeName}}
                   {
                       sealed int {{fieldName}};
                   }
                   class Program
                   {
                       static void Main()
                       {
                       }
                   }
                   """
               select new SealedPlacementScenario(source,
                   $"Sealed on field '{fieldName}' in class '{typeName}'",
                   shouldReject: true);
    }

    /// <summary>
    /// Generates a scenario where <c>sealed</c> is placed on a property.
    /// This should produce MIST_TC_0179 (Req 13.3).
    /// </summary>
    private static Gen<SealedPlacementScenario> GenSealedOnProperty()
    {
        return from typeIdx in Gen.Choose(0, TypeNames.Length - 1)
               from propIdx in Gen.Choose(0, PropertyNames.Length - 1)
               let typeName = TypeNames[typeIdx]
               let propName = PropertyNames[propIdx]
               let source = $$"""
                   public class {{typeName}}
                   {
                       sealed int {{propName}} { get { return 0; } }
                   }
                   class Program
                   {
                       static void Main()
                       {
                       }
                   }
                   """
               select new SealedPlacementScenario(source,
                   $"Sealed on property '{propName}' in class '{typeName}'",
                   shouldReject: true);
    }

    // ─── Generators: ACCEPT scenarios (sealed on class/struct) ───────────

    /// <summary>
    /// Generates a scenario where <c>sealed</c> is placed on a class declaration.
    /// This should succeed (Req 12.1 — sealed on class is valid).
    /// </summary>
    private static Gen<SealedPlacementScenario> GenSealedOnClass()
    {
        return from typeIdx in Gen.Choose(0, TypeNames.Length - 1)
               let typeName = TypeNames[typeIdx]
               let source = $$"""
                   public sealed class {{typeName}}
                   {
                       private int value;
                   }
                   class Program
                   {
                       static void Main()
                       {
                       }
                   }
                   """
               select new SealedPlacementScenario(source,
                   $"Sealed on class declaration '{typeName}'",
                   shouldReject: false);
    }

    /// <summary>
    /// Generates a scenario where <c>sealed</c> is placed on a struct declaration.
    /// This should succeed (Req 12.1 — sealed on struct is valid).
    /// </summary>
    private static Gen<SealedPlacementScenario> GenSealedOnStruct()
    {
        return from typeIdx in Gen.Choose(0, TypeNames.Length - 1)
               let typeName = TypeNames[typeIdx]
               let source = $$"""
                   public sealed struct {{typeName}}
                   {
                       private int value;
                   }
                   class Program
                   {
                       static void Main()
                       {
                       }
                   }
                   """
               select new SealedPlacementScenario(source,
                   $"Sealed on struct declaration '{typeName}'",
                   shouldReject: false);
    }

    // ─── Combined generator ──────────────────────────────────────────────

    private static Gen<SealedPlacementScenario> GenScenario()
    {
        return Gen.OneOf(
            GenSealedOnTypeAlias(),
            GenSealedOnMethod(),
            GenSealedOnField(),
            GenSealedOnProperty(),
            GenSealedOnClass(),
            GenSealedOnStruct());
    }

    public static Arbitrary<SealedPlacementScenario> ArbitrarySealedPlacementScenario()
    {
        return GenScenario().ToArbitrary();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 16: Sealed is valid only on class and struct declarations
    //
    // For any declaration bearing the sealed modifier that is not a class or
    // struct type declaration -- a type alias, a method, or any other non-type
    // member -- an error (MIST_TC_0179) is reported. Sealed on a class or struct
    // declaration is accepted without a diagnostic.
    //
    // **Validates: Requirements 12.9, 13.3 (Property 16)**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(SealedPlacementPropertyTests) })]
    public Property SealedPlacement_RejectsNonTypeDeclarations(SealedPlacementScenario scenario)
    {
        var diagnostics = CompileAndAnalyze(scenario.Source);

        var hasSealedOnNonTypeError = HasSealedOnNonTypeError(diagnostics);

        if(scenario.ShouldReject)
        {
            // Sealed on alias/method/field/property should produce MIST_TC_0179
            return hasSealedOnNonTypeError.ToProperty()
                .Label($"Expected REJECT ({SealedOnNonTypeId}) for: {scenario.Description}" +
                       $"\nDiagnostics: {FormatDiagnostics(diagnostics)}");
        }
        else
        {
            // Sealed on class/struct should have no sealed-placement error
            return (!hasSealedOnNonTypeError).ToProperty()
                .Label($"Expected ACCEPT (no {SealedOnNonTypeId}) for: {scenario.Description}" +
                       $"\nDiagnostics: {FormatDiagnostics(diagnostics)}");
        }
    }

    // ─── Formatting helper ───────────────────────────────────────────────

    private static string FormatDiagnostics(ImmutableArray<Diagnostic> diagnostics)
    {
        if(diagnostics.IsEmpty)
        {
            return "(none)";
        }

        return string.Join("; ", diagnostics.Select(d => $"{d.Id.Value}[{d.Severity}]: {d.Message}"));
    }
}