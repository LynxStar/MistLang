// Feature: full-accessibility-model, Property 11: Base types and interfaces are at least as accessible as the deriving type
//
// **Validates: Requirements 9.1, 9.2, 9.3, 9.4**

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
/// Represents a scenario for testing base-type and interface accessibility enforcement.
/// </summary>
public sealed class BaseTypeAccessibilityScenario
{
    public string Source { get; }
    public string Description { get; }
    public bool ShouldReject { get; }

    public BaseTypeAccessibilityScenario(string source, string description, bool shouldReject)
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
/// Property-based tests for base-type and interface accessibility enforcement.
///
/// Property 11: For any type, a base/interface accessibility error is reported if and
/// only if its base class, an implemented interface, or a base interface is not at
/// least as accessible as the type itself.
///
/// The test generates Mist programs with type hierarchies where the base type/interface
/// has various accessibility levels relative to the derived type. It compiles in-process
/// via the standard lex/parse/build/analyze pipeline and asserts that MIST_TC_0174
/// (BaseLessAccessible) is reported exactly when the base is less accessible.
///
/// **Validates: Requirements 9.1, 9.2, 9.3, 9.4**
/// </summary>
public class BaseTypeInterfaceAccessibilityPropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();
    private static readonly SemanticModelBuilder Builder = new();
    private static readonly TypeCheckerAnalyzer Analyzer = new();

    private const string BaseLessAccessibleId = "MIST_TC_0174";

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

    private static bool HasBaseLessAccessibleError(ImmutableArray<Diagnostic> diagnostics)
    {
        return diagnostics.Any(d =>
            d.Id.Value == BaseLessAccessibleId &&
            d.Severity == DiagnosticSeverity.Error);
    }

    // ─── Accessibility modifier strings ──────────────────────────────────

    private static string AccessibilityKeyword(Accessibility accessibility)
    {
        return accessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Private => "private",
            Accessibility.Protected => "protected",
            Accessibility.Internal => "internal",
            _ => "internal",
        };
    }

    /// <summary>
    /// Oracle: returns true if <paramref name="baseAccess"/> is at least as accessible
    /// as <paramref name="derivedAccess"/> per the C# partial order.
    /// </summary>
    private static bool IsAtLeastAsAccessible(Accessibility baseAccess, Accessibility derivedAccess)
    {
        return AccessibilityOrder.IsAtLeastAsAccessibleAs(baseAccess, derivedAccess);
    }

    // ─── Name pools ──────────────────────────────────────────────────────

    private static readonly string[] BaseClassNames = ["Animal", "Vehicle", "Shape", "Widget", "Gadget"];
    private static readonly string[] DerivedClassNames = ["Cat", "Car", "Circle", "Button", "Gizmo"];
    private static readonly string[] InterfaceNames = ["IWorker", "IReader", "IWriter", "IHandler", "IProcessor"];
    private static readonly string[] DerivedInterfaceNames = ["IFastWorker", "IBufferedReader", "IAsyncWriter", "IEventHandler", "IBatchProcessor"];

    // ─── Generators ──────────────────────────────────────────────────────

    private static Gen<Accessibility> GenTopLevelAccessibility()
    {
        // Top-level types can be public or internal (private/protected are invalid at top level).
        return Gen.Elements(Accessibility.Public, Accessibility.Internal);
    }

    /// <summary>
    /// Generates a scenario with a derived class inheriting from a base class.
    /// The base class and derived class have randomized accessibility levels.
    /// Req 9.1: base class must be at least as accessible as derived class.
    /// </summary>
    private static Gen<BaseTypeAccessibilityScenario> GenBaseClassScenario()
    {
        return from baseAccess in GenTopLevelAccessibility()
               from derivedAccess in GenTopLevelAccessibility()
               from baseIdx in Gen.Choose(0, BaseClassNames.Length - 1)
               from derivedIdx in Gen.Choose(0, DerivedClassNames.Length - 1)
               let baseName = BaseClassNames[baseIdx]
               let derivedName = DerivedClassNames[derivedIdx]
               let baseKw = AccessibilityKeyword(baseAccess)
               let derivedKw = AccessibilityKeyword(derivedAccess)
               let shouldReject = !IsAtLeastAsAccessible(baseAccess, derivedAccess)
               let source = $$"""
                   {{baseKw}} class {{baseName}}
                   {
                       public {{baseName}}()
                       {
                       }
                   }
                   {{derivedKw}} class {{derivedName}} : {{baseName}}
                   {
                       public {{derivedName}}() : base()
                       {
                       }
                   }
                   class Program
                   {
                       static void Main()
                       {
                       }
                   }
                   """
               select new BaseTypeAccessibilityScenario(source,
                   $"Base class '{baseName}' ({baseKw}) derived by '{derivedName}' ({derivedKw})",
                   shouldReject: shouldReject);
    }

    /// <summary>
    /// Generates a scenario with a class implementing an abstract type (interface).
    /// The abstract type and implementing class have randomized accessibility levels.
    /// Req 9.2: implemented interface must be at least as accessible as implementing type.
    /// </summary>
    private static Gen<BaseTypeAccessibilityScenario> GenImplementedInterfaceScenario()
    {
        return from ifaceAccess in GenTopLevelAccessibility()
               from implAccess in GenTopLevelAccessibility()
               from ifaceIdx in Gen.Choose(0, InterfaceNames.Length - 1)
               from implIdx in Gen.Choose(0, DerivedClassNames.Length - 1)
               let ifaceName = InterfaceNames[ifaceIdx]
               let implName = DerivedClassNames[implIdx]
               let ifaceKw = AccessibilityKeyword(ifaceAccess)
               let implKw = AccessibilityKeyword(implAccess)
               let shouldReject = !IsAtLeastAsAccessible(ifaceAccess, implAccess)
               let source = $$"""
                   {{ifaceKw}} abstract {{ifaceName}}
                   {
                       int DoWork();
                   }
                   {{implKw}} class {{implName}} : {{ifaceName}}
                   {
                       public int DoWork()
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
               select new BaseTypeAccessibilityScenario(source,
                   $"Abstract '{ifaceName}' ({ifaceKw}) implemented by '{implName}' ({implKw})",
                   shouldReject: shouldReject);
    }

    /// <summary>
    /// Generates a scenario with an interface (abstract) inheriting from a base interface.
    /// Req 9.3: base interface must be at least as accessible as derived interface.
    /// </summary>
    private static Gen<BaseTypeAccessibilityScenario> GenBaseInterfaceScenario()
    {
        return from baseIfaceAccess in GenTopLevelAccessibility()
               from derivedIfaceAccess in GenTopLevelAccessibility()
               from baseIdx in Gen.Choose(0, InterfaceNames.Length - 1)
               from derivedIdx in Gen.Choose(0, DerivedInterfaceNames.Length - 1)
               let baseIfaceName = InterfaceNames[baseIdx]
               let derivedIfaceName = DerivedInterfaceNames[derivedIdx]
               let baseKw = AccessibilityKeyword(baseIfaceAccess)
               let derivedKw = AccessibilityKeyword(derivedIfaceAccess)
               let shouldReject = !IsAtLeastAsAccessible(baseIfaceAccess, derivedIfaceAccess)
               let source = $$"""
                   {{baseKw}} abstract {{baseIfaceName}}
                   {
                       int BaseOp();
                   }
                   {{derivedKw}} abstract {{derivedIfaceName}} : {{baseIfaceName}}
                   {
                       int DerivedOp();
                   }
                   class Program
                   {
                       static void Main()
                       {
                       }
                   }
                   """
               select new BaseTypeAccessibilityScenario(source,
                   $"Base abstract '{baseIfaceName}' ({baseKw}) extended by '{derivedIfaceName}' ({derivedKw})",
                   shouldReject: shouldReject);
    }

    /// <summary>
    /// Generates a scenario with a struct inheriting from a base struct.
    /// Mist structs are inheritable; the same base-accessibility rule applies.
    /// Req 9.1 applied to structs.
    /// </summary>
    private static Gen<BaseTypeAccessibilityScenario> GenBaseStructScenario()
    {
        return from baseAccess in GenTopLevelAccessibility()
               from derivedAccess in GenTopLevelAccessibility()
               let baseName = "BaseVector"
               let derivedName = "UnitVector"
               let baseKw = AccessibilityKeyword(baseAccess)
               let derivedKw = AccessibilityKeyword(derivedAccess)
               let shouldReject = !IsAtLeastAsAccessible(baseAccess, derivedAccess)
               let source = $$"""
                   {{baseKw}} struct {{baseName}}
                   {
                       public int x;

                       public {{baseName}}(int x)
                       {
                           this.x = x;
                       }
                   }
                   {{derivedKw}} struct {{derivedName}} : {{baseName}}
                   {
                       public {{derivedName}}() : base(1)
                       {
                       }
                   }
                   class Program
                   {
                       static void Main()
                       {
                       }
                   }
                   """
               select new BaseTypeAccessibilityScenario(source,
                   $"Base struct '{baseName}' ({baseKw}) derived by struct '{derivedName}' ({derivedKw})",
                   shouldReject: shouldReject);
    }

    /// <summary>
    /// Generates a scenario where a class implements multiple abstract types (interfaces)
    /// with mixed accessibility levels. An error should be reported for each that is less
    /// accessible than the implementing type.
    /// Req 9.2 with multiple interfaces.
    /// </summary>
    private static Gen<BaseTypeAccessibilityScenario> GenMultipleInterfaceScenario()
    {
        return from iface1Access in GenTopLevelAccessibility()
               from iface2Access in GenTopLevelAccessibility()
               from implAccess in GenTopLevelAccessibility()
               let iface1Name = "IAlpha"
               let iface2Name = "IBeta"
               let implName = "Impl"
               let iface1Kw = AccessibilityKeyword(iface1Access)
               let iface2Kw = AccessibilityKeyword(iface2Access)
               let implKw = AccessibilityKeyword(implAccess)
               let shouldReject = !IsAtLeastAsAccessible(iface1Access, implAccess)
                   || !IsAtLeastAsAccessible(iface2Access, implAccess)
               let source = $$"""
                   {{iface1Kw}} abstract {{iface1Name}}
                   {
                       int OpA();
                   }
                   {{iface2Kw}} abstract {{iface2Name}}
                   {
                       int OpB();
                   }
                   {{implKw}} class {{implName}} : {{iface1Name}}, {{iface2Name}}
                   {
                       public int OpA()
                       {
                           return 1;
                       }
                       public int OpB()
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
               select new BaseTypeAccessibilityScenario(source,
                   $"Class '{implName}' ({implKw}) implements '{iface1Name}' ({iface1Kw}) + '{iface2Name}' ({iface2Kw})",
                   shouldReject: shouldReject);
    }

    /// <summary>
    /// Generates an always-valid scenario: a public class with a public base.
    /// This ensures the accepting path is well-exercised (Req 9.4).
    /// </summary>
    private static Gen<BaseTypeAccessibilityScenario> GenPublicBasePublicDerived()
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
                   class Program
                   {
                       static void Main()
                       {
                       }
                   }
                   """
               select new BaseTypeAccessibilityScenario(source,
                   $"Public base '{baseName}' with public derived '{derivedName}' - always valid",
                   shouldReject: false);
    }

    // ─── Combined generator ──────────────────────────────────────────────

    private static Gen<BaseTypeAccessibilityScenario> GenScenario()
    {
        return Gen.OneOf(
            GenBaseClassScenario(),
            GenImplementedInterfaceScenario(),
            GenBaseInterfaceScenario(),
            GenBaseStructScenario(),
            GenMultipleInterfaceScenario(),
            GenPublicBasePublicDerived());
    }

    public static Arbitrary<BaseTypeAccessibilityScenario> ArbitraryBaseTypeAccessibilityScenario()
    {
        return GenScenario().ToArbitrary();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 11: Base types and interfaces are at least as accessible as
    // the deriving type.
    //
    // For any type, a base/interface accessibility error (MIST_TC_0174) is
    // reported if and only if its base class, an implemented interface, or a
    // base interface is not at least as accessible as the type itself.
    //
    // **Validates: Requirements 9.1, 9.2, 9.3, 9.4**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(BaseTypeInterfaceAccessibilityPropertyTests) })]
    public Property BaseTypeAccessibility_ErrorIffBaseLessAccessible(BaseTypeAccessibilityScenario scenario)
    {
        var diagnostics = CompileAndAnalyze(scenario.Source);

        var hasBaseLessAccessibleError = HasBaseLessAccessibleError(diagnostics);

        if(scenario.ShouldReject)
        {
            return hasBaseLessAccessibleError.ToProperty()
                .Label($"Expected REJECT ({BaseLessAccessibleId}) for: {scenario.Description}" +
                       $"\nDiagnostics: {FormatDiagnostics(diagnostics)}");
        }
        else
        {
            return (!hasBaseLessAccessibleError).ToProperty()
                .Label($"Expected ACCEPT (no {BaseLessAccessibleId}) for: {scenario.Description}" +
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