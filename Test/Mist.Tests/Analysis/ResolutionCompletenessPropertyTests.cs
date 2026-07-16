// Feature: full-accessibility-model, Property 5: Every declaration carries a concrete resolved accessibility
// **Validates: Requirements 3.1, 5.8, 5.10**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for resolution completeness of accessibility.
///
/// Property 5: For any valid program, after Semantic Model construction completes,
/// every applicable declaration carries a concrete Accessibility enum value, with
/// no declaration retaining an unspecified or default-pending state.
///
/// The test generates Mist source programs with various combinations of explicit
/// and missing access modifiers across different scope kinds (namespace-level types,
/// class members, struct members, interface members, enum members, nested types).
/// It builds the Semantic Model in-process (no process spawn) and walks all symbols
/// to verify each carries one of the four concrete accessibility values.
///
/// **Validates: Requirements 3.1, 5.8, 5.10**
/// </summary>
public class ResolutionCompletenessPropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();
    private static readonly SemanticModelBuilder Builder = new();

    // ─── Valid accessibility values ──────────────────────────────────────

    private static readonly Accessibility[] ValidAccessibilities =
    [
        Accessibility.Public,
        Accessibility.Private,
        Accessibility.Protected,
        Accessibility.Internal,
    ];

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

        // Skip programs with parse errors (they are invalid input for this property).
        if(parseBag.ToImmutableArray().Any(d => d.Severity == DiagnosticSeverity.Error))
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

    /// <summary>
    /// Checks that every symbol in the model carries a valid concrete accessibility value.
    /// Returns a tuple (allValid, description) where description names the first failing symbol.
    /// </summary>
    private static (bool AllValid, string FailureDescription) VerifyAllSymbolsHaveConcreteAccessibility(
        ISemanticModel model)
    {
        foreach(var symbol in model.AllSymbols)
        {
            // Skip symbols that do not carry modifiers (parameters, locals).
            if(symbol.Kind is SymbolKind.Parameter or SymbolKind.LocalVariable or SymbolKind.Namespace)
            {
                continue;
            }

            var accessibility = symbol.Accessibility;
            if(!ValidAccessibilities.Contains(accessibility))
            {
                return (false, $"Symbol '{symbol.Name}' (kind={symbol.Kind}) has invalid " +
                               $"accessibility value: {accessibility} ({(int)accessibility})");
            }
        }

        return (true, string.Empty);
    }

    // ─── Scenario type ───────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// A generated Mist source program with various modifier combinations.
    /// </summary>
    public sealed record MistProgramScenario(string Source, string Description)
    {
        public override string ToString()
        {
            return Description;
        }
    }

#pragma warning restore CA1034

    // ─── Source generators ───────────────────────────────────────────────

    /// <summary>
    /// The four access modifier keywords (plus empty for no modifier).
    /// </summary>
    private static readonly string[] AccessModifiers = ["public", "private", "protected", "internal", ""];

    /// <summary>
    /// The subset of modifiers valid for top-level type declarations.
    /// </summary>
    private static readonly string[] TopLevelModifiers = ["public", "internal", ""];

    /// <summary>
    /// Generates a Mist source program containing a class with members carrying
    /// various combinations of modifiers (or no modifier at all).
    /// </summary>
    private static Gen<MistProgramScenario> GenClassWithMembers()
    {
        return from typeModifier in Gen.Elements(TopLevelModifiers)
               from fieldModifier in Gen.Elements(AccessModifiers)
               from methodModifier in Gen.Elements(AccessModifiers)
               from ctorModifier in Gen.Elements(AccessModifiers)
               let source = BuildClassSource(typeModifier, fieldModifier, methodModifier, ctorModifier)
               select new MistProgramScenario(source,
                   $"Class({typeModifier ?? "default"}) field({fieldModifier ?? "default"}) " +
                   $"method({methodModifier ?? "default"}) ctor({ctorModifier ?? "default"})");
    }

    private static string BuildClassSource(
        string typeModifier, string fieldModifier, string methodModifier, string ctorModifier)
    {
        var typeMod = string.IsNullOrEmpty(typeModifier) ? "" : typeModifier + " ";
        var fieldMod = string.IsNullOrEmpty(fieldModifier) ? "" : fieldModifier + " ";
        var methodMod = string.IsNullOrEmpty(methodModifier) ? "" : methodModifier + " ";
        var ctorMod = string.IsNullOrEmpty(ctorModifier) ? "" : ctorModifier + " ";

        return $$"""
            {{typeMod}}class TestClass
            {
                {{fieldMod}}int value;

                {{ctorMod}}TestClass()
                {
                }

                {{methodMod}}int GetValue()
                {
                    return value;
                }
            }
            """;
    }

    /// <summary>
    /// Generates a Mist source program containing a struct with members carrying
    /// various combinations of modifiers (or no modifier at all).
    /// </summary>
    private static Gen<MistProgramScenario> GenStructWithMembers()
    {
        return from typeModifier in Gen.Elements(TopLevelModifiers)
               from fieldModifier in Gen.Elements(AccessModifiers)
               from methodModifier in Gen.Elements(AccessModifiers)
               let source = BuildStructSource(typeModifier, fieldModifier, methodModifier)
               select new MistProgramScenario(source,
                   $"Struct({typeModifier ?? "default"}) field({fieldModifier ?? "default"}) " +
                   $"method({methodModifier ?? "default"})");
    }

    private static string BuildStructSource(
        string typeModifier, string fieldModifier, string methodModifier)
    {
        var typeMod = string.IsNullOrEmpty(typeModifier) ? "" : typeModifier + " ";
        var fieldMod = string.IsNullOrEmpty(fieldModifier) ? "" : fieldModifier + " ";
        var methodMod = string.IsNullOrEmpty(methodModifier) ? "" : methodModifier + " ";

        return $$"""
            {{typeMod}}struct TestStruct
            {
                {{fieldMod}}int x;

                {{methodMod}}int GetX()
                {
                    return x;
                }
            }
            """;
    }

    /// <summary>
    /// Generates a Mist source program containing an abstract type (interface)
    /// with members that default to Public.
    /// </summary>
    private static Gen<MistProgramScenario> GenAbstractTypeWithMembers()
    {
        return from typeModifier in Gen.Elements(TopLevelModifiers)
               from methodModifier in Gen.Elements("public", "")
               let source = BuildAbstractSource(typeModifier, methodModifier)
               select new MistProgramScenario(source,
                   $"Abstract({typeModifier ?? "default"}) method({methodModifier ?? "default"})");
    }

    private static string BuildAbstractSource(string typeModifier, string methodModifier)
    {
        var typeMod = string.IsNullOrEmpty(typeModifier) ? "" : typeModifier + " ";
        var methodMod = string.IsNullOrEmpty(methodModifier) ? "" : methodModifier + " ";

        return $$"""
            {{typeMod}}abstract ITestable
            {
                {{methodMod}}int Compute();
            }
            """;
    }

    /// <summary>
    /// Generates a Mist source program containing a class with a nested class,
    /// testing nested type accessibility resolution.
    /// </summary>
    private static Gen<MistProgramScenario> GenNestedTypes()
    {
        return from outerModifier in Gen.Elements(TopLevelModifiers)
               from nestedModifier in Gen.Elements(AccessModifiers)
               from memberModifier in Gen.Elements(AccessModifiers)
               let source = BuildNestedSource(outerModifier, nestedModifier, memberModifier)
               select new MistProgramScenario(source,
                   $"Outer({outerModifier ?? "default"}) Nested({nestedModifier ?? "default"}) " +
                   $"member({memberModifier ?? "default"})");
    }

    private static string BuildNestedSource(
        string outerModifier, string nestedModifier, string memberModifier)
    {
        var outerMod = string.IsNullOrEmpty(outerModifier) ? "" : outerModifier + " ";
        var nestedMod = string.IsNullOrEmpty(nestedModifier) ? "" : nestedModifier + " ";
        var memberMod = string.IsNullOrEmpty(memberModifier) ? "" : memberModifier + " ";

        return $$"""
            {{outerMod}}class Outer
            {
                {{nestedMod}}class Inner
                {
                    {{memberMod}}int data;
                }
            }
            """;
    }

    /// <summary>
    /// Generates a Mist source program containing a sealed class.
    /// </summary>
    private static Gen<MistProgramScenario> GenSealedClass()
    {
        return from typeModifier in Gen.Elements(TopLevelModifiers)
               from fieldModifier in Gen.Elements("public", "private", "internal", "")
               let source = BuildSealedClassSource(typeModifier, fieldModifier)
               select new MistProgramScenario(source,
                   $"SealedClass({typeModifier ?? "default"}) field({fieldModifier ?? "default"})");
    }

    private static string BuildSealedClassSource(string typeModifier, string fieldModifier)
    {
        var typeMod = string.IsNullOrEmpty(typeModifier) ? "" : typeModifier + " ";
        var fieldMod = string.IsNullOrEmpty(fieldModifier) ? "" : fieldModifier + " ";

        return $$"""
            {{typeMod}}sealed class SealedItem
            {
                {{fieldMod}}int id;

                public SealedItem()
                {
                }
            }
            """;
    }

    /// <summary>
    /// Generates a Mist source program with multiple types in the same compilation,
    /// combining classes, structs, and abstract types.
    /// </summary>
    private static Gen<MistProgramScenario> GenMultiTypeProgram()
    {
        return from classMod in Gen.Elements(TopLevelModifiers)
               from structMod in Gen.Elements(TopLevelModifiers)
               from abstractMod in Gen.Elements(TopLevelModifiers)
               from fieldMod in Gen.Elements(AccessModifiers)
               let source = BuildMultiTypeSource(classMod, structMod, abstractMod, fieldMod)
               select new MistProgramScenario(source,
                   $"Multi: class({classMod ?? "default"}) struct({structMod ?? "default"}) " +
                   $"abstract({abstractMod ?? "default"}) field({fieldMod ?? "default"})");
    }

    private static string BuildMultiTypeSource(
        string classMod, string structMod, string abstractMod, string fieldMod)
    {
        var cm = string.IsNullOrEmpty(classMod) ? "" : classMod + " ";
        var sm = string.IsNullOrEmpty(structMod) ? "" : structMod + " ";
        var am = string.IsNullOrEmpty(abstractMod) ? "" : abstractMod + " ";
        var fm = string.IsNullOrEmpty(fieldMod) ? "" : fieldMod + " ";

        return $$"""
            {{cm}}class MyClass
            {
                {{fm}}int value;
            }

            {{sm}}struct MyStruct
            {
                {{fm}}int x;
            }

            {{am}}abstract IMyAbstract
            {
                int DoSomething();
            }
            """;
    }

    // ─── Combined generator ──────────────────────────────────────────────

    private static Gen<MistProgramScenario> GenProgram()
    {
        return Gen.OneOf(
            GenClassWithMembers(),
            GenStructWithMembers(),
            GenAbstractTypeWithMembers(),
            GenNestedTypes(),
            GenSealedClass(),
            GenMultiTypeProgram());
    }

    // ─── Arbitrary registration ──────────────────────────────────────────

    public static Arbitrary<MistProgramScenario> ArbitraryMistProgramScenario()
    {
        return GenProgram().ToArbitrary();
    }

    // ─── Property ────────────────────────────────────────────────────────

    /// <summary>
    /// Property 5: Every declaration carries a concrete resolved accessibility.
    ///
    /// For any valid Mist program, after Semantic Model construction completes,
    /// every applicable declaration carries a concrete Accessibility enum value
    /// (Public, Private, Protected, or Internal) with no declaration retaining
    /// an unspecified or default-pending state.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ResolutionCompletenessPropertyTests) })]
    public Property EveryDeclaration_CarriesConcreteAccessibility(MistProgramScenario scenario)
    {
        var model = BuildModel(scenario.Source);

        if(model is null)
        {
            // Program failed to parse — discard this input (it's invalid).
            return true.ToProperty().Label("Discarded: parse failure");
        }

        var (allValid, failureDescription) = VerifyAllSymbolsHaveConcreteAccessibility(model);

        return allValid.ToProperty()
            .Label(string.IsNullOrEmpty(failureDescription)
                ? $"All symbols have concrete accessibility. Source: {scenario.Description}"
                : $"FAILURE: {failureDescription}. Source:\n{scenario.Source}");
    }
}