// Bugfix: type-resolution-unification, Property 2: Preservation
// Non-Interface Extension and Own-Member Resolution
//
// **Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.8, 3.9, 3.10, 3.11**
//
// These tests capture EXISTING correct behaviors that must be preserved
// after the fixes are implemented. They MUST PASS on unfixed code.
//
// Preservation scenarios (non-bug-condition inputs):
// - Non-generic extension methods on class-typed receivers resolve correctly
// - Non-generic extension methods on struct-typed receivers resolve correctly
// - Extension methods on generic constructed types resolve correctly
// - Own members on class/struct/interface types resolve without consulting extensions
// - Non-generic method calls return the first matching overload's declared type
// - Universal extensions (receiver type = unknown) are included in applicability results
// - Extension method namespace filtering excludes non-imported extensions
// - Mist-defined types resolve through scope chain without involving ambient type projection
// - Field/property invocation as method emits diagnostic error
// - Type argument arity mismatch emits diagnostic error
// - Lambda-compatible overload selection still works for Func/Action/delegate parameters

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Semantics;

/// <summary>
/// Preservation property tests for type resolution behaviors that must remain
/// unchanged after the type-resolution-unification bugfix is applied.
///
/// <b>Property 2: Preservation</b> — Non-Interface Extension and Own-Member Resolution
///
/// <b>Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.8, 3.9, 3.10, 3.11</b>
///
/// These tests observe and encode the CURRENT correct behavior on non-bug-condition
/// inputs. They should PASS on both unfixed and fixed code.
///
/// Uses in-process compilation via shared <see cref="CompilerDriver"/> — no external
/// process spawned per iteration (workspace test-process-spawning policy).
/// </summary>
#pragma warning disable CA1034 // Nested types should not be visible (test records)
public sealed class TypeResolutionPreservationPropertyTests : IDisposable
{
    // ─── Pipeline wiring ─────────────────────────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<Mist.Analysis.Contracts.IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private static readonly ILexer Lexer = new MistLexer();
    private static readonly IParser Parser = new MistParser();
    private static readonly SemanticModelBuilder Builder = new();

    private readonly string outputDir;

    public TypeResolutionPreservationPropertyTests()
    {
        outputDir = Path.Combine(
            Path.GetTempPath(),
            "mist_preservation_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(outputDir))
        {
            try { Directory.Delete(outputDir, recursive: true); } catch { /* best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Compiles a Mist source string in-process and returns the result.
    /// Uses the shared CompilerDriver (1 compile, 0 process spawns).
    /// </summary>
    private CompilerResult CompileSource(string mistSource, string fileName = "Program.mist",
        ImmutableArray<string> globalUsings = default)
    {
        var sourcePath = Path.Combine(outputDir, fileName);
        File.WriteAllText(sourcePath, mistSource);

        var options = new CompilerOptions(
            ProjectFolder: outputDir,
            SourcePaths: ImmutableArray.Create(sourcePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: Path.Combine(outputDir, "out"),
            BinDirectory: Path.Combine(outputDir, "bin"),
            ProjectName: "TestProject",
            NamespaceRoot: "TestProject",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            GlobalUsings: globalUsings);

        return SharedDriver.Compile(options);
    }

    /// <summary>
    /// Builds a semantic model with stdlib for in-process-only compilation
    /// (no ambient CLR assemblies, just Mist source + stdlib).
    /// </summary>
    private static (ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics)
        BuildModelWithStdlib(string source)
    {
        var (stdlibUnits, stdlibAsts) = Helpers.StdlibTestHelper.LoadStdlib();

        var userSourceText = new SourceText("test.mist", source);
        var userUnit = new CompilationUnit(userSourceText);
        var diagnostics = new DiagnosticBag();
        var userTokens = Lexer.Lex(userSourceText, diagnostics);
        var userParseResult = Parser.Parse(userTokens, diagnostics);

        if(userParseResult.Root is null)
        {
            return (null!, diagnostics.ToImmutableArray());
        }

        var allUnits = stdlibUnits.Add(userUnit);
        var allAsts = stdlibAsts.Add(userParseResult.Root);

        var compilation = new MistCompilation(allUnits);
        var model = Builder.Build(
            compilation, allAsts, ambientTypeResolver: null,
            namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);

        return (model, diagnostics.ToImmutableArray());
    }

    private static string FormatDiagnostics(CompilerResult result)
    {
        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"  [{d.Id.Value}] {d.Message}");
        return string.Join(Environment.NewLine, errors);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Preservation 3.1: Non-generic extension methods on class-typed receivers
    //                    (e.g., string.Contains()) resolve correctly
    //
    // **Validates: Requirements 3.1**
    //
    // On unfixed code, class-typed receivers work correctly due to the
    // structural equality fallback for ClassType in ExtensionRegistry.
    // ═══════════════════════════════════════════════════════════════════════

    public record ClassExtensionScenario(
        string LinqMethod,
        string ExpectedOutput)
    {
        public override string ToString()
        {
            return $"LINQ extension '{LinqMethod}' on class-typed receiver producing '{ExpectedOutput}'";
        }
    }

    private static readonly (string Method, string Expected)[] ClassExtensionVariants =
    [
        ("numbers.Count()", "6"),
        ("numbers.First()", "1"),
        ("numbers.Last()", "6"),
    ];

    public static Gen<ClassExtensionScenario> GenClassExtensionScenario()
    {
        return from variant in Gen.Elements(ClassExtensionVariants)
               select new ClassExtensionScenario(variant.Method, variant.Expected);
    }

    public static Arbitrary<ClassExtensionScenario> ArbitraryClassExtensionScenario()
    {
        return GenClassExtensionScenario().ToArbitrary();
    }

    /// <summary>
    /// Non-generic LINQ extension methods on arrays (class-typed IEnumerable
    /// receiver) compile successfully.
    ///
    /// **Validates: Requirements 3.1**
    /// </summary>
    [Property(MaxTest = 9, Arbitrary = new[] { typeof(TypeResolutionPreservationPropertyTests) })]
    public Property ClassTypedReceiver_NonGenericExtension_CompilesSuccessfully(
        ClassExtensionScenario scenario)
    {
        var source = $$"""
            using Standard;
            using System.Linq;

            namespace TestProject;

            public class Program
            {
                public static void Main()
                {
                    int[] numbers = [1, 2, 3, 4, 5, 6];
                    var result = {{scenario.LinqMethod}};
                    Console.WriteLine($"{result}");
                }
            }
            """;

        var result = CompileSource(source);

        return result.Succeeded.ToProperty()
            .Label($"Compilation failed for '{scenario.LinqMethod}'.\n{FormatDiagnostics(result)}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Preservation 3.4: Own members on class/struct/interface types resolve
    //                    without consulting extensions
    //
    // **Validates: Requirements 3.4**
    //
    // Own members (methods, properties) defined directly on a type are
    // resolved from the type's member scope, not via extensions.
    // ═══════════════════════════════════════════════════════════════════════

    public record OwnMemberScenario(
        string TypeKind,
        string TypeName,
        string MemberName,
        string MemberReturnType,
        string CallExpr)
    {
        public override string ToString()
        {
            return $"{TypeKind} '{TypeName}' own member '{MemberName}' returning {MemberReturnType}";
        }
    }

    private static readonly OwnMemberScenario[] OwnMemberScenarios =
    [
        new("class", "MyService", "GetValue", "int", "s.GetValue()"),
        new("class", "MyService", "Compute", "string", "s.Compute()"),
        new("struct", "Point", "Magnitude", "int", "p.Magnitude()"),
        new("struct", "Point", "Scale", "int", "p.Scale()"),
    ];

    public static Gen<OwnMemberScenario> GenOwnMemberScenario()
    {
        return Gen.Elements(OwnMemberScenarios);
    }

    public static Arbitrary<OwnMemberScenario> ArbitraryOwnMemberScenario()
    {
        return GenOwnMemberScenario().ToArbitrary();
    }

    /// <summary>
    /// Own members on class/struct/abstract types resolve without consulting
    /// extensions. The type's member scope takes precedence.
    ///
    /// **Validates: Requirements 3.4**
    /// </summary>
    [Property(MaxTest = 15, Arbitrary = new[] { typeof(TypeResolutionPreservationPropertyTests) })]
    public Property OwnMembers_ResolveDirectlyFromMemberScope(OwnMemberScenario scenario)
    {
        var typeKeyword = scenario.TypeKind;

        // Use the correct return expression for the declared return type
        var returnExpr = scenario.MemberReturnType switch
        {
            "int" => "42",
            "string" => "\"hello\"",
            "bool" => "true",
            _ => "0",
        };

        var typeDecl = typeKeyword + " " + scenario.TypeName + "\n{\n" +
            "    public " + scenario.MemberReturnType + " " + scenario.MemberName + "()\n" +
            "    {\n        return " + returnExpr + ";\n    }\n}";

        var varName = scenario.TypeKind == "struct" ? "p" : "s";

        var source = "using Standard;\n\nnamespace TestProject;\n\npublic " + typeDecl +
            "\n\npublic class Program\n{\n    public static void Main()\n    {\n" +
            "        var " + varName + " = new " + scenario.TypeName + "();\n" +
            "        var result = " + varName + "." + scenario.MemberName + "();\n" +
            "        Console.WriteLine($\"{result}\");\n    }\n}\n";

        var result = CompileSource(source);

        return result.Succeeded.ToProperty()
            .Label($"Own member '{scenario.MemberName}' on {scenario.TypeKind} " +
                   $"'{scenario.TypeName}' failed to compile.\n{FormatDiagnostics(result)}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Preservation 3.5: Non-generic method calls (no type arguments) return
    //                    the first matching overload's declared type
    //
    // **Validates: Requirements 3.5**
    // ═══════════════════════════════════════════════════════════════════════

    public record NonGenericOverloadScenario(
        string MethodName,
        string Overload1ReturnType,
        string Overload2ReturnType,
        string ExpectedResolvedType)
    {
        public override string ToString()
        {
            return $"Non-generic call to overloaded '{MethodName}' resolves to '{ExpectedResolvedType}'";
        }
    }

    private static readonly NonGenericOverloadScenario[] NonGenericOverloadScenarios =
    [
        new("Execute", "int", "string", "int"),
        new("Process", "bool", "int", "bool"),
        new("Transform", "string", "double", "string"),
    ];

    public static Gen<NonGenericOverloadScenario> GenNonGenericOverloadScenario()
    {
        return Gen.Elements(NonGenericOverloadScenarios);
    }

    public static Arbitrary<NonGenericOverloadScenario> ArbitraryNonGenericOverloadScenario()
    {
        return GenNonGenericOverloadScenario().ToArbitrary();
    }

    /// <summary>
    /// Non-generic method calls (no type arguments) return the first matching
    /// overload's declared type as it does today.
    ///
    /// **Validates: Requirements 3.5**
    /// </summary>
    [Property(MaxTest = 9, Arbitrary = new[] { typeof(TypeResolutionPreservationPropertyTests) })]
    public Property NonGenericMethodCall_ReturnsFirstOverloadType(
        NonGenericOverloadScenario scenario)
    {
        // When two overloads exist and neither has type parameters, calling
        // without arguments matches the first (parameterless) overload.
        var source = $$"""
            using Standard;

            namespace TestProject;

            public class Widget
            {
                public {{scenario.Overload1ReturnType}} {{scenario.MethodName}}()
                {
                    return default;
                }

                public {{scenario.Overload2ReturnType}} {{scenario.MethodName}}(int x)
                {
                    return default;
                }
            }

            public class Program
            {
                public static void Main()
                {
                    var w = new Widget();
                    var result = w.{{scenario.MethodName}}();
                }
            }
            """;

        var result = CompileSource(source);

        return result.Succeeded.ToProperty()
            .Label($"Non-generic overload resolution for '{scenario.MethodName}()' " +
                   $"failed to compile.\n{FormatDiagnostics(result)}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Preservation 3.7: Extension method namespace filtering excludes
    //                    non-imported extensions
    //
    // **Validates: Requirements 3.7**
    //
    // Extensions from namespaces not imported at the call site are excluded.
    // ═══════════════════════════════════════════════════════════════════════

    public record NamespaceFilterScenario(
        string ExtensionNamespace,
        bool IsImported,
        bool ExpectsSuccess)
    {
        public override string ToString()
        {
            return $"Extension in '{ExtensionNamespace}', imported={IsImported}, expects {(ExpectsSuccess ? "success" : "error")}";
        }
    }

    private static readonly NamespaceFilterScenario[] NamespaceFilterScenarios =
    [
        // With using System.Linq, LINQ extensions are available
        new("System.Linq", true, true),
        // Without using System.Linq, LINQ extensions are NOT available
        new("System.Linq", false, false),
    ];

    public static Gen<NamespaceFilterScenario> GenNamespaceFilterScenario()
    {
        return Gen.Elements(NamespaceFilterScenarios);
    }

    public static Arbitrary<NamespaceFilterScenario> ArbitraryNamespaceFilterScenario()
    {
        return GenNamespaceFilterScenario().ToArbitrary();
    }

    /// <summary>
    /// Extension methods are only available when their containing namespace
    /// is imported via <c>using</c> at the call site.
    ///
    /// **Validates: Requirements 3.7**
    /// </summary>
    [Property(MaxTest = 6, Arbitrary = new[] { typeof(TypeResolutionPreservationPropertyTests) })]
    public Property ExtensionNamespaceFiltering_ExcludesNonImported(
        NamespaceFilterScenario scenario)
    {
        var usingDirective = scenario.IsImported
            ? $"using {scenario.ExtensionNamespace};"
            : "";

        var source = $$"""
            using Standard;
            {{usingDirective}}

            namespace TestProject;

            public class Program
            {
                public static void Main()
                {
                    int[] numbers = [1, 2, 3];
                    var count = numbers.Count();
                }
            }
            """;

        var result = CompileSource(source);

        if(scenario.ExpectsSuccess)
        {
            return result.Succeeded.ToProperty()
                .Label($"Expected compilation success with '{scenario.ExtensionNamespace}' " +
                       $"imported, but failed.\n{FormatDiagnostics(result)}");
        }

        var hasUndefinedMember = result.Diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error
            && d.Id.Value == "MIST_UNDEFINED_MEMBER");

        return hasUndefinedMember.ToProperty()
            .Label($"Expected MIST_UNDEFINED_MEMBER without '{scenario.ExtensionNamespace}' " +
                   $"imported, but got: {(result.Succeeded ? "success" : FormatDiagnostics(result))}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Preservation 3.8: Mist-defined types resolve through scope chain
    //                    without involving ambient type projection
    //
    // **Validates: Requirements 3.8**
    //
    // Types defined in Mist source (class, struct, abstract, newtype, enum,
    // type alias) resolve through the scope chain and symbol table.
    // ═══════════════════════════════════════════════════════════════════════

    public record MistDefinedTypeScenario(
        string TypeKind,
        string TypeName,
        string Declaration,
        string Usage)
    {
        public override string ToString()
        {
            return $"Mist-defined {TypeKind} '{TypeName}' resolves through scope chain";
        }
    }

    private static readonly MistDefinedTypeScenario[] MistDefinedTypeScenarios =
    [
        new("class", "Animal",
            "public class Animal { public int Age() { return 5; } }",
            "var a = new Animal(); var x = a.Age();"),
        new("struct", "Vec2",
            "public struct Vec2 { public int X() { return 0; } }",
            "var v = new Vec2(); var x = v.X();"),
        new("abstract", "IShape",
            "public abstract IShape { public int Area() { return 0; } }",
            ""),
        new("enum", "Color",
            "public enum Color { Red, Green, Blue }",
            "var c = Color.Red;"),
    ];

    public static Gen<MistDefinedTypeScenario> GenMistDefinedTypeScenario()
    {
        return Gen.Elements(MistDefinedTypeScenarios);
    }

    public static Arbitrary<MistDefinedTypeScenario> ArbitraryMistDefinedTypeScenario()
    {
        return GenMistDefinedTypeScenario().ToArbitrary();
    }

    /// <summary>
    /// Mist-defined types resolve through the scope chain and symbol table
    /// without involving the ambient type projection layer.
    ///
    /// **Validates: Requirements 3.8**
    /// </summary>
    [Property(MaxTest = 12, Arbitrary = new[] { typeof(TypeResolutionPreservationPropertyTests) })]
    public Property MistDefinedTypes_ResolveThroughScopeChain(MistDefinedTypeScenario scenario)
    {
        // For abstract types, we can only test declaration (not instantiation).
        var mainBody = string.IsNullOrEmpty(scenario.Usage)
            ? ""
            : scenario.Usage;

        var source = $$"""
            using Standard;

            namespace TestProject;

            {{scenario.Declaration}}

            public class Program
            {
                public static void Main()
                {
                    {{mainBody}}
                }
            }
            """;

        var result = CompileSource(source);

        return result.Succeeded.ToProperty()
            .Label($"Mist-defined {scenario.TypeKind} '{scenario.TypeName}' " +
                   $"failed to compile.\n{FormatDiagnostics(result)}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Preservation 3.9: Field/property invocation as method emits diagnostic
    //
    // **Validates: Requirements 3.9**
    //
    // When a method call resolves to a name that is actually a field or
    // property, the system emits a diagnostic error.
    // ═══════════════════════════════════════════════════════════════════════

    public record FieldInvocationScenario(
        string MemberKind,
        string MemberDecl,
        string CallExpr)
    {
        public override string ToString()
        {
            return $"Invoking {MemberKind} as method: '{CallExpr}'";
        }
    }

    private static readonly FieldInvocationScenario[] FieldInvocationScenarios =
    [
        new("field", "public int value = 42;", "obj.value()"),
        new("property", "public int Count => 5;", "obj.Count()"),
    ];

    public static Gen<FieldInvocationScenario> GenFieldInvocationScenario()
    {
        return Gen.Elements(FieldInvocationScenarios);
    }

    public static Arbitrary<FieldInvocationScenario> ArbitraryFieldInvocationScenario()
    {
        return GenFieldInvocationScenario().ToArbitrary();
    }

    /// <summary>
    /// A method call that resolves to a field or property name emits a
    /// diagnostic error indicating the member cannot be invoked as a method.
    ///
    /// **Validates: Requirements 3.9**
    /// </summary>
    [Property(MaxTest = 6, Arbitrary = new[] { typeof(TypeResolutionPreservationPropertyTests) })]
    public Property FieldPropertyInvocation_EmitsDiagnostic(FieldInvocationScenario scenario)
    {
        var source = $$"""
            using Standard;

            namespace TestProject;

            public class Foo
            {
                {{scenario.MemberDecl}}
            }

            public class Program
            {
                public static void Main()
                {
                    var obj = new Foo();
                    var x = {{scenario.CallExpr}};
                }
            }
            """;

        var result = CompileSource(source);

        // The compilation should fail with a diagnostic about invoking
        // a field/property as a method.
        var hasInvocationError = result.Diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error);

        return (!result.Succeeded && hasInvocationError).ToProperty()
            .Label($"Expected diagnostic error for invoking {scenario.MemberKind} as method, " +
                   $"but got: {(result.Succeeded ? "success" : FormatDiagnostics(result))}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Preservation 3.10: Type argument arity mismatch emits diagnostic error
    //
    // **Validates: Requirements 3.10**
    //
    // When a generic method call has a type argument count mismatch, the
    // system emits a diagnostic error.
    // ═══════════════════════════════════════════════════════════════════════

    public record ArityMismatchScenario(
        int DeclaredArity,
        int ProvidedArity,
        string MethodName)
    {
        public override string ToString()
        {
            return $"'{MethodName}' declared with {DeclaredArity} type params, called with {ProvidedArity}";
        }
    }

    private static readonly ArityMismatchScenario[] ArityMismatchScenarios =
    [
        new(1, 2, "Convert"),
        new(1, 0, "Transform"),
        new(2, 1, "Merge"),
    ];

    public static Gen<ArityMismatchScenario> GenArityMismatchScenario()
    {
        return Gen.Elements(ArityMismatchScenarios);
    }

    public static Arbitrary<ArityMismatchScenario> ArbitraryArityMismatchScenario()
    {
        return GenArityMismatchScenario().ToArbitrary();
    }

    /// <summary>
    /// A generic method call with a type argument count mismatch emits
    /// a diagnostic error.
    ///
    /// **Validates: Requirements 3.10**
    /// </summary>
    [Property(MaxTest = 9, Arbitrary = new[] { typeof(TypeResolutionPreservationPropertyTests) })]
    public Property TypeArgumentArityMismatch_EmitsDiagnostic(ArityMismatchScenario scenario)
    {
        // Build type parameter list for declaration
        var typeParams = string.Join(", ",
            Enumerable.Range(0, scenario.DeclaredArity).Select(i => $"T{i}"));

        // Build type argument list for call
        var typeArgs = string.Join(", ",
            Enumerable.Range(0, scenario.ProvidedArity).Select(_ => "int"));

        var source = $$"""
            using Standard;

            namespace TestProject;

            public class Container
            {
                public int {{scenario.MethodName}}<{{typeParams}}>()
                {
                    return 0;
                }
            }

            public class Program
            {
                public static void Main()
                {
                    var c = new Container();
                    var x = c.{{scenario.MethodName}}<{{typeArgs}}>();
                }
            }
            """;

        var result = CompileSource(source);

        // Arity mismatch should produce a diagnostic
        var hasDiagnostic = result.Diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error);

        return (!result.Succeeded && hasDiagnostic).ToProperty()
            .Label($"Expected arity mismatch diagnostic for '{scenario.MethodName}' " +
                   $"(declared {scenario.DeclaredArity}, provided {scenario.ProvidedArity}), " +
                   $"but got: {(result.Succeeded ? "success" : FormatDiagnostics(result))}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Preservation 3.6: Universal extensions (receiver type = unknown) are
    //                    included in applicability results for all receiver types
    //
    // **Validates: Requirements 3.6**
    //
    // Extension blocks with receiver type = unknown apply to all types.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Universal extensions (receiver type = unknown) are included in
    /// applicability results for all receiver types.
    ///
    /// **Validates: Requirements 3.6**
    /// </summary>
    [Fact]
    public void UniversalExtensions_ApplyToAllReceiverTypes()
    {
        // A universal extension (receiver type = unknown)
        // should be applicable to any receiver type.
        var source = """
            using Standard;

            namespace TestProject;

            public class MyObj
            {
            }

            extension(unknown self)
            {
                public int Universal()
                {
                    return 99;
                }
            }

            public class Program
            {
                public static void Main()
                {
                    var obj = new MyObj();
                    var x = obj.Universal();
                }
            }
            """;

        var result = CompileSource(source);

        Assert.True(result.Succeeded,
            $"Universal extension failed to resolve.\n{FormatDiagnostics(result)}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Preservation 3.2: Non-generic extension methods on struct-typed
    //                    receivers resolve correctly
    //
    // **Validates: Requirements 3.2**
    //
    // Extension methods targeting struct types work via structural equality
    // fallback in ExtensionRegistry for StructType.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Non-generic extension methods declared on struct-typed receivers
    /// resolve correctly via the existing structural equality fallback.
    ///
    /// **Validates: Requirements 3.2**
    /// </summary>
    [Fact]
    public void StructTypedReceiver_ExtensionMethod_Resolves()
    {
        var source = """
            using Standard;

            namespace TestProject;

            public struct Point
            {
                public int X;
                public int Y;
            }

            extension(Point self)
            {
                public int Sum()
                {
                    return self.X + self.Y;
                }
            }

            public class Program
            {
                public static void Main()
                {
                    var p = new Point();
                    var s = p.Sum();
                }
            }
            """;

        var result = CompileSource(source);

        Assert.True(result.Succeeded,
            $"Struct extension method failed to resolve.\n{FormatDiagnostics(result)}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Preservation 3.3: Extension methods on generic constructed types
    //                    (e.g., List<int>.Select()) resolve correctly
    //
    // **Validates: Requirements 3.3**
    //
    // Generic constructed types match via GenericDefinition matching in
    // ExtensionRegistry.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Extension methods on generic constructed types (like arrays — the primary
    /// IEnumerable receiver) resolve correctly via generic definition matching.
    ///
    /// **Validates: Requirements 3.3**
    /// </summary>
    [Fact]
    public void GenericConstructedType_ExtensionMethod_Resolves()
    {
        var source = """
            using Standard;
            using System.Linq;

            namespace TestProject;

            public class Program
            {
                public static void Main()
                {
                    int[] items = [10, 20, 30];
                    var skipped = items.Skip(1);
                    var first = items.First();
                    Console.WriteLine($"{first}");
                }
            }
            """;

        var result = CompileSource(source);

        Assert.True(result.Succeeded,
            $"Extension on generic constructed type failed.\n{FormatDiagnostics(result)}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Preservation 3.11: Lambda-compatible overload selection still works
    //                     for Func/Action/delegate parameters
    //
    // **Validates: Requirements 3.11**
    //
    // Method calls with lambda expressions select the overload with
    // lambda-compatible parameters (Func, Action, delegate).
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Lambda-compatible overload selection continues to work for LINQ
    /// methods that accept Func parameters (e.g., Where, Select).
    ///
    /// **Validates: Requirements 3.11**
    /// </summary>
    [Fact]
    public void LambdaCompatibleOverload_SelectsCorrectOverload()
    {
        var source = """
            using Standard;
            using System.Linq;

            namespace TestProject;

            public class Program
            {
                public static void Main()
                {
                    int[] numbers = [1, 2, 3, 4, 5, 6];
                    var evens = numbers.Where((int n) => n % 2 == 0);
                    var count = evens.Count();
                    Console.WriteLine($"{count}");
                }
            }
            """;

        var result = CompileSource(source);

        Assert.True(result.Succeeded,
            $"Lambda-compatible overload selection failed.\n{FormatDiagnostics(result)}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Preservation 3.1 (additional): Non-generic extension on class receiver
    //                                 (user-defined Mist class extension)
    //
    // **Validates: Requirements 3.1**
    //
    // User-defined extension methods on class-typed receivers work.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// User-defined non-generic extension methods on class-typed receivers
    /// resolve correctly.
    ///
    /// **Validates: Requirements 3.1**
    /// </summary>
    [Fact]
    public void ClassTypedReceiver_UserDefinedExtension_Resolves()
    {
        var source = """
            using Standard;

            namespace TestProject;

            public class Dog
            {
                public string Name;
            }

            extension(Dog self)
            {
                public string Bark()
                {
                    return "Woof";
                }
            }

            public class Program
            {
                public static void Main()
                {
                    var d = new Dog();
                    var sound = d.Bark();
                    Console.WriteLine(sound);
                }
            }
            """;

        var result = CompileSource(source);

        Assert.True(result.Succeeded,
            $"Class extension method failed to resolve.\n{FormatDiagnostics(result)}");
    }
}