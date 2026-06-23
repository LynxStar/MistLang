// Feature: associated-types, Property 5: Semantic registration records correct constraints
// **Validates: Requirements 2.1, 2.2, 2.3, 2.4**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Represents a single associated type slot with its expected constraint metadata.
/// </summary>
public sealed class SlotSpec
{
    public string Name { get; }
    public bool ExpectStruct { get; }
    public bool ExpectClass { get; }
    public IReadOnlyList<string> ExpectInterfaceConstraints { get; }
    public bool ExpectConstructorConstraint { get; }

    public SlotSpec(
        string name,
        bool expectStruct,
        bool expectClass,
        IReadOnlyList<string> expectInterfaceConstraints,
        bool expectConstructorConstraint)
    {
        Name = name;
        ExpectStruct = expectStruct;
        ExpectClass = expectClass;
        ExpectInterfaceConstraints = expectInterfaceConstraints;
        ExpectConstructorConstraint = expectConstructorConstraint;
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if (ExpectStruct) parts.Add("struct");
        if (ExpectClass) parts.Add("class");
        foreach (var iface in ExpectInterfaceConstraints) parts.Add(iface);
        if (ExpectConstructorConstraint) parts.Add("new()");
        return $"{Name} : {(parts.Count > 0 ? string.Join(", ", parts) : "(unconstrained)")}";
    }
}

/// <summary>
/// A test scenario containing an interface with one or more associated type slots.
/// </summary>
public sealed class SemanticRegistrationScenario
{
    public string InterfaceName { get; }
    public IReadOnlyList<SlotSpec> Slots { get; }
    public string Source { get; }

    public SemanticRegistrationScenario(
        string interfaceName,
        IReadOnlyList<SlotSpec> slots,
        string source)
    {
        InterfaceName = interfaceName;
        Slots = slots;
        Source = source;
    }

    public override string ToString()
    {
        var slotDescs = string.Join("; ", Slots.Select(s => s.ToString()));
        return $"abstract {InterfaceName} {{ {slotDescs} }}";
    }
}

/// <summary>
/// Property-based tests verifying that the semantic model correctly registers
/// associated type slots with their declared constraints.
///
/// Property 5: For any interface or trait containing one or more associated type
/// slots with varying constraint lists (including struct, class, interface references,
/// new(), and combinations thereof), the semantic model SHALL register each slot as
/// a discoverable type member whose TypeParameterConstraints metadata exactly reflects
/// the declared constraints.
///
/// **Validates: Requirements 2.1, 2.2, 2.3, 2.4**
/// </summary>
public sealed class AssociatedTypeSemanticPropertyTests : IDisposable
{
    private readonly string _outputDir;

    public AssociatedTypeSemanticPropertyTests()
    {
        _outputDir = Path.Combine(
            Path.GetTempPath(),
            "MistAssocTypeSemantic_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_outputDir))
        {
            try { Directory.Delete(_outputDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Capturing semantic model builder ────────────────────────────────

    /// <summary>
    /// Wraps the real SemanticModelBuilder to capture the constructed model
    /// so tests can inspect associated type slot registrations.
    /// </summary>
    private sealed class CapturingSemanticModelBuilder : ISemanticModelBuilder
    {
        private readonly SemanticModelBuilder _inner = new();
        public ISemanticModel? Captured { get; private set; }

        public ISemanticModel Build(
            MistCompilation compilation,
            ImmutableArray<Storm.Runtime.SyntaxNode> asts,
            IAmbientTypeResolver? ambientTypeResolver,
            string namespaceRoot,
            string projectFolder,
            DiagnosticBag diagnostics,
            ImmutableHashSet<string>? directReferencePaths = null)
        {
            var model = _inner.Build(compilation, asts, ambientTypeResolver, namespaceRoot, projectFolder, diagnostics, directReferencePaths);
            Captured = model;
            return model;
        }
    }

    // ─── Name pools ─────────────────────────────────────────────────────

    private static readonly string[] InterfaceNames =
        ["IIterable", "IRepository", "ICollection", "IMapper", "IFactory", "IProcessor"];

    private static readonly string[] SlotNames =
        ["Enumerator", "Element", "Key", "Value", "Iterator", "Builder", "Output", "State"];

    private static readonly string[] InterfaceConstraintNames =
        ["IComparable", "IDisposable", "ICloneable", "ISerializable"];

    // ─── Generators ─────────────────────────────────────────────────────

    /// <summary>Generates a slot with only the struct constraint.</summary>
    private static Gen<SlotSpec> GenStructOnlySlot(string name)
    {
        return Gen.Constant(new SlotSpec(name, expectStruct: true, expectClass: false,
            expectInterfaceConstraints: Array.Empty<string>(), expectConstructorConstraint: false));
    }

    /// <summary>Generates a slot with only the class constraint.</summary>
    private static Gen<SlotSpec> GenClassOnlySlot(string name)
    {
        return Gen.Constant(new SlotSpec(name, expectStruct: false, expectClass: true,
            expectInterfaceConstraints: Array.Empty<string>(), expectConstructorConstraint: false));
    }

    /// <summary>Generates a slot with only interface reference constraints.</summary>
    private static Gen<SlotSpec> GenInterfaceOnlySlot(string name)
    {
        return Gen.Choose(1, 2).SelectMany(count =>
            Gen.ArrayOf(Gen.Elements(InterfaceConstraintNames), count)
                .Select(ifaces => ifaces.Distinct().ToArray())
                .Where(ifaces => ifaces.Length > 0)
                .Select(ifaces => new SlotSpec(name, expectStruct: false, expectClass: false,
                    expectInterfaceConstraints: ifaces, expectConstructorConstraint: false)));
    }

    /// <summary>Generates a slot with only the new() constraint.</summary>
    private static Gen<SlotSpec> GenNewOnlySlot(string name)
    {
        return Gen.Constant(new SlotSpec(name, expectStruct: false, expectClass: false,
            expectInterfaceConstraints: Array.Empty<string>(), expectConstructorConstraint: true));
    }

    /// <summary>Generates a slot with a combination of constraints.</summary>
    private static Gen<SlotSpec> GenCombinedSlot(string name)
    {
        return from hasStruct in Gen.Elements(true, false)
               from hasClass in Gen.Elements(true, false)
               where !(hasStruct && hasClass) // struct and class are mutually exclusive
               from ifaceCount in Gen.Choose(0, 2)
               from ifaces in Gen.ArrayOf(Gen.Elements(InterfaceConstraintNames), ifaceCount)
               let distinctIfaces = ifaces.Distinct().ToArray()
               from hasNew in Gen.Elements(true, false)
               where hasStruct || hasClass || distinctIfaces.Length > 0 || hasNew // at least one constraint
               select new SlotSpec(name, hasStruct, hasClass, distinctIfaces, hasNew);
    }

    /// <summary>Generates a slot spec with a random constraint combination.</summary>
    private static Gen<SlotSpec> GenSlotSpec(string name)
    {
        return Gen.OneOf(
            GenStructOnlySlot(name),
            GenClassOnlySlot(name),
            GenInterfaceOnlySlot(name),
            GenNewOnlySlot(name),
            GenCombinedSlot(name));
    }

    /// <summary>Builds the constraint list source text from a SlotSpec.</summary>
    private static string BuildConstraintList(SlotSpec slot)
    {
        var parts = new List<string>();
        if (slot.ExpectStruct) parts.Add("struct");
        if (slot.ExpectClass) parts.Add("class");
        foreach (var iface in slot.ExpectInterfaceConstraints) parts.Add(iface);
        if (slot.ExpectConstructorConstraint) parts.Add("new()");
        return string.Join(", ", parts);
    }

    /// <summary>Generates a complete scenario with an interface containing 1-3 slots.</summary>
    private static Gen<SemanticRegistrationScenario> GenScenario()
    {
        return from ifaceIdx in Gen.Choose(0, InterfaceNames.Length - 1)
               from slotCount in Gen.Choose(1, 3)
               from slotIndices in Gen.ArrayOf(Gen.Choose(0, SlotNames.Length - 1), slotCount)
               let distinctSlotNames = slotIndices.Select(i => SlotNames[i]).Distinct().ToArray()
               where distinctSlotNames.Length > 0
               from slots in GenSlotSpecs(distinctSlotNames)
               let ifaceName = InterfaceNames[ifaceIdx]
               let source = BuildSource(ifaceName, slots)
               select new SemanticRegistrationScenario(ifaceName, slots, source);
    }

    private static Gen<SlotSpec[]> GenSlotSpecs(string[] names)
    {
        if (names.Length == 0)
            return Gen.Constant(Array.Empty<SlotSpec>());

        Gen<SlotSpec[]> result = GenSlotSpec(names[0]).Select(s => new[] { s });
        for (var i = 1; i < names.Length; i++)
        {
            var name = names[i];
            result = result.SelectMany(prev =>
                GenSlotSpec(name).Select(s => prev.Append(s).ToArray()));
        }

        return result;
    }

    /// <summary>
    /// Builds the Mist source code for an interface with the given slots.
    /// Includes stub interface declarations for constraint references.
    /// </summary>
    private static string BuildSource(string ifaceName, IReadOnlyList<SlotSpec> slots)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("namespace TestProject;");
        sb.AppendLine();

        // Emit stub interfaces for any interface constraints referenced
        var referencedInterfaces = slots
            .SelectMany(s => s.ExpectInterfaceConstraints)
            .Distinct()
            .ToList();

        foreach (var iface in referencedInterfaces)
        {
            sb.AppendLine($"abstract {iface} {{ }}");
        }

        if (referencedInterfaces.Count > 0)
            sb.AppendLine();

        // Emit the main interface with associated type slots
        sb.AppendLine($"abstract {ifaceName} {{");
        foreach (var slot in slots)
        {
            var constraintList = BuildConstraintList(slot);
            if (string.IsNullOrEmpty(constraintList))
            {
                sb.AppendLine($"    type {slot.Name};");
            }
            else
            {
                sb.AppendLine($"    type {slot.Name} : {constraintList};");
            }
        }
        sb.AppendLine("}");

        return sb.ToString();
    }

    // ─── Arbitrary registration ─────────────────────────────────────────

    public static Arbitrary<SemanticRegistrationScenario> ArbitraryScenario() =>
        GenScenario().ToArbitrary();

    // ─── Helpers ────────────────────────────────────────────────────────

    private (ISemanticModel? Model, ImmutableArray<Diagnostic> Diagnostics) CompileAndCapture(string source)
    {
        var filePath = Path.Combine(_outputDir, $"Test_{Guid.NewGuid():N}.mist");
        File.WriteAllText(filePath, source);

        var capture = new CapturingSemanticModelBuilder();
        var driver = new CompilerDriver(
            new Mist.Lexing.MistLexer(),
            new Mist.Parsing.MistParser(),
            capture,
            ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
            ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

        var options = new CompilerOptions(
            ProjectFolder: _outputDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: _outputDir,
            BinDirectory: Path.Combine(_outputDir, "bin"),
            ProjectName: "TestProject",
            NamespaceRoot: "TestProject",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);

        var result = driver.Compile(options);
        return (capture.Captured, result.Diagnostics);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 5: Semantic registration records correct constraints
    //
    // **Validates: Requirements 2.1, 2.2, 2.3, 2.4**
    //
    // For any interface or trait containing one or more associated type slots
    // with varying constraint lists (including struct, class, interface
    // references, new(), and combinations thereof), the semantic model SHALL
    // register each slot as a discoverable type member whose
    // TypeParameterConstraints metadata exactly reflects the declared
    // constraints.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AssociatedTypeSemanticPropertyTests) })]
    public Property SemanticRegistration_RecordsCorrectConstraints(SemanticRegistrationScenario scenario)
    {
        var (model, diagnostics) = CompileAndCapture(scenario.Source);

        if (model is null)
        {
            var errors = diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.Message)
                .ToList();
            return false.ToProperty()
                .Label($"Semantic model was null. Errors:\n  {string.Join("\n  ", errors)}\nSource:\n{scenario.Source}");
        }

        // Find the interface symbol in the semantic model
        var ifaceSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == scenario.InterfaceName && s.Kind == SymbolKind.Interface);

        if (ifaceSymbol is null)
            return false.ToProperty()
                .Label($"Interface '{scenario.InterfaceName}' not found in semantic model.\nSource:\n{scenario.Source}");

        if (ifaceSymbol.DeclaredType is not AbstractType ifaceType)
            return false.ToProperty()
                .Label($"Interface '{scenario.InterfaceName}' DeclaredType is not AbstractType (got {ifaceSymbol.DeclaredType?.GetType().Name ?? "null"}).\nSource:\n{scenario.Source}");

        // Verify each slot is discoverable and has correct constraints
        foreach (var expectedSlot in scenario.Slots)
        {
            // Look up the slot in the interface's member scope
            var slotSymbol = ifaceType.MemberScope.Lookup(expectedSlot.Name);
            if (slotSymbol is null)
                return false.ToProperty()
                    .Label($"Slot '{expectedSlot.Name}' not found in interface '{scenario.InterfaceName}' member scope.\nSource:\n{scenario.Source}");

            if (slotSymbol.DeclaredType is not AssociatedTypeSlotType slotType)
                return false.ToProperty()
                    .Label($"Slot '{expectedSlot.Name}' DeclaredType is not AssociatedTypeSlotType (got {slotSymbol.DeclaredType?.GetType().Name ?? "null"}).\nSource:\n{scenario.Source}");

            var constraints = slotType.Constraints;

            // Verify struct constraint
            if (constraints.HasStructConstraint != expectedSlot.ExpectStruct)
                return false.ToProperty()
                    .Label($"Slot '{expectedSlot.Name}': HasStructConstraint={constraints.HasStructConstraint}, expected={expectedSlot.ExpectStruct}.\nSource:\n{scenario.Source}");

            // Verify class constraint
            if (constraints.HasClassConstraint != expectedSlot.ExpectClass)
                return false.ToProperty()
                    .Label($"Slot '{expectedSlot.Name}': HasClassConstraint={constraints.HasClassConstraint}, expected={expectedSlot.ExpectClass}.\nSource:\n{scenario.Source}");

            // Verify interface constraints
            var actualInterfaceNames = constraints.InterfaceConstraints
                .Select(t => t.DisplayName)
                .OrderBy(n => n)
                .ToList();
            var expectedInterfaceNames = expectedSlot.ExpectInterfaceConstraints
                .OrderBy(n => n)
                .ToList();

            if (!actualInterfaceNames.SequenceEqual(expectedInterfaceNames))
                return false.ToProperty()
                    .Label($"Slot '{expectedSlot.Name}': InterfaceConstraints=[{string.Join(", ", actualInterfaceNames)}], expected=[{string.Join(", ", expectedInterfaceNames)}].\nSource:\n{scenario.Source}");

            // Verify constructor constraint
            var hasConstructorConstraint = constraints.ConstructorConstraints.Count > 0;
            if (hasConstructorConstraint != expectedSlot.ExpectConstructorConstraint)
                return false.ToProperty()
                    .Label($"Slot '{expectedSlot.Name}': HasConstructorConstraint={hasConstructorConstraint}, expected={expectedSlot.ExpectConstructorConstraint}.\nSource:\n{scenario.Source}");
        }

        return true.ToProperty();
    }
}
