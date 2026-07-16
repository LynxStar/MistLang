// Feature: full-accessibility-model, Property 17: A protected member in a sealed type compiles with exactly one warning
//
// **Validates: Requirements 13.1, 13.2 (Property 17)**

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
/// Represents a scenario for testing the protected-in-sealed warning behavior.
/// </summary>
public sealed class ProtectedInSealedScenario
{
    public string Source { get; }
    public string Description { get; }
    public bool IsSealed { get; }
    public int ProtectedMemberCount { get; }

    public ProtectedInSealedScenario(string source, string description, bool isSealed, int protectedMemberCount)
    {
        Source = source;
        Description = description;
        IsSealed = isSealed;
        ProtectedMemberCount = protectedMemberCount;
    }

    public override string ToString()
    {
        return $"[{(IsSealed ? "SEALED" : "NON-SEALED")} {ProtectedMemberCount} protected] {Description}";
    }
}

/// <summary>
/// Property-based tests for the protected-in-sealed warning diagnostic.
///
/// Property 17: For any sealed class or sealed struct that declares a <c>Protected</c>
/// member, the declaration compiles and exactly one warning-severity diagnostic
/// (<c>MIST_TC_0180</c>) is reported for each such member; for any non-sealed class or
/// struct with a <c>Protected</c> member, no such warning is reported.
///
/// The test generates Mist programs with sealed and non-sealed types containing
/// protected members. It compiles in-process via the standard lex/parse/build/analyze
/// pipeline and asserts the appropriate warning count.
///
/// **Validates: Requirements 13.1, 13.2 (Property 17)**
/// </summary>
public class ProtectedInSealedWarningPropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();
    private static readonly SemanticModelBuilder Builder = new();
    private static readonly TypeCheckerAnalyzer Analyzer = new();

    private const string ProtectedInSealedId = "MIST_TC_0180";

    // --- Helpers ---

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

    private static int CountProtectedInSealedWarnings(ImmutableArray<Diagnostic> diagnostics)
    {
        return diagnostics.Count(d =>
            d.Id.Value == ProtectedInSealedId &&
            d.Severity == DiagnosticSeverity.Warning);
    }

    private static bool HasErrors(ImmutableArray<Diagnostic> diagnostics)
    {
        return diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
    }

    // --- Name pools ---

    private static readonly string[] TypeNames = ["Container", "Wrapper", "Holder", "Storage", "Registry"];
    private static readonly string[] FieldNames = ["data", "state", "value", "level", "count"];
    private static readonly string[] MethodNames = ["Compute", "Process", "Transform", "Evaluate", "Calculate"];

    // --- Generators ---

    /// <summary>
    /// Generates a sealed class with one protected field.
    /// Should compile with exactly one MIST_TC_0180 warning.
    /// </summary>
    private static Gen<ProtectedInSealedScenario> GenSealedClass_OneProtectedField()
    {
        return from typeIdx in Gen.Choose(0, TypeNames.Length - 1)
               from fieldIdx in Gen.Choose(0, FieldNames.Length - 1)
               let typeName = TypeNames[typeIdx]
               let fieldName = FieldNames[fieldIdx]
               let source = $$"""
                   public sealed class {{typeName}}
                   {
                       protected int {{fieldName}};

                       public {{typeName}}()
                       {
                           this.{{fieldName}} = 0;
                       }
                   }
                   class Program
                   {
                       static void Main()
                       {
                       }
                   }
                   """
               select new ProtectedInSealedScenario(source,
                   $"Sealed class '{typeName}' with one protected field '{fieldName}'",
                   isSealed: true, protectedMemberCount: 1);
    }

    /// <summary>
    /// Generates a sealed class with one protected method.
    /// Should compile with exactly one MIST_TC_0180 warning.
    /// </summary>
    private static Gen<ProtectedInSealedScenario> GenSealedClass_OneProtectedMethod()
    {
        return from typeIdx in Gen.Choose(0, TypeNames.Length - 1)
               from methodIdx in Gen.Choose(0, MethodNames.Length - 1)
               let typeName = TypeNames[typeIdx]
               let methodName = MethodNames[methodIdx]
               let source = $$"""
                   public sealed class {{typeName}}
                   {
                       protected int {{methodName}}()
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
               select new ProtectedInSealedScenario(source,
                   $"Sealed class '{typeName}' with one protected method '{methodName}'",
                   isSealed: true, protectedMemberCount: 1);
    }

    /// <summary>
    /// Generates a sealed struct with one protected field.
    /// Should compile with exactly one MIST_TC_0180 warning.
    /// </summary>
    private static Gen<ProtectedInSealedScenario> GenSealedStruct_OneProtectedField()
    {
        return from typeIdx in Gen.Choose(0, TypeNames.Length - 1)
               from fieldIdx in Gen.Choose(0, FieldNames.Length - 1)
               let typeName = TypeNames[typeIdx]
               let fieldName = FieldNames[fieldIdx]
               let source = $$"""
                   public sealed struct {{typeName}}
                   {
                       protected int {{fieldName}};

                       public {{typeName}}()
                       {
                           this.{{fieldName}} = 0;
                       }
                   }
                   class Program
                   {
                       static void Main()
                       {
                       }
                   }
                   """
               select new ProtectedInSealedScenario(source,
                   $"Sealed struct '{typeName}' with one protected field '{fieldName}'",
                   isSealed: true, protectedMemberCount: 1);
    }

    /// <summary>
    /// Generates a sealed struct with one protected method.
    /// Should compile with exactly one MIST_TC_0180 warning.
    /// </summary>
    private static Gen<ProtectedInSealedScenario> GenSealedStruct_OneProtectedMethod()
    {
        return from typeIdx in Gen.Choose(0, TypeNames.Length - 1)
               from methodIdx in Gen.Choose(0, MethodNames.Length - 1)
               let typeName = TypeNames[typeIdx]
               let methodName = MethodNames[methodIdx]
               let source = $$"""
                   public sealed struct {{typeName}}
                   {
                       protected int {{methodName}}()
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
               select new ProtectedInSealedScenario(source,
                   $"Sealed struct '{typeName}' with one protected method '{methodName}'",
                   isSealed: true, protectedMemberCount: 1);
    }

    /// <summary>
    /// Generates a sealed class with two protected members.
    /// Should compile with exactly two MIST_TC_0180 warnings.
    /// </summary>
    private static Gen<ProtectedInSealedScenario> GenSealedClass_TwoProtectedMembers()
    {
        return from typeIdx in Gen.Choose(0, TypeNames.Length - 1)
               from fieldIdx in Gen.Choose(0, FieldNames.Length - 1)
               from methodIdx in Gen.Choose(0, MethodNames.Length - 1)
               let typeName = TypeNames[typeIdx]
               let fieldName = FieldNames[fieldIdx]
               let methodName = MethodNames[methodIdx]
               let source = $$"""
                   public sealed class {{typeName}}
                   {
                       protected int {{fieldName}};
                       protected int {{methodName}}()
                       {
                           return 42;
                       }

                       public {{typeName}}()
                       {
                           this.{{fieldName}} = 0;
                       }
                   }
                   class Program
                   {
                       static void Main()
                       {
                       }
                   }
                   """
               select new ProtectedInSealedScenario(source,
                   $"Sealed class '{typeName}' with two protected members (field '{fieldName}', method '{methodName}')",
                   isSealed: true, protectedMemberCount: 2);
    }

    /// <summary>
    /// Generates a non-sealed class with one protected field.
    /// Should compile with no MIST_TC_0180 warning (Req 13.1).
    /// </summary>
    private static Gen<ProtectedInSealedScenario> GenNonSealedClass_ProtectedField()
    {
        return from typeIdx in Gen.Choose(0, TypeNames.Length - 1)
               from fieldIdx in Gen.Choose(0, FieldNames.Length - 1)
               let typeName = TypeNames[typeIdx]
               let fieldName = FieldNames[fieldIdx]
               let source = $$"""
                   public class {{typeName}}
                   {
                       protected int {{fieldName}};

                       public {{typeName}}()
                       {
                           this.{{fieldName}} = 0;
                       }
                   }
                   class Program
                   {
                       static void Main()
                       {
                       }
                   }
                   """
               select new ProtectedInSealedScenario(source,
                   $"Non-sealed class '{typeName}' with protected field '{fieldName}'",
                   isSealed: false, protectedMemberCount: 1);
    }

    /// <summary>
    /// Generates a non-sealed class with one protected method.
    /// Should compile with no MIST_TC_0180 warning (Req 13.1).
    /// </summary>
    private static Gen<ProtectedInSealedScenario> GenNonSealedClass_ProtectedMethod()
    {
        return from typeIdx in Gen.Choose(0, TypeNames.Length - 1)
               from methodIdx in Gen.Choose(0, MethodNames.Length - 1)
               let typeName = TypeNames[typeIdx]
               let methodName = MethodNames[methodIdx]
               let source = $$"""
                   public class {{typeName}}
                   {
                       protected int {{methodName}}()
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
               select new ProtectedInSealedScenario(source,
                   $"Non-sealed class '{typeName}' with protected method '{methodName}'",
                   isSealed: false, protectedMemberCount: 1);
    }

    /// <summary>
    /// Generates a non-sealed struct with one protected field.
    /// Should compile with no MIST_TC_0180 warning (Req 13.1).
    /// </summary>
    private static Gen<ProtectedInSealedScenario> GenNonSealedStruct_ProtectedField()
    {
        return from typeIdx in Gen.Choose(0, TypeNames.Length - 1)
               from fieldIdx in Gen.Choose(0, FieldNames.Length - 1)
               let typeName = TypeNames[typeIdx]
               let fieldName = FieldNames[fieldIdx]
               let source = $$"""
                   public struct {{typeName}}
                   {
                       protected int {{fieldName}};

                       public {{typeName}}()
                       {
                           this.{{fieldName}} = 0;
                       }
                   }
                   class Program
                   {
                       static void Main()
                       {
                       }
                   }
                   """
               select new ProtectedInSealedScenario(source,
                   $"Non-sealed struct '{typeName}' with protected field '{fieldName}'",
                   isSealed: false, protectedMemberCount: 1);
    }

    /// <summary>
    /// Generates a non-sealed struct with one protected method.
    /// Should compile with no MIST_TC_0180 warning (Req 13.1).
    /// </summary>
    private static Gen<ProtectedInSealedScenario> GenNonSealedStruct_ProtectedMethod()
    {
        return from typeIdx in Gen.Choose(0, TypeNames.Length - 1)
               from methodIdx in Gen.Choose(0, MethodNames.Length - 1)
               let typeName = TypeNames[typeIdx]
               let methodName = MethodNames[methodIdx]
               let source = $$"""
                   public struct {{typeName}}
                   {
                       protected int {{methodName}}()
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
               select new ProtectedInSealedScenario(source,
                   $"Non-sealed struct '{typeName}' with protected method '{methodName}'",
                   isSealed: false, protectedMemberCount: 1);
    }

    // --- Combined generator ---

    private static Gen<ProtectedInSealedScenario> GenScenario()
    {
        return Gen.OneOf(
            GenSealedClass_OneProtectedField(),
            GenSealedClass_OneProtectedMethod(),
            GenSealedStruct_OneProtectedField(),
            GenSealedStruct_OneProtectedMethod(),
            GenSealedClass_TwoProtectedMembers(),
            GenNonSealedClass_ProtectedField(),
            GenNonSealedClass_ProtectedMethod(),
            GenNonSealedStruct_ProtectedField(),
            GenNonSealedStruct_ProtectedMethod());
    }

    public static Arbitrary<ProtectedInSealedScenario> ArbitraryProtectedInSealedScenario()
    {
        return GenScenario().ToArbitrary();
    }

    // =========================================================================
    // Property 17: A protected member in a sealed type compiles with exactly
    // one warning.
    //
    // For any sealed class or sealed struct that declares a Protected member, the
    // declaration compiles and exactly one warning-severity diagnostic (MIST_TC_0180)
    // is reported for each such protected member; for any non-sealed class or struct
    // with a Protected member, no such warning is reported.
    //
    // **Validates: Requirements 13.1, 13.2 (Property 17)**
    // =========================================================================

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ProtectedInSealedWarningPropertyTests) })]
    public Property ProtectedInSealed_WarningCountMatchesExpectation(ProtectedInSealedScenario scenario)
    {
        var diagnostics = CompileAndAnalyze(scenario.Source);
        var warningCount = CountProtectedInSealedWarnings(diagnostics);
        var hasCompileErrors = HasErrors(diagnostics);

        if(scenario.IsSealed)
        {
            // Sealed type with protected members should compile (no errors)
            // and produce exactly one warning per protected member.
            var compilesSuccessfully = !hasCompileErrors;
            var correctWarningCount = warningCount == scenario.ProtectedMemberCount;

            return (compilesSuccessfully && correctWarningCount).ToProperty()
                .Label($"Expected: compiles with {scenario.ProtectedMemberCount} MIST_TC_0180 warning(s)" +
                       $"\nActual: errors={hasCompileErrors}, warnings={warningCount}" +
                       $"\nScenario: {scenario.Description}" +
                       $"\nDiagnostics: {FormatDiagnostics(diagnostics)}");
        }
        else
        {
            // Non-sealed type with protected members should produce no
            // MIST_TC_0180 warning (Req 13.1 — fully enforced, no spurious diagnostic).
            return (warningCount == 0).ToProperty()
                .Label($"Expected: no MIST_TC_0180 warning for non-sealed type" +
                       $"\nActual: warnings={warningCount}" +
                       $"\nScenario: {scenario.Description}" +
                       $"\nDiagnostics: {FormatDiagnostics(diagnostics)}");
        }
    }

    // --- Formatting helper ---

    private static string FormatDiagnostics(ImmutableArray<Diagnostic> diagnostics)
    {
        if(diagnostics.IsEmpty)
        {
            return "(none)";
        }

        return string.Join("; ", diagnostics.Select(d => $"{d.Id.Value}[{d.Severity}]: {d.Message}"));
    }
}