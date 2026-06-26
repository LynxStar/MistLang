// Feature: generic-constraints, Property 3: Constraint Emission Ordering
//
// **Validates: Requirements 8.8, 9.1**

using System.Collections.Immutable;
using System.Text.RegularExpressions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

/// <summary>
/// Represents a scenario for testing constraint emission ordering.
/// </summary>
public sealed class ConstraintEmissionOrderingScenario
{
    public string Source { get; }
    public string Description { get; }
    public string TypeParamName { get; }

    public ConstraintEmissionOrderingScenario(string source, string description, string typeParamName)
    {
        Source = source;
        Description = description;
        TypeParamName = typeParamName;
    }

    public override string ToString() => Description;
}

/// <summary>
/// Property-based tests verifying constraint emission ordering.
///
/// Property 3: For any Mist declaration with constraints written in any order,
/// the emitted C# where clause SHALL contain the constraints in C#'s required order:
/// class/struct first, then base class, then abstract types, then new() last —
/// regardless of the source order in Mist.
///
/// **Validates: Requirements 8.8, 9.1**
/// </summary>
public sealed class ConstraintEmissionOrderingPropertyTests : IDisposable
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();
    private static readonly SemanticModelBuilder Builder = new();
    private readonly string outputDir;

    public ConstraintEmissionOrderingPropertyTests()
    {
        outputDir = Path.Combine(
            Path.GetTempPath(),
            "MistConstraintOrderTests_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(outputDir))
        {
            try { Directory.Delete(outputDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static readonly string[] AbstractNames = ["IAlpha", "IBeta", "IGamma"];
    private static readonly string[] BaseClassNames = ["Animal", "Vehicle", "Shape"];

    private (ISemanticModel? Model, string CsContent) CompileAndEmit(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);

        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);

        if(parseResult.Root is null)
        {
            return (null, "");
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

        // Clean output dir
        foreach(var file in Directory.GetFiles(outputDir))
        {
            File.Delete(file);
        }

        var emitBag = new DiagnosticBag();
        CSharpEmitter.Emit(model, outputDir, "TestProject", emitBag);

        var csFile = Directory.GetFiles(outputDir, "*.cs").FirstOrDefault();
        var csContent = csFile is not null ? File.ReadAllText(csFile) : "";
        return (model, csContent);
    }

    /// <summary>
    /// Extracts the where clause for a given type parameter from emitted C#.
    /// Returns the constraint list portion (after "where T : ").
    /// </summary>
    private static string? ExtractWhereClause(string csContent, string typeParamName)
    {
        var pattern = $@"where\s+{Regex.Escape(typeParamName)}\s*:\s*(.+)";
        var match = Regex.Match(csContent, pattern);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    /// <summary>
    /// Verifies that the emitted constraint order follows C# rules:
    /// class/struct → base class → abstract types → new() last.
    /// </summary>
    private static bool VerifyConstraintOrder(string constraintList)
    {
        // Parse the constraint list into individual constraints
        var constraints = constraintList.Split(',').Select(c => c.Trim()).ToList();
        if(constraints.Count == 0)
        {
            return false;
        }

        // Determine the category of each constraint
        var categories = constraints.Select(CategorizeConstraint).ToList();

        // Verify ordering: class/struct (0) → base class (1) → abstract types (2) → new() (3)
        var lastCategory = -1;
        foreach(var category in categories)
        {
            if(category < lastCategory)
            {
                return false;
            }

            lastCategory = category;
        }
        return true;
    }

    /// <summary>
    /// Categorizes a constraint string into its ordering category:
    /// 0 = class/struct, 1 = base class, 2 = abstract type, 3 = new()
    /// </summary>
    private static int CategorizeConstraint(string constraint)
    {
        if(constraint == "class" || constraint == "struct")
        {
            return 0;
        }

        if(constraint == "new()")
        {
            return 3;
        }
        // Base classes start with uppercase and are not interfaces (no I prefix convention)
        // For our test scenarios, base classes are Animal, Vehicle, Shape
        if(BaseClassNames.Any(bc => constraint.StartsWith(bc)))
        {
            return 1;
        }
        // Everything else is an abstract type
        return 2;
    }

    // ─── Generators ──────────────────────────────────────────────────────

    private static Gen<ConstraintEmissionOrderingScenario> GenClassWithInterfacesAndNew()
    {
        return Gen.Elements(AbstractNames)
            .SelectMany(abs => Gen.Constant(new ConstraintEmissionOrderingScenario(
                $@"
abstract {abs} {{ }}
class Container<T> where T : new(), {abs}, class {{ }}
class Program {{
    public static void Main() {{ }}
}}",
                $"class + {abs} + new() in reversed order",
                "T")));
    }

    private static Gen<ConstraintEmissionOrderingScenario> GenStructWithInterfaces()
    {
        return Gen.Elements(AbstractNames)
            .SelectMany(abs => Gen.Constant(new ConstraintEmissionOrderingScenario(
                $@"
abstract {abs} {{ }}
class Container<T> where T : {abs}, struct {{ }}
class Program {{
    public static void Main() {{ }}
}}",
                $"struct + {abs} in reversed order",
                "T")));
    }

    private static Gen<ConstraintEmissionOrderingScenario> GenBaseClassWithInterfacesAndNew()
    {
        return Gen.Elements(BaseClassNames)
            .SelectMany(baseClass => Gen.Elements(AbstractNames)
                .SelectMany(abs => Gen.Constant(new ConstraintEmissionOrderingScenario(
                    $@"
abstract {abs} {{ }}
class {baseClass} {{ }}
class Container<T> where T : new(), {abs}, {baseClass} {{ }}
class Program {{
    public static void Main() {{ }}
}}",
                    $"new() + {abs} + {baseClass} in wrong order",
                    "T"))));
    }

    private static Gen<ConstraintEmissionOrderingScenario> GenClassWithMultipleInterfaces()
    {
        return Gen.Constant(new ConstraintEmissionOrderingScenario(
            @"
abstract IAlpha { }
abstract IBeta { }
abstract IGamma { }
class Container<T> where T : new(), IGamma, IAlpha, IBeta, class { }
class Program {
    public static void Main() { }
}",
            "class + 3 interfaces + new() all scrambled",
            "T"));
    }

    private static Gen<ConstraintEmissionOrderingScenario> GenMethodConstraintsScrambled()
    {
        return Gen.Elements(AbstractNames)
            .SelectMany(abs => Gen.Constant(new ConstraintEmissionOrderingScenario(
                $@"
abstract {abs} {{ }}
class Program {{
    public static void DoWork<T>(T item) where T : new(), {abs}, class {{ }}
    public static void Main() {{ }}
}}",
                $"method with class + {abs} + new() scrambled",
                "T")));
    }

    private static Gen<ConstraintEmissionOrderingScenario> GenBaseClassFirst()
    {
        return Gen.Elements(BaseClassNames)
            .SelectMany(baseClass => Gen.Elements(AbstractNames)
                .SelectMany(abs => Gen.Constant(new ConstraintEmissionOrderingScenario(
                    $@"
abstract {abs} {{ }}
class {baseClass} {{ }}
class Container<T> where T : {baseClass}, new(), {abs} {{ }}
class Program {{
    public static void Main() {{ }}
}}",
                    $"{baseClass} first, then new(), then {abs}",
                    "T"))));
    }

    private static Gen<ConstraintEmissionOrderingScenario> GenScenario()
    {
        return Gen.OneOf(
            GenClassWithInterfacesAndNew(),
            GenStructWithInterfaces(),
            GenBaseClassWithInterfacesAndNew(),
            GenClassWithMultipleInterfaces(),
            GenMethodConstraintsScrambled(),
            GenBaseClassFirst());
    }

    public static Arbitrary<ConstraintEmissionOrderingScenario> ArbitraryConstraintEmissionOrderingScenario() =>
        Arb.From(GenScenario());

    // ═══════════════════════════════════════════════════════════════════════
    // Property 3: Constraint Emission Ordering
    //
    // For any Mist declaration with constraints written in any order,
    // the emitted C# where clause SHALL contain the constraints in C#'s
    // required order: class/struct → base class → abstract types → new() last.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ConstraintEmissionOrderingPropertyTests) })]
    public Property EmittedCSharp_ConstraintsAreInCorrectOrder(ConstraintEmissionOrderingScenario scenario)
    {
        var (model, csContent) = CompileAndEmit(scenario.Source);

        if(string.IsNullOrEmpty(csContent))
        {
            return false.ToProperty().Label($"No C# output for: {scenario.Description}");
        }

        var whereClause = ExtractWhereClause(csContent, scenario.TypeParamName);
        if(whereClause is null)
        {
            return false.ToProperty().Label($"No where clause found for {scenario.TypeParamName}");
        }

        var isCorrectOrder = VerifyConstraintOrder(whereClause);
        return isCorrectOrder.ToProperty()
            .Label($"Constraint order incorrect: '{whereClause}' for scenario: {scenario.Description}");
    }
}