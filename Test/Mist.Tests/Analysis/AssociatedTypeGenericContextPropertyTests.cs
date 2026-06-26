// Feature: associated-types, Property 9: Generic context resolution of associated types
// **Validates: Requirements 5.1, 5.2, 5.3**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Tests.Helpers;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests verifying generic context resolution of associated types.
///
/// <b>Property 9</b>: For any generic function or method whose type parameter <c>T</c>
/// is constrained by an abstract type declaring associated type slots, references to
/// <c>T.SlotName</c> in any type position (local variable types, parameter types,
/// return types, generic type arguments) SHALL be accepted by the type checker, and
/// operations valid on the slot's constraint list SHALL be permitted on values of
/// type <c>T.SlotName</c>.
///
/// All work runs in-process using compiler APIs — no external process is spawned.
/// </summary>
public class AssociatedTypeGenericContextPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();
    private static readonly TypeCheckerAnalyzer Analyzer = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static ImmutableArray<Diagnostic> CompileAndGetAllDiagnostics(string source)
    {
        var sourceText = new SourceText("test.mist", source);

        var lexDiag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexDiag);

        var parseDiag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseDiag);
        if(parseResult.Root is null)
        {
            return lexDiag.ToImmutableArray().AddRange(parseDiag.ToImmutableArray());
        }

        var (model, buildDiag) = StdlibTestHelper.BuildModelWithStdlib(
            parseResult.Root, source: source);

        var tcDiag = new DiagnosticBag();
        Analyzer.Analyze(model, tcDiag);

        var all = new List<Diagnostic>();
        all.AddRange(lexDiag.ToImmutableArray());
        all.AddRange(parseDiag.ToImmutableArray());
        all.AddRange(buildDiag);
        all.AddRange(tcDiag.ToImmutableArray());
        return all.ToImmutableArray();
    }

    // ─── Type position enum ──────────────────────────────────────────────

    /// <summary>Describes where T.SlotName is used in the generic function.</summary>
    public enum TypePosition
    {
        /// <summary>T.SlotName used as a parameter type.</summary>
        ParameterType,
        /// <summary>T.SlotName used as a return type.</summary>
        ReturnType,
        /// <summary>T.SlotName used as a local variable type.</summary>
        LocalVariableType,
        /// <summary>T.SlotName used in both parameter and return type positions.</summary>
        ParameterAndReturnType
    }

    // ─── Scenario model ──────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>Describes a complete test scenario for generic context resolution.</summary>
    public sealed class GenericContextScenario
    {
        public string AbstractName { get; }
        public string SlotName { get; }
        public string MethodName { get; }
        public string TypeParamName { get; }
        public TypePosition Position { get; }
        public bool HasSlotConstraint { get; }
        public string? SlotConstraintAbstractName { get; }
        public string? SlotConstraintMethodName { get; }

        public GenericContextScenario(
            string abstractName,
            string slotName,
            string methodName,
            string typeParamName,
            TypePosition position,
            bool hasSlotConstraint,
            string? slotConstraintAbstractName,
            string? slotConstraintMethodName)
        {
            AbstractName = abstractName;
            SlotName = slotName;
            MethodName = methodName;
            TypeParamName = typeParamName;
            Position = position;
            HasSlotConstraint = hasSlotConstraint;
            SlotConstraintAbstractName = slotConstraintAbstractName;
            SlotConstraintMethodName = slotConstraintMethodName;
        }

        public override string ToString()
        {
            return $"{TypeParamName}.{SlotName} in {Position} " +
            $"(abstract={AbstractName}, method={MethodName}" +
            (HasSlotConstraint ? $", constraint={SlotConstraintAbstractName}" : "") + ")";
        }
    }

#pragma warning restore CA1034

    // ─── Name pools ──────────────────────────────────────────────────────

    private static readonly string[] AbstractNames =
        ["IContainer", "IFactory", "IProcessor", "IMapper", "IProvider"];

    private static readonly string[] SlotNames =
        ["Element", "Product", "Result", "Output", "Item"];

    private static readonly string[] MethodNames =
        ["Process", "Transform", "Create", "Handle", "Execute"];

    private static readonly string[] TypeParamNames =
        ["T", "U", "V", "TSource", "TTarget"];

    private static readonly string[] ConstraintAbstractNames =
        ["IStringable", "IComparable", "ICloneable"];

    private static readonly string[] ConstraintMethodNames =
        ["GetValue", "Compare", "Clone"];

    // ─── Generators ──────────────────────────────────────────────────────

    private static Gen<GenericContextScenario> GenScenario()
    {
        return from abstractName in Gen.Elements(AbstractNames)
               from slotName in Gen.Elements(SlotNames)
               from methodName in Gen.Elements(MethodNames)
               from typeParamName in Gen.Elements(TypeParamNames)
               from position in Gen.Elements(
                   TypePosition.ParameterType,
                   TypePosition.ReturnType,
                   TypePosition.LocalVariableType,
                   TypePosition.ParameterAndReturnType)
               from hasConstraint in Gen.Elements(true, false)
               from constraintAbstractIdx in Gen.Choose(0, ConstraintAbstractNames.Length - 1)
               from constraintMethodIdx in Gen.Choose(0, ConstraintMethodNames.Length - 1)
               select new GenericContextScenario(
                   abstractName,
                   slotName,
                   methodName,
                   typeParamName,
                   position,
                   hasConstraint,
                   hasConstraint ? ConstraintAbstractNames[constraintAbstractIdx] : null,
                   hasConstraint ? ConstraintMethodNames[constraintMethodIdx] : null);
    }

    public static Arbitrary<GenericContextScenario> ArbitraryGenericContextScenario()
    {
        return GenScenario().ToArbitrary();
    }

    // ─── Source generation ───────────────────────────────────────────────

    /// <summary>
    /// Builds valid Mist source code for the scenario. The generated code
    /// declares an abstract type with an associated type slot, optionally a
    /// constraint abstract type for the slot, and a generic function that uses
    /// T.SlotName in the specified type position.
    /// </summary>
    private static string BuildSource(GenericContextScenario scenario)
    {
        var sb = new System.Text.StringBuilder();

        // If the slot has a constraint abstract type, declare it
        if(scenario.HasSlotConstraint)
        {
            _ = sb.AppendLine($"abstract {scenario.SlotConstraintAbstractName} {{");
            _ = sb.AppendLine($"    int {scenario.SlotConstraintMethodName}();");
            _ = sb.AppendLine("}");
            _ = sb.AppendLine();
        }

        // Declare the abstract type with the associated type slot
        _ = sb.AppendLine($"abstract {scenario.AbstractName} {{");
        if(scenario.HasSlotConstraint)
        {
            _ = sb.AppendLine($"    type {scenario.SlotName} : {scenario.SlotConstraintAbstractName};");
        }
        else
        {
            _ = sb.AppendLine($"    type {scenario.SlotName};");
        }

        _ = sb.AppendLine("}");
        _ = sb.AppendLine();

        // Declare a class with a generic method using T.SlotName
        _ = sb.AppendLine("class TestClass {");
        _ = sb.AppendLine(BuildGenericMethod(scenario));
        _ = sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>
    /// Builds the generic method body based on the type position.
    /// </summary>
    private static string BuildGenericMethod(GenericContextScenario scenario)
    {
        var tp = scenario.TypeParamName;
        var slot = $"{tp}.{scenario.SlotName}";
        var constraint = $"where {tp} : {scenario.AbstractName}";

        return scenario.Position switch
        {
            TypePosition.ParameterType =>
                $"    void {scenario.MethodName}<{tp}>(input: {slot}) {constraint} {{\n" +
                "    }",

            TypePosition.ReturnType =>
                $"    {slot} {scenario.MethodName}<{tp}>(input: {tp}) {constraint} {{\n" +
                "    }",

            TypePosition.LocalVariableType =>
                $"    void {scenario.MethodName}<{tp}>(input: {slot}) {constraint} {{\n" +
                $"        {slot} local = input;\n" +
                "    }",

            TypePosition.ParameterAndReturnType =>
                $"    {slot} {scenario.MethodName}<{tp}>(input: {slot}) {constraint} {{\n" +
                $"        return input;\n" +
                "    }",

            _ => throw new ArgumentOutOfRangeException()
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 9: Generic context resolution of associated types
    //
    // **Validates: Requirements 5.1, 5.2, 5.3**
    //
    // For any generic function or method whose type parameter T is
    // constrained by an abstract type declaring associated type slots, references
    // to T.SlotName in any type position (local variable types, parameter
    // types, return types, generic type arguments) SHALL be accepted by the
    // type checker, and operations valid on the slot's constraint list SHALL
    // be permitted on values of type T.SlotName.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AssociatedTypeGenericContextPropertyTests) })]
    public Property GenericContextResolution_AcceptsValidSlotReferences(GenericContextScenario scenario)
    {
        var source = BuildSource(scenario);
        var diagnostics = CompileAndGetAllDiagnostics(source);

        // Filter for diagnostics that indicate the T.SlotName reference was rejected
        var typeErrors = diagnostics.Where(d =>
            d.Id.Value is "MIST_AT_0004" or
            "MIST_UNDEFINED_TYPE").ToList();

        if(typeErrors.Count > 0)
        {
            return false.ToProperty()
                .Label($"Expected no MIST_AT_0004 or MIST_UNDEFINED_TYPE but got {typeErrors.Count}.\n" +
                       $"Scenario: {scenario}\n" +
                       $"Source:\n{source}\n" +
                       $"Type errors: [{string.Join(", ", typeErrors.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
        }

        return true.ToProperty();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 9b: Constrained slot allows member access
    //
    // **Validates: Requirements 5.3**
    //
    // When a slot has a constraint abstract type, operations valid on that
    // constraint (method calls) SHALL be permitted on values of type
    // T.SlotName.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AssociatedTypeGenericContextPropertyTests) })]
    public Property GenericContextResolution_ConstrainedSlotAllowsMemberAccess(GenericContextScenario scenario)
    {
        // Only test scenarios that have a slot constraint — member access is
        // only meaningful when the slot has a constraint abstract type.
        if(!scenario.HasSlotConstraint)
        {
            return true.ToProperty().Label("Skipped: no slot constraint");
        }

        var source = BuildSourceWithMemberAccess(scenario);
        var diagnostics = CompileAndGetAllDiagnostics(source);

        // Filter for diagnostics that indicate the member access was rejected
        var typeErrors = diagnostics.Where(d =>
            d.Id.Value is "MIST_AT_0004" or
            "MIST_UNDEFINED_TYPE" or
            "MIST_SM_0009").ToList();

        if(typeErrors.Count > 0)
        {
            return false.ToProperty()
                .Label($"Expected member access on constrained slot to be accepted but got errors.\n" +
                       $"Scenario: {scenario}\n" +
                       $"Source:\n{source}\n" +
                       $"Errors: [{string.Join(", ", typeErrors.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
        }

        return true.ToProperty();
    }

    /// <summary>
    /// Builds source that exercises member access on a value of type T.SlotName
    /// where the slot is constrained by an abstract type declaring a method.
    /// </summary>
    private static string BuildSourceWithMemberAccess(GenericContextScenario scenario)
    {
        var sb = new System.Text.StringBuilder();
        var tp = scenario.TypeParamName;
        var slot = $"{tp}.{scenario.SlotName}";

        // Declare the constraint abstract type
        _ = sb.AppendLine($"abstract {scenario.SlotConstraintAbstractName} {{");
        _ = sb.AppendLine($"    int {scenario.SlotConstraintMethodName}();");
        _ = sb.AppendLine("}");
        _ = sb.AppendLine();

        // Declare the abstract type with the constrained associated type slot
        _ = sb.AppendLine($"abstract {scenario.AbstractName} {{");
        _ = sb.AppendLine($"    type {scenario.SlotName} : {scenario.SlotConstraintAbstractName};");
        _ = sb.AppendLine("}");
        _ = sb.AppendLine();

        // Declare a class with a generic method that calls the constraint method
        _ = sb.AppendLine("class TestClass {");
        _ = sb.AppendLine($"    int {scenario.MethodName}<{tp}>(input: {slot}) where {tp} : {scenario.AbstractName} {{");
        _ = sb.AppendLine($"        return input.{scenario.SlotConstraintMethodName}();");
        _ = sb.AppendLine("    }");
        _ = sb.AppendLine("}");

        return sb.ToString();
    }
}