// Feature: abstract-type-unification, Property 5: Default methods do not require implementation

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
/// methods (1-3, methods with bodies). A concrete type declares `: AbstractType`,
/// fills ALL required slots, but provides NO implementation for default methods.
/// The compiler should accept this without error diagnostics.
/// </summary>
public sealed class DefaultMethodsOptionalScenario
{
    public string Source { get; }
    public int RequiredSlotCount { get; }
    public int DefaultMethodCount { get; }
    public string Description { get; }

    public DefaultMethodsOptionalScenario(string source, int requiredSlotCount, int defaultMethodCount, string description)
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
/// and default methods. Concrete types fill all required slots but do NOT
/// provide implementations for default methods.
/// </summary>
public static class DefaultMethodsOptionalGenerators
{
    private static readonly string[] ReturnTypes = ["string", "int", "bool"];

    /// <summary>
    /// Represents a required slot signature (no body).
    /// </summary>
    private sealed record SlotSignature(string ReturnType, string Name, (string Type, string Name)[] Parameters);

    /// <summary>
    /// Represents a default method (has a body).
    /// </summary>
    private sealed record DefaultMethod(string ReturnType, string Name, (string Type, string Name)[] Parameters);

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
               from paramCount in Gen.Choose(0, 2)
               from paramTypes in Gen.ArrayOf<string>(Gen.Elements(ReturnTypes), paramCount)
               let methodName = $"Required{index}"
               let parameters = paramTypes.Select((t, i) => (Type: t, Name: $"p{i}")).ToArray()
               select new SlotSignature(returnType, methodName, parameters);
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
               from paramCount in Gen.Choose(0, 2)
               from paramTypes in Gen.ArrayOf<string>(Gen.Elements(ReturnTypes), paramCount)
               let methodName = $"Default{index}"
               let parameters = paramTypes.Select((t, i) => (Type: t, Name: $"d{i}")).ToArray()
               select new DefaultMethod(returnType, methodName, parameters);
    }

    /// <summary>
    /// Generates a scenario: abstract type with required slots + default methods,
    /// and a concrete type that fills only the required slots.
    /// </summary>
    public static Gen<DefaultMethodsOptionalScenario> GenScenario()
    {
        return from requiredCount in Gen.Choose(1, 3)
               from defaultCount in Gen.Choose(1, 3)
               from slots in GenRequiredSlots(requiredCount)
               from defaults in GenDefaultMethods(defaultCount)
               let source = BuildSource(slots, defaults)
               select new DefaultMethodsOptionalScenario(
                   source,
                   slots.Length,
                   defaults.Length,
                   $"{slots.Length} required slots, {defaults.Length} default methods");
    }

    private static string BuildSource(SlotSignature[] slots, DefaultMethod[] defaults)
    {
        // Build abstract type members: required slots (semicolon-terminated) + default methods (with bodies)
        var slotMembers = string.Join("\n", slots.Select(s =>
        {
            var paramList = string.Join(", ", s.Parameters.Select(p => $"{p.Type} {p.Name}"));
            return $"    {s.ReturnType} {s.Name}({paramList});";
        }));

        var defaultMembers = string.Join("\n\n", defaults.Select(d =>
        {
            var paramList = string.Join(", ", d.Parameters.Select(p => $"{p.Type} {p.Name}"));
            var returnValue = d.ReturnType switch
            {
                "string" => "\"default\"",
                "int" => "42",
                "bool" => "true",
                _ => "default"
            };
            return $"    {d.ReturnType} {d.Name}({paramList})\n    {{\n        return {returnValue};\n    }}";
        }));

        // Build concrete type: fills only the required slots, NO implementations for defaults
        var concreteMembers = string.Join("\n\n", slots.Select(s =>
        {
            var paramList = string.Join(", ", s.Parameters.Select(p => $"{p.Type} {p.Name}"));
            var returnValue = s.ReturnType switch
            {
                "string" => "\"filled\"",
                "int" => "99",
                "bool" => "false",
                _ => "default"
            };
            return $"    public {s.ReturnType} {s.Name}({paramList})\n    {{\n        return {returnValue};\n    }}";
        }));

        return $$"""
            abstract IContract
            {
            {{slotMembers}}

            {{defaultMembers}}
            }

            public class Implementer : IContract
            {
            {{concreteMembers}}
            }

            public class Program
            {
                public static void Main(string[] args)
                {
                }
            }
            """;
    }
}

// ─── Property Tests ─────────────────────────────────────────────────────────

/// <summary>
/// Property-based tests verifying that default methods do not require implementation.
///
/// Property 5: For any abstract type with M default methods and any concrete type
/// that declares `: AbstractType` and fills all required slots but provides no
/// implementation for any default method, the type checker SHALL accept the type
/// as valid.
///
/// **Validates: Requirements 4.5, 5.1**
/// </summary>
public sealed class DefaultMethodsOptionalPropertyTests
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

    public static Arbitrary<DefaultMethodsOptionalScenario> ArbitraryDefaultMethodsOptionalScenario() =>
        DefaultMethodsOptionalGenerators.GenScenario().ToArbitrary();

    // ─── Property test ──────────────────────────────────────────────────

    /// <summary>
    /// Property 5: Default methods do not require implementation.
    ///
    /// For any abstract type with M required slots (1-3) and N default methods
    /// (1-3, methods with bodies), when a concrete type declares `: AbstractType`
    /// and fills ALL required slots but provides NO implementation for default
    /// methods, the compiler SHALL produce NO error diagnostics.
    ///
    /// **Validates: Requirements 4.5, 5.1**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(DefaultMethodsOptionalPropertyTests) })]
    public Property DefaultMethodsDoNotRequireImplementation(DefaultMethodsOptionalScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        var errors = diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        return (errors.Count == 0).ToProperty()
            .Label($"Expected no error diagnostics when defaults are not implemented ({scenario.RequiredSlotCount} required, {scenario.DefaultMethodCount} defaults)")
            .Label($"Errors: [{string.Join("; ", errors.Select(d => $"{d.Id.Value}: {d.Message}"))}]")
            .Label($"Source:\n{scenario.Source}");
    }
}
