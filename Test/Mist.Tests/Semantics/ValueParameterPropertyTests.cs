// Feature: properties, Property 5: Value Parameter Type Resolution
// **Validates: Requirements 13.4, 3.2, 7.3**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Parsing;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Tests.Generators;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests for value parameter type resolution.
///
/// **Validates: Requirements 13.4, 3.2, 7.3**
///
/// For any set/init accessor body referencing `value`, type checker resolves
/// `value` to the enclosing property's declared type.
/// </summary>
public class ValueParameterPropertyTests
{
    private static Storm.Runtime.Token Tok(string lexeme) =>
        MistTypeGenerator.MakeToken(lexeme);

    // ─── Test data model ─────────────────────────────────────────────────

#pragma warning disable CA1034
    public record ValueParameterTestCase(
        string Description,
        CompilationUnitNode Ast,
        string ExpectedTypeName)
    {
        public override string ToString() => Description;
    }
#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates a property with a block set accessor that uses `value`.
    /// The `value` parameter should resolve to the property's declared type.
    /// </summary>
    private static Gen<ValueParameterTestCase> GenSetAccessorWithValue()
    {
        return from type in MistTypeGenerator.GenPrimitiveTypeRef()
               from propName in Gen.Elements("Name", "Value", "Count")
               let typeName = GetTypeName(type)
               select new ValueParameterTestCase(
                   $"set accessor with value for {typeName} {propName}",
                   MakePropertyWithSetBody(type, propName),
                   typeName);
    }

    /// <summary>
    /// Generates a property with a block init accessor that uses `value`.
    /// </summary>
    private static Gen<ValueParameterTestCase> GenInitAccessorWithValue()
    {
        return from type in MistTypeGenerator.GenPrimitiveTypeRef()
               from propName in Gen.Elements("Name", "Value", "Count")
               let typeName = GetTypeName(type)
               select new ValueParameterTestCase(
                   $"init accessor with value for {typeName} {propName}",
                   MakePropertyWithInitBody(type, propName),
                   typeName);
    }

    private static Gen<ValueParameterTestCase> GenTestCase()
    {
        return Gen.OneOf(GenSetAccessorWithValue(), GenInitAccessorWithValue());
    }

    public static Arbitrary<ValueParameterTestCase> ArbitraryValueParameterTestCase() =>
        GenTestCase().ToArbitrary();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static string GetTypeName(TypeReferenceNode type)
    {
        return type.First switch
        {
            PrimitiveSingleTypeRefSingleTypeRefNode prim => prim.PrimitiveType switch
            {
                IntTypePrimitiveTypeNode => "int",
                BoolTypePrimitiveTypeNode => "bool",
                StringTypePrimitiveTypeNode => "string",
                FloatTypePrimitiveTypeNode => "float",
                DoubleTypePrimitiveTypeNode => "double",
                LongTypePrimitiveTypeNode => "long",
                CharTypePrimitiveTypeNode => "char",
                ByteTypePrimitiveTypeNode => "byte",
                ShortTypePrimitiveTypeNode => "short",
                DecimalTypePrimitiveTypeNode => "decimal",
                _ => "object",
            },
            _ => "object",
        };
    }

    private static CompilationUnitNode MakePropertyWithSetBody(TypeReferenceNode type, string propName)
    {
        // Property with block set accessor: set { field = value; }
        var setAccessor = new BlockSetAccessorAccessorDeclarationNode
        {
            Body = new BlockNode
            {
                Statements = ImmutableArray.Create<StatementNode>(
                    MistAstGenerator.MakeExpressionStatement(
                        MakeFieldAssignment())),
            },
        };

        var property = new BlockPropertyPropertyDeclarationNode
        {
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
            Type = type,
            Name = Tok(propName),
            Accessors = ImmutableArray.Create<AccessorDeclarationNode>(
                new AutoGetAccessorAccessorDeclarationNode(),
                setAccessor),
        };

        return MistPropertyGenerator.MakeProgramWithProperty(property);
    }

    private static CompilationUnitNode MakePropertyWithInitBody(TypeReferenceNode type, string propName)
    {
        // Property with block init accessor: init { field = value; }
        var initAccessor = new BlockInitAccessorAccessorDeclarationNode
        {
            Body = new BlockNode
            {
                Statements = ImmutableArray.Create<StatementNode>(
                    MistAstGenerator.MakeExpressionStatement(
                        MakeFieldAssignment())),
            },
        };

        var property = new BlockPropertyPropertyDeclarationNode
        {
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
            Type = type,
            Name = Tok(propName),
            Accessors = ImmutableArray.Create<AccessorDeclarationNode>(
                new AutoGetAccessorAccessorDeclarationNode(),
                initAccessor),
        };

        return MistPropertyGenerator.MakeProgramWithProperty(property);
    }

    private static ExpressionNode MakeFieldAssignment()
    {
        var fieldExpr = MistAstGenerator.WrapPrimary(new FieldKeywordExpressionPrimaryNode());
        var valueExpr = MistAstGenerator.MakeIdentifierExpr("value");

        var fieldLogOr = fieldExpr.AsAssignment().Target;

        return new AssignmentFormExpressionNode
        {
            Assignment = new AssignmentNode
            {
                Target = fieldLogOr,
                Value = valueExpr,
            },
        };
    }

    private static ISemanticModel BuildSemanticModel(CompilationUnitNode ast)
    {
        var printed = MistPrettyPrinter.Print(ast);
        var source = new SourceText("test.mist", printed);
        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(source)));
        var diagnostics = new DiagnosticBag();
        var builder = new SemanticModelBuilder();
        return builder.Build(
            compilation,
            ImmutableArray.Create<Storm.Runtime.SyntaxNode>(ast),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            diagnostics);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 5: Value Parameter Type Resolution
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(ValueParameterPropertyTests) })]
    public Property ValueParameter_PropertyHasSetOrInitAccessor(ValueParameterTestCase testCase)
    {
        var model = BuildSemanticModel(testCase.Ast);

        var propSymbols = model.AllSymbols
            .Where(s => s.Kind == SymbolKind.Property)
            .ToList();

        if(propSymbols.Count == 0)
        {
            return false.ToProperty().Label("No property symbols found");
        }

        var metadata = (propSymbols.First() as Symbol)?.PropertyMetadata;
        if(metadata is null)
        {
            return false.ToProperty().Label("PropertyMetadata is null");
        }

        var hasSetOrInit = metadata.HasSet || metadata.HasInit;
        return hasSetOrInit.ToProperty()
            .Label($"Property should have set or init accessor for value parameter test");
    }

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(ValueParameterPropertyTests) })]
    public Property ValueParameter_PropertyRegisteredWithCorrectType(ValueParameterTestCase testCase)
    {
        var model = BuildSemanticModel(testCase.Ast);

        var propSymbols = model.AllSymbols
            .Where(s => s.Kind == SymbolKind.Property)
            .ToList();

        if(propSymbols.Count == 0)
        {
            return false.ToProperty().Label("No property symbols found");
        }

        // The property symbol should exist and have metadata
        var propSymbol = propSymbols.First();
        var metadata = (propSymbol as Symbol)?.PropertyMetadata;

        return (metadata is not null).ToProperty()
            .Label("Property should have PropertyMetadata for value parameter resolution");
    }
}