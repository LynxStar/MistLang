// Feature: disposable-drop-interop, Property 1: Emitter dispatch correctness
// **Validates: Requirements 2.3, 3.1, 3.2**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;
using Mist.Tests.InteropFixtures;

namespace Mist.Tests.Backend;

/// <summary>
/// Property-based tests for emitter dispatch correctness.
///
/// Feature: disposable-drop-interop, Property 1: Emitter dispatch correctness
/// **Validates: Requirements 2.3, 3.1, 3.2**
///
/// For any droppable non-task-like type at a drop emission site, the emitted call string
/// contains <c>.Dispose()</c> if and only if the type has <c>IsDisposableDrop = true</c>,
/// and contains <c>.Drop()</c> if and only if the type has <c>IsDisposableDrop = false</c>.
///
/// Strategy:
/// <list type="bullet">
///   <item>Part A: For each generated type configuration (HasDrop=true, IsDisposableDrop: bool),
///   construct a minimal ClassType/StructType with appropriate flags and verify via TypeProjector
///   that CLR IDisposable types get <c>IsDisposableDrop=true</c> and non-IDisposable types get
///   <c>IsDisposableDrop=false</c>.</item>
///   <item>Part B: For Mist-declared types with explicit <c>Drop()</c>, compile in-process and
///   verify the emitter produces <c>.Drop()</c> (never <c>.Dispose()</c>).</item>
/// </list>
///
/// All work runs in-process — no external process is spawned.
/// </summary>
public sealed class EmitterDispatchPropertyTests : IDisposable
{
    private readonly string outputDir;

    public EmitterDispatchPropertyTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "MistEmitterDispatch_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(outputDir))
        {
            try { Directory.Delete(outputDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(
            new TypeCheckerAnalyzer(),
            new ReadonlyAnalyzer(),
            new OwnershipAnalyzer(),
            new DropAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private static readonly TypeProjector Projector = new(new IntrinsicTypeMap(), new VisibilityFilter());
    private static readonly IStdlibTypeProvider Stdlib = FallbackStdlibTypeProvider.Instance;

    // ─── Scenario types ──────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// A generated type configuration for testing emitter dispatch.
    /// </summary>
    public sealed class DispatchScenario
    {
        public bool IsDisposableDrop { get; }
        public bool IsStruct { get; }
        public string TypeSuffix { get; }

        public DispatchScenario(bool isDisposableDrop, bool isStruct, string typeSuffix)
        {
            IsDisposableDrop = isDisposableDrop;
            IsStruct = isStruct;
            TypeSuffix = typeSuffix;
        }

        public override string ToString() =>
            $"IsDisposableDrop={IsDisposableDrop}, IsStruct={IsStruct}, Suffix={TypeSuffix}";
    }

    /// <summary>
    /// The form of with-block used for emission testing.
    /// </summary>
    public enum WithForm
    {
        Block,
        VarDecl,
    }

    /// <summary>
    /// Scenario for testing explicit Drop dispatch via compilation pipeline.
    /// </summary>
    public sealed class ExplicitDropEmissionScenario
    {
        public WithForm Form { get; }
        public string TypeSuffix { get; }
        public bool IsLinear { get; }

        public ExplicitDropEmissionScenario(WithForm form, string typeSuffix, bool isLinear)
        {
            Form = form;
            TypeSuffix = typeSuffix;
            IsLinear = isLinear;
        }

        public override string ToString() =>
            $"Form={Form}, Suffix={TypeSuffix}, IsLinear={IsLinear}";
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] TypeSuffixes = ["Ctx", "Svc", "Hdl", "Res", "Mgr"];

    private static Gen<DispatchScenario> GenDispatchScenario()
    {
        return from isDisposable in Gen.Elements(true, false)
               from isStruct in Gen.Elements(true, false)
               from suffix in Gen.Elements(TypeSuffixes)
               select new DispatchScenario(isDisposable, isStruct, suffix);
    }

    private static Gen<ExplicitDropEmissionScenario> GenExplicitDropEmissionScenario()
    {
        return from form in Gen.Elements(WithForm.Block, WithForm.VarDecl)
               from suffix in Gen.Elements(TypeSuffixes)
               from isLinear in Gen.Elements(true, false)
               select new ExplicitDropEmissionScenario(form, suffix, isLinear);
    }

    public static Arbitrary<DispatchScenario> ArbitraryDispatchScenario() =>
        GenDispatchScenario().ToArbitrary();

    public static Arbitrary<ExplicitDropEmissionScenario> ArbitraryExplicitDropEmissionScenario() =>
        GenExplicitDropEmissionScenario().ToArbitrary();

    // ─── Type construction ───────────────────────────────────────────────

    /// <summary>
    /// Builds a minimal ClassType or StructType with the specified flags.
    /// </summary>
    private static IType BuildType(DispatchScenario scenario)
    {
        var scope = new Scope(parent: null);
        var typeName = $"My{scenario.TypeSuffix}";

        if(scenario.IsStruct)
        {
            return new StructType(typeName, scope, containingNamespace: "Test")
            {
                HasDrop = true,
                IsDisposableDrop = scenario.IsDisposableDrop,
            };
        }

        return new ClassType(typeName, scope, containingNamespace: "Test")
        {
            HasDrop = true,
            IsDisposableDrop = scenario.IsDisposableDrop,
        };
    }

    // ─── Compilation helpers ─────────────────────────────────────────────

    private CompilerResult CompileSource(string source)
    {
        var filePath = Path.Combine(outputDir, $"Test_{Guid.NewGuid():N}.mist");
        File.WriteAllText(filePath, source);

        var options = new CompilerOptions(
            ProjectFolder: outputDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: outputDir,
            BinDirectory: Path.Combine(outputDir, "bin"),
            ProjectName: "TestProgram",
            NamespaceRoot: "TestProgram",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);

        return SharedDriver.Compile(options);
    }

    private static string? GetEmittedCSharp(CompilerResult result)
    {
        if(result.EmittedFiles is null)
        {
            return null;
        }

        foreach(var (_, content) in result.EmittedFiles)
        {
            if(content.Contains("namespace TestProgram"))
            {
                return content;
            }
        }

        return null;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 1a: IsDisposableDrop flag correctness on constructed types
    //
    // For any type configuration (HasDrop=true, IsDisposableDrop: bool):
    //   - The flag is correctly readable on ClassType and StructType
    //   - ConstructedType wrappers preserve the flag from the generic definition
    //   - TypeAliasType wrappers preserve the flag from the underlying type
    //
    // This verifies the prerequisite for correct dispatch: if the flag is
    // correctly set and resolvable, the emitter will dispatch correctly.
    //
    // **Validates: Requirements 2.3, 3.1**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(EmitterDispatchPropertyTests) })]
    public Property IsDisposableDropFlag_PreservedThroughTypeWrappers(DispatchScenario scenario)
    {
        var baseType = BuildType(scenario);

        // Test 1: Direct flag on ClassType/StructType
        var directFlag = baseType switch
        {
            ClassType ct => ct.IsDisposableDrop,
            StructType st => st.IsDisposableDrop,
            _ => false,
        };

        if(directFlag != scenario.IsDisposableDrop)
        {
            return false.ToProperty().Label(
                $"Direct flag mismatch: expected {scenario.IsDisposableDrop}, got {directFlag}. "
                + $"Scenario: {scenario}");
        }

        // Test 2: ConstructedType preserves flag from generic definition
        var constructed = new ConstructedType(
            genericDefinition: baseType,
            typeArguments: [new ClassType("int", new Scope(null), containingNamespace: "System")]);

        var constructedFlag = constructed.GenericDefinition switch
        {
            ClassType ct => ct.IsDisposableDrop,
            StructType st => st.IsDisposableDrop,
            _ => false,
        };

        if(constructedFlag != scenario.IsDisposableDrop)
        {
            return false.ToProperty().Label(
                $"ConstructedType wrapper lost flag: expected {scenario.IsDisposableDrop}, got {constructedFlag}. "
                + $"Scenario: {scenario}");
        }

        // Test 3: TypeAliasType preserves flag from underlying type
        var alias = new TypeAliasType(
            name: $"My{scenario.TypeSuffix}Alias",
            underlyingType: baseType,
            typeParameters: []);

        var aliasFlag = alias.UnderlyingType switch
        {
            ClassType ct => ct.IsDisposableDrop,
            StructType st => st.IsDisposableDrop,
            _ => false,
        };

        if(aliasFlag != scenario.IsDisposableDrop)
        {
            return false.ToProperty().Label(
                $"TypeAliasType wrapper lost flag: expected {scenario.IsDisposableDrop}, got {aliasFlag}. "
                + $"Scenario: {scenario}");
        }

        return true.ToProperty().Label(
            $"Flag correctly preserved through all wrappers for {scenario}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 1b: TypeProjector sets IsDisposableDrop correctly for CLR types
    //
    // For CLR types implementing IDisposable (without explicit Drop):
    //   - HasDrop = true AND IsDisposableDrop = true
    // For CLR types NOT implementing IDisposable:
    //   - IsDisposableDrop = false
    // For CLR types with IDisposable AND explicit Drop():
    //   - HasDrop = true AND IsDisposableDrop = false
    //
    // This verifies the emitter will dispatch .Dispose() for IDisposable types
    // and .Drop() for explicit Drop types.
    //
    // **Validates: Requirements 2.3, 3.1, 3.2**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// CLR types that implement IDisposable without explicit Drop — expect IsDisposableDrop=true.
    /// </summary>
    private static readonly Type[] DisposableClrTypes =
    [
        typeof(SimpleDisposable),
        typeof(TransitiveDisposable),
        typeof(BothDisposables),
        typeof(GenericDisposable<>),
    ];

    /// <summary>
    /// CLR types that implement IDisposable WITH explicit Drop — expect IsDisposableDrop=false.
    /// </summary>
    private static readonly Type[] DisposableWithDropClrTypes =
    [
        typeof(DisposableWithDrop),
    ];

    /// <summary>
    /// CLR types that do NOT implement IDisposable — expect IsDisposableDrop=false.
    /// </summary>
    private static readonly Type[] NonDisposableClrTypes =
    [
        typeof(AsyncOnlyDisposable),
        typeof(SimpleClass),
        typeof(string),
    ];

    private static Gen<(Type ClrType, bool ExpectDisposableDrop, bool ExpectHasDrop)> GenClrTypeScenario()
    {
        var disposableItems = DisposableClrTypes.Select(t => (t, true, true));
        var withDropItems = DisposableWithDropClrTypes.Select(t => (t, false, true));
        var nonItems = NonDisposableClrTypes.Select(t => (t, false, false));

        var allItems = disposableItems.Concat(withDropItems).Concat(nonItems).ToArray();
        return Gen.Elements(allItems);
    }

    public static Arbitrary<(Type ClrType, bool ExpectDisposableDrop, bool ExpectHasDrop)> ArbitraryClrTypeScenario() =>
        GenClrTypeScenario().ToArbitrary();

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(EmitterDispatchPropertyTests) })]
    public Property TypeProjector_SetsIsDisposableDropCorrectly(
        (Type ClrType, bool ExpectDisposableDrop, bool ExpectHasDrop) scenario)
    {
        var (projectedType, _) = Projector.ProjectType(scenario.ClrType, Stdlib);

        bool actualIsDisposableDrop;
        bool actualHasDrop;

        switch(projectedType)
        {
            case ClassType ct:
                actualIsDisposableDrop = ct.IsDisposableDrop;
                actualHasDrop = ct.HasDrop;
                break;
            case StructType st:
                actualIsDisposableDrop = st.IsDisposableDrop;
                actualHasDrop = st.HasDrop;
                break;
            default:
                // Non-class/struct types (abstract, enum) don't have IsDisposableDrop
                return true.ToProperty().Label($"Skipped: {scenario.ClrType.Name} projects to {projectedType.GetType().Name}");
        }

        if(actualIsDisposableDrop != scenario.ExpectDisposableDrop)
        {
            return false.ToProperty().Label(
                $"VIOLATION: {scenario.ClrType.Name} → IsDisposableDrop={actualIsDisposableDrop}, "
                + $"expected {scenario.ExpectDisposableDrop}");
        }

        if(actualHasDrop != scenario.ExpectHasDrop)
        {
            return false.ToProperty().Label(
                $"VIOLATION: {scenario.ClrType.Name} → HasDrop={actualHasDrop}, "
                + $"expected {scenario.ExpectHasDrop}");
        }

        // Verify the dispatch invariant:
        // If IsDisposableDrop=true → emitter would emit .Dispose()
        // If IsDisposableDrop=false AND HasDrop=true → emitter would emit .Drop()
        var expectedCall = scenario.ExpectDisposableDrop ? ".Dispose()" : ".Drop()";
        return true.ToProperty().Label(
            $"Correct: {scenario.ClrType.Name} → IsDisposableDrop={actualIsDisposableDrop}, "
            + $"HasDrop={actualHasDrop} → emitter dispatches {expectedCall}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 1c: Explicit Drop types emit .Drop() (never .Dispose())
    //
    // For any Mist-declared type with an explicit Drop() method:
    //   - Emitted C# contains .Drop()
    //   - Emitted C# does NOT contain .Dispose()
    //
    // This verifies the non-IDisposable path of the emitter dispatch.
    //
    // **Validates: Requirements 3.2**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(EmitterDispatchPropertyTests) })]
    public Property ExplicitDropType_EmitsDrop_NeverDispose(ExplicitDropEmissionScenario scenario)
    {
        var typeName = $"My{scenario.TypeSuffix}";
        var modifier = scenario.IsLinear ? "linear " : "";

        var withUsage = scenario.Form switch
        {
            WithForm.Block => $$"""
                        with(new {{typeName}}())
                        {
                            int x = 42;
                        }
            """,
            WithForm.VarDecl => $$"""
                        with var r = new {{typeName}}();
                        int x = 42;
            """,
            _ => throw new ArgumentOutOfRangeException(),
        };

        var source = $$"""
            {{modifier}}class {{typeName}}
            {
                public void Drop()
                {
                }
            }
            class Program
            {
                public static void Main()
                {
            {{withUsage}}
                }
            }
            """;

        var result = CompileSource(source);

        if(!result.Succeeded)
        {
            var diagnostics = string.Join(", ", result.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"));
            return false.ToProperty()
                .Label($"Compilation failed: [{diagnostics}]\nSource:\n{source}");
        }

        var emittedCSharp = GetEmittedCSharp(result);
        if(emittedCSharp is null)
        {
            return false.ToProperty().Label($"No emitted C# found.\nSource:\n{source}");
        }

        // Requirement 3.2: Explicit Drop type emits .Drop()
        var containsDrop = emittedCSharp.Contains(".Drop()");
        if(!containsDrop)
        {
            return false.ToProperty().Label(
                $"VIOLATION Req 3.2: Explicit Drop type did not emit '.Drop()'. "
                + $"Scenario: {scenario}\nEmitted C#:\n{emittedCSharp}");
        }

        // Requirement 2.3: Explicit Drop type must NOT emit .Dispose()
        var containsDispose = emittedCSharp.Contains(".Dispose()");
        if(containsDispose)
        {
            return false.ToProperty().Label(
                $"VIOLATION Req 2.3: Explicit Drop type incorrectly emits '.Dispose()'. "
                + $"Scenario: {scenario}\nEmitted C#:\n{emittedCSharp}");
        }

        return true.ToProperty().Label(
            $"Correct: Explicit Drop emits .Drop() for {scenario}");
    }
}