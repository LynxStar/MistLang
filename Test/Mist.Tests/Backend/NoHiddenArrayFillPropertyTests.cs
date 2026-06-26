// Feature: definite-assignment
// Property 6: No hidden default-fill
//
// No array-creation path emits a call to `__CreateDefault()` per element;
// the bare stampable path emits no per-element loop, and the initializer
// path invokes only the user-supplied `fn`.
//
// All work runs in-process using compiler APIs — no external process is spawned.
// Uses CSharpEmitter.EmitToMemory for content generation without disk I/O.
//
// **Validates: Requirements 4.3, 4.4, 6.3**

using System.Collections.Immutable;
using System.Text;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

/// <summary>
/// Property-based tests for Property 6 of the definite-assignment spec:
/// No hidden default-fill.
///
/// Asserts that:
/// - No array-creation path emits <c>__CreateDefault()</c> per element.
/// - Bare stampable arrays emit no per-element loop (rely on CLR zero-fill).
/// - Initializer arrays invoke only the user-supplied <c>fn</c>.
///
/// **Validates: Requirements 4.3, 4.4, 6.3**
/// </summary>
public sealed class NoHiddenArrayFillPropertyTests
{
    // ─── Pipeline wiring ─────────────────────────────────────────────────

    private static readonly ILexer Lexer = new MistLexer();
    private static readonly IParser Parser = new MistParser();
    private static readonly SemanticModelBuilder ModelBuilder = new();
    private static readonly TypeCheckerAnalyzer TypeChecker = new();

    // ─── In-memory compilation helper ────────────────────────────────────

    /// <summary>
    /// Compiles a Mist source string through lex → parse → semantic model →
    /// type-check → emit-to-memory and returns the emitted C# content for the
    /// user file (test.cs) or all emitted files concatenated.
    /// </summary>
    private static (bool Succeeded, string CsContent, string ErrorText) BuildAndEmit(string mistSource)
    {
        var (stdlibUnits, stdlibAsts) = StdlibTestHelper.LoadStdlib();

        var userSourceText = new SourceText("test.mist", mistSource);
        var userUnit = new CompilationUnit(userSourceText);
        var diagnostics = new DiagnosticBag();
        var userTokens = Lexer.Lex(userSourceText, diagnostics);
        var userParseResult = Parser.Parse(userTokens, diagnostics);

        if(userParseResult.Root is null)
        {
            return (false, "", $"Failed to parse source:\n{mistSource}");
        }

        var allUnits = stdlibUnits.Add(userUnit);
        var allAsts = stdlibAsts.Add(userParseResult.Root);

        var compilation = new MistCompilation(allUnits);
        var model = ModelBuilder.Build(
            compilation,
            allAsts,
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            diagnostics);

        var buildErrors = diagnostics.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        if(buildErrors.Count > 0)
        {
            var errorText = string.Join("\n", buildErrors.Select(d => $"  {d.Id.Value}: {d.Message}"));
            return (false, "", $"Build errors:\n{errorText}");
        }

        // Type checker
        var tcBag = new DiagnosticBag();
        TypeChecker.Analyze(model, tcBag);
        var tcErrors = tcBag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        if(tcErrors.Count > 0)
        {
            var errorText = string.Join("\n", tcErrors.Select(d => $"  {d.Id.Value}: {d.Message}"));
            return (false, "", $"Type-check errors:\n{errorText}");
        }

        // Emit in-memory
        var emitBag = new DiagnosticBag();
        var files = CSharpEmitter.EmitToMemory(model, "TestProject", emitBag);

        if(emitBag.HasErrors)
        {
            var emitErrors = emitBag.ToImmutableArray()
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => $"  {d.Id.Value}: {d.Message}");
            return (false, "", $"Emit errors:\n{string.Join("\n", emitErrors)}");
        }

        if(files.TryGetValue("test.cs", out var testContent))
        {
            return (true, testContent, "");
        }

        // Fallback: concatenate all emitted files
        var sb = new StringBuilder();
        foreach(var (_, content) in files)
        {
            _ = sb.AppendLine(content);
        }

        return (true, sb.ToString(), "");
    }

    // ─── Scenario types ──────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// Describes the array creation path being tested.
    /// </summary>
    public enum ArrayCreationPath
    {
        /// <summary>Bare <c>new T[size]</c> for a primitive type (stampable, zero-fill).</summary>
        BareStampablePrimitive,

        /// <summary>Bare <c>new T[size]</c> for a pure struct (stampable, zero-fill).</summary>
        BareStampableStruct,

        /// <summary><c>new Array&lt;T, Size&gt;(fn)</c> for a class type (initializer path).</summary>
        InitializerClass,

        /// <summary><c>new Array&lt;T, Size&gt;(fn)</c> for a string type (initializer path).</summary>
        InitializerString,

        /// <summary><c>new Array&lt;T, Size&gt;(fn)</c> for an int type (initializer path).</summary>
        InitializerPrimitive,
    }

    /// <summary>
    /// A generated scenario for testing the no-hidden-default-fill property.
    /// </summary>
    public sealed record NoFillScenario(
        string Source,
        ArrayCreationPath Path,
        int Size)
    {
        public override string ToString() =>
            $"NoFillScenario(Path={Path}, Size={Size})";
    }

#pragma warning restore CA1034

    // ─── Generator ───────────────────────────────────────────────────────

    private static readonly string[] PrimitiveTypes = ["int", "bool", "char", "float", "double", "long"];

    private static readonly string[] PureStructNames = ["Vec2", "Point", "Dims", "Pair"];

    private static Gen<NoFillScenario> GenNoFillScenario()
    {
        return Gen.Elements(
            ArrayCreationPath.BareStampablePrimitive,
            ArrayCreationPath.BareStampableStruct,
            ArrayCreationPath.InitializerClass,
            ArrayCreationPath.InitializerString,
            ArrayCreationPath.InitializerPrimitive)
            .SelectMany(GenScenarioForPath);
    }

    private static Gen<NoFillScenario> GenScenarioForPath(ArrayCreationPath path)
    {
        return path switch
        {
            ArrayCreationPath.BareStampablePrimitive => GenBareStampablePrimitive(),
            ArrayCreationPath.BareStampableStruct => GenBareStampableStruct(),
            ArrayCreationPath.InitializerClass => GenInitializerClass(),
            ArrayCreationPath.InitializerString => GenInitializerString(),
            ArrayCreationPath.InitializerPrimitive => GenInitializerPrimitive(),
            _ => GenInitializerPrimitive(),
        };
    }

    private static Gen<NoFillScenario> GenBareStampablePrimitive()
    {
        return from primType in Gen.Elements(PrimitiveTypes)
               from size in Gen.Choose(1, 20)
               let source = $$"""
                   namespace TestProject;

                   public class Program
                   {
                       public static void Main(string[] args)
                       {
                           var arr = new {{primType}}[{{size}}];
                       }
                   }
                   """
               select new NoFillScenario(source, ArrayCreationPath.BareStampablePrimitive, size);
    }

    private static Gen<NoFillScenario> GenBareStampableStruct()
    {
        return from structName in Gen.Elements(PureStructNames)
               from fieldCount in Gen.Choose(1, 3)
               from size in Gen.Choose(1, 20)
               let fields = string.Join("\n",
                   Enumerable.Range(0, fieldCount).Select(i =>
                       $"    public {PrimitiveTypes[i % PrimitiveTypes.Length]} f{i};"))
               let source = $$"""
                   namespace TestProject;

                   public struct {{structName}}
                   {
                   {{fields}}
                   }

                   public class Program
                   {
                       public static void Main(string[] args)
                       {
                           var arr = new {{structName}}[{{size}}];
                       }
                   }
                   """
               select new NoFillScenario(source, ArrayCreationPath.BareStampableStruct, size);
    }

    private static Gen<NoFillScenario> GenInitializerClass()
    {
        return from size in Gen.Choose(1, 10)
               let source = $$"""
                   using Standard;
                   namespace TestProject;

                   public class Item
                   {
                       public int Id;
                       public Item(int id) { this.Id = id; }
                   }

                   public class Program
                   {
                       public static void Main(string[] args)
                       {
                           var arr = new Array<Item, {{size}}>(i => new Item(i));
                       }
                   }
                   """
               select new NoFillScenario(source, ArrayCreationPath.InitializerClass, size);
    }

    private static Gen<NoFillScenario> GenInitializerString()
    {
        return from size in Gen.Choose(1, 10)
               let source = $$"""
                   using Standard;
                   namespace TestProject;

                   public class Program
                   {
                       public static void Main(string[] args)
                       {
                           var arr = new Array<string, {{size}}>(i => "hello");
                       }
                   }
                   """
               select new NoFillScenario(source, ArrayCreationPath.InitializerString, size);
    }

    private static Gen<NoFillScenario> GenInitializerPrimitive()
    {
        return from size in Gen.Choose(1, 10)
               let source = $$"""
                   using Standard;
                   namespace TestProject;

                   public class Program
                   {
                       public static void Main(string[] args)
                       {
                           var arr = new Array<int, {{size}}>(i => i * 2);
                       }
                   }
                   """
               select new NoFillScenario(source, ArrayCreationPath.InitializerPrimitive, size);
    }

    // ─── Arbitrary registration ──────────────────────────────────────────

    public static Arbitrary<NoFillScenario> ArbitraryNoFillScenario() =>
        GenNoFillScenario().ToArbitrary();

    // ═══════════════════════════════════════════════════════════════════════
    // Property 6: No hidden default-fill
    //
    // No array-creation path emits a call to `__CreateDefault()` per element;
    // the bare stampable path emits no per-element loop, and the initializer
    // path invokes only the user-supplied `fn`.
    //
    // **Validates: Requirements 4.3, 4.4, 6.3**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 6: No array-creation path emits a per-element <c>__CreateDefault()</c>
    /// call. Additionally, bare stampable arrays have no per-element loop, and
    /// initializer arrays invoke only the user function.
    ///
    /// Note: <c>__CreateDefault()</c> method *declarations* on types are expected
    /// (they are the standard factory for defaultable types). What this property
    /// asserts is that array-creation code does NOT call <c>__CreateDefault()</c>
    /// to fill array elements.
    ///
    /// **Validates: Requirements 4.3, 4.4, 6.3**
    /// </summary>
    [Property(
        Arbitrary = new[] { typeof(NoHiddenArrayFillPropertyTests) },
        MaxTest = 100)]
    public Property NoArrayPath_EmitsCreateDefault(NoFillScenario scenario)
    {
        var (succeeded, csContent, errorText) = BuildAndEmit(scenario.Source);

        if(!succeeded)
        {
            return false
                .ToProperty()
                .Label($"Compilation failed for {scenario}: {errorText}");
        }

        // Extract only the Main method body to inspect array-creation code.
        // __CreateDefault() declarations on types are fine — we only care that
        // the array-creation *code sites* don't invoke it per element.
        var mainBody = ExtractMainMethodBody(csContent);

        // Core assertion: the Main body (where arrays are created) should not
        // contain any call to __CreateDefault as part of filling array elements.
        var mainCallsCreateDefault = mainBody.Contains("__CreateDefault");

        if(mainCallsCreateDefault)
        {
            return false
                .ToProperty()
                .Label(
                    $"Array-creation code in Main calls __CreateDefault for {scenario}.\n" +
                    $"Main body:\n{mainBody}\n\nFull emitted content:\n{csContent}");
        }

        // Path-specific assertions
        return scenario.Path switch
        {
            ArrayCreationPath.BareStampablePrimitive or
            ArrayCreationPath.BareStampableStruct =>
                AssertBareStampableNoLoop(mainBody, scenario),

            ArrayCreationPath.InitializerClass or
            ArrayCreationPath.InitializerString or
            ArrayCreationPath.InitializerPrimitive =>
                AssertInitializerUsesOnlyFn(mainBody, scenario),

            _ => true.ToProperty()
        };
    }

    // ─── Helper to extract Main method body ──────────────────────────────

    /// <summary>
    /// Extracts the body of the Main method from emitted C# content.
    /// Looks for the method signature and extracts everything between
    /// its opening and closing braces.
    /// </summary>
    private static string ExtractMainMethodBody(string csContent)
    {
        var lines = csContent.Split('\n');
        var inMain = false;
        var braceDepth = 0;
        var bodyLines = new StringBuilder();

        foreach(var line in lines)
        {
            if(!inMain)
            {
                if(line.Contains("static void Main("))
                {
                    inMain = true;
                    // The opening brace may be on this line or the next
                    if(line.Contains('{'))
                    {
                        braceDepth = 1;
                    }
                }
                continue;
            }

            // Count braces to track when we exit Main
            foreach(var ch in line)
            {
                if(ch == '{')
                {
                    braceDepth++;
                }
                else if(ch == '}')
                {
                    braceDepth--;
                }
            }

            if(braceDepth <= 0)
            {
                break;
            }

            _ = bodyLines.AppendLine(line);
        }

        return bodyLines.ToString();
    }

    // ─── Path-specific assertion helpers ─────────────────────────────────

    /// <summary>
    /// For bare stampable arrays: the Main body should NOT contain a per-element
    /// initialization loop (no <c>for (int __i_</c> pattern, no <c>__arr_</c>
    /// temps that are the hallmark of the initializer-construction emission).
    /// </summary>
    private static Property AssertBareStampableNoLoop(string mainBody, NoFillScenario scenario)
    {
        // The bare stampable path should emit `new T[size]` directly.
        // It should NOT have a for-loop that fills elements (__i_ pattern from initializer).
        var hasInitializerLoop = mainBody.Contains("__i_") && mainBody.Contains("__arr_");

        if(hasInitializerLoop)
        {
            return false
                .ToProperty()
                .Label(
                    $"Bare stampable path has an initializer-style loop for {scenario}.\n" +
                    $"Main body:\n{mainBody}");
        }

        return true
            .ToProperty()
            .Label($"OK: bare stampable path has no per-element loop ({scenario})");
    }

    /// <summary>
    /// For initializer arrays: the Main body should contain the user's fn
    /// invocation (via the <c>__i_</c> loop pattern) but NOT <c>__CreateDefault</c>.
    /// The __CreateDefault check is already done above; here we confirm the loop
    /// uses the user fn (the __arr_[__i_] = ... pattern is present).
    /// </summary>
    private static Property AssertInitializerUsesOnlyFn(string mainBody, NoFillScenario scenario)
    {
        // The initializer path should emit a for-loop that assigns fn(i) to each slot.
        var hasLoop = mainBody.Contains("for (int __i_");
        var hasArrayAssignment = mainBody.Contains("__arr_");

        if(!hasLoop || !hasArrayAssignment)
        {
            return false
                .ToProperty()
                .Label(
                    $"Initializer path missing expected for-loop or array assignment for {scenario}.\n" +
                    $"HasLoop={hasLoop}, HasArrayAssignment={hasArrayAssignment}\n" +
                    $"Main body:\n{mainBody}");
        }

        return true
            .ToProperty()
            .Label($"OK: initializer path uses user fn ({scenario})");
    }
}