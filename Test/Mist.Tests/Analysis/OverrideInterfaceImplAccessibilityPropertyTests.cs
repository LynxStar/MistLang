// Feature: full-accessibility-model, Property 12: Overrides and interface implementations obey position accessibility rules
//
// **Validates: Requirements 10.1, 10.2, 10.3**

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
/// Represents a scenario for testing override/interface-implementation accessibility rules.
/// </summary>
public sealed class OverrideInterfaceImplScenario
{
    public string Source { get; }
    public string Description { get; }
    public bool ShouldReject { get; }

    public OverrideInterfaceImplScenario(string source, string description, bool shouldReject)
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
/// Property-based tests for override and interface-implementation accessibility enforcement.
///
/// Property 12: For any overriding member or interface-implementing member, an
/// accessibility-violation error is reported if and only if its accessibility
/// violates the C# accessibility rule for that position.
///
/// In Mist, abstract types (interfaces) declare members that are implicitly public.
/// Therefore, implementing members MUST be public to satisfy the C# rule that interface
/// implementations are public (Req 10.2). A non-public implementation produces
/// MIST_TC_0175 (OverrideAccessibilityViolation).
///
/// The test generates Mist programs with types implementing abstract types (interfaces)
/// where the implementing member has various accessibility levels. It compiles in-process
/// via the standard pipeline and asserts the appropriate diagnostics.
///
/// **Validates: Requirements 10.1, 10.2, 10.3**
/// </summary>
public class OverrideInterfaceImplAccessibilityPropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();
    private static readonly SemanticModelBuilder Builder = new();
    private static readonly TypeCheckerAnalyzer Analyzer = new();

    private const string OverrideAccessibilityViolationId = "MIST_TC_0175";

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

    private static bool HasOverrideAccessibilityViolation(ImmutableArray<Diagnostic> diagnostics)
    {
        return diagnostics.Any(d =>
            d.Id.Value == OverrideAccessibilityViolationId &&
            d.Severity == DiagnosticSeverity.Error);
    }

    // ─── Name pools ──────────────────────────────────────────────────────

    private static readonly string[] AbstractTypeNames = ["IProcessor", "IHandler", "IService", "IWorker", "IProvider"];
    private static readonly string[] ImplTypeNames = ["Processor", "Handler", "Service", "Worker", "Provider"];
    private static readonly string[] MethodNames = ["Execute", "Handle", "Process", "Run", "Compute"];

    // Non-public modifiers that should produce MIST_TC_0175 when used on interface implementations
    private static readonly string[] NonPublicModifiers = ["private", "protected", "internal"];

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates a scenario where a class implements an abstract type method with public accessibility.
    /// This should be accepted (Req 10.2, 10.3 -- public implementation satisfies the interface contract).
    /// </summary>
    private static Gen<OverrideInterfaceImplScenario> GenPublicImplementation_Class()
    {
        return from absIdx in Gen.Choose(0, AbstractTypeNames.Length - 1)
               from implIdx in Gen.Choose(0, ImplTypeNames.Length - 1)
               from methodIdx in Gen.Choose(0, MethodNames.Length - 1)
               let absName = AbstractTypeNames[absIdx]
               let implName = ImplTypeNames[implIdx]
               let methodName = MethodNames[methodIdx]
               let source = $$"""
                   abstract {{absName}}
                   {
                       int {{methodName}}();
                   }
                   public class {{implName}} : {{absName}}
                   {
                       public int {{methodName}}()
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
               select new OverrideInterfaceImplScenario(source,
                   $"Public implementation of '{absName}.{methodName}' in class '{implName}' (should accept)",
                   shouldReject: false);
    }

    /// <summary>
    /// Generates a scenario where a class implements an abstract type method with a non-public modifier.
    /// This should be rejected (Req 10.2 -- interface implementations must be public).
    /// </summary>
    private static Gen<OverrideInterfaceImplScenario> GenNonPublicImplementation_Class()
    {
        return from absIdx in Gen.Choose(0, AbstractTypeNames.Length - 1)
               from implIdx in Gen.Choose(0, ImplTypeNames.Length - 1)
               from methodIdx in Gen.Choose(0, MethodNames.Length - 1)
               from modIdx in Gen.Choose(0, NonPublicModifiers.Length - 1)
               let absName = AbstractTypeNames[absIdx]
               let implName = ImplTypeNames[implIdx]
               let methodName = MethodNames[methodIdx]
               let modifier = NonPublicModifiers[modIdx]
               let source = $$"""
                   abstract {{absName}}
                   {
                       int {{methodName}}();
                   }
                   public class {{implName}} : {{absName}}
                   {
                       {{modifier}} int {{methodName}}()
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
               select new OverrideInterfaceImplScenario(source,
                   $"Non-public ({modifier}) implementation of '{absName}.{methodName}' in class '{implName}' (should reject)",
                   shouldReject: true);
    }

    /// <summary>
    /// Generates a scenario where a struct implements an abstract type method with public accessibility.
    /// This should be accepted (Req 10.2, 10.3 -- public implementation satisfies the interface contract).
    /// </summary>
    private static Gen<OverrideInterfaceImplScenario> GenPublicImplementation_Struct()
    {
        return from absIdx in Gen.Choose(0, AbstractTypeNames.Length - 1)
               from implIdx in Gen.Choose(0, ImplTypeNames.Length - 1)
               from methodIdx in Gen.Choose(0, MethodNames.Length - 1)
               let absName = AbstractTypeNames[absIdx]
               let implName = ImplTypeNames[implIdx]
               let methodName = MethodNames[methodIdx]
               let source = $$"""
                   abstract {{absName}}
                   {
                       int {{methodName}}();
                   }
                   public struct {{implName}} : {{absName}}
                   {
                       public int {{methodName}}()
                       {
                           return 99;
                       }
                   }
                   class Program
                   {
                       static void Main()
                       {
                       }
                   }
                   """
               select new OverrideInterfaceImplScenario(source,
                   $"Public implementation of '{absName}.{methodName}' in struct '{implName}' (should accept)",
                   shouldReject: false);
    }

    /// <summary>
    /// Generates a scenario where a struct implements an abstract type method with a non-public modifier.
    /// This should be rejected (Req 10.2 -- interface implementations must be public).
    /// </summary>
    private static Gen<OverrideInterfaceImplScenario> GenNonPublicImplementation_Struct()
    {
        return from absIdx in Gen.Choose(0, AbstractTypeNames.Length - 1)
               from implIdx in Gen.Choose(0, ImplTypeNames.Length - 1)
               from methodIdx in Gen.Choose(0, MethodNames.Length - 1)
               from modIdx in Gen.Choose(0, NonPublicModifiers.Length - 1)
               let absName = AbstractTypeNames[absIdx]
               let implName = ImplTypeNames[implIdx]
               let methodName = MethodNames[methodIdx]
               let modifier = NonPublicModifiers[modIdx]
               let source = $$"""
                   abstract {{absName}}
                   {
                       int {{methodName}}();
                   }
                   public struct {{implName}} : {{absName}}
                   {
                       {{modifier}} int {{methodName}}()
                       {
                           return 99;
                       }
                   }
                   class Program
                   {
                       static void Main()
                       {
                       }
                   }
                   """
               select new OverrideInterfaceImplScenario(source,
                   $"Non-public ({modifier}) implementation of '{absName}.{methodName}' in struct '{implName}' (should reject)",
                   shouldReject: true);
    }

    /// <summary>
    /// Generates a scenario where a class implements multiple abstract type methods all with public accessibility.
    /// This should be accepted (Req 10.2, 10.3).
    /// </summary>
    private static Gen<OverrideInterfaceImplScenario> GenMultiplePublicImplementations()
    {
        return from absIdx in Gen.Choose(0, AbstractTypeNames.Length - 1)
               from implIdx in Gen.Choose(0, ImplTypeNames.Length - 1)
               let absName = AbstractTypeNames[absIdx]
               let implName = ImplTypeNames[implIdx]
               let source = $$"""
                   abstract {{absName}}
                   {
                       int Execute();
                       int Handle();
                   }
                   public class {{implName}} : {{absName}}
                   {
                       public int Execute()
                       {
                           return 1;
                       }
                       public int Handle()
                       {
                           return 2;
                       }
                   }
                   class Program
                   {
                       static void Main()
                       {
                       }
                   }
                   """
               select new OverrideInterfaceImplScenario(source,
                   $"Multiple public implementations of '{absName}' methods in class '{implName}' (should accept)",
                   shouldReject: false);
    }

    /// <summary>
    /// Generates a scenario where a class implements an abstract method without an explicit modifier
    /// (defaults to private in Mist). This should be rejected (Req 10.2).
    /// </summary>
    private static Gen<OverrideInterfaceImplScenario> GenDefaultAccessibilityImplementation_Class()
    {
        return from absIdx in Gen.Choose(0, AbstractTypeNames.Length - 1)
               from implIdx in Gen.Choose(0, ImplTypeNames.Length - 1)
               from methodIdx in Gen.Choose(0, MethodNames.Length - 1)
               let absName = AbstractTypeNames[absIdx]
               let implName = ImplTypeNames[implIdx]
               let methodName = MethodNames[methodIdx]
               let source = $$"""
                   abstract {{absName}}
                   {
                       int {{methodName}}();
                   }
                   public class {{implName}} : {{absName}}
                   {
                       int {{methodName}}()
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
               select new OverrideInterfaceImplScenario(source,
                   $"Default (private) implementation of '{absName}.{methodName}' in class '{implName}' (should reject)",
                   shouldReject: true);
    }

    /// <summary>
    /// Generates a scenario where a struct implements an abstract method without an explicit modifier
    /// (defaults to private in Mist). This should be rejected (Req 10.2).
    /// </summary>
    private static Gen<OverrideInterfaceImplScenario> GenDefaultAccessibilityImplementation_Struct()
    {
        return from absIdx in Gen.Choose(0, AbstractTypeNames.Length - 1)
               from implIdx in Gen.Choose(0, ImplTypeNames.Length - 1)
               from methodIdx in Gen.Choose(0, MethodNames.Length - 1)
               let absName = AbstractTypeNames[absIdx]
               let implName = ImplTypeNames[implIdx]
               let methodName = MethodNames[methodIdx]
               let source = $$"""
                   abstract {{absName}}
                   {
                       int {{methodName}}();
                   }
                   public struct {{implName}} : {{absName}}
                   {
                       int {{methodName}}()
                       {
                           return 99;
                       }
                   }
                   class Program
                   {
                       static void Main()
                       {
                       }
                   }
                   """
               select new OverrideInterfaceImplScenario(source,
                   $"Default (private) implementation of '{absName}.{methodName}' in struct '{implName}' (should reject)",
                   shouldReject: true);
    }

    /// <summary>
    /// Generates a scenario where a class implements methods from two abstract types, all public.
    /// This should be accepted (Req 10.2, 10.3).
    /// </summary>
    private static Gen<OverrideInterfaceImplScenario> GenMultipleAbstractTypeImplementation_AllPublic()
    {
        return from implIdx in Gen.Choose(0, ImplTypeNames.Length - 1)
               let implName = ImplTypeNames[implIdx]
               let source = $$"""
                   abstract IReadable
                   {
                       int Read();
                   }
                   abstract IWritable
                   {
                       int Write();
                   }
                   public class {{implName}} : IReadable, IWritable
                   {
                       public int Read()
                       {
                           return 1;
                       }
                       public int Write()
                       {
                           return 2;
                       }
                   }
                   class Program
                   {
                       static void Main()
                       {
                       }
                   }
                   """
               select new OverrideInterfaceImplScenario(source,
                   $"Class '{implName}' implements two abstract types with all public methods (should accept)",
                   shouldReject: false);
    }

    /// <summary>
    /// Generates a scenario where a class implements methods from two abstract types
    /// but one is non-public. This should be rejected (Req 10.2).
    /// </summary>
    private static Gen<OverrideInterfaceImplScenario> GenMultipleAbstractTypeImplementation_OneNonPublic()
    {
        return from implIdx in Gen.Choose(0, ImplTypeNames.Length - 1)
               from modIdx in Gen.Choose(0, NonPublicModifiers.Length - 1)
               let implName = ImplTypeNames[implIdx]
               let modifier = NonPublicModifiers[modIdx]
               let source = $$"""
                   abstract IReadable
                   {
                       int Read();
                   }
                   abstract IWritable
                   {
                       int Write();
                   }
                   public class {{implName}} : IReadable, IWritable
                   {
                       public int Read()
                       {
                           return 1;
                       }
                       {{modifier}} int Write()
                       {
                           return 2;
                       }
                   }
                   class Program
                   {
                       static void Main()
                       {
                       }
                   }
                   """
               select new OverrideInterfaceImplScenario(source,
                   $"Class '{implName}' implements two abstract types, Write is {modifier} (should reject)",
                   shouldReject: true);
    }

    // ─── Combined generator ──────────────────────────────────────────────

    private static Gen<OverrideInterfaceImplScenario> GenScenario()
    {
        return Gen.OneOf(
            GenPublicImplementation_Class(),
            GenNonPublicImplementation_Class(),
            GenPublicImplementation_Struct(),
            GenNonPublicImplementation_Struct(),
            GenMultiplePublicImplementations(),
            GenDefaultAccessibilityImplementation_Class(),
            GenDefaultAccessibilityImplementation_Struct(),
            GenMultipleAbstractTypeImplementation_AllPublic(),
            GenMultipleAbstractTypeImplementation_OneNonPublic());
    }

    public static Arbitrary<OverrideInterfaceImplScenario> ArbitraryOverrideInterfaceImplScenario()
    {
        return GenScenario().ToArbitrary();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 12: Overrides and interface implementations obey position
    // accessibility rules.
    //
    // For any interface-implementing member, an accessibility-violation error
    // (MIST_TC_0175) is reported if and only if its accessibility violates the
    // C# accessibility rule for that position:
    // - Interface implementations must be public (Req 10.2)
    // - Abstract members in Mist are implicitly public, so implementing members
    //   MUST be public
    //
    // **Validates: Requirements 10.1, 10.2, 10.3**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(OverrideInterfaceImplAccessibilityPropertyTests) })]
    public Property OverrideAndInterfaceImpl_AcceptRejectMatchesPositionRules(OverrideInterfaceImplScenario scenario)
    {
        var diagnostics = CompileAndAnalyze(scenario.Source);

        var hasOverrideViolation = HasOverrideAccessibilityViolation(diagnostics);

        if(scenario.ShouldReject)
        {
            return hasOverrideViolation.ToProperty()
                .Label($"Expected REJECT ({OverrideAccessibilityViolationId}) for: {scenario.Description}" +
                       $"\nDiagnostics: {FormatDiagnostics(diagnostics)}");
        }
        else
        {
            return (!hasOverrideViolation).ToProperty()
                .Label($"Expected ACCEPT (no {OverrideAccessibilityViolationId}) for: {scenario.Description}" +
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