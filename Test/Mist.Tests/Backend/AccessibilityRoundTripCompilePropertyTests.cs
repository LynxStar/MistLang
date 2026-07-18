// Feature: full-accessibility-model, Property 19: Accepted programs emit valid, compilable C#
//
// **Validates: Requirements 15.4, 15.5**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

/// <summary>
/// Property-based tests verifying that accepted Mist programs (including those
/// exercising inheritable structs, sealed structs, and <c>protected</c> struct members)
/// emit valid, compilable C# when compiled in-process via Roslyn.
///
/// Feature: full-accessibility-model, Property 19: Accepted programs emit valid,
/// compilable C#.
///
/// For any program accepted by the classic pipeline — including programs exercising
/// inheritable structs, sealed structs, and protected struct members — the emitted C#
/// compiles without C# accessibility errors.
///
/// Strategy:
/// 1. Generate random Mist programs exercising the interesting lowering cases.
/// 2. Compile each program in-process via the shared <see cref="CompilerDriver"/>
///    (in-memory, <c>EmitToDisk: false</c>).
/// 3. Collect the emitted C# files as Roslyn syntax trees.
/// 4. Compile via <see cref="CSharpCompilation"/> against the same reference set
///    used by <see cref="CSharpCompilationDriver"/> (<c>CoreReferences</c>).
/// 5. Assert zero Roslyn errors.
///
/// Spawns 0 external processes.
///
/// **Validates: Requirements 15.4, 15.5**
/// </summary>
public sealed class AccessibilityRoundTripCompilePropertyTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    /// <summary>
    /// Compiles the Mist source in-process and returns the emitted C# files,
    /// or <c>null</c> if compilation fails (the program is not accepted).
    /// </summary>
    private static IReadOnlyDictionary<string, string>? CompileAndGetEmittedFiles(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_rt_compile_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(tempDir);
        try
        {
            var sourcePath = Path.Combine(tempDir, "Program.mist");
            File.WriteAllText(sourcePath, source);

            var options = new CompilerOptions(
                ProjectFolder: tempDir,
                SourcePaths: ImmutableArray.Create(sourcePath),
                Backend: new BackendId("csharp"),
                OutputDirectory: tempDir,
                BinDirectory: Path.Combine(tempDir, "bin"),
                ProjectName: "RoundTripTest",
                NamespaceRoot: "RoundTripTest",
                EnabledAnalyzerIds: ImmutableArray<string>.Empty,
                EmitToDisk: false);

            var result = SharedDriver.Compile(options);

            if(!result.Succeeded || result.EmittedFiles is null)
            {
                return null;
            }

            return result.EmittedFiles;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Compiles emitted C# files via Roslyn in-process and returns the diagnostics.
    /// No external process is spawned.
    /// </summary>
    private static ImmutableArray<Diagnostic> CompileEmittedCSharpViaRoslyn(
        IReadOnlyDictionary<string, string> emittedFiles)
    {
        var syntaxTrees = new List<SyntaxTree>();
        foreach(var (fileName, content) in emittedFiles)
        {
            if(fileName.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            syntaxTrees.Add(CSharpSyntaxTree.ParseText(
                content,
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
                path: fileName));
        }

        var compilation = CSharpCompilation.Create(
            assemblyName: "AccessibilityRoundTripTest",
            syntaxTrees: syntaxTrees,
            references: CSharpCompilationDriver.CoreReferences,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Debug,
                nullableContextOptions: NullableContextOptions.Enable,
                allowUnsafe: true,
                deterministic: true));

        using var peStream = new MemoryStream();
        var emitResult = compilation.Emit(peStream);
        return emitResult.Diagnostics;
    }

    // ─── Scenario model ──────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// Represents a generated Mist program scenario for round-trip compilation testing.
    /// </summary>
    public sealed record RoundTripScenario(
        string Source,
        string Description,
        RoundTripCategory Category)
    {
        public override string ToString()
        {
            return $"[{Category}] {Description}";
        }
    }

    /// <summary>
    /// Categories of generated programs, covering the interesting lowering cases.
    /// </summary>
    public enum RoundTripCategory
    {
        InheritableStruct,
        SealedStruct,
        ProtectedStructMember,
        SealedClass,
        MixedAccessibility,
        NestedTypes,
    }

#pragma warning restore CA1034

    // ─── Name pools ──────────────────────────────────────────────────────

    private static readonly string[] BaseStructNames =
        ["Vec", "Coord", "Cell", "Shape", "Item", "Node"];

    private static readonly string[] DerivedStructNames =
        ["UnitVec", "SubCoord", "SubCell", "SubShape", "SubItem", "SubNode"];

    private static readonly string[] ClassNames =
        ["Alpha", "Beta", "Gamma", "Delta", "Epsilon", "Zeta"];

    private static readonly string[] FieldNames =
        ["value", "count", "length", "size", "index", "depth"];

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates an inheritable struct scenario: a base struct with a derived struct
    /// that accesses inherited members. This exercises the struct-to-class lowering path.
    /// </summary>
    private static Gen<RoundTripScenario> GenInheritableStruct()
    {
        var genBaseIdx = Gen.Choose(0, BaseStructNames.Length - 1);
        var genDerivedIdx = Gen.Choose(0, DerivedStructNames.Length - 1);
        var genFieldIdx = Gen.Choose(0, FieldNames.Length - 1);
        var genAccess = Gen.Elements("public", "internal", "protected");

        return from baseIdx in genBaseIdx
               from derivedIdx in genDerivedIdx
               from fieldIdx in genFieldIdx
               from access in genAccess
               select new RoundTripScenario(
                   $$"""
                   public struct {{BaseStructNames[baseIdx]}}
                   {
                       {{access}} int {{FieldNames[fieldIdx]}};

                       public {{BaseStructNames[baseIdx]}}(int v)
                       {
                           this.{{FieldNames[fieldIdx]}} = v;
                       }

                       public int GetValue()
                       {
                           return this.{{FieldNames[fieldIdx]}};
                       }
                   }
                   public struct {{DerivedStructNames[derivedIdx]}} : {{BaseStructNames[baseIdx]}}
                   {
                       public {{DerivedStructNames[derivedIdx]}}(int v) : base(v)
                       {
                       }

                       public int Doubled()
                       {
                           return this.{{FieldNames[fieldIdx]}} * 2;
                       }
                   }
                   class Program
                   {
                       static void Main()
                       {
                           var x = new {{DerivedStructNames[derivedIdx]}}(5);
                           Console.WriteLine(x.Doubled());
                       }
                   }
                   """,
                   $"inheritable struct {BaseStructNames[baseIdx]} ({access} field) -> {DerivedStructNames[derivedIdx]}",
                   RoundTripCategory.InheritableStruct);
    }

    /// <summary>
    /// Generates a sealed struct scenario. A sealed struct with various access levels
    /// on its members (but never protected, to avoid the warning). Because it's sealed,
    /// it cannot be inherited.
    /// </summary>
    private static Gen<RoundTripScenario> GenSealedStruct()
    {
        var genNameIdx = Gen.Choose(0, BaseStructNames.Length - 1);
        var genFieldIdx = Gen.Choose(0, FieldNames.Length - 1);
        var genFieldAccess = Gen.Elements("public", "private", "internal");

        return from nameIdx in genNameIdx
               from fieldIdx in genFieldIdx
               from fieldAccess in genFieldAccess
               select new RoundTripScenario(
                   $$"""
                   public sealed struct {{BaseStructNames[nameIdx]}}
                   {
                       {{fieldAccess}} int {{FieldNames[fieldIdx]}};

                       public {{BaseStructNames[nameIdx]}}(int v)
                       {
                           this.{{FieldNames[fieldIdx]}} = v;
                       }

                       public int GetValue()
                       {
                           return this.{{FieldNames[fieldIdx]}};
                       }
                   }
                   class Program
                   {
                       static void Main()
                       {
                           var s = new {{BaseStructNames[nameIdx]}}(42);
                           Console.WriteLine(s.GetValue());
                       }
                   }
                   """,
                   $"sealed struct {BaseStructNames[nameIdx]} ({fieldAccess} field)",
                   RoundTripCategory.SealedStruct);
    }

    /// <summary>
    /// Generates a struct with a protected member, exercising the struct-to-class lowering
    /// that is required because C# value-type structs cannot have protected members.
    /// </summary>
    private static Gen<RoundTripScenario> GenProtectedStructMember()
    {
        var genBaseIdx = Gen.Choose(0, BaseStructNames.Length - 1);
        var genDerivedIdx = Gen.Choose(0, DerivedStructNames.Length - 1);
        var genFieldIdx = Gen.Choose(0, FieldNames.Length - 1);

        return from baseIdx in genBaseIdx
               from derivedIdx in genDerivedIdx
               from fieldIdx in genFieldIdx
               select new RoundTripScenario(
                   $$"""
                   public struct {{BaseStructNames[baseIdx]}}
                   {
                       protected int {{FieldNames[fieldIdx]}};

                       public {{BaseStructNames[baseIdx]}}(int v)
                       {
                           this.{{FieldNames[fieldIdx]}} = v;
                       }
                   }
                   public struct {{DerivedStructNames[derivedIdx]}} : {{BaseStructNames[baseIdx]}}
                   {
                       public {{DerivedStructNames[derivedIdx]}}(int v) : base(v)
                       {
                       }

                       public int ReadProtected()
                       {
                           return this.{{FieldNames[fieldIdx]}};
                       }
                   }
                   class Program
                   {
                       static void Main()
                       {
                           var x = new {{DerivedStructNames[derivedIdx]}}(7);
                           Console.WriteLine(x.ReadProtected());
                       }
                   }
                   """,
                   $"protected field on struct {BaseStructNames[baseIdx]} -> derived {DerivedStructNames[derivedIdx]}",
                   RoundTripCategory.ProtectedStructMember);
    }

    /// <summary>
    /// Generates a sealed class scenario with various member accessibilities.
    /// </summary>
    private static Gen<RoundTripScenario> GenSealedClass()
    {
        var genNameIdx = Gen.Choose(0, ClassNames.Length - 1);
        var genFieldIdx = Gen.Choose(0, FieldNames.Length - 1);
        var genFieldAccess = Gen.Elements("public", "private", "internal");

        return from nameIdx in genNameIdx
               from fieldIdx in genFieldIdx
               from fieldAccess in genFieldAccess
               select new RoundTripScenario(
                   $$"""
                   public sealed class {{ClassNames[nameIdx]}}
                   {
                       {{fieldAccess}} int {{FieldNames[fieldIdx]}};

                       public {{ClassNames[nameIdx]}}(int v)
                       {
                           this.{{FieldNames[fieldIdx]}} = v;
                       }

                       public int GetValue()
                       {
                           return this.{{FieldNames[fieldIdx]}};
                       }
                   }
                   class Program
                   {
                       static void Main()
                       {
                           var obj = new {{ClassNames[nameIdx]}}(10);
                           Console.WriteLine(obj.GetValue());
                       }
                   }
                   """,
                   $"sealed class {ClassNames[nameIdx]} ({fieldAccess} field)",
                   RoundTripCategory.SealedClass);
    }

    /// <summary>
    /// Generates programs with mixed accessibility levels on a class hierarchy,
    /// including public, private, protected, and internal members.
    /// </summary>
    private static Gen<RoundTripScenario> GenMixedAccessibility()
    {
        var genBaseIdx = Gen.Choose(0, ClassNames.Length - 1);
        var genDerivedIdx = Gen.Choose(0, ClassNames.Length - 1);

        return from baseIdx in genBaseIdx
               from derivedIdx in genDerivedIdx.Where(i => i != baseIdx)
               select new RoundTripScenario(
                   $$"""
                   public class {{ClassNames[baseIdx]}}
                   {
                       public int pub;
                       private int priv;
                       protected int prot;
                       internal int intern;

                       public {{ClassNames[baseIdx]}}(int a, int b, int c, int d)
                       {
                           this.pub = a;
                           this.priv = b;
                           this.prot = c;
                           this.intern = d;
                       }

                       public int Sum()
                       {
                           return pub + priv + prot + intern;
                       }
                   }
                   public class {{ClassNames[derivedIdx]}} : {{ClassNames[baseIdx]}}
                   {
                       public {{ClassNames[derivedIdx]}}() : base(1, 2, 3, 4)
                       {
                       }

                       public int DerivedSum()
                       {
                           return pub + prot + intern;
                       }
                   }
                   class Program
                   {
                       static void Main()
                       {
                           var d = new {{ClassNames[derivedIdx]}}();
                           Console.WriteLine(d.DerivedSum());
                       }
                   }
                   """,
                   $"mixed accessibility class {ClassNames[baseIdx]} -> {ClassNames[derivedIdx]}",
                   RoundTripCategory.MixedAccessibility);
    }

    /// <summary>
    /// Generates programs with nested types at various accessibility levels.
    /// </summary>
    private static Gen<RoundTripScenario> GenNestedTypes()
    {
        var genOuterIdx = Gen.Choose(0, ClassNames.Length - 1);
        var genInnerAccess = Gen.Elements("public", "private", "internal", "protected");

        return from outerIdx in genOuterIdx
               from innerAccess in genInnerAccess
               select new RoundTripScenario(
                   $$"""
                   public class {{ClassNames[outerIdx]}}
                   {
                       {{innerAccess}} class Inner
                       {
                           public int tag;

                           public Inner(int t)
                           {
                               this.tag = t;
                           }
                       }

                       private Inner nested;

                       public {{ClassNames[outerIdx]}}(int t)
                       {
                           this.nested = new Inner(t);
                       }

                       public int GetTag()
                       {
                           return nested.tag;
                       }
                   }
                   class Program
                   {
                       static void Main()
                       {
                           var obj = new {{ClassNames[outerIdx]}}(99);
                           Console.WriteLine(obj.GetTag());
                       }
                   }
                   """,
                   $"nested {innerAccess} class inside {ClassNames[outerIdx]}",
                   RoundTripCategory.NestedTypes);
    }

    /// <summary>
    /// Combined generator that produces scenarios from all categories.
    /// Weighted toward the struct-lowering categories since those are the
    /// interesting cases for Requirement 15.4.
    /// </summary>
    private static Gen<RoundTripScenario> GenRoundTripScenario()
    {
        return Gen.Frequency(
            (3, GenInheritableStruct()),
            (3, GenProtectedStructMember()),
            (2, GenSealedStruct()),
            (2, GenSealedClass()),
            (2, GenMixedAccessibility()),
            (2, GenNestedTypes()));
    }

    public static Arbitrary<RoundTripScenario> ArbitraryRoundTripScenario()
    {
        return GenRoundTripScenario().ToArbitrary();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 19: Accepted programs emit valid, compilable C#
    //
    // For any program accepted by the classic pipeline — including programs
    // exercising inheritable structs, sealed structs, and protected struct
    // members — the emitted C# compiles without C# accessibility errors.
    //
    // **Validates: Requirements 15.4, 15.5**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AccessibilityRoundTripCompilePropertyTests) })]
    public Property AcceptedPrograms_EmitValidCompilableCSharp(RoundTripScenario scenario)
    {
        // Step 1: Compile the Mist source in-process via the classic pipeline.
        var emittedFiles = CompileAndGetEmittedFiles(scenario.Source);

        if(emittedFiles is null)
        {
            // If the Mist compiler rejects the program, the property is vacuously true
            // (the property only applies to accepted programs).
            return true.ToProperty()
                .Label($"Mist compilation rejected (vacuously true): {scenario.Description}");
        }

        // Step 2: Compile the emitted C# via Roslyn in-process (no process spawn).
        var roslynDiagnostics = CompileEmittedCSharpViaRoslyn(emittedFiles);

        // Step 3: Assert zero Roslyn errors.
        var errors = roslynDiagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if(errors.Count > 0)
        {
            var errorDetails = string.Join("\n",
                errors.Select(e => $"  {e.Id}: {e.GetMessage()}\n    at {e.Location}"));

            return false.ToProperty()
                .Label($"Roslyn compilation produced {errors.Count} error(s) for: {scenario.Description}\n"
                    + $"Errors:\n{errorDetails}\n"
                    + $"Source:\n{scenario.Source}");
        }

        return true.ToProperty()
            .Label($"OK: {scenario.Description}");
    }
}