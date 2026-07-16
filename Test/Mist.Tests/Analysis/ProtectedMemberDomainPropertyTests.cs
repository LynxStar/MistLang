// Feature: full-accessibility-model, Property 8: Protected members are reachable only from the declaring or derived types
//
// **Validates: Requirements 7.2, 7.4, 13.1**

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
/// Represents a scenario for testing protected member accessibility domains.
/// </summary>
public sealed class ProtectedMemberDomainScenario
{
    public string Source { get; }
    public string Description { get; }
    public bool ShouldReject { get; }

    public ProtectedMemberDomainScenario(string source, string description, bool shouldReject)
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
/// Property-based tests for protected member accessibility domain enforcement.
///
/// Property 8: For any <c>Protected</c> member, a reference from a location that is
/// neither the declaring type nor a type derived from the declaring type is reported
/// as an accessibility-violation error (MIST_TC_0171), and a reference from the
/// declaring type or a derived type is accepted without a warning.
///
/// The test generates Mist programs with protected members and reference sites from:
/// the declaring type (should succeed), a derived type (should succeed), and an
/// unrelated type (should produce MIST_TC_0171). It compiles in-process via the
/// standard lex/parse/build/analyze pipeline and asserts the appropriate diagnostics.
///
/// **Validates: Requirements 7.2, 7.4, 13.1**
/// </summary>
public class ProtectedMemberDomainPropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();
    private static readonly SemanticModelBuilder Builder = new();
    private static readonly TypeCheckerAnalyzer Analyzer = new();

    private const string ProtectedNonDerivedId = "MIST_TC_0171";

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

        return semBag.ToImmutableArray().AddRange(tcBag.ToImmutableArray());
    }

    private static bool HasProtectedNonDerivedError(ImmutableArray<Diagnostic> diagnostics)
    {
        return diagnostics.Any(d =>
            d.Id.Value == ProtectedNonDerivedId &&
            d.Severity == DiagnosticSeverity.Error);
    }

    private static bool HasProtectedInSealedWarning(ImmutableArray<Diagnostic> diagnostics)
    {
        return diagnostics.Any(d =>
            d.Id.Value == "MIST_TC_0180" &&
            d.Severity == DiagnosticSeverity.Warning);
    }

    // ─── Name pools ──────────────────────────────────────────────────────

    private static readonly string[] BaseTypeNames = ["Animal", "Vehicle", "Shape", "Widget", "Gadget"];
    private static readonly string[] DerivedTypeNames = ["Cat", "Car", "Circle", "Button", "Gizmo"];
    private static readonly string[] UnrelatedTypeNames = ["Logger", "Helper", "Utility", "Service", "Handler"];
    private static readonly string[] FieldNames = ["data", "state", "count", "level", "score"];
    private static readonly string[] MethodNames = ["Compute", "Process", "Transform", "Evaluate", "Calculate"];

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates a scenario where a protected field is accessed from within the declaring type.
    /// This should be accepted (Req 7.4 — reference from within the member's domain).
    /// </summary>
    private static Gen<ProtectedMemberDomainScenario> GenAccessFromDeclaringType_Field()
    {
        return from baseIdx in Gen.Choose(0, BaseTypeNames.Length - 1)
               from fieldIdx in Gen.Choose(0, FieldNames.Length - 1)
               let baseName = BaseTypeNames[baseIdx]
               let fieldName = FieldNames[fieldIdx]
               let source = $$"""
                   public class {{baseName}}
                   {
                       protected int {{fieldName}};

                       public {{baseName}}()
                       {
                           this.{{fieldName}} = 0;
                       }

                       public int Read()
                       {
                           return this.{{fieldName}};
                       }
                   }
                   class Program
                   {
                       static void Main()
                       {
                       }
                   }
                   """
               select new ProtectedMemberDomainScenario(source,
                   $"Protected field '{fieldName}' accessed from declaring type '{baseName}'",
                   shouldReject: false);
    }

    /// <summary>
    /// Generates a scenario where a protected method is accessed from within the declaring type.
    /// This should be accepted (Req 7.4 — reference from within the member's domain).
    /// </summary>
    private static Gen<ProtectedMemberDomainScenario> GenAccessFromDeclaringType_Method()
    {
        return from baseIdx in Gen.Choose(0, BaseTypeNames.Length - 1)
               from methodIdx in Gen.Choose(0, MethodNames.Length - 1)
               let baseName = BaseTypeNames[baseIdx]
               let methodName = MethodNames[methodIdx]
               let source = $$"""
                   public class {{baseName}}
                   {
                       protected int {{methodName}}()
                       {
                           return 42;
                       }

                       public int CallProtected()
                       {
                           return this.{{methodName}}();
                       }
                   }
                   class Program
                   {
                       static void Main()
                       {
                       }
                   }
                   """
               select new ProtectedMemberDomainScenario(source,
                   $"Protected method '{methodName}' accessed from declaring type '{baseName}'",
                   shouldReject: false);
    }

    /// <summary>
    /// Generates a scenario where a protected field is accessed from a derived type.
    /// This should be accepted (Req 7.2, 7.4 — derived type is within the protected domain).
    /// </summary>
    private static Gen<ProtectedMemberDomainScenario> GenAccessFromDerivedType_Field()
    {
        return from baseIdx in Gen.Choose(0, BaseTypeNames.Length - 1)
               from derivedIdx in Gen.Choose(0, DerivedTypeNames.Length - 1)
               from fieldIdx in Gen.Choose(0, FieldNames.Length - 1)
               let baseName = BaseTypeNames[baseIdx]
               let derivedName = DerivedTypeNames[derivedIdx]
               let fieldName = FieldNames[fieldIdx]
               let source = $$"""
                   public class {{baseName}}
                   {
                       protected int {{fieldName}};

                       public {{baseName}}()
                       {
                           this.{{fieldName}} = 0;
                       }
                   }
                   public class {{derivedName}} : {{baseName}}
                   {
                       public {{derivedName}}() : base()
                       {
                       }

                       public int ReadBase()
                       {
                           return this.{{fieldName}};
                       }
                   }
                   class Program
                   {
                       static void Main()
                       {
                       }
                   }
                   """
               select new ProtectedMemberDomainScenario(source,
                   $"Protected field '{fieldName}' accessed from derived type '{derivedName}' (base '{baseName}')",
                   shouldReject: false);
    }

    /// <summary>
    /// Generates a scenario where a protected method is accessed from a derived type.
    /// This should be accepted (Req 7.2, 7.4 — derived type is within the protected domain).
    /// </summary>
    private static Gen<ProtectedMemberDomainScenario> GenAccessFromDerivedType_Method()
    {
        return from baseIdx in Gen.Choose(0, BaseTypeNames.Length - 1)
               from derivedIdx in Gen.Choose(0, DerivedTypeNames.Length - 1)
               from methodIdx in Gen.Choose(0, MethodNames.Length - 1)
               let baseName = BaseTypeNames[baseIdx]
               let derivedName = DerivedTypeNames[derivedIdx]
               let methodName = MethodNames[methodIdx]
               let source = $$"""
                   public class {{baseName}}
                   {
                       protected int {{methodName}}()
                       {
                           return 42;
                       }
                   }
                   public class {{derivedName}} : {{baseName}}
                   {
                       public {{derivedName}}() : base()
                       {
                       }

                       public int CallBase()
                       {
                           return this.{{methodName}}();
                       }
                   }
                   class Program
                   {
                       static void Main()
                       {
                       }
                   }
                   """
               select new ProtectedMemberDomainScenario(source,
                   $"Protected method '{methodName}' accessed from derived type '{derivedName}' (base '{baseName}')",
                   shouldReject: false);
    }

    /// <summary>
    /// Generates a scenario where a protected field is accessed from an unrelated type.
    /// This should be rejected with MIST_TC_0171 (Req 7.2 — not declaring or derived).
    /// </summary>
    private static Gen<ProtectedMemberDomainScenario> GenAccessFromUnrelatedType_Field()
    {
        return from baseIdx in Gen.Choose(0, BaseTypeNames.Length - 1)
               from unrelatedIdx in Gen.Choose(0, UnrelatedTypeNames.Length - 1)
               from fieldIdx in Gen.Choose(0, FieldNames.Length - 1)
               let baseName = BaseTypeNames[baseIdx]
               let unrelatedName = UnrelatedTypeNames[unrelatedIdx]
               let fieldName = FieldNames[fieldIdx]
               let source = $$"""
                   public class {{baseName}}
                   {
                       protected int {{fieldName}};

                       public {{baseName}}()
                       {
                           this.{{fieldName}} = 0;
                       }
                   }
                   public class {{unrelatedName}}
                   {
                       public int TryRead({{baseName}} obj)
                       {
                           return obj.{{fieldName}};
                       }
                   }
                   class Program
                   {
                       static void Main()
                       {
                       }
                   }
                   """
               select new ProtectedMemberDomainScenario(source,
                   $"Protected field '{fieldName}' accessed from unrelated type '{unrelatedName}' (declaring '{baseName}')",
                   shouldReject: true);
    }

    /// <summary>
    /// Generates a scenario where a protected method is accessed from an unrelated type.
    /// This should be rejected with MIST_TC_0171 (Req 7.2 — not declaring or derived).
    /// </summary>
    private static Gen<ProtectedMemberDomainScenario> GenAccessFromUnrelatedType_Method()
    {
        return from baseIdx in Gen.Choose(0, BaseTypeNames.Length - 1)
               from unrelatedIdx in Gen.Choose(0, UnrelatedTypeNames.Length - 1)
               from methodIdx in Gen.Choose(0, MethodNames.Length - 1)
               let baseName = BaseTypeNames[baseIdx]
               let unrelatedName = UnrelatedTypeNames[unrelatedIdx]
               let methodName = MethodNames[methodIdx]
               let source = $$"""
                   public class {{baseName}}
                   {
                       protected int {{methodName}}()
                       {
                           return 42;
                       }
                   }
                   public class {{unrelatedName}}
                   {
                       public int TryCall({{baseName}} obj)
                       {
                           return obj.{{methodName}}();
                       }
                   }
                   class Program
                   {
                       static void Main()
                       {
                       }
                   }
                   """
               select new ProtectedMemberDomainScenario(source,
                   $"Protected method '{methodName}' accessed from unrelated type '{unrelatedName}' (declaring '{baseName}')",
                   shouldReject: true);
    }

    /// <summary>
    /// Generates a scenario where a protected field on a non-sealed type is accessed
    /// from the declaring type — verifying no spurious warning is emitted (Req 13.1).
    /// </summary>
    private static Gen<ProtectedMemberDomainScenario> GenProtectedOnNonSealed_NoWarning()
    {
        return from baseIdx in Gen.Choose(0, BaseTypeNames.Length - 1)
               from fieldIdx in Gen.Choose(0, FieldNames.Length - 1)
               let baseName = BaseTypeNames[baseIdx]
               let fieldName = FieldNames[fieldIdx]
               let source = $$"""
                   public class {{baseName}}
                   {
                       protected int {{fieldName}};

                       public {{baseName}}()
                       {
                           this.{{fieldName}} = 0;
                       }

                       public int GetValue()
                       {
                           return this.{{fieldName}};
                       }
                   }
                   class Program
                   {
                       static void Main()
                       {
                       }
                   }
                   """
               select new ProtectedMemberDomainScenario(source,
                   $"Protected field '{fieldName}' on non-sealed type '{baseName}' - no warning expected",
                   shouldReject: false);
    }

    // ─── Combined generator ──────────────────────────────────────────────

    private static Gen<ProtectedMemberDomainScenario> GenScenario()
    {
        return Gen.OneOf(
            GenAccessFromDeclaringType_Field(),
            GenAccessFromDeclaringType_Method(),
            GenAccessFromDerivedType_Field(),
            GenAccessFromDerivedType_Method(),
            GenAccessFromUnrelatedType_Field(),
            GenAccessFromUnrelatedType_Method(),
            GenProtectedOnNonSealed_NoWarning());
    }

    public static Arbitrary<ProtectedMemberDomainScenario> ArbitraryProtectedMemberDomainScenario()
    {
        return GenScenario().ToArbitrary();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 8: Protected members are reachable only from the declaring or
    // derived types.
    //
    // For any Protected member, a reference from a location that is neither the
    // declaring type nor a type derived from the declaring type is reported as an
    // accessibility-violation error (MIST_TC_0171), and a reference from the
    // declaring type or a derived type is accepted without a warning (protected on
    // a non-sealed type is fully enforced with no spurious diagnostic).
    //
    // **Validates: Requirements 7.2, 7.4, 13.1**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ProtectedMemberDomainPropertyTests) })]
    public Property ProtectedMember_AcceptRejectMatchesDomain(ProtectedMemberDomainScenario scenario)
    {
        var diagnostics = CompileAndAnalyze(scenario.Source);

        var hasProtectedError = HasProtectedNonDerivedError(diagnostics);
        var hasWarning = HasProtectedInSealedWarning(diagnostics);

        if(scenario.ShouldReject)
        {
            // Unrelated type access should produce MIST_TC_0171
            return hasProtectedError.ToProperty()
                .Label($"Expected REJECT ({ProtectedNonDerivedId}) for: {scenario.Description}" +
                       $"\nDiagnostics: {FormatDiagnostics(diagnostics)}");
        }
        else
        {
            // Declaring or derived type access should have no protected error and no warning
            var accepted = !hasProtectedError && !hasWarning;
            return accepted.ToProperty()
                .Label($"Expected ACCEPT (no {ProtectedNonDerivedId}, no warning) for: {scenario.Description}" +
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