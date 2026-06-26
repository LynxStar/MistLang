// Feature: anonymous-types, Property 4: Property access resolution
// Feature: anonymous-types, Property 5: Anonymous type properties are read-only
// **Validates: Requirements 3.1, 3.2, 3.3, 10.4**
//
// Property 4 (design.md):
//   For any AnonymousType with N properties and any property name, if the name
//   exists in the type's property list at index i, member access SHALL resolve to
//   PropertyTypes[i]; if the name does not exist, the type checker SHALL report an
//   undefined member diagnostic.
//
// Property 5 (design.md):
//   For any AnonymousType and any property name in its property list, an assignment
//   targeting that property SHALL be rejected with a read-only diagnostic
//   (MIST_ANON_READONLY).
//
// Uses in-process CompilerDriver.Compile() with EmitToDisk:false — ZERO external
// process spawns per FsCheck iteration.

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Model;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests verifying:
/// - Property 4: Anonymous type property access resolves to the correct type or
///   reports MIST_UNDEFINED_MEMBER.
/// - Property 5: Assignments to anonymous type properties are rejected with
///   MIST_ANON_READONLY.
///
/// **Validates: Requirements 3.1, 3.2, 3.3, 10.4**
/// </summary>
public sealed class AnonymousTypePropertyAccessPropertyTests
{
    private const string UndefinedMemberId = "MIST_UNDEFINED_MEMBER";
    private const string ReadonlyId = "MIST_ANON_READONLY";

    // ─── Shared infrastructure (in-process compile, 0 process spawns) ─────

    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_anon_access_pbt_" + Guid.NewGuid().ToString("N"));
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
                NamespaceRoot: "TestProgram",
                EnabledAnalyzerIds: ImmutableArray<string>.Empty,
                EmitToDisk: false);

            return SharedDriver.Compile(options).Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Scenario models ─────────────────────────────────────────────────

#pragma warning disable CA1034 // nested types — FsCheck-friendly scenario types

    /// <summary>
    /// A property definition with a name and a value expression that determines the type.
    /// </summary>
    public sealed record PropertyDef(string Name, string ValueExpr, string ExpectedTypeName);

    /// <summary>
    /// Scenario for Property 4 (valid access): access an existing property and verify
    /// no MIST_UNDEFINED_MEMBER diagnostic is produced.
    /// </summary>
    public sealed record ValidAccessScenario(
        ImmutableArray<PropertyDef> Properties,
        int AccessedIndex,
        string Source)
    {
        public string AccessedName => Properties[AccessedIndex].Name;
        public string ExpectedType => Properties[AccessedIndex].ExpectedTypeName;
        public override string ToString()
        {
            return $"Access '{AccessedName}' (expect type '{ExpectedType}')";
        }
    }

    /// <summary>
    /// Scenario for Property 4 (invalid access): access a non-existent property and
    /// verify MIST_UNDEFINED_MEMBER is produced.
    /// </summary>
    public sealed record InvalidAccessScenario(
        ImmutableArray<PropertyDef> Properties,
        string NonExistentName,
        string Source)
    {
        public override string ToString()
        {
            return $"Access non-existent '{NonExistentName}'";
        }
    }

    /// <summary>
    /// Scenario for Property 5: assign to an existing anonymous type property and
    /// verify MIST_ANON_READONLY is produced.
    /// </summary>
    public sealed record ReadonlyAssignmentScenario(
        ImmutableArray<PropertyDef> Properties,
        int AssignedIndex,
        string Source)
    {
        public string AssignedName => Properties[AssignedIndex].Name;
        public override string ToString()
        {
            return $"Assign to '{AssignedName}'";
        }
    }

#pragma warning restore CA1034

    // ─── Property name and value pools ───────────────────────────────────

    /// <summary>
    /// Valid C# identifiers suitable for property names. PascalCase so they are
    /// valid as property names in anonymous types.
    /// </summary>
    private static readonly string[] NamePool =
    [
        "Alpha", "Beta", "Gamma", "Delta", "Epsilon",
        "Zeta", "Eta", "Theta", "Iota", "Kappa",
        "Name", "Value", "Count", "Index", "Label",
        "Score", "Width", "Height", "Color", "Status",
    ];

    /// <summary>
    /// Names guaranteed to NOT be in the NamePool (used for undefined member tests).
    /// </summary>
    private static readonly string[] NonExistentNames =
    [
        "Foo", "Bar", "Baz", "Qux", "Quux",
        "Missing", "Unknown", "Absent", "Void", "None",
    ];

    /// <summary>
    /// Typed value expressions with their expected type display name.
    /// </summary>
    private static readonly (string Expr, string TypeName)[] TypedValues =
    [
        ("42", "int"),
        ("\"hello\"", "string"),
        ("3.14", "double"),
        ("true", "bool"),
        ("'x'", "char"),
    ];

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates 1-5 distinct property definitions (name + typed value).
    /// </summary>
    private static Gen<ImmutableArray<PropertyDef>> GenPropertyDefs()
    {
        return from count in Gen.Choose(1, 5)
               from indices in Gen.ListOf(Gen.Choose(0, NamePool.Length - 1))
               from valueIndices in Gen.ListOf(Gen.Choose(0, TypedValues.Length - 1))
               let distinctNames = indices.Select(i => NamePool[i]).Distinct().Take(count).ToArray()
               where distinctNames.Length >= 1
               select distinctNames
                   .Select((name, i) =>
                   {
                       var vi = valueIndices.Count > 0
                           ? valueIndices[i % valueIndices.Count]
                           : 0;
                       return new PropertyDef(name, TypedValues[vi].Expr, TypedValues[vi].TypeName);
                   })
                   .ToImmutableArray();
    }

    /// <summary>
    /// Builds a Mist source with an anonymous type and accesses the property at the given index.
    /// </summary>
    private static string BuildValidAccessSource(ImmutableArray<PropertyDef> props, int accessIndex)
    {
        var propList = string.Join(", ", props.Select(p => $"{p.Name} = {p.ValueExpr}"));
        var accessedName = props[accessIndex].Name;

        return $$"""
            using Standard;

            namespace TestProgram;

            class Program
            {
                public static void Main()
                {
                    var anon = new { {{propList}} };
                    var result = anon.{{accessedName}};
                    Console.WriteLine(result);
                }
            }
            """;
    }

    /// <summary>
    /// Builds a Mist source with an anonymous type and accesses a non-existent property.
    /// </summary>
    private static string BuildInvalidAccessSource(ImmutableArray<PropertyDef> props, string nonExistentName)
    {
        var propList = string.Join(", ", props.Select(p => $"{p.Name} = {p.ValueExpr}"));

        return $$"""
            namespace TestProgram;

            class Program
            {
                public static void Main()
                {
                    var anon = new { {{propList}} };
                    var result = anon.{{nonExistentName}};
                }
            }
            """;
    }

    /// <summary>
    /// Builds a Mist source with an anonymous type and attempts to assign to a property.
    /// </summary>
    private static string BuildReadonlyAssignmentSource(ImmutableArray<PropertyDef> props, int assignIndex)
    {
        var propList = string.Join(", ", props.Select(p => $"{p.Name} = {p.ValueExpr}"));
        var assignedName = props[assignIndex].Name;
        // Use a value expression matching the property's type
        var assignValue = props[assignIndex].ValueExpr;

        return $$"""
            namespace TestProgram;

            class Program
            {
                public static void Main()
                {
                    var anon = new { {{propList}} };
                    anon.{{assignedName}} = {{assignValue}};
                }
            }
            """;
    }

    // ─── Arbitrary instances ─────────────────────────────────────────────

    private static Gen<ValidAccessScenario> GenValidAccess()
    {
        return from props in GenPropertyDefs()
               from accessIndex in Gen.Choose(0, props.Length - 1)
               let source = BuildValidAccessSource(props, accessIndex)
               select new ValidAccessScenario(props, accessIndex, source);
    }

    private static Gen<InvalidAccessScenario> GenInvalidAccess()
    {
        return from props in GenPropertyDefs()
               from nonExistentIdx in Gen.Choose(0, NonExistentNames.Length - 1)
               let nonExistentName = NonExistentNames[nonExistentIdx]
               where !props.Any(p => p.Name == nonExistentName)
               let source = BuildInvalidAccessSource(props, nonExistentName)
               select new InvalidAccessScenario(props, nonExistentName, source);
    }

    private static Gen<ReadonlyAssignmentScenario> GenReadonlyAssignment()
    {
        return from props in GenPropertyDefs()
               from assignIndex in Gen.Choose(0, props.Length - 1)
               let source = BuildReadonlyAssignmentSource(props, assignIndex)
               select new ReadonlyAssignmentScenario(props, assignIndex, source);
    }

    public static Arbitrary<ValidAccessScenario> ArbitraryValidAccess()
    {
        return GenValidAccess().ToArbitrary();
    }

    public static Arbitrary<InvalidAccessScenario> ArbitraryInvalidAccess()
    {
        return GenInvalidAccess().ToArbitrary();
    }

    public static Arbitrary<ReadonlyAssignmentScenario> ArbitraryReadonlyAssignment()
    {
        return GenReadonlyAssignment().ToArbitrary();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 4: Property access resolution — valid access
    //
    //   For any AnonymousType with N properties and any property name that EXISTS
    //   in the property list, member access SHALL resolve successfully (no
    //   MIST_UNDEFINED_MEMBER diagnostic).
    //
    // **Validates: Requirements 3.1**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AnonymousTypePropertyAccessPropertyTests) })]
    public Property ValidPropertyAccess_ResolvesWithoutError(ValidAccessScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        var undefinedMemberDiags = diagnostics
            .Where(d => d.Id.Value == UndefinedMemberId
                        && d.Message.Contains(scenario.AccessedName, StringComparison.Ordinal))
            .ToList();

        if(undefinedMemberDiags.Count > 0)
        {
            var messages = string.Join("\n", undefinedMemberDiags.Select(d => d.Message));
            return false.ToProperty().Label(
                $"Expected no MIST_UNDEFINED_MEMBER for '{scenario.AccessedName}' but got:\n"
                + $"{messages}\nScenario: {scenario}\nSource:\n{scenario.Source}");
        }

        return true.ToProperty();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 4: Property access resolution — invalid access
    //
    //   For any AnonymousType with N properties and any property name that DOES NOT
    //   exist in the property list, member access SHALL produce a
    //   MIST_UNDEFINED_MEMBER diagnostic.
    //
    // **Validates: Requirements 3.2**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AnonymousTypePropertyAccessPropertyTests) })]
    public Property InvalidPropertyAccess_ProducesUndefinedMemberDiagnostic(InvalidAccessScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        var undefinedMemberDiags = diagnostics
            .Where(d => d.Id.Value == UndefinedMemberId
                        && d.Message.Contains(scenario.NonExistentName, StringComparison.Ordinal))
            .ToList();

        if(undefinedMemberDiags.Count == 0)
        {
            var allIds = string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"));
            return false.ToProperty().Label(
                $"Expected MIST_UNDEFINED_MEMBER for '{scenario.NonExistentName}' but got none.\n"
                + $"Scenario: {scenario}\nAll diagnostics: [{allIds}]\nSource:\n{scenario.Source}");
        }

        return true.ToProperty();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 5: Anonymous type properties are read-only
    //
    //   For any AnonymousType and any property name in its property list, an
    //   assignment targeting that property SHALL be rejected with MIST_ANON_READONLY.
    //
    // **Validates: Requirements 3.3, 10.4**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AnonymousTypePropertyAccessPropertyTests) })]
    public Property ReadonlyPropertyAssignment_ProducesReadonlyDiagnostic(ReadonlyAssignmentScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        var readonlyDiags = diagnostics
            .Where(d => d.Id.Value == ReadonlyId)
            .ToList();

        if(readonlyDiags.Count == 0)
        {
            var allIds = string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"));
            return false.ToProperty().Label(
                $"Expected MIST_ANON_READONLY for assignment to '{scenario.AssignedName}' but got none.\n"
                + $"Scenario: {scenario}\nAll diagnostics: [{allIds}]\nSource:\n{scenario.Source}");
        }

        // The diagnostic message should mention "read-only"
        var mentionsReadonly = readonlyDiags.Any(d =>
            d.Message.Contains("read-only", StringComparison.OrdinalIgnoreCase));

        if(!mentionsReadonly)
        {
            var messages = string.Join("\n", readonlyDiags.Select(d => d.Message));
            return false.ToProperty().Label(
                $"MIST_ANON_READONLY found but message does not mention 'read-only'.\n"
                + $"Scenario: {scenario}\nMessages:\n{messages}\nSource:\n{scenario.Source}");
        }

        return true.ToProperty();
    }
}