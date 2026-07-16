// Feature: full-accessibility-model, Property 14: Deriving from a sealed type is rejected, from a non-sealed type is accepted
//
// **Validates: Requirements 12.1, 12.2, 12.3, 12.4 (Property 14)**

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
/// Property-based tests for sealed inheritance enforcement.
///
/// Property 14: For any type, declaring a sealed class or sealed struct — whether
/// user-declared or projected from an External_Assembly — as its base type is reported
/// as a sealed-inheritance error (MIST_TC_0177), while declaring a non-sealed base type
/// is accepted without a sealed-inheritance diagnostic; and declaring a class or struct
/// with the <c>sealed</c> modifier is itself accepted without a diagnostic.
///
/// The test generates Mist programs with sealed and non-sealed classes/structs as base
/// types, compiles in-process via the standard pipeline with no process spawn, and asserts
/// that MIST_TC_0177 is reported exactly when deriving from a sealed type.
///
/// **Validates: Requirements 12.1, 12.2, 12.3, 12.4 (Property 14)**
/// </summary>
public class SealedInheritancePropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();
    private static readonly SemanticModelBuilder Builder = new();
    private static readonly TypeCheckerAnalyzer Analyzer = new();

    private const string SealedInheritanceId = "MIST_TC_0177";

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

    private static bool HasSealedInheritanceError(ImmutableArray<Diagnostic> diagnostics)
    {
        return diagnostics.Any(d =>
            d.Id.Value == SealedInheritanceId &&
            d.Severity == DiagnosticSeverity.Error);
    }

    private static string MainClass()
    {
        return @"
class Program {
    public static void Main() { }
}";
    }

    private static string FormatDiagnostics(ImmutableArray<Diagnostic> diagnostics)
    {
        if(diagnostics.IsEmpty)
        {
            return "(none)";
        }

        return string.Join("; ", diagnostics.Select(d => $"{d.Id.Value}[{d.Severity}]: {d.Message}"));
    }

    // ─── Scenario type ───────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// A generated scenario for testing sealed inheritance enforcement.
    /// </summary>
    public sealed record SealedInheritanceScenario(
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

    private static readonly string[] BaseClassNames = ["Animal", "Vehicle", "Shape", "Widget", "Gadget"];
    private static readonly string[] DerivedClassNames = ["Cat", "Car", "Circle", "Button", "Gizmo"];
    private static readonly string[] BaseStructNames = ["Vector", "Point", "Color", "Coord", "Pixel"];
    private static readonly string[] DerivedStructNames = ["UnitVec", "Origin", "Red", "Center", "Dot"];

    // ─── Generators ──────────────────────────────────────────────────────

    // ─── REJECT scenarios: deriving from a sealed type ────────────────────

    /// <summary>
    /// Generates scenarios where a class derives from a sealed class.
    /// Sealed-inheritance error (MIST_TC_0177) should be reported (Req 12.2).
    /// </summary>
    private static Gen<SealedInheritanceScenario> GenDeriveFromSealedClass()
    {
        return from baseIdx in Gen.Choose(0, BaseClassNames.Length - 1)
               from derivedIdx in Gen.Choose(0, DerivedClassNames.Length - 1)
               let baseName = BaseClassNames[baseIdx]
               let derivedName = DerivedClassNames[derivedIdx]
               let source = $$"""
                   public sealed class {{baseName}}
                   {
                       public {{baseName}}()
                       {
                       }
                   }
                   public class {{derivedName}} : {{baseName}}
                   {
                       public {{derivedName}}() : base()
                       {
                       }
                   }
                   """ + MainClass()
               select new SealedInheritanceScenario(source,
                   $"Class '{derivedName}' derives from sealed class '{baseName}'",
                   ShouldReject: true);
    }

    /// <summary>
    /// Generates scenarios where a class derives from a sealed struct.
    /// Sealed-inheritance error (MIST_TC_0177) should be reported (Req 12.2).
    /// Mist structs are inheritable, and sealing one prevents further derivation.
    /// </summary>
    private static Gen<SealedInheritanceScenario> GenDeriveFromSealedStruct()
    {
        return from baseIdx in Gen.Choose(0, BaseStructNames.Length - 1)
               from derivedIdx in Gen.Choose(0, DerivedStructNames.Length - 1)
               let baseName = BaseStructNames[baseIdx]
               let derivedName = DerivedStructNames[derivedIdx]
               let source = $$"""
                   public sealed struct {{baseName}}
                   {
                       public int x;
                       public {{baseName}}(int x)
                       {
                           this.x = x;
                       }
                   }
                   public struct {{derivedName}} : {{baseName}}
                   {
                       public {{derivedName}}() : base(1)
                       {
                       }
                   }
                   """ + MainClass()
               select new SealedInheritanceScenario(source,
                   $"Struct '{derivedName}' derives from sealed struct '{baseName}'",
                   ShouldReject: true);
    }

    /// <summary>
    /// Generates scenarios where a class derives from a sealed struct.
    /// In Mist, ClassType.BaseType can resolve from any IType including StructType
    /// but the base list in a class context resolves through LookupWithUsings.
    /// However, in the general case the sealed check fires when the base IS resolved
    /// and IsSealed=true. We test class-to-sealed-class and struct-to-sealed-struct.
    /// This generator produces a struct deriving from another sealed struct (Req 12.2, 12.4).
    /// </summary>
    private static Gen<SealedInheritanceScenario> GenStructDerivesFromSealedStruct()
    {
        return from baseIdx in Gen.Choose(0, BaseStructNames.Length - 1)
               from derivedIdx in Gen.Choose(0, DerivedStructNames.Length - 1)
               from accessKw in Gen.Elements("public", "internal")
               let baseName = BaseStructNames[baseIdx]
               let derivedName = DerivedStructNames[derivedIdx]
               let source = $$"""
                   {{accessKw}} sealed struct {{baseName}}
                   {
                       public int x;
                       public {{baseName}}(int x)
                       {
                           this.x = x;
                       }
                   }
                   {{accessKw}} struct {{derivedName}} : {{baseName}}
                   {
                       public {{derivedName}}() : base(1)
                       {
                       }
                   }
                   """ + MainClass()
               select new SealedInheritanceScenario(source,
                   $"Struct '{derivedName}' derives from {accessKw} sealed struct '{baseName}'",
                   ShouldReject: true);
    }

    /// <summary>
    /// Generates scenarios where a class derives from a sealed class that itself
    /// has an internal or private accessibility. The sealed enforcement applies
    /// regardless of the base type's accessibility (Req 12.2, 12.4).
    /// </summary>
    private static Gen<SealedInheritanceScenario> GenDeriveFromSealedClassWithAccessibility()
    {
        return from baseIdx in Gen.Choose(0, BaseClassNames.Length - 1)
               from derivedIdx in Gen.Choose(0, DerivedClassNames.Length - 1)
               from accessKw in Gen.Elements("public", "internal")
               let baseName = BaseClassNames[baseIdx]
               let derivedName = DerivedClassNames[derivedIdx]
               let source = $$"""
                   {{accessKw}} sealed class {{baseName}}
                   {
                       public {{baseName}}()
                       {
                       }
                   }
                   {{accessKw}} class {{derivedName}} : {{baseName}}
                   {
                       public {{derivedName}}() : base()
                       {
                       }
                   }
                   """ + MainClass()
               select new SealedInheritanceScenario(source,
                   $"{accessKw} class '{derivedName}' derives from {accessKw} sealed class '{baseName}'",
                   ShouldReject: true);
    }

    // ─── ACCEPT scenarios: non-sealed bases ──────────────────────────────

    /// <summary>
    /// Generates scenarios where a class derives from a non-sealed class.
    /// No sealed-inheritance diagnostic should be reported (Req 12.3).
    /// </summary>
    private static Gen<SealedInheritanceScenario> GenDeriveFromNonSealedClass()
    {
        return from baseIdx in Gen.Choose(0, BaseClassNames.Length - 1)
               from derivedIdx in Gen.Choose(0, DerivedClassNames.Length - 1)
               let baseName = BaseClassNames[baseIdx]
               let derivedName = DerivedClassNames[derivedIdx]
               let source = $$"""
                   public class {{baseName}}
                   {
                       public {{baseName}}()
                       {
                       }
                   }
                   public class {{derivedName}} : {{baseName}}
                   {
                       public {{derivedName}}() : base()
                       {
                       }
                   }
                   """ + MainClass()
               select new SealedInheritanceScenario(source,
                   $"Class '{derivedName}' derives from non-sealed class '{baseName}'",
                   ShouldReject: false);
    }

    /// <summary>
    /// Generates scenarios where a struct derives from a non-sealed struct.
    /// No sealed-inheritance diagnostic should be reported (Req 12.3).
    /// </summary>
    private static Gen<SealedInheritanceScenario> GenDeriveFromNonSealedStruct()
    {
        return from baseIdx in Gen.Choose(0, BaseStructNames.Length - 1)
               from derivedIdx in Gen.Choose(0, DerivedStructNames.Length - 1)
               let baseName = BaseStructNames[baseIdx]
               let derivedName = DerivedStructNames[derivedIdx]
               let source = $$"""
                   public struct {{baseName}}
                   {
                       public int x;
                       public {{baseName}}(int x)
                       {
                           this.x = x;
                       }
                   }
                   public struct {{derivedName}} : {{baseName}}
                   {
                       public {{derivedName}}() : base(1)
                       {
                       }
                   }
                   """ + MainClass()
               select new SealedInheritanceScenario(source,
                   $"Struct '{derivedName}' derives from non-sealed struct '{baseName}'",
                   ShouldReject: false);
    }

    /// <summary>
    /// Generates scenarios where a class derives from a non-sealed class
    /// with various accessibility modifiers.
    /// No sealed-inheritance diagnostic should be reported (Req 12.3).
    /// </summary>
    private static Gen<SealedInheritanceScenario> GenDeriveFromNonSealedClassWithAccessibility()
    {
        return from baseIdx in Gen.Choose(0, BaseClassNames.Length - 1)
               from derivedIdx in Gen.Choose(0, DerivedClassNames.Length - 1)
               from accessKw in Gen.Elements("public", "internal")
               let baseName = BaseClassNames[baseIdx]
               let derivedName = DerivedClassNames[derivedIdx]
               let source = $$"""
                   {{accessKw}} class {{baseName}}
                   {
                       public {{baseName}}()
                       {
                       }
                   }
                   {{accessKw}} class {{derivedName}} : {{baseName}}
                   {
                       public {{derivedName}}() : base()
                       {
                       }
                   }
                   """ + MainClass()
               select new SealedInheritanceScenario(source,
                   $"{accessKw} class '{derivedName}' derives from non-sealed {accessKw} class '{baseName}'",
                   ShouldReject: false);
    }

    // ─── ACCEPT scenarios: sealed declarations themselves ─────────────────

    /// <summary>
    /// Generates scenarios where a class is declared with the sealed modifier
    /// (with no inheritance). The sealed declaration itself should be accepted
    /// without any diagnostic (Req 12.1).
    /// </summary>
    private static Gen<SealedInheritanceScenario> GenSealedClassDeclaration()
    {
        return from nameIdx in Gen.Choose(0, BaseClassNames.Length - 1)
               from accessKw in Gen.Elements("public", "internal")
               let name = BaseClassNames[nameIdx]
               let source = $$"""
                   {{accessKw}} sealed class {{name}}
                   {
                       public {{name}}()
                       {
                       }
                   }
                   """ + MainClass()
               select new SealedInheritanceScenario(source,
                   $"{accessKw} sealed class '{name}' declaration (no derivation)",
                   ShouldReject: false);
    }

    /// <summary>
    /// Generates scenarios where a struct is declared with the sealed modifier
    /// (with no inheritance). The sealed declaration itself should be accepted
    /// without any diagnostic (Req 12.1).
    /// </summary>
    private static Gen<SealedInheritanceScenario> GenSealedStructDeclaration()
    {
        return from nameIdx in Gen.Choose(0, BaseStructNames.Length - 1)
               from accessKw in Gen.Elements("public", "internal")
               let name = BaseStructNames[nameIdx]
               let source = $$"""
                   {{accessKw}} sealed struct {{name}}
                   {
                       public int x;
                       public {{name}}(int x)
                       {
                           this.x = x;
                       }
                   }
                   """ + MainClass()
               select new SealedInheritanceScenario(source,
                   $"{accessKw} sealed struct '{name}' declaration (no derivation)",
                   ShouldReject: false);
    }

    /// <summary>
    /// Generates scenarios where a sealed class has methods and fields but
    /// nothing derives from it. Should compile cleanly (Req 12.1).
    /// </summary>
    private static Gen<SealedInheritanceScenario> GenSealedClassWithMembers()
    {
        return from nameIdx in Gen.Choose(0, BaseClassNames.Length - 1)
               let name = BaseClassNames[nameIdx]
               let source = $$"""
                   public sealed class {{name}}
                   {
                       private int value;
                       public {{name}}(int v)
                       {
                           this.value = v;
                       }
                       public int GetValue()
                       {
                           return value;
                       }
                   }
                   """ + MainClass()
               select new SealedInheritanceScenario(source,
                   $"Sealed class '{name}' with members, no derivation",
                   ShouldReject: false);
    }

    // ─── Combined generator ──────────────────────────────────────────────

    private static Gen<SealedInheritanceScenario> GenScenario()
    {
        return Gen.OneOf(
            // REJECT: deriving from sealed types
            GenDeriveFromSealedClass(),
            GenDeriveFromSealedStruct(),
            GenStructDerivesFromSealedStruct(),
            GenDeriveFromSealedClassWithAccessibility(),
            // ACCEPT: deriving from non-sealed types
            GenDeriveFromNonSealedClass(),
            GenDeriveFromNonSealedStruct(),
            GenDeriveFromNonSealedClassWithAccessibility(),
            // ACCEPT: sealed declarations themselves
            GenSealedClassDeclaration(),
            GenSealedStructDeclaration(),
            GenSealedClassWithMembers());
    }

    public static Arbitrary<SealedInheritanceScenario> ArbitrarySealedInheritanceScenario()
    {
        return GenScenario().ToArbitrary();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 14: Deriving from a sealed type is rejected, from a non-sealed
    // type is accepted.
    //
    // For any type, declaring a sealed class or sealed struct as its base type
    // is reported as a sealed-inheritance error (MIST_TC_0177), while declaring
    // a non-sealed base type is accepted without a sealed-inheritance diagnostic;
    // and declaring a class or struct with the sealed modifier is itself accepted
    // without a diagnostic.
    //
    // **Validates: Requirements 12.1, 12.2, 12.3, 12.4**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(SealedInheritancePropertyTests) })]
    public Property SealedInheritance_RejectSealedBase_AcceptNonSealedBase(SealedInheritanceScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        var hasSealedError = HasSealedInheritanceError(diagnostics);

        if(scenario.ShouldReject)
        {
            return hasSealedError.ToProperty()
                .Label($"Expected REJECT ({SealedInheritanceId}) for: {scenario.Description}" +
                       $"\nDiagnostics: {FormatDiagnostics(diagnostics)}");
        }
        else
        {
            // Should NOT have a sealed-inheritance error. We also verify there's
            // no unrelated error that would mask the accept path (allowing only
            // warnings, e.g., the protected-in-sealed warning which is not tested here).
            var hasUnrelatedErrors = diagnostics.Any(d =>
                d.Severity == DiagnosticSeverity.Error &&
                d.Id.Value != SealedInheritanceId);

            return ((!hasSealedError) & (!hasUnrelatedErrors)).ToProperty()
                .Label($"Expected ACCEPT (no {SealedInheritanceId} and no errors) for: {scenario.Description}" +
                       $"\nDiagnostics: {FormatDiagnostics(diagnostics)}");
        }
    }
}