// Feature: ref-structs, Property 4: Ref struct generic type argument rejection
// Feature: ref-structs, Property 8: Ref struct member access equivalence
// **Validates: Requirements 3.7, 4.1, 4.2, 4.3, 4.4**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Tests.Helpers;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for ref struct usage restrictions enforced by the type checker.
///
/// Property 4: Ref struct generic type argument rejection
/// For any ref struct type R and generic type G&lt;T&gt;, using R as a type argument
/// SHALL cause the type checker to emit diagnostic MIST2004.
///
/// Property 8: Ref struct member access equivalence
/// For any ref struct type R with public members (fields, methods, properties),
/// accessing those members on a value of type R SHALL resolve successfully using
/// the same rules as regular struct member access, producing no diagnostics.
///
/// Uses in-process compilation via shared lexer, parser, semantic model builder,
/// and type checker. No per-iteration process spawning.
///
/// **Validates: Requirements 3.7, 4.1, 4.2, 4.3, 4.4**
/// </summary>
public sealed class RefStructUsagePropertyTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly MistLexer SharedLexer = new();
    private static readonly MistParser SharedParser = new();
    private static readonly TypeCheckerAnalyzer SharedAnalyzer = new();

    private static ImmutableArray<Diagnostic> CompileAndAnalyze(string source)
    {
        var sourceText = new SourceText("test.mist", source);

        var lexDiag = new DiagnosticBag();
        var tokens = SharedLexer.Lex(sourceText, lexDiag);

        var parseDiag = new DiagnosticBag();
        var parseResult = SharedParser.Parse(tokens, parseDiag);
        if(parseResult.Root is null)
        {
            return parseDiag.ToImmutableArray();
        }

        var (model, buildDiag) = StdlibTestHelper.BuildModelWithStdlib(
            parseResult.Root, source: source);

        var tcDiag = new DiagnosticBag();
        SharedAnalyzer.Analyze(model, tcDiag);

        var all = new List<Diagnostic>();
        all.AddRange(buildDiag);
        all.AddRange(tcDiag.ToImmutableArray());
        return all.ToImmutableArray();
    }

    // ─── Scenario model for Property 4 ──────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// Describes how a ref struct is used as a generic type argument.
    /// </summary>
    public enum GenericUsageKind
    {
        /// <summary>Used as type argument in object creation: new G&lt;R&gt;()</summary>
        ObjectCreation,

        /// <summary>Used as type argument in a base class: class C : G&lt;R&gt;</summary>
        BaseType,

        /// <summary>Used as type argument in an abstract type implementation: class C : IFace&lt;R&gt;</summary>
        AbstractImpl,
    }

    /// <summary>
    /// A scenario for Property 4: ref struct used as a generic type argument.
    /// </summary>
    public sealed record GenericTypeArgScenario(
        string RefStructName,
        string GenericTypeName,
        GenericUsageKind UsageKind)
    {
        public override string ToString()
        {
            return $"{UsageKind}: {GenericTypeName}<{RefStructName}>";
        }
    }

#pragma warning restore CA1034

    // ─── Name pools ──────────────────────────────────────────────────────

    private static readonly string[] RefStructNames =
        ["MySpan", "Buffer", "StackSlice", "Window", "Frame", "Region"];

    private static readonly string[] GenericTypeNames =
        ["Container", "Wrapper", "Holder", "Box", "Store", "Cache"];

    // ─── FsCheck generators ─────────────────────────────────────────────

    private static Gen<GenericTypeArgScenario> GenScenario()
    {
        return from refStructName in Gen.Elements(RefStructNames)
               from genericTypeName in Gen.Elements(GenericTypeNames)
               from usageKind in Gen.Elements(
                   GenericUsageKind.ObjectCreation,
                   GenericUsageKind.BaseType,
                   GenericUsageKind.AbstractImpl)
               select new GenericTypeArgScenario(refStructName, genericTypeName, usageKind);
    }

    public static Arbitrary<GenericTypeArgScenario> ArbitraryGenericTypeArgScenario()
    {
        return GenScenario().ToArbitrary();
    }

    // ─── Source generation ───────────────────────────────────────────────

    private static string BuildSource(GenericTypeArgScenario scenario)
    {
        return scenario.UsageKind switch
        {
            GenericUsageKind.ObjectCreation => BuildObjectCreationSource(scenario),
            GenericUsageKind.BaseType => BuildBaseTypeSource(scenario),
            GenericUsageKind.AbstractImpl => BuildAbstractImplSource(scenario),
            _ => throw new ArgumentOutOfRangeException(),
        };
    }

    private static string BuildObjectCreationSource(GenericTypeArgScenario scenario)
    {
        return $@"using Standard;

public ref struct {scenario.RefStructName}
{{
    public int value;
}}

public class {scenario.GenericTypeName}<T>
{{
    public int dummy;
}}

public class Program
{{
    public static void Main()
    {{
        var x = new {scenario.GenericTypeName}<{scenario.RefStructName}>();
    }}
}}";
    }

    private static string BuildBaseTypeSource(GenericTypeArgScenario scenario)
    {
        return $@"using Standard;

public ref struct {scenario.RefStructName}
{{
    public int value;
}}

public class {scenario.GenericTypeName}<T>
{{
    public int dummy;
}}

public class Derived : {scenario.GenericTypeName}<{scenario.RefStructName}>
{{
}}

public class Program
{{
    public static void Main()
    {{
    }}
}}";
    }

    private static string BuildAbstractImplSource(GenericTypeArgScenario scenario)
    {
        return $@"using Standard;

public ref struct {scenario.RefStructName}
{{
    public int value;
}}

public abstract I{scenario.GenericTypeName}<T>
{{
}}

public class Derived : I{scenario.GenericTypeName}<{scenario.RefStructName}>
{{
}}

public class Program
{{
    public static void Main()
    {{
    }}
}}";
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 4: Ref struct generic type argument rejection
    //
    // For any ref struct type R and generic type G<T>, using R as a type
    // argument (i.e., G<R>) SHALL cause the type checker to emit a
    // diagnostic error MIST2004.
    //
    // **Validates: Requirements 3.7**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        Arbitrary = new[] { typeof(RefStructUsagePropertyTests) },
        MaxTest = 100)]
    public Property RefStructAsGenericTypeArgument_EmitsMIST2004(GenericTypeArgScenario scenario)
    {
        var source = BuildSource(scenario);
        var diagnostics = CompileAndAnalyze(source);

        var mist2004Diags = diagnostics
            .Where(d => d.Id.Value == "MIST2004")
            .ToList();

        var hasDiagnostic = mist2004Diags.Count > 0;

        if(!hasDiagnostic)
        {
            return false.ToProperty()
                .Label($"Expected MIST2004 but got none.\n" +
                       $"Scenario: {scenario}\n" +
                       $"Source:\n{source}\n" +
                       $"All diagnostics: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
        }

        // Verify the diagnostic message mentions the ref struct name
        var mentionsRefStruct = mist2004Diags.Any(d =>
            d.Message.Contains(scenario.RefStructName, StringComparison.OrdinalIgnoreCase));

        return mentionsRefStruct.ToProperty()
            .Label($"MIST2004 emitted but does not mention '{scenario.RefStructName}'.\n" +
                   $"Scenario: {scenario}\n" +
                   $"Diagnostic messages: [{string.Join(", ", mist2004Diags.Select(d => d.Message))}]");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 8: Ref struct member access equivalence
    //
    // **Validates: Requirements 4.1, 4.2, 4.3, 4.4**
    //
    // For any ref struct type R with public members (fields, methods,
    // properties), accessing those members on a value of type R SHALL
    // resolve successfully using the same rules as regular struct member
    // access, producing no diagnostics.
    // ═══════════════════════════════════════════════════════════════════════

    // ─── Scenario model for Property 8 ───────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// Describes which member access operation to perform on a ref struct value.
    /// </summary>
    public enum MemberAccessKind
    {
        /// <summary>Read a field: rs.fieldName</summary>
        FieldRead,

        /// <summary>Call a method: rs.MethodName()</summary>
        MethodCall,

        /// <summary>Read a property: rs.PropertyName</summary>
        PropertyRead,
    }

    /// <summary>
    /// A scenario for Property 8: accessing members on a ref struct value.
    /// </summary>
    public sealed record MemberAccessScenario(
        string RefStructName,
        string MemberName,
        MemberAccessKind AccessKind)
    {
        public override string ToString()
        {
            return $"{RefStructName}.{MemberName} ({AccessKind})";
        }
    }

#pragma warning restore CA1034

    // ─── Name pools for Property 8 ───────────────────────────────────────

    private static readonly string[] MemberAccessStructNames =
        ["MySpan", "Buffer", "StackSlice", "Window", "Frame", "Region", "Chunk", "Segment"];

    private static readonly string[] FieldNames =
        ["length", "offset", "capacity", "count", "start", "size"];

    private static readonly string[] MethodNames =
        ["GetValue", "Clear", "Reset", "Advance", "DoSlice", "CopyTo"];

    private static readonly string[] PropertyNames =
        ["Length", "IsEmpty", "Count", "Capacity", "First", "Position"];

    // ─── FsCheck generators for Property 8 ───────────────────────────────

    private static Gen<MemberAccessScenario> GenMemberAccessScenario()
    {
        var fieldGen =
            from structName in Gen.Elements(MemberAccessStructNames)
            from fieldName in Gen.Elements(FieldNames)
            select new MemberAccessScenario(structName, fieldName, MemberAccessKind.FieldRead);

        var methodGen =
            from structName in Gen.Elements(MemberAccessStructNames)
            from methodName in Gen.Elements(MethodNames)
            select new MemberAccessScenario(structName, methodName, MemberAccessKind.MethodCall);

        var propertyGen =
            from structName in Gen.Elements(MemberAccessStructNames)
            from propertyName in Gen.Elements(PropertyNames)
            select new MemberAccessScenario(structName, propertyName, MemberAccessKind.PropertyRead);

        return Gen.OneOf(fieldGen, methodGen, propertyGen);
    }

    public static Arbitrary<MemberAccessScenario> ArbitraryMemberAccessScenario()
    {
        return GenMemberAccessScenario().ToArbitrary();
    }

    // ─── Source generation for Property 8 ────────────────────────────────

    private static string BuildMemberAccessSource(MemberAccessScenario scenario)
    {
        // Build the ref struct declaration with the appropriate member
        var memberDecl = scenario.AccessKind switch
        {
            MemberAccessKind.FieldRead =>
                $"    public int {scenario.MemberName};",
            MemberAccessKind.MethodCall =>
                $"    public int {scenario.MemberName}()\n" +
                "    {\n" +
                "        return 42;\n" +
                "    }",
            MemberAccessKind.PropertyRead =>
                $"    public int {scenario.MemberName}\n" +
                "    {\n" +
                "        get\n" +
                "        {\n" +
                "            return 7;\n" +
                "        }\n" +
                "    }",
            _ => throw new ArgumentOutOfRangeException(),
        };

        // Build the access expression in Main
        var accessExpr = scenario.AccessKind switch
        {
            MemberAccessKind.FieldRead => $"int result = rs.{scenario.MemberName};",
            MemberAccessKind.MethodCall => $"int result = rs.{scenario.MemberName}();",
            MemberAccessKind.PropertyRead => $"int result = rs.{scenario.MemberName};",
            _ => throw new ArgumentOutOfRangeException(),
        };

        return $@"using Standard;

public ref struct {scenario.RefStructName}
{{
{memberDecl}
}}

public class Program
{{
    public static void Main(string[] args)
    {{
        var rs = new {scenario.RefStructName}();
        {accessExpr}
    }}
}}";
    }

    // ─── Property 8 test ─────────────────────────────────────────────────

    /// <summary>
    /// Property 8: Ref struct member access equivalence
    ///
    /// For any ref struct type R with public members (fields, methods, properties),
    /// accessing those members on a value of type R SHALL resolve successfully
    /// using the same rules as regular struct member access, producing no diagnostics.
    ///
    /// **Validates: Requirements 4.1, 4.2, 4.3, 4.4**
    /// </summary>
    [Property(
        Arbitrary = new[] { typeof(RefStructUsagePropertyTests) },
        MaxTest = 100)]
    public Property RefStructMemberAccess_ResolvesWithZeroDiagnostics(
        MemberAccessScenario scenario)
    {
        var source = BuildMemberAccessSource(scenario);
        var diagnostics = CompileAndAnalyze(source);

        var errors = diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if(errors.Count > 0)
        {
            var errorText = string.Join("\n", errors.Select(d => $"  {d.Id.Value}: {d.Message}"));
            return false.ToProperty()
                .Label($"Expected zero errors for member access on ref struct.\n" +
                       $"Scenario: {scenario}\n" +
                       $"Source:\n{source}\n" +
                       $"Errors:\n{errorText}");
        }

        return true.ToProperty()
            .Label($"Member access '{scenario}' resolved successfully with no diagnostics");
    }
}