// Feature: abstract-type-unification, Property 11: With-block defaults don't require mappings

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
using Mist.Semantics.Model;

namespace Mist.Tests.AbstractTypeUnification;

// ─── Scenario type ──────────────────────────────────────────────────────────

/// <summary>
/// A scenario where an abstract type has M required slots (1-3) AND N default
/// methods (1-3). A concrete type has methods with different names (to map to
/// required slots). A <c>with</c> block maps ONLY the required slots (omits any
/// mapping for default methods). An assignment <c>AbstractType x = new ConcreteType();</c>
/// is attempted. The compiler should accept this without error diagnostics.
/// </summary>
public sealed class WithBlockDefaultsOptionalScenario
{
    public string Source { get; }
    public int RequiredSlotCount { get; }
    public int DefaultMethodCount { get; }
    public string Description { get; }

    public WithBlockDefaultsOptionalScenario(string source, int requiredSlotCount, int defaultMethodCount, string description)
    {
        Source = source;
        RequiredSlotCount = requiredSlotCount;
        DefaultMethodCount = defaultMethodCount;
        Description = description;
    }

    public override string ToString() => Description;
}

// ─── Generators ─────────────────────────────────────────────────────────────

/// <summary>
/// Generates Mist source code with abstract types containing required slots
/// and default methods. A concrete type has methods with DIFFERENT names than
/// the abstract type's required slots. A <c>with</c> block maps only the required
/// slots using <c>as</c> syntax, omitting any mapping for default methods.
/// </summary>
public static class WithBlockDefaultsOptionalGenerators
{
    private static readonly string[] ReturnTypes = ["string", "int", "bool"];

    /// <summary>
    /// Represents a required slot signature (no body) in the abstract type.
    /// </summary>
    private sealed record SlotSignature(string ReturnType, string SlotName, string ConcreteName);

    /// <summary>
    /// Represents a default method (has a body) in the abstract type.
    /// </summary>
    private sealed record DefaultMethod(string ReturnType, string Name);

    private static Gen<SlotSignature[]> GenRequiredSlots(int count)
    {
        Gen<SlotSignature[]> result = Gen.Constant(Array.Empty<SlotSignature>());
        for (var i = 0; i < count; i++)
        {
            var index = i;
            result = result.SelectMany(arr =>
                GenSlotSignature(index).Select(slot => arr.Append(slot).ToArray()));
        }
        return result;
    }

    private static Gen<SlotSignature> GenSlotSignature(int index)
    {
        return from returnType in Gen.Elements(ReturnTypes)
               let slotName = $"Required{index}"
               let concreteName = $"DoRequired{index}"
               select new SlotSignature(returnType, slotName, concreteName);
    }

    private static Gen<DefaultMethod[]> GenDefaultMethods(int count)
    {
        Gen<DefaultMethod[]> result = Gen.Constant(Array.Empty<DefaultMethod>());
        for (var i = 0; i < count; i++)
        {
            var index = i;
            result = result.SelectMany(arr =>
                GenDefaultMethod(index).Select(dm => arr.Append(dm).ToArray()));
        }
        return result;
    }

    private static Gen<DefaultMethod> GenDefaultMethod(int index)
    {
        return from returnType in Gen.Elements(ReturnTypes)
               let methodName = $"Default{index}"
               select new DefaultMethod(returnType, methodName);
    }

    /// <summary>
    /// Generates a scenario: abstract type with required slots + default methods,
    /// a concrete type with differently-named methods, and a with block that maps
    /// only the required slots.
    /// </summary>
    public static Gen<WithBlockDefaultsOptionalScenario> GenScenario()
    {
        return from requiredCount in Gen.Choose(1, 3)
               from defaultCount in Gen.Choose(1, 3)
               from slots in GenRequiredSlots(requiredCount)
               from defaults in GenDefaultMethods(defaultCount)
               let source = BuildSource(slots, defaults)
               select new WithBlockDefaultsOptionalScenario(
                   source,
                   slots.Length,
                   defaults.Length,
                   $"{slots.Length} required slots, {defaults.Length} default methods (with block)");
    }

    private static string BuildSource(SlotSignature[] slots, DefaultMethod[] defaults)
    {
        // Build abstract type members: required slots (semicolon-terminated) + default methods (with bodies)
        var slotMembers = string.Join("\n", slots.Select(s =>
            $"    {s.ReturnType} {s.SlotName}();"));

        var defaultMembers = string.Join("\n\n", defaults.Select(d =>
        {
            var returnValue = d.ReturnType switch
            {
                "string" => "\"default\"",
                "int" => "42",
                "bool" => "true",
                _ => "default"
            };
            return $"    {d.ReturnType} {d.Name}()\n    {{\n        return {returnValue};\n    }}";
        }));

        // Build concrete type with differently-named methods (to be mapped via with block)
        var concreteMembers = string.Join("\n\n", slots.Select(s =>
        {
            var returnValue = s.ReturnType switch
            {
                "string" => "\"filled\"",
                "int" => "99",
                "bool" => "false",
                _ => "default"
            };
            return $"    public {s.ReturnType} {s.ConcreteName}()\n    {{\n        return {returnValue};\n    }}";
        }));

        // Build with block: map ONLY the required slots, omit default methods
        var mappings = string.Join("\n", slots.Select(s =>
            $"    {s.ConcreteName} as {s.SlotName};"));

        return $$"""
            abstract IContract
            {
            {{slotMembers}}

            {{defaultMembers}}
            }

            public class MyClass
            {
            {{concreteMembers}}
            }

            with MyClass : IContract
            {
            {{mappings}}
            }

            public class Program
            {
                public static void Main(string[] args)
                {
                    IContract x = new MyClass();
                }
            }
            """;
    }
}

// ─── Property Tests ─────────────────────────────────────────────────────────

/// <summary>
/// Property-based tests verifying that with-block defaults don't require mappings.
///
/// Property 11: For any abstract type with M default methods and N required slots,
/// a with block SHALL only require mappings for the N required slots. Omitting
/// mappings for default methods SHALL be accepted.
///
/// **Validates: Requirements 6.3**
/// </summary>
public sealed class WithBlockDefaultsOptionalPropertyTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_pbt_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
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

            var result = SharedDriver.Compile(options);
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Arbitrary registration ─────────────────────────────────────────

    public static Arbitrary<WithBlockDefaultsOptionalScenario> ArbitraryWithBlockDefaultsOptionalScenario() =>
        WithBlockDefaultsOptionalGenerators.GenScenario().ToArbitrary();

    // ─── Property test ──────────────────────────────────────────────────

    /// <summary>
    /// Property 11: With-block defaults don't require mappings.
    ///
    /// For any abstract type with M required slots (1-3) and N default methods
    /// (1-3, methods with bodies), when a with block maps ONLY the required slots
    /// (omitting any mapping for default methods), the compiler SHALL produce
    /// NO error diagnostics.
    ///
    /// **Validates: Requirements 6.3**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(WithBlockDefaultsOptionalPropertyTests) })]
    public Property WithBlockDefaultsDoNotRequireMappings(WithBlockDefaultsOptionalScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        var errors = diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        return (errors.Count == 0).ToProperty()
            .Label($"Expected no error diagnostics when with block omits default method mappings ({scenario.RequiredSlotCount} required, {scenario.DefaultMethodCount} defaults)")
            .Label($"Errors: [{string.Join("; ", errors.Select(d => $"{d.Id.Value}: {d.Message}"))}]")
            .Label($"Source:\n{scenario.Source}");
    }
}
