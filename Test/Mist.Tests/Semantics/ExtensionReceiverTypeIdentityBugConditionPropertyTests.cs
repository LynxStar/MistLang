// Bugfix: type-resolution-unification, Property 1: Bug Condition
// Extension Receiver Type Identity Failure
//
// **Validates: Requirements 1.1, 1.2, 1.3, 1.4**
//
// CRITICAL: This test MUST FAIL on unfixed code — failure confirms the bug exists.
// DO NOT attempt to fix the test or the code when it fails.
//
// Bug Condition: A CLR interface type projected as an extension receiver via
//                ProjectTypeReference (cache-only path) creates a disconnected
//                shell that is NOT ReferenceEquals to the canonical instance from
//                AssemblyTypeProvider.ResolveType.
//
// Expected behavior after fix:
//   - ReceiverType on ExtensionBlockInfo IS ReferenceEquals to canonical instance
//   - Generic extension method calls with explicit type args resolve to concrete type

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Bug condition exploration property test for the type-resolution-unification defect.
///
/// <b>Property 1: Bug Condition</b> — Extension Receiver Type Identity Failure
///
/// <b>Validates: Requirements 1.1, 1.2, 1.3, 1.4</b>
///
/// For any CLR interface type registered as an extension receiver (via
/// <c>RegisterAmbientExtensions</c>), the extension block's <c>ReceiverType</c>
/// SHALL be <c>ReferenceEquals</c> to the canonical type instance returned by
/// <c>AssemblyTypeProvider.ResolveType</c> for the same namespace/name.
///
/// Additionally, a generic extension method call with explicit type arguments
/// (e.g., <c>Cast&lt;int&gt;()</c> on <c>IEnumerable</c>) SHALL resolve to the
/// concrete substituted return type, not <c>unknown</c>.
///
/// On unfixed code this test is EXPECTED TO FAIL, confirming the bug exists:
/// <c>ProjectTypeReference</c> uses the cache-only resolver that creates a
/// disconnected shell on cache miss, so the extension's receiver type is a
/// different instance than the canonical one. The generic extension resolution
/// may also produce <c>unknown</c> due to the fragmented lookup API.
///
/// All work runs in-process — no external process is spawned per iteration
/// (in keeping with the workspace test-process-spawning policy).
/// </summary>
public sealed class ExtensionReceiverTypeIdentityBugConditionPropertyTests : IDisposable
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly SemanticModelBuilder Builder = new();
    private static readonly MistLexer Lexer = new();
    private static readonly MistParser Parser = new();

    /// <summary>
    /// Shared CompilerDriver for in-process compilation (Property B scenarios).
    /// </summary>
    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    /// <summary>
    /// Runtime assembly paths for creating AssemblyTypeProvider.
    /// </summary>
    private static readonly ImmutableArray<string> RuntimeAssemblyPaths = GetRuntimeAssemblyPaths();

    private readonly AssemblyTypeProvider provider;

    public ExtensionReceiverTypeIdentityBugConditionPropertyTests()
    {
        var diagnostics = new DiagnosticBag();
        provider = new AssemblyTypeProvider(RuntimeAssemblyPaths, diagnostics);
    }

    public void Dispose()
    {
        provider.Dispose();
    }

    private static ImmutableArray<string> GetRuntimeAssemblyPaths()
    {
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        return Directory.GetFiles(runtimeDir, "*.dll").ToImmutableArray();
    }

    // ─── Scenario types ──────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// Describes a CLR interface type that has ambient extension methods registered
    /// in a specific namespace. Used for Property A (reference identity).
    /// </summary>
    public record InterfaceExtensionScenario(
        string InterfaceNamespace,
        string InterfaceName,
        string ExtensionNamespace)
    {
        public override string ToString()
        {
            return $"{InterfaceNamespace}.{InterfaceName} with extensions from {ExtensionNamespace}";
        }
    }

#pragma warning restore CA1034

    // ─── FsCheck generators ──────────────────────────────────────────────

    /// <summary>
    /// Known CLR interfaces that have extension methods in the runtime.
    /// These are non-generic interfaces (AbstractType) targeted by extension
    /// methods in System.Linq or other runtime namespaces.
    /// </summary>
    private static readonly InterfaceExtensionScenario[] KnownInterfaceExtensionScenarios =
    [
        // IEnumerable (non-generic) has Cast<T> and OfType<T> from System.Linq.Enumerable
        new("System.Collections", "IEnumerable", "System.Linq"),
    ];

    public static Arbitrary<InterfaceExtensionScenario> ArbitraryInterfaceExtensionScenario()
    {
        return Gen.Elements(KnownInterfaceExtensionScenarios).ToArbitrary();
    }

    // ─── Model building ──────────────────────────────────────────────────

    /// <summary>
    /// Builds a semantic model with the AssemblyTypeProvider as the ambient resolver,
    /// with the specified namespace imported (to trigger ambient extension registration).
    /// </summary>
    private (ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics) BuildModelWithProvider(
        string importedNamespace)
    {
        var (stdlibUnits, stdlibAsts) = Helpers.StdlibTestHelper.LoadStdlib();

        // Create a minimal Mist source with the required using declaration
        var source = $"using {importedNamespace};\n";
        var userSourceText = new SourceText("test.mist", source);
        var userUnit = new CompilationUnit(userSourceText);
        var diagnostics = new DiagnosticBag();
        var userTokens = Lexer.Lex(userSourceText, diagnostics);
        var userParseResult = Parser.Parse(userTokens, diagnostics);

        if(userParseResult.Root is null)
        {
            throw new InvalidOperationException($"Failed to parse source with using {importedNamespace}");
        }

        var allUnits = stdlibUnits.Add(userUnit);
        var allAsts = stdlibAsts.Add(userParseResult.Root);

        var compilation = new MistCompilation(allUnits);
        var model = Builder.Build(
            compilation, allAsts, provider,
            namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);

        return (model, diagnostics.ToImmutableArray());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 1A: Bug Condition — Extension Receiver Type Identity Failure
    //
    // **Validates: Requirements 1.1, 1.2**
    //
    // For any CLR interface type registered as an extension receiver, the
    // extension block's ReceiverType SHALL be ReferenceEquals to the canonical
    // type instance from AssemblyTypeProvider.ResolveType.
    //
    // On UNFIXED code this test WILL FAIL — ProjectTypeReference creates a
    // disconnected shell for the receiver type that is a different object
    // than the canonical instance in the cache.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 10, Arbitrary = new[] { typeof(ExtensionReceiverTypeIdentityBugConditionPropertyTests) })]
    public Property ExtensionReceiverType_ShouldBeReferenceEqualToCanonicalType(
        InterfaceExtensionScenario scenario)
    {
        // Build a semantic model that triggers ambient extension registration
        // for the given namespace.
        var (model, _) = BuildModelWithProvider(scenario.ExtensionNamespace);

        // Get the canonical type from the provider's cache.
        var canonicalType = provider.ResolveType(scenario.InterfaceNamespace, scenario.InterfaceName);

        if(canonicalType is null)
        {
            return false.ToProperty()
                .Label($"AssemblyTypeProvider.ResolveType returned null for " +
                       $"{scenario.InterfaceNamespace}.{scenario.InterfaceName}");
        }

        // Find the extension block whose receiver type represents this interface.
        // Iterate all registered extension groups to find one that structurally
        // matches our target interface.
        ExtensionBlockInfo? matchingBlock = null;
        IType? extensionReceiverType = null;

        foreach(var (receiverType, blocks) in model.ExtensionRegistry.AllGroups)
        {
            // Check if this receiver type represents our target interface by name.
            if(receiverType is AbstractType abstractReceiver
                && abstractReceiver.DisplayName == scenario.InterfaceName
                && abstractReceiver.ContainingNamespace == scenario.InterfaceNamespace)
            {
                matchingBlock = blocks.FirstOrDefault();
                extensionReceiverType = receiverType;
                break;
            }
        }

        if(matchingBlock is null || extensionReceiverType is null)
        {
            return false.ToProperty()
                .Label($"No extension block found for {scenario.InterfaceNamespace}.{scenario.InterfaceName}. " +
                       $"Extension registry has {model.ExtensionRegistry.AllGroups.Count()} groups.");
        }

        // THE KEY ASSERTION: Verify reference equality between the extension's
        // receiver type and the canonical type from the provider.
        //
        // On UNFIXED code: This WILL fail because ProjectTypeReference creates a
        // fresh shell (via the cache-only resolver) that is NOT the same instance
        // as what ResolveType returns.
        var isReferenceEqual = ReferenceEquals(extensionReceiverType, canonicalType);

        if(!isReferenceEqual)
        {
            return false.ToProperty()
                .Label($"BUG CONFIRMED: Extension receiver type for " +
                       $"{scenario.InterfaceNamespace}.{scenario.InterfaceName} is NOT " +
                       $"ReferenceEquals to the canonical type from ResolveType. " +
                       $"Receiver type: {extensionReceiverType.GetType().Name} " +
                       $"'{extensionReceiverType.DisplayName}' @ {extensionReceiverType.GetHashCode():X8}, " +
                       $"Canonical type: {canonicalType.GetType().Name} " +
                       $"'{canonicalType.DisplayName}' @ {canonicalType.GetHashCode():X8}. " +
                       $"These are two disconnected instances of the same logical type.");
        }

        return true.ToProperty()
            .Label($"Correctly: extension receiver type is reference-equal to canonical type " +
                   $"for {scenario.InterfaceNamespace}.{scenario.InterfaceName}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 1B: Bug Condition — Generic Extension Method Resolution
    //
    // **Validates: Requirements 1.3, 1.4**
    //
    // For a generic extension method call with explicit type arguments on an
    // interface-typed receiver (e.g., Cast<int>() on IEnumerable), the resolved
    // expression type SHALL be the concrete substituted return type (IEnumerable<int>),
    // not unknown.
    //
    // On UNFIXED code this test WILL FAIL — the fragmented lookup API returns
    // the wrong overload or unknown.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void GenericExtensionMethodCall_ShouldResolveToConcreteType_NotUnknown()
    {
        // Compile a Mist program that calls a generic extension method with
        // explicit type arguments on an interface-typed receiver.
        //
        // Scenario: IEnumerable receiver calling Cast<int>()
        // Expected: expression type is IEnumerable<int>
        // Actual (on unfixed code): unknown
        var source = """
            using System.Collections;
            using System.Linq;

            namespace TestProject;

            public class TestClass
            {
                public static void Main()
                {
                    var items = System.Collections.ArrayList();
                    var casted = items.Cast<int>();
                }
            }
            """;

        var tempDir = Path.Combine(Path.GetTempPath(), "mist_ext_identity_pbt_" + Guid.NewGuid().ToString("N"));
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
                ProjectName: "TestProgram",
                NamespaceRoot: "TestProject",
                EnabledAnalyzerIds: ImmutableArray<string>.Empty,
                EmitToDisk: false,
                FrameworkReferences: ImmutableArray<FrameworkReference>.Empty,
                Packages: ImmutableArray<NuGetPackageReference>.Empty);

            var result = SharedDriver.Compile(options);

            // On unfixed code, the generic extension method resolution produces unknown.
            // This manifests as either:
            // 1. The expression type for the method call is unknown
            // 2. A diagnostic is produced because 'Cast' is not found as a method
            //    (because extension matching fails due to receiver type mismatch)
            //
            // We check that there are NO error diagnostics about undefined members
            // or type resolution failures. On unfixed code, we expect errors because
            // the extension method can't be found or resolves incorrectly.
            var errorDiagnostics = result.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .ToList();

            // On UNFIXED code: this assertion SHOULD FAIL — either the extension
            // is not found (MIST_UNDEFINED_MEMBER) or the type resolves to unknown.
            // That failure CONFIRMS the bug exists.
            Assert.Empty(errorDiagnostics);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}