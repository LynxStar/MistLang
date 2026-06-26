// Feature: disposable-drop-interop, Property 2: Synthetic drop inference exclusion
// **Validates: Requirements 5.1, 5.2, 5.3**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Semantics;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests for synthetic drop inference exclusion of IDisposable fields
/// (<see cref="SyntheticDropInferenceEngine"/>, disposable-drop-interop feature).
///
/// Feature: disposable-drop-interop, Property 2: Synthetic drop inference exclusion
/// **Validates: Requirements 5.1, 5.2, 5.3**
///
/// For any Mist-declared type whose fields include types with <c>IsDisposableDrop = true</c>,
/// the <see cref="SyntheticDropInferenceEngine"/> shall NOT include those fields in the
/// synthesized drop field list. Only fields whose type has <c>HasDrop = true</c> AND
/// <c>IsDisposableDrop = false</c> shall contribute to synthetic drop inference.
///
/// This test operates entirely in-process by constructing types directly and calling
/// <see cref="SyntheticDropInferenceEngine.RunInference"/>. Spawns 0 external processes.
/// </summary>
public sealed class SyntheticDropInferenceDisposableExclusionPropertyTests
{
    // ─── Minimal ISemanticModel stub ─────────────────────────────────────

    /// <summary>
    /// A minimal <see cref="ISemanticModel"/> implementation that only provides
    /// <see cref="AllSymbols"/>. This is sufficient because
    /// <see cref="SyntheticDropInferenceEngine.RunInference"/> only accesses
    /// <c>model.AllSymbols</c>.
    /// </summary>
    private sealed class StubSemanticModel : ISemanticModel
    {
        private readonly ImmutableArray<ISymbol> _symbols;

        public StubSemanticModel(ImmutableArray<ISymbol> symbols) => _symbols = symbols;

        public ImmutableArray<ISymbol> AllSymbols => _symbols;

        // ── Unused members (RunInference only reads AllSymbols) ──
        public MistCompilation Compilation => null!;
        public IScope GlobalScope => null!;
        public IReadOnlyList<CompilationUnit> Units => Array.Empty<CompilationUnit>();
        public ImmutableArray<Storm.Runtime.SyntaxNode> Asts => ImmutableArray<Storm.Runtime.SyntaxNode>.Empty;
        public Storm.Runtime.SyntaxNode GetDeclaringNode(ISymbol symbol) => null!;
        public ISymbol? GetDeclaredSymbol(Storm.Runtime.SyntaxNode node) => null;
        public IType? GetTypeOf(Storm.Runtime.SyntaxNode node) => null;
        public void SetTypeOf(Storm.Runtime.SyntaxNode node, IType type) { }
        public void RecordResolvedOperator(Storm.Runtime.SyntaxNode node, ISymbol op) { }
        public ISymbol? GetResolvedOperator(Storm.Runtime.SyntaxNode node) => null;
        public ExtensionRegistry ExtensionRegistry => null!;
        public ConformanceMap ConformanceMap => null!;
        public RefinementPredicate? GetParameterRefinement(ISymbol parameterSymbol) => null;
        public void RecordGuardNeeded(ISymbol parameterSymbol) { }
        public bool IsGuardElidable(ISymbol parameterSymbol) => false;
        public IReadOnlyList<DiscardedTaskInfo> GetDiscardedTasks(Storm.Runtime.SyntaxNode blockScopeNode) => Array.Empty<DiscardedTaskInfo>();
        public void RecordInferredTypeArguments(Storm.Runtime.SyntaxNode callSiteNode, IReadOnlyList<IType> inferredTypeArgs) { }
        public IReadOnlyList<IType>? GetInferredTypeArguments(Storm.Runtime.SyntaxNode callSiteNode) => null;
        public void RecordImplicitClone(Storm.Runtime.SyntaxNode source, IType destinationType) { }
        public IType? GetImplicitCloneDestination(Storm.Runtime.SyntaxNode source) => null;
        public void RecordAsConversionKind(Storm.Runtime.SyntaxNode node, AsConversionKind kind) { }
        public AsConversionKind? GetAsConversionKind(Storm.Runtime.SyntaxNode node) => null;
    }

    // ─── Field configuration enum ───────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// Describes a field's type configuration in the generated container.
    /// </summary>
    public enum FieldConfig
    {
        /// <summary>Non-droppable type (HasDrop=false, IsDisposableDrop=false).</summary>
        NonDroppable,

        /// <summary>Explicitly droppable Mist type (HasDrop=true, IsDisposableDrop=false).</summary>
        ExplicitDrop,

        /// <summary>IDisposable CLR type (HasDrop=true, IsDisposableDrop=true).</summary>
        DisposableDrop,
    }

    /// <summary>
    /// A generated scenario describing a container type with N fields of various
    /// drop configurations.
    /// </summary>
    public sealed class DisposableExclusionScenario
    {
        public IReadOnlyList<FieldConfig> FieldConfigs { get; }
        public string Label { get; }

        public DisposableExclusionScenario(IReadOnlyList<FieldConfig> fieldConfigs, string label)
        {
            FieldConfigs = fieldConfigs;
            Label = label;
        }

        public override string ToString() => Label;
    }

#pragma warning restore CA1034

    // ─── Generator ───────────────────────────────────────────────────────

    private static Gen<DisposableExclusionScenario> GenScenario()
    {
        return from fieldCount in Gen.Choose(1, 6)
               from configs in Gen.ArrayOf(
                   Gen.Elements(FieldConfig.NonDroppable, FieldConfig.ExplicitDrop, FieldConfig.DisposableDrop),
                   fieldCount)
               select BuildScenario(configs);
    }

    private static DisposableExclusionScenario BuildScenario(FieldConfig[] configs)
    {
        var label = $"Container[{string.Join(",", configs)}]";
        return new DisposableExclusionScenario(configs, label);
    }

    public static Arbitrary<DisposableExclusionScenario> ArbitraryDisposableExclusionScenario() =>
        GenScenario().ToArbitrary();

    // ─── Test infrastructure ─────────────────────────────────────────────

    /// <summary>
    /// Builds a minimal in-memory type graph for the given scenario and runs
    /// <see cref="SyntheticDropInferenceEngine.RunInference"/>.
    /// Returns the container type so the caller can inspect <c>SyntheticDropFields</c>.
    /// </summary>
    private static ClassType RunInferenceForScenario(DisposableExclusionScenario scenario)
    {
        var allSymbols = new List<ISymbol>();

        // Create field types based on configuration
        var fieldTypes = new ClassType[scenario.FieldConfigs.Count];
        for(var i = 0; i < scenario.FieldConfigs.Count; i++)
        {
            var config = scenario.FieldConfigs[i];
            var fieldTypeScope = new Scope(parent: null);
            var fieldTypeName = config switch
            {
                FieldConfig.NonDroppable => $"PlainType_{i}",
                FieldConfig.ExplicitDrop => $"DroppableType_{i}",
                FieldConfig.DisposableDrop => $"DisposableType_{i}",
                _ => $"Unknown_{i}",
            };

            var fieldType = new ClassType(fieldTypeName, fieldTypeScope, containingNamespace: "Test");

            switch(config)
            {
                case FieldConfig.ExplicitDrop:
                    fieldType.HasDrop = true;
                    fieldType.IsDisposableDrop = false;
                    break;
                case FieldConfig.DisposableDrop:
                    fieldType.HasDrop = true;
                    fieldType.IsDisposableDrop = true;
                    break;
                case FieldConfig.NonDroppable:
                default:
                    // HasDrop defaults to false, IsDisposableDrop defaults to false
                    break;
            }

            fieldTypes[i] = fieldType;

            // Register each field type as a symbol in the model (RunInference iterates AllSymbols)
            var fieldTypeSymbol = new Symbol(
                name: fieldTypeName,
                kind: SymbolKind.Class,
                origin: SymbolOrigin.UserCode,
                containingScope: fieldTypeScope,
                declaredType: fieldType,
                declarationSpan: new SourceSpan(i * 100, 10));
            allSymbols.Add(fieldTypeSymbol);
        }

        // Create the container type with fields referencing the above types
        var containerScope = new Scope(parent: null);
        for(var i = 0; i < scenario.FieldConfigs.Count; i++)
        {
            var fieldSymbol = new Symbol(
                name: $"field_{i}",
                kind: SymbolKind.Field,
                origin: SymbolOrigin.UserCode,
                containingScope: containerScope,
                declaredType: fieldTypes[i],
                declarationSpan: new SourceSpan(1000 + i * 10, 5));
            containerScope.AddMember(fieldSymbol);
        }

        var containerType = new ClassType("Container", containerScope, containingNamespace: "Test");

        // Register the container type symbol
        var containerSymbol = new Symbol(
            name: "Container",
            kind: SymbolKind.Class,
            origin: SymbolOrigin.UserCode,
            containingScope: containerScope,
            declaredType: containerType,
            declarationSpan: new SourceSpan(900, 10));
        allSymbols.Add(containerSymbol);

        // Build the stub model and run inference
        var model = new StubSemanticModel(allSymbols.ToImmutableArray());
        SyntheticDropInferenceEngine.RunInference(model);

        return containerType;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 2: Synthetic drop inference exclusion
    //
    //   * Fields with IsDisposableDrop=true NEVER appear in SyntheticDropFields
    //   * Fields with HasDrop=true AND IsDisposableDrop=false ALWAYS appear
    //   * Fields with HasDrop=false never appear (baseline)
    //
    // **Validates: Requirements 5.1, 5.2, 5.3**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 200, Arbitrary = new[] { typeof(SyntheticDropInferenceDisposableExclusionPropertyTests) })]
    public Property DisposableFields_AreExcluded_FromSyntheticDropInference(DisposableExclusionScenario scenario)
    {
        var containerType = RunInferenceForScenario(scenario);

        var synthFields = (containerType.SyntheticDropFields ?? Array.Empty<ISymbol>())
            .Select(f => f.Name)
            .ToHashSet();

        // Compute expected: only ExplicitDrop fields should appear
        var expectedFields = new List<string>();
        for(var i = 0; i < scenario.FieldConfigs.Count; i++)
        {
            var fieldName = $"field_{i}";
            var config = scenario.FieldConfigs[i];

            if(config == FieldConfig.DisposableDrop)
            {
                // Requirement 5.1: IsDisposableDrop=true fields must NOT appear
                if(synthFields.Contains(fieldName))
                {
                    return false.ToProperty().Label(
                        $"VIOLATION Req 5.1: Field '{fieldName}' has IsDisposableDrop=true but appeared in SyntheticDropFields. "
                        + $"Scenario: {scenario}");
                }
            }
            else if(config == FieldConfig.ExplicitDrop)
            {
                // Requirement 5.3: HasDrop=true AND IsDisposableDrop=false fields MUST appear
                expectedFields.Add(fieldName);
                if(!synthFields.Contains(fieldName))
                {
                    return false.ToProperty().Label(
                        $"VIOLATION Req 5.3: Field '{fieldName}' has HasDrop=true, IsDisposableDrop=false but was NOT in SyntheticDropFields. "
                        + $"Scenario: {scenario}");
                }
            }
            else
            {
                // NonDroppable: must not appear
                if(synthFields.Contains(fieldName))
                {
                    return false.ToProperty().Label(
                        $"VIOLATION: Non-droppable field '{fieldName}' appeared in SyntheticDropFields. "
                        + $"Scenario: {scenario}");
                }
            }
        }

        // Verify HasDrop on container matches expectations (Req 5.2)
        if(expectedFields.Count == 0)
        {
            // No explicit-drop fields → container should NOT have synthetic drop
            var ok = !containerType.HasDrop && containerType.SyntheticDropFields is null;
            return ok.ToProperty().Label(
                $"VIOLATION Req 5.2: Container with only NonDroppable/DisposableDrop fields should NOT get synthetic Drop(). "
                + $"Got HasDrop={containerType.HasDrop}, SyntheticDropFields=[{string.Join(",", synthFields)}]. "
                + $"Scenario: {scenario}");
        }
        else
        {
            // Has explicit-drop fields → container MUST have synthetic drop with exactly those fields
            var actualFieldList = (containerType.SyntheticDropFields ?? Array.Empty<ISymbol>())
                .Select(f => f.Name).ToList();
            var orderOk = actualFieldList.SequenceEqual(expectedFields);
            var hasDropOk = containerType.HasDrop;

            return (hasDropOk && orderOk).ToProperty().Label(
                $"Container synthetic drop mismatch. HasDrop={containerType.HasDrop} (want true), "
                + $"SyntheticDropFields=[{string.Join(",", actualFieldList)}] want [{string.Join(",", expectedFields)}] (declaration order). "
                + $"Scenario: {scenario}");
        }
    }
}