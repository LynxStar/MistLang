// Feature: full-accessibility-model, Property 6: Both pipelines resolve the same default for the same position
//
// **Validates: Requirements 5.7**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Model.Types;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using ExperimentalAccessibility = Mist.ProgramModel.Model.Accessibility;
using ExperimentalMistType = Mist.ProgramModel.Model.Types.MistType;
using ProgramModelScope = Mist.ProgramModel.Model.Scope;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests verifying that both the classic and experimental pipelines
/// resolve the same default accessibility for the same declaration position.
///
/// Property 6: For any declaration position with no explicit access modifier, the
/// classic default resolution and the experimental default resolution produce the
/// same Accessibility value.
///
/// The classic pipeline resolves defaults during SemanticModelBuilder construction
/// (via the private GetScopeKindDefault method). The experimental pipeline resolves
/// defaults via ModifierBuilder.GetScopeDefault(MistType? declaringType).
///
/// This test generates modifier-free declarations in various scope kinds, builds the
/// classic semantic model to obtain the classic-resolved value, calls the experimental
/// resolver with the equivalent declaring-type argument, and asserts both produce the
/// same result (compared by ordinal value since the enums are in different namespaces
/// but structurally identical).
///
/// All iterations run in-process with no process spawn.
///
/// **Validates: Requirements 5.7 (Property 6)**
/// </summary>
public class CrossPipelineDefaultAgreementPropertyTests
{
    private static readonly MistLexer Lexer = new();
    private static readonly MistParser Parser = new();
    private static readonly SemanticModelBuilder Builder = new();

    // ─── Scenario type ───────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// Represents a cross-pipeline default agreement scenario: a Mist source with a
    /// modifier-free declaration, the target symbol to inspect, the scope kind that
    /// determines the experimental resolver argument, and a description.
    /// </summary>
    public sealed record CrossPipelineDefaultScenario(
        string Source,
        string TargetSymbolName,
        SymbolKind TargetSymbolKind,
        ScopeKindForExperimental ScopeKind,
        string Description)
    {
        public override string ToString()
        {
            return $"[{Description}] {TargetSymbolName} in {ScopeKind}";
        }
    }

    /// <summary>
    /// The scope kind categories used to select the experimental resolver argument.
    /// </summary>
    public enum ScopeKindForExperimental
    {
        /// <summary>Top-level (no declaring type) — null argument.</summary>
        TopLevel,
        /// <summary>Inside a class — ConcreteType (ClassType) argument.</summary>
        Class,
        /// <summary>Inside a struct — ConcreteType (StructType) argument.</summary>
        Struct,
        /// <summary>Inside an interface (abstract type) — AbstractType argument.</summary>
        Interface,
        /// <summary>Inside an enum — EnumType argument.</summary>
        Enum,
    }

#pragma warning restore CA1034

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static ISemanticModel? BuildModel(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);

        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);

        if(parseResult.Root is null)
        {
            return null;
        }

        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));
        var semBag = new DiagnosticBag();
        var model = Builder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root!),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            semBag);

        return model;
    }

    private static ISymbol? FindSymbol(ISemanticModel model, string name, SymbolKind kind)
    {
        return model.AllSymbols.FirstOrDefault(s => s.Name == name && s.Kind == kind);
    }

    /// <summary>
    /// Calls the experimental pipeline's ModifierBuilder.GetScopeDefault with the
    /// appropriate MistType? argument corresponding to the given scope kind.
    /// </summary>
    private static ExperimentalAccessibility GetExperimentalDefault(ScopeKindForExperimental scopeKind)
    {
        // ModifierBuilder.GetScopeDefault pattern-matches on the runtime type of the
        // declaring MistType?:
        //   null          → Internal (top-level)
        //   ConcreteType  → Private  (class or struct)
        //   AbstractType  → Public   (interface)
        //   EnumType      → Public   (enum)
        //
        // We create minimal instances of the appropriate type to invoke the resolver.
        var globalScope = new ProgramModelScope(null);

        ExperimentalMistType? declaringType = scopeKind switch
        {
            ScopeKindForExperimental.TopLevel => null,
            ScopeKindForExperimental.Class => new ClassType(
                "TestClass", "Test", globalScope, declaringType: null, declarationNode: null),
            ScopeKindForExperimental.Struct => new StructType(
                "TestStruct", "Test", globalScope, declaringType: null, declarationNode: null),
            ScopeKindForExperimental.Interface => new AbstractType(
                "TestInterface", "Test", globalScope, declaringType: null, declarationNode: null),
            ScopeKindForExperimental.Enum => new EnumType(
                "TestEnum", "Test", globalScope, declaringType: null, declarationNode: null),
            _ => null,
        };

        return ModifierBuilder.GetScopeDefault(declaringType);
    }

    // ─── Scenario generators ─────────────────────────────────────────────

    private static readonly string[] TypeNames =
        ["Alpha", "Beta", "Gamma", "Delta", "Epsilon"];

    private static readonly string[] MemberNames =
        ["value", "count", "data", "flag", "item"];

    /// <summary>Top-level type (class) — defaults to Internal.</summary>
    private static Gen<CrossPipelineDefaultScenario> GenTopLevelClass()
    {
        return from name in Gen.Elements(TypeNames)
               select new CrossPipelineDefaultScenario(
                   Source: $@"
class {name} {{
    public static void Main() {{ }}
}}",
                   TargetSymbolName: name,
                   TargetSymbolKind: SymbolKind.Class,
                   ScopeKind: ScopeKindForExperimental.TopLevel,
                   Description: $"Top-level class '{name}'");
    }

    /// <summary>Top-level type (struct) — defaults to Internal.</summary>
    private static Gen<CrossPipelineDefaultScenario> GenTopLevelStruct()
    {
        return from name in Gen.Elements(TypeNames)
               select new CrossPipelineDefaultScenario(
                   Source: $@"
struct {name} {{
}}
class Program {{
    public static void Main() {{ }}
}}",
                   TargetSymbolName: name,
                   TargetSymbolKind: SymbolKind.Struct,
                   ScopeKind: ScopeKindForExperimental.TopLevel,
                   Description: $"Top-level struct '{name}'");
    }

    /// <summary>Class field member — defaults to Private.</summary>
    private static Gen<CrossPipelineDefaultScenario> GenClassFieldMember()
    {
        return from name in Gen.Elements(MemberNames)
               select new CrossPipelineDefaultScenario(
                   Source: $@"
class Container {{
    int {name};
    public static void Main() {{ }}
}}",
                   TargetSymbolName: name,
                   TargetSymbolKind: SymbolKind.Field,
                   ScopeKind: ScopeKindForExperimental.Class,
                   Description: $"Class field '{name}'");
    }

    /// <summary>Class method member — defaults to Private.</summary>
    private static Gen<CrossPipelineDefaultScenario> GenClassMethodMember()
    {
        return from name in Gen.Elements(MemberNames)
               select new CrossPipelineDefaultScenario(
                   Source: $@"
class Container {{
    int {name}() {{
        return 0;
    }}
    public static void Main() {{ }}
}}",
                   TargetSymbolName: name,
                   TargetSymbolKind: SymbolKind.Method,
                   ScopeKind: ScopeKindForExperimental.Class,
                   Description: $"Class method '{name}'");
    }

    /// <summary>Struct field member — defaults to Private.</summary>
    private static Gen<CrossPipelineDefaultScenario> GenStructFieldMember()
    {
        return from name in Gen.Elements(MemberNames)
               select new CrossPipelineDefaultScenario(
                   Source: $@"
struct Container {{
    int {name};
}}
class Program {{
    public static void Main() {{ }}
}}",
                   TargetSymbolName: name,
                   TargetSymbolKind: SymbolKind.Field,
                   ScopeKind: ScopeKindForExperimental.Struct,
                   Description: $"Struct field '{name}'");
    }

    /// <summary>Struct method member — defaults to Private.</summary>
    private static Gen<CrossPipelineDefaultScenario> GenStructMethodMember()
    {
        return from name in Gen.Elements(MemberNames)
               select new CrossPipelineDefaultScenario(
                   Source: $@"
struct Container {{
    int {name}() {{
        return 0;
    }}
}}
class Program {{
    public static void Main() {{ }}
}}",
                   TargetSymbolName: name,
                   TargetSymbolKind: SymbolKind.Method,
                   ScopeKind: ScopeKindForExperimental.Struct,
                   Description: $"Struct method '{name}'");
    }

    /// <summary>Interface (abstract) method member — defaults to Public.</summary>
    private static Gen<CrossPipelineDefaultScenario> GenInterfaceMember()
    {
        return from name in Gen.Elements(MemberNames)
               select new CrossPipelineDefaultScenario(
                   Source: $@"
abstract IContract {{
    int {name}();
}}
class Program {{
    public static void Main() {{ }}
}}",
                   TargetSymbolName: name,
                   TargetSymbolKind: SymbolKind.Method,
                   ScopeKind: ScopeKindForExperimental.Interface,
                   Description: $"Interface method '{name}'");
    }

    /// <summary>Nested class inside a class — defaults to Private.</summary>
    private static Gen<CrossPipelineDefaultScenario> GenNestedClassInClass()
    {
        return from name in Gen.Elements(TypeNames)
               select new CrossPipelineDefaultScenario(
                   Source: $@"
class Outer {{
    class {name} {{
    }}
    public static void Main() {{ }}
}}",
                   TargetSymbolName: name,
                   TargetSymbolKind: SymbolKind.Class,
                   ScopeKind: ScopeKindForExperimental.Class,
                   Description: $"Nested class '{name}' in class");
    }

    /// <summary>Nested struct inside a struct — defaults to Private.</summary>
    private static Gen<CrossPipelineDefaultScenario> GenNestedStructInStruct()
    {
        return from name in Gen.Elements(TypeNames)
               select new CrossPipelineDefaultScenario(
                   Source: $@"
struct Outer {{
    struct {name} {{
    }}
}}
class Program {{
    public static void Main() {{ }}
}}",
                   TargetSymbolName: name,
                   TargetSymbolKind: SymbolKind.Struct,
                   ScopeKind: ScopeKindForExperimental.Struct,
                   Description: $"Nested struct '{name}' in struct");
    }

    // ─── Combined generator ──────────────────────────────────────────────

    private static Gen<CrossPipelineDefaultScenario> GenScenario()
    {
        return Gen.OneOf(
            GenTopLevelClass(),
            GenTopLevelStruct(),
            GenClassFieldMember(),
            GenClassMethodMember(),
            GenStructFieldMember(),
            GenStructMethodMember(),
            GenInterfaceMember(),
            GenNestedClassInClass(),
            GenNestedStructInStruct());
    }

    public static Arbitrary<CrossPipelineDefaultScenario> ArbitraryCrossPipelineDefaultScenario()
    {
        return Arb.From(GenScenario());
    }

    // ─── Property ────────────────────────────────────────────────────────

    /// <summary>
    /// Property 6: Both pipelines resolve the same default for the same position.
    ///
    /// For any modifier-free declaration position, the classic pipeline's resolved
    /// accessibility (via SemanticModelBuilder.GetScopeKindDefault) and the experimental
    /// pipeline's resolved accessibility (via ModifierBuilder.GetScopeDefault) produce
    /// the same value.
    ///
    /// Since the two Accessibility enums are in different namespaces but have the same
    /// values in the same order, we compare by casting to int.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(CrossPipelineDefaultAgreementPropertyTests) })]
    public Property BothPipelines_ResolveSameDefault(
        CrossPipelineDefaultScenario scenario)
    {
        // 1. Build the classic semantic model to get the classic-resolved accessibility.
        var model = BuildModel(scenario.Source);

        if(model is null)
        {
            return false.ToProperty()
                .Label($"Failed to build classic model for: {scenario.Description}\nSource:\n{scenario.Source}");
        }

        var symbol = FindSymbol(model, scenario.TargetSymbolName, scenario.TargetSymbolKind);

        if(symbol is null)
        {
            return false.ToProperty()
                .Label($"Symbol '{scenario.TargetSymbolName}' ({scenario.TargetSymbolKind}) not found in classic model. " +
                       $"Scenario: {scenario.Description}\n" +
                       $"Available symbols: [{string.Join(", ", model.AllSymbols.Select(s => $"{s.Name}:{s.Kind}"))}]");
        }

        var classicValue = symbol.Accessibility;

        // 2. Call the experimental resolver for the same scope kind.
        var experimentalValue = GetExperimentalDefault(scenario.ScopeKind);

        // 3. Compare by ordinal value (both enums have the same members in the same order).
        var classicOrdinal = (int)classicValue;
        var experimentalOrdinal = (int)experimentalValue;

        var agrees = classicOrdinal == experimentalOrdinal;

        return agrees.ToProperty()
            .Label($"Classic resolved {classicValue} (ordinal {classicOrdinal}), " +
                   $"Experimental resolved {experimentalValue} (ordinal {experimentalOrdinal}). " +
                   $"Scenario: {scenario.Description}");
    }
}