// SampleFingerprint — derives an objective per-sample syntax-feature fingerprint
// by parsing every .mist file in every sample with the real Mist parser and
// collecting the distinct AST node kinds used.
//
// The fingerprints then drive a greedy "fewest new constructs first" ordering so
// that each sample in the progression introduces only constructs that no earlier
// sample used. This replaces hand-guessed feature lists with measured ones.
//
// Usage: dotnet run --project tools/SampleFingerprint -- <repoRoot> <outputJson>

using System.Text.Json;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using Storm.Runtime;

var repoRoot = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
var outputPath = args.Length > 1
    ? args[1]
    : Path.Combine(repoRoot, "backlog", "emitter-v2-sample-fingerprints.json");

// Samples already working in the v2 emitter. Their constructs seed the covered set so
// the reported deltas answer "what is the next cheapest sample" rather than
// re-deriving the progression from nothing.
var done = args.Length > 2 && args[2].Length > 0
    ? args[2].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal)
    : new HashSet<string>(StringComparer.Ordinal);

var samplesRoot = Path.Combine(repoRoot, "samples");
if(!Directory.Exists(samplesRoot))
{
    Console.Error.WriteLine($"samples directory not found at {samplesRoot}");
    return 1;
}

// A sample is a directory containing mist.toml. AllFeatures is a meta-sample that
// calls every other sample, so it carries the union of all fingerprints and is
// excluded from the progression.
var excluded = new HashSet<string>(StringComparer.Ordinal) { "AllFeatures" };

var samples = new List<SampleFingerprint>();

foreach(var dir in Directory.GetDirectories(samplesRoot).OrderBy(d => d, StringComparer.Ordinal))
{
    var name = Path.GetFileName(dir);
    if(name.StartsWith("__", StringComparison.Ordinal) || excluded.Contains(name))
    {
        continue;
    }

    if(!File.Exists(Path.Combine(dir, "mist.toml")))
    {
        continue;
    }

    var mistFiles = Directory
        .GetFiles(dir, "*.mist", SearchOption.AllDirectories)
        .OrderBy(f => f, StringComparer.Ordinal)
        .ToList();

    var nodeKinds = new SortedSet<string>(StringComparer.Ordinal);
    var parseErrors = new List<string>();
    var totalNodes = 0;

    foreach(var file in mistFiles)
    {
        var contents = File.ReadAllText(file);
        var sourceText = new Mist.Common.Source.SourceText(file, contents);
        ILexer lexer = new Mist.Lexing.MistLexer();
        IParser parser = new Mist.Parsing.MistParser();
        var diagnostics = new Mist.Common.Diagnostics.DiagnosticBag();

        SyntaxNode? root;
        try
        {
            var tokens = lexer.Lex(sourceText, diagnostics);
            var result = parser.Parse(tokens, diagnostics);
            root = result.Root;
        }
        catch(Exception ex)
        {
            parseErrors.Add($"{Path.GetFileName(file)}: threw {ex.GetType().Name}");
            continue;
        }

        var errors = diagnostics
            .ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.Id.ToString())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if(errors.Count > 0)
        {
            parseErrors.Add($"{Path.GetFileName(file)}: {string.Join(",", errors)}");
        }

        if(root is null)
        {
            continue;
        }

        totalNodes += Collect(root, nodeKinds);
    }

    samples.Add(new SampleFingerprint(
        Name: name,
        FileCount: mistFiles.Count,
        TotalNodes: totalNodes,
        ParseErrors: parseErrors,
        NodeKinds: nodeKinds.ToList()));
}

// ── Ordering ───────────────────────────────────────────────────────────────
// Two layers:
//
//   1. Semantic gate (curated). Syntax measurement cannot see semantic difficulty.
//      CSharpInteropV2 is syntactically trivial but needs the whole ambient type
//      provider; StructuredConcurrency needs async lowering. Samples are grouped
//      into gate tiers, and no sample from a later tier is ordered before an
//      earlier tier is exhausted.
//
//   2. Greedy minimum-new-syntax within each gate tier (measured). Repeatedly pick
//      the sample introducing the fewest constructs not already covered. Ties break
//      on smaller total fingerprint, then name, so output is deterministic.
var remaining = samples.Where(s => s.ParseErrors.Count == 0).ToList();
var unparseable = samples.Where(s => s.ParseErrors.Count > 0).ToList();
var covered = new HashSet<string>(StringComparer.Ordinal);
var ordered = new List<OrderedSample>();

// Seed with the constructs of already-completed samples and drop them from the queue.
foreach(var name in done)
{
    var sample = remaining.FirstOrDefault(s => s.Name == name);
    if(sample is null)
    {
        Console.Error.WriteLine($"--done named '{name}' which is not a parseable sample.");
        return 2;
    }

    foreach(var kind in sample.NodeKinds)
    {
        _ = covered.Add(kind);
    }

    ordered.Add(new OrderedSample(
        Name: sample.Name,
        GateTier: SemanticGates.TierOf(sample.Name, sample.NodeKinds),
        GateName: SemanticGates.NameOf(SemanticGates.TierOf(sample.Name, sample.NodeKinds)),
        FileCount: sample.FileCount,
        TotalKinds: sample.NodeKinds.Count,
        NewKindCount: 0,
        NewKinds: [],
        Done: true));

    _ = remaining.Remove(sample);
}

foreach(var tier in SemanticGates.Tiers)
{
    var inTier = remaining.Where(s => SemanticGates.TierOf(s.Name, s.NodeKinds) == tier.Index).ToList();

    while(inTier.Count > 0)
    {
        var best = inTier
            .Select(s => (Sample: s, New: s.NodeKinds.Where(k => !covered.Contains(k)).ToList()))
            .OrderBy(t => t.New.Count)
            .ThenBy(t => t.Sample.NodeKinds.Count)
            .ThenBy(t => t.Sample.Name, StringComparer.Ordinal)
            .First();

        ordered.Add(new OrderedSample(
            Name: best.Sample.Name,
            GateTier: tier.Index,
            GateName: tier.Name,
            FileCount: best.Sample.FileCount,
            TotalKinds: best.Sample.NodeKinds.Count,
            NewKindCount: best.New.Count,
            NewKinds: best.New,
            Done: false));

        foreach(var kind in best.Sample.NodeKinds)
        {
            _ = covered.Add(kind);
        }

        _ = inTier.Remove(best.Sample);
        _ = remaining.Remove(best.Sample);
    }
}

if(remaining.Count > 0)
{
    Console.Error.WriteLine("Samples with no gate assignment (add them to SemanticGates):");
    foreach(var s in remaining)
    {
        Console.Error.WriteLine($"  {s.Name} (tier resolved to {SemanticGates.TierOf(s.Name, s.NodeKinds)})");
    }

    return 2;
}

var allKinds = samples
    .SelectMany(s => s.NodeKinds)
    .Distinct(StringComparer.Ordinal)
    .OrderBy(k => k, StringComparer.Ordinal)
    .ToList();

var perSampleKinds = samples
    .OrderBy(s => s.Name, StringComparer.Ordinal)
    .ToDictionary(s => s.Name, s => s.NodeKinds, StringComparer.Ordinal);

var report = new Report(
    GeneratedNote: "Kinds are concrete Storm AST node type names collected by parsing each sample, "
        + "plus 'op:X' entries for operators actually applied (grammar precedence-chain nodes are "
        + "excluded because Storm materializes them for every expression regardless of operator use). "
        + "This measures SYNTACTIC surface only; semantic features (overload resolution, type inference, "
        + "ownership analysis, void lowering) are NOT captured and still need manual review.",
    SampleCount: samples.Count,
    DistinctKindCount: covered.Count,
    Unparseable: unparseable.Select(s => new UnparseableSample(s.Name, s.ParseErrors)).ToList(),
    Order: ordered,
    AllKinds: allKinds,
    KindsBySample: perSampleKinds);

var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
_ = Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
File.WriteAllText(outputPath, json);

// Emit the progression YAML next to the JSON so the two can never drift apart.
var yamlPath = Path.Combine(
    Path.GetDirectoryName(outputPath)!,
    "emitter-v2-sample-progression.yaml");
File.WriteAllText(yamlPath, BuildYaml(ordered, samples, done, covered.Count));

static string BuildYaml(
    List<OrderedSample> ordered,
    List<SampleFingerprint> samples,
    HashSet<string> done,
    int coveredCount)
{
    var kindsBySample = samples.ToDictionary(s => s.Name, s => s.NodeKinds, StringComparer.Ordinal);
    var sb = new System.Text.StringBuilder();

    _ = sb.AppendLine("# V2 Emitter Sample Progression");
    _ = sb.AppendLine("#");
    _ = sb.AppendLine("# GENERATED FILE -- do not hand-edit. Regenerate with:");
    _ = sb.AppendLine("#   dotnet run --project tools/SampleFingerprint -- <repoRoot> \\");
    _ = sb.AppendLine("#     backlog/emitter-v2-sample-fingerprints.json \"<comma,separated,done,samples>\"");
    _ = sb.AppendLine("#");
    _ = sb.AppendLine("# HOW THE ORDER IS DERIVED");
    _ = sb.AppendLine("#");
    _ = sb.AppendLine("#   syntax_constructs / new_constructs are MEASURED: every .mist file in the sample is");
    _ = sb.AppendLine("#   parsed with the real Mist parser and the distinct AST node kinds are collected.");
    _ = sb.AppendLine("#   Operator-precedence chain nodes are excluded (Storm materializes them for every");
    _ = sb.AppendLine("#   expression whether or not an operator is used); operators actually applied appear");
    _ = sb.AppendLine("#   as 'op:X', and union / optional types as 'type:union' / 'type:optional'.");
    _ = sb.AppendLine("#");
    _ = sb.AppendLine("#   gate_tier is the compiler subsystem the sample depends on. It is the HIGHER of a");
    _ = sb.AppendLine("#   curated assignment and the tier proven by the constructs the sample contains, so a");
    _ = sb.AppendLine("#   sample cannot be filed early when its source says otherwise. Samples are ordered");
    _ = sb.AppendLine("#   tier by tier, and within a tier by fewest new constructs.");
    _ = sb.AppendLine("#");
    _ = sb.AppendLine("# IMPORTANT CAVEAT");
    _ = sb.AppendLine("#");
    _ = sb.AppendLine("#   new_constructs measures SYNTAX ONLY. It is a lower bound on the work, never an");
    _ = sb.AppendLine("#   estimate of it. new_constructs: [] means the sample introduces no new syntax, NOT");
    _ = sb.AppendLine("#   that it is free -- semantic work (overload resolution, type inference, void");
    _ = sb.AppendLine("#   lowering, ownership analysis) is invisible to this measurement.");
    _ = sb.AppendLine("#");
    _ = sb.AppendLine("#   gated_by lists the constructs that force a sample above its curated tier. A long");
    _ = sb.AppendLine("#   gated_by means the sample is a kitchen sink whose name understates what it needs.");
    _ = sb.AppendLine("#");
    _ = sb.AppendLine($"# AllFeatures is excluded (meta-sample calling all others). {samples.Count} samples, "
        + $"{coveredCount} distinct constructs currently covered.");
    _ = sb.AppendLine();
    _ = sb.AppendLine("samples:");

    var lastHeader = "";
    foreach(var o in ordered)
    {
        var header = o.Done ? "already working in v2" : $"tier {o.GateTier}: {o.GateName}";
        if(header != lastHeader)
        {
            _ = sb.AppendLine();
            _ = sb.AppendLine($"  # --- {header} ---");
            lastHeader = header;
        }

        _ = sb.AppendLine();
        _ = sb.AppendLine($"  - name: {o.Name}");
        _ = sb.AppendLine($"    status: {(done.Contains(o.Name) ? "done" : "pending")}");
        _ = sb.AppendLine($"    gate_tier: {o.GateTier}");
        _ = sb.AppendLine($"    gate: {o.GateName}");
        _ = sb.AppendLine($"    files: {o.FileCount}");
        _ = sb.AppendLine($"    syntax_constructs: {o.TotalKinds}");

        if(o.NewKinds.Count == 0)
        {
            _ = sb.AppendLine("    new_constructs: []");
        }
        else
        {
            _ = sb.AppendLine("    new_constructs:");
            foreach(var k in o.NewKinds)
            {
                _ = sb.AppendLine($"      - {Quote(k)}");
            }
        }

        var manual = SemanticGates.ManualTierOf(o.Name);
        var kinds = kindsBySample[o.Name];
        if(SemanticGates.DerivedTierOf(kinds) > manual)
        {
            var gating = SemanticGates.GatingConstructs(o.Name, kinds);
            _ = sb.AppendLine($"    # source requires tier {o.GateTier} though the name suggests tier {manual}");
            _ = sb.AppendLine("    gated_by:");
            foreach(var g in gating)
            {
                _ = sb.AppendLine($"      - {Quote(g)}");
            }
        }
    }

    return sb.ToString();
}

// 'op:*' and 'type:*' entries contain a colon, which YAML would read as a mapping.
static string Quote(string value)
{
    return value.Contains(':', StringComparison.Ordinal) ? $"\"{value}\"" : value;
}

Console.WriteLine($"Analyzed {samples.Count} samples, {covered.Count} distinct node kinds.");
Console.WriteLine($"Unparseable: {unparseable.Count}");
foreach(var u in unparseable)
{
    Console.WriteLine($"  {u.Name}: {string.Join("; ", u.ParseErrors)}");
}

Console.WriteLine();
var lastHeader = "";
foreach(var o in ordered)
{
    var header = o.Done ? "already working" : $"tier {o.GateTier}: {o.GateName}";
    if(header != lastHeader)
    {
        Console.WriteLine($"--- {header} ---");
        lastHeader = header;
    }

    Console.WriteLine($"  new={o.NewKindCount,3}  total={o.TotalKinds,3}  {o.Name}");
}

Console.WriteLine();
Console.WriteLine($"Wrote {outputPath}");
return 0;

// The Storm PEG grammar materializes the ENTIRE operator-precedence chain for every
// expression, whether or not an operator is present. So `LogicalAndNode`,
// `AdditiveNode`, `EqualityNode` etc. appear even in a program containing no
// operators at all. Recording their bare presence as a "feature" makes every sample
// look like it uses every operator.
//
// These nodes are therefore treated as plumbing: instead of recording the node type,
// we inspect whether an operator was actually applied and record the concrete
// operator (e.g. "op:&&") only then.
static int Collect(SyntaxNode node, SortedSet<string> kinds)
{
    var count = 1;
    var typeName = node.GetType().Name;

    if(Plumbing.Nodes.Contains(typeName))
    {
        RecordActiveOperators(node, kinds);
    }
    else
    {
        _ = kinds.Add(typeName);
        RecordSyntheticTypeFeatures(node, kinds);
    }

    foreach(var child in node.Children())
    {
        if(child is not null)
        {
            count += Collect(child, kinds);
        }
    }

    return count;
}

// Union types (A | B) and optional types (T?) have no dedicated AST node kind -- they
// are encoded as extra SingleTypeRefs on TypeReferenceNode and as an Optional token on
// the individual type refs. Without these synthetic entries the fingerprint cannot see
// two significant type-system features at all.
static void RecordSyntheticTypeFeatures(SyntaxNode node, SortedSet<string> kinds)
{
    switch(node)
    {
        // Union predicate per the grammar: SingleTypeRefs is the PIPE-separated tail.
        // Rest is a non-nullable codegen artifact and is NOT a valid union signal.
        case TypeReferenceNode tr when !tr.SingleTypeRefs.IsDefaultOrEmpty:
            _ = kinds.Add("type:union");
            break;

        case NamedSingleTypeRefSingleTypeRefNode named when named.Optional is not null:
            _ = kinds.Add("type:optional");
            break;

        case PrimitiveSingleTypeRefSingleTypeRefNode prim when prim.Optional is not null:
            _ = kinds.Add("type:optional");
            break;

        default:
            break;
    }
}

// Records the concrete operator for a precedence-chain node, but only when an
// operator is genuinely applied. Mirrors OperatorBinder.IsBare in the ProgramModel.
static void RecordActiveOperators(SyntaxNode node, SortedSet<string> kinds)
{
    switch(node)
    {
        case VoidCoalescingNode v when !v.LogicalOrs.IsDefaultOrEmpty:
            _ = kinds.Add("op:??");
            break;

        case LogicalOrNode l when !l.LogicalAnds.IsDefaultOrEmpty:
            _ = kinds.Add("op:||");
            break;

        case LogicalAndNode l when !l.Equalitys.IsDefaultOrEmpty:
            _ = kinds.Add("op:&&");
            break;

        case EqualityNode e:
            foreach(var g in e.Group0)
            {
                _ = kinds.Add($"op:{g.Operator.Lexeme}");
            }

            break;

        case RelationalComparisonRelationalNode r:
            foreach(var g in r.Group0)
            {
                _ = kinds.Add($"op:{g.Operator.Lexeme}");
            }

            break;

        case AdditiveNode a:
            foreach(var g in a.Group0)
            {
                _ = kinds.Add($"op:{g.Operator.Lexeme}");
            }

            break;

        case MultiplicativeNode m:
            foreach(var g in m.Group0)
            {
                _ = kinds.Add($"op:{g.Operator.Lexeme}");
            }

            break;

        case AssignmentNode asn when asn.Group0 is { } ag:
            _ = kinds.Add($"op:{ag.Operator.Lexeme}");
            break;

        default:
            // Bare pass-through: contributes no feature.
            break;
    }
}

/// <summary>
/// The curated half of the ordering. Syntax fingerprints measure surface constructs
/// but cannot see semantic machinery, so each sample is assigned a gate tier naming
/// the compiler subsystem it depends on. Samples in a later tier are never ordered
/// before an earlier tier is exhausted.
/// <para>
/// This is deliberate, reviewable judgment -- unlike the old hand-written feature
/// lists, it is confined to this one table and every other number in the report is
/// measured from the parser.
/// </para>
/// </summary>
internal static class SemanticGates
{
    public static readonly (int Index, string Name)[] Tiers =
    [
        (0, "core-language"),
        (1, "object-model"),
        (2, "type-system"),
        (3, "abstracts-and-extensions"),
        (4, "functions-and-patterns"),
        (5, "clr-interop"),
        (6, "ownership-and-drop"),
        (7, "refinement-and-dependent"),
        (8, "concurrency-and-context"),
    ];

    private static readonly Dictionary<string, int> Gates = new(StringComparer.Ordinal)
    {
        // ── 0: core language — statements, expressions, literals, operators ──
        ["Simpliest"] = 0,
        ["Primitives"] = 0,
        ["ArrayLiterals"] = 0,
        ["ReadonlyLocals"] = 0,
        ["DefaultKeyword"] = 0,
        ["RangesAndIndices"] = 0,
        ["DefiniteAssignment"] = 0,
        ["TopLevelStatements"] = 0,
        ["Namespaces"] = 0,
        ["GlobalUsings"] = 0,
        ["ProjectReferences"] = 0,

        // ── 1: object model — classes, structs, fields, ctors, properties ──
        ["Classes"] = 1,
        ["HelloMist"] = 1,
        ["Properties"] = 1,
        ["PropertyInitializers"] = 1,
        ["Inheritance"] = 1,
        ["StructInheritance"] = 1,
        ["Overloading"] = 1,
        ["CastOperators"] = 1,
        ["NestedScoping"] = 1,
        ["Enums"] = 1,
        ["EnumReadonlyRestriction"] = 1,
        ["FullAccessibilityModel"] = 1,
        ["ReadonlyContexts"] = 1,

        // ── 2: type system — generics, tuples, unions, aliases, void, unknown ──
        ["GenericTypes"] = 2,
        ["GenericConstraints"] = 2,
        ["TypeArgumentInference"] = 2,
        ["ArityOverloads"] = 2,
        ["Tuples"] = 2,
        ["TypeUnions"] = 2,
        ["TypeAliases"] = 2,
        ["UnknownType"] = 2,
        ["UnknownReadonlySemantics"] = 2,
        ["OptionalTypes"] = 2,
        ["OptionalDefaultability"] = 2,
        ["VoidType"] = 2,
        ["VoidValueLiteral"] = 2,
        ["Newtypes"] = 2,
        ["AnonymousTypes"] = 2,
        ["TargetTypedNew"] = 2,
        ["StackOnlyTypes"] = 2,
        ["RefStructs"] = 2,
        ["RefParameters"] = 2,
        ["Variadics"] = 2,

        // ── 3: abstracts, conformance, extensions, attributes ──
        ["AbstractTypes"] = 3,
        ["ShapeAbstracts"] = 3,
        ["AssociatedTypes"] = 3,
        ["ExtensionMethods"] = 3,
        ["ExtensionMethodBinding"] = 3,
        ["Attributes"] = 3,
        ["Testing"] = 3,
        ["StormAnonymousGroups"] = 3,

        // ── 4: first-class functions, iterators, pattern matching ──
        ["FirstClassFunctions"] = 4,
        ["DelegateFuncConversion"] = 4,
        ["Iterators"] = 4,
        ["PatternMatching"] = 4,
        ["SwitchExpressions"] = 4,
        ["TuplePatternMatching"] = 4,
        ["AsOperator"] = 4,
        ["RegexLiterals"] = 4,

        // ── 5: CLR interop — ambient provider, real assembly projection ──
        ["AmbientTypes"] = 5,
        ["AssemblyTypeProvider"] = 5,
        ["CSharpInteropV2"] = 5,
        ["ClrRefInterop"] = 5,

        // ── 6: ownership, drop, substructural discipline ──
        ["OwnershipSystem"] = 6,
        ["Ownership"] = 6,
        ["DeepOwnershipTypes"] = 6,
        ["DropSemantics"] = 6,
        ["DisposableDropInterop"] = 6,
        ["SubstructuralFieldDiscipline"] = 6,

        // ── 7: refinement and dependent types ──
        ["RefinementTypes"] = 7,
        ["RefinementComparison"] = 7,
        ["AdhocRefinementConstraints"] = 7,
        ["DependentTypes"] = 7,
        ["DependentTypeRefinementConstraints"] = 7,
        ["ArrayBoundsFlowAnalysis"] = 7,

        // ── 8: async, structured concurrency, structured context ──
        ["StructuredConcurrency"] = 8,
        ["StructuredContext"] = 8,
        ["AsyncDropChaining"] = 8,
    };

    /// <summary>
    /// Constructs that PROVE a sample depends on a given subsystem. A sample's tier is
    /// at least the highest tier among the constructs it actually contains, which stops
    /// a sample from being filed in an early tier when its source says otherwise.
    /// <para>
    /// Only CLR interop has no syntactic signature, so it relies on the manual table.
    /// </para>
    /// </summary>
    private static readonly (int Tier, string[] Kinds)[] DerivedGates =
    [
        // ── 1: object model ──
        (1, [
            "FieldDeclarationNode", "FieldMemberMemberDeclarationNode",
            "ConstructorDeclarationNode", "ConstructorMemberMemberDeclarationNode",
            "ThisChainConstructorChainNode", "BaseChainConstructorChainNode",
            "ObjectCreationExpressionPrimaryNode", "ObjectCreationExpressionNoArgsPrimaryNode",
            "ThisExpressionPrimaryNode", "StructKeywordTypeKeywordNode",
            "BaseListNode", "BaseTypeRefNode",
            "PropertyMemberMemberDeclarationNode", "BlockPropertyPropertyDeclarationNode",
            "ExpressionBodiedPropertyPropertyDeclarationNode", "PropertyInitializerBlockNode",
            "FieldKeywordExpressionPrimaryNode", "DefaultFieldOverrideDefaultFieldInitializerNode",
            "IndexerDeclarationNode", "IndexerMemberMemberDeclarationNode",
            "BlockIndexerBodyIndexerBodyNode",
            "EnumDeclarationNode", "EnumTypeTypeDeclarationNode", "EnumMemberNode",
            "EnumMemberListNode", "NestedEnumMemberMemberDeclarationNode",
            "NestedBodyTypeMemberMemberDeclarationNode",
        ]),

        // ── 2: type system ──
        (2, [
            "TypeParameterListNode", "RegularTypeParameterTypeParameterNode",
            "TypeArgumentListNode", "TypeReferenceTypeArgumentTypeArgumentNode",
            "GenericInvocationExpressionPrimaryNode",
            "TupleLiteralExpressionPrimaryNode", "TupleSingleTypeRefSingleTypeRefNode",
            "TupleTypeElementNode", "TupleDeconstructionElementNode",
            "TypedTupleDeconstructionVariableDeclarationStatementNode",
            "VarTupleDeconstructionVariableDeclarationStatementNode",
            "NamedTupleLiteralElementTupleLiteralElementNode",
            "UnnamedTupleLiteralElementTupleLiteralElementNode",
            "type:union", "type:optional", "OptionalMethodCallSuffixPostfixSuffixNode",
            "NewtypeDeclarationNode", "NewtypeTypeTypeDeclarationNode",
            "AnonymousTypeExpressionPrimaryNode", "AnonymousPropertyListNode",
            "ExplicitAnonymousPropertyAnonymousPropertyNode",
            "ProjectedAnonymousPropertyAnonymousPropertyNode",
            "TargetTypedNewExpressionPrimaryNode",
            "RefModifierModifierNode", "RefArgumentArgumentNode", "RefSpecifierOwnershipSpecifierNode",
            "StructConstraintConstraintNode", "ConstructorConstraintConstraintNode",
            "GenericConstraintClauseWhereClauseNode",
            "StackallocExpressionPrimaryNode",
            "BracketedPackListNode", "BracketedPackTypeArgumentTypeArgumentNode",
            "ExplicitPackTypeParameterTypeParameterNode", "ParamsPackTypeParameterTypeParameterNode",
            "SpreadSingleTypeRefSingleTypeRefNode",
        ]),

        // ── 3: abstracts, conformance, extensions, attributes ──
        (3, [
            "AbstractKeywordTypeKeywordNode",
            "WithDeclarationNode", "WithItemTopLevelItemNode",
            "ExtensionBlockDeclarationNode", "ExtensionMethodMemberExtensionMemberDeclarationNode",
            "ExtensionPropertyMemberExtensionMemberDeclarationNode",
            "NamespaceExtensionItemNamespaceBodyItemNode",
            "AttributeNode", "AttributeListNode", "AttributeArgumentsNode",
            "AttributeArgumentListNode", "PositionalAttributeArgumentAttributeArgumentNode",
            "RegularExpressionAttributeExpressionNode",
            "GenericTypeLiteralExpressionAttributeExpressionNode",
        ]),

        // ── 4: first-class functions and pattern matching ──
        (4, [
            "LambdaFormExpressionNode", "BlockLambdaLambdaExpressionNode",
            "ExpressionLambdaLambdaExpressionNode", "LambdaParamListNode",
            "TypedLambdaParamLambdaParamNode", "UntypedLambdaParamLambdaParamNode",
            "ParenthesizedLambdaParamsLambdaParameterListNode",
            "SingleImplicitParamLambdaParameterListNode",
            "StaticLambdaModifierLambdaModifierNode",
            "FuncSingleTypeRefSingleTypeRefNode",
            "PatternNode", "AndPatternNode", "OrPatternNode",
            "NotPatternUnaryPatternNode", "DirectPatternUnaryPatternNode",
            "TypePatternPrimaryPatternNode", "BareTypePatternPrimaryPatternNode",
            "ConstantPatternPrimaryPatternNode", "DiscardPatternPrimaryPatternNode",
            "VarPatternPrimaryPatternNode", "PropertyPatternPrimaryPatternNode",
            "PropertySubpatternNode", "PropertySubpatternListNode",
            "ListPatternPrimaryPatternNode", "ListPatternElementListNode",
            "PatternElementListPatternElementNode", "SliceElementListPatternElementNode",
            "ParenthesizedPatternPrimaryPatternNode", "TuplePositionalPatternPrimaryPatternNode",
            "GreaterThanRelationalPatternPrimaryPatternNode",
            "GreaterThanOrEqualRelationalPatternPrimaryPatternNode",
            "LessThanRelationalPatternPrimaryPatternNode",
            "LessThanOrEqualRelationalPatternPrimaryPatternNode",
            "SwitchArmNode", "SwitchArmListNode", "SwitchExpressionSuffixPostfixSuffixNode",
            "RegexLiteralExpressionPrimaryNode", "RegexTypePrimitiveTypeNode",
            "RegexConstantConstantLiteralNode",
        ]),

        // ── 6: ownership and drop ──
        (6, [
            "OwnedModifierModifierNode", "OwnedSpecifierOwnershipSpecifierNode",
            "BorrowedSpecifierOwnershipSpecifierNode", "FrozenSpecifierOwnershipSpecifierNode",
        ]),

        // ── 7: refinement and dependent types ──
        (7, [
            "AdhocRefinementConstraintWhereClauseNode",
            "SimpleRefinementPredicateRefinementPredicateNode",
            "RefinableSingleTypeExprNode", "RefinableTypeExpressionNode",
            "DependentTypeParameterTypeParameterNode",
            "NamedConstraintDependentTypeParameterTypeParameterNode",
            "IntegerLiteralTypeArgumentTypeArgumentNode", "CharLiteralTypeArgumentTypeArgumentNode",
            "TrueLiteralTypeArgumentTypeArgumentNode", "NegatedIntegerTypeArgumentTypeArgumentNode",
        ]),

        // ── 8: async and structured context ──
        (8, [
            "AsyncModifierModifierNode", "AwaitExpressionUnaryNode",
            "AsyncExpressionUnaryNode", "AsyncBlockExpressionUnaryNode",
            "AsyncHintBlockExpressionUnaryNode", "AsyncLambdaModifierLambdaModifierNode",
            "WithBlockStatementNode", "WithBlockStatementStatementNode",
            "WithPushStatementNode", "WithPushStatementStatementNode",
            "WithVarBlockStatementNode", "WithVarBlockStatementStatementNode",
            "WithVarDeclarationStatementNode", "WithVarDeclarationStatementStatementNode",
            "WithTypedDeclarationStatementNode", "WithTypedDeclarationStatementStatementNode",
            "WithDiscardDeclarationStatementNode", "WithDiscardDeclarationStatementStatementNode",
            "ContextWithClauseWithClauseNode", "ThisTierWithClauseWithClauseNode",
        ]),
    ];

    /// <summary>
    /// Returns the gate tier for a sample: the higher of the curated assignment and the
    /// tier implied by the constructs the sample actually contains. Returns -1 when the
    /// sample has no curated assignment, which the caller reports as an error so new
    /// samples cannot be silently dropped from the progression.
    /// </summary>
    public static int TierOf(string sampleName, IReadOnlyCollection<string>? nodeKinds = null)
    {
        if(!Gates.TryGetValue(sampleName, out var manual))
        {
            return -1;
        }

        if(nodeKinds is null)
        {
            return manual;
        }

        return Math.Max(manual, DerivedTierOf(nodeKinds));
    }

    /// <summary>The curated tier for a sample, ignoring what its source actually contains.</summary>
    public static int ManualTierOf(string sampleName)
    {
        return Gates.TryGetValue(sampleName, out var manual) ? manual : -1;
    }

    /// <summary>The tier implied purely by the constructs present in the sample.</summary>
    public static int DerivedTierOf(IReadOnlyCollection<string> nodeKinds)
    {
        var derived = 0;
        foreach(var (tier, kinds) in DerivedGates)
        {
            if(tier > derived && kinds.Any(nodeKinds.Contains))
            {
                derived = tier;
            }
        }

        return derived;
    }

    /// <summary>
    /// The specific constructs that forced a sample above its curated tier. These are the
    /// reason a sample cannot be attempted as early as its name suggests.
    /// </summary>
    public static List<string> GatingConstructs(string sampleName, IReadOnlyCollection<string> nodeKinds)
    {
        var manual = ManualTierOf(sampleName);
        var result = new List<string>();
        foreach(var (tier, kinds) in DerivedGates)
        {
            if(tier > manual)
            {
                result.AddRange(kinds.Where(nodeKinds.Contains));
            }
        }

        result.Sort(StringComparer.Ordinal);
        return result;
    }

    public static string NameOf(int tierIndex)
    {
        foreach(var tier in Tiers)
        {
            if(tier.Index == tierIndex)
            {
                return tier.Name;
            }
        }

        return "unassigned";
    }
}

/// <summary>
/// Operator-precedence chain node types that the Storm PEG grammar materializes for
/// every expression regardless of whether an operator is applied. Their bare presence
/// is not a language feature, so they are excluded from the fingerprint and replaced
/// by concrete "op:X" entries when an operator is genuinely used.
/// </summary>
internal static class Plumbing
{
    public static readonly HashSet<string> Nodes = new(StringComparer.Ordinal)
    {
        "VoidCoalescingNode",
        "LogicalOrNode",
        "LogicalAndNode",
        "EqualityNode",
        "RelationalComparisonRelationalNode",
        "AdditiveNode",
        "MultiplicativeNode",
        "NonRangeRangeNode",
        "PostfixNode",
        "PostfixExpressionUnaryNode",
        "AssignmentNode",
        "AssignmentFormExpressionNode",
    };
}

internal sealed record SampleFingerprint(
    string Name,
    int FileCount,
    int TotalNodes,
    List<string> ParseErrors,
    List<string> NodeKinds);

internal sealed record OrderedSample(
    string Name,
    int GateTier,
    string GateName,
    int FileCount,
    int TotalKinds,
    int NewKindCount,
    List<string> NewKinds,
    bool Done);

internal sealed record UnparseableSample(string Name, List<string> ParseErrors);

internal sealed record Report(
    string GeneratedNote,
    int SampleCount,
    int DistinctKindCount,
    List<UnparseableSample> Unparseable,
    List<OrderedSample> Order,
    List<string> AllKinds,
    Dictionary<string, List<string>> KindsBySample);
