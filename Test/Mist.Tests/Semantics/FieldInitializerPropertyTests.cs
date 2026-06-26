using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests for field initializer handling (Bug 2 fix).
///
/// Property 2 (Bug Condition): For any field declaration with an initializer
/// (random primitive types, random field names, with/without static modifier),
/// the class type should be registered in the semantic model and the field
/// symbol should exist.
///
/// Property 5 (Preservation): For any field declaration WITHOUT an initializer
/// (random primitive types, random field names), the class type should be
/// registered and the field symbol should exist — same as before the fix.
/// </summary>
public class FieldInitializerPropertyTests
{
    // ─── Helpers ─────────────────────────────────────────────────────────

    private static Storm.Runtime.Token MakeToken(string lexeme, int start = 0)
    {
        return new Storm.Runtime.Token(
            kind: 0,
            span: new Storm.Runtime.SourceSpan(start, lexeme.Length),
            start: new Storm.Runtime.LinePosition(1, 1),
            end: new Storm.Runtime.LinePosition(1, 1 + lexeme.Length),
            channel: Storm.Runtime.TokenChannel.Default,
            lexeme: lexeme);
    }

    private static MistCompilation MakeCompilation(string source = "")
    {
        var sourceText = new SourceText("test.mist", source);
        return new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
    }

#pragma warning disable CA1822
    private (ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics) BuildModelWithDiagnostics(
        Storm.Runtime.SyntaxNode ast,
        string source = "")
    {
        var compilation = MakeCompilation(source);
        var diagnostics = new DiagnosticBag();
        var builder = new SemanticModelBuilder();
        var model = builder.Build(compilation, ImmutableArray.Create(ast), null, namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);
        return (model, diagnostics.ToImmutableArray());
    }
#pragma warning restore CA1822

    private static PrimitiveTypeNode MakePrimitiveType(string typeName)
    {
        return typeName switch
        {
            "int" => new IntTypePrimitiveTypeNode(),
            "bool" => new BoolTypePrimitiveTypeNode(),
            "string" => new StringTypePrimitiveTypeNode(),
            "double" => new DoubleTypePrimitiveTypeNode(),
            "float" => new FloatTypePrimitiveTypeNode(),
            "char" => new CharTypePrimitiveTypeNode(),
            "long" => new LongTypePrimitiveTypeNode(),
            _ => new IntTypePrimitiveTypeNode(),
        };
    }

    private static ExpressionNode MakeIntLiteralExpression(string value = "0")
    {
        var primary = new IntegerLiteralExpressionPrimaryNode
        {
            IntegerLiteral = MakeToken(value),
        };
        var postfix = new PostfixNode
        {
            Target = primary,
            Suffix = ImmutableArray<PostfixSuffixNode>.Empty,
        };
        var unary = new PostfixExpressionUnaryNode { Primary = postfix };
        var mult = new MultiplicativeNode { Left = unary };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        return new AssignmentFormExpressionNode { Assignment = assign };
    }

    private static FieldDeclarationNode MakeFieldWithInitializer(
        string fieldName,
        PrimitiveTypeNode primitiveType,
        params ModifierNode[] modifiers)
    {
        return new FieldDeclarationNode
        {
            Name = MakeToken(fieldName),
            Type = new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = primitiveType } },
            Initializer = MakeIntLiteralExpression("0"),
            Modifiers = modifiers.Length > 0
                ? ImmutableArray.Create(modifiers)
                : ImmutableArray.Create<ModifierNode>(new PrivateModifierModifierNode()),
        };
    }

    private static FieldDeclarationNode MakeFieldWithoutInitializer(
        string fieldName,
        PrimitiveTypeNode primitiveType,
        params ModifierNode[] modifiers)
    {
        return new FieldDeclarationNode
        {
            Name = MakeToken(fieldName),
            Type = new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = primitiveType } },
            Modifiers = modifiers.Length > 0
                ? ImmutableArray.Create(modifiers)
                : ImmutableArray.Create<ModifierNode>(new PrivateModifierModifierNode()),
        };
    }

    private static CompilationUnitNode MakeClassWithMembers(
        string className,
        params MemberDeclarationNode[] members)
    {
        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken(className),
            Members = ImmutableArray.Create(members),
        };

        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };
    }

    // ─── Test scenario records ───────────────────────────────────────────

    /// <summary>
    /// Represents a test scenario for a field WITH an initializer.
    /// </summary>
#pragma warning disable CA1034
    public record FieldWithInitializerScenario(string FieldType, string FieldName, bool IsStatic)
    {
        public override string ToString() =>
            $"Type={FieldType}, Name={FieldName}, IsStatic={IsStatic}";
    }

    /// <summary>
    /// Represents a test scenario for a field WITHOUT an initializer.
    /// </summary>
    public record FieldWithoutInitializerScenario(string FieldType, string FieldName)
    {
        public override string ToString() =>
            $"Type={FieldType}, Name={FieldName}";
    }
#pragma warning restore CA1034

    // ─── FsCheck Arbitrary ───────────────────────────────────────────────

    private static readonly string[] AvailableTypes =
        ["int", "bool", "string", "double", "float", "char", "long"];

    private static readonly string[] FieldNames =
        ["value", "count", "data", "flag", "total", "name", "result", "score"];

    public static Arbitrary<FieldWithInitializerScenario> ArbitraryFieldWithInitializerScenario()
    {
        var gen = from type in Gen.Elements(AvailableTypes)
                  from name in Gen.Elements(FieldNames)
                  from isStatic in Gen.Elements(true, false)
                  select new FieldWithInitializerScenario(type, name, isStatic);
        return gen.ToArbitrary();
    }

    public static Arbitrary<FieldWithoutInitializerScenario> ArbitraryFieldWithoutInitializerScenario()
    {
        var gen = from type in Gen.Elements(AvailableTypes)
                  from name in Gen.Elements(FieldNames)
                  select new FieldWithoutInitializerScenario(type, name);
        return gen.ToArbitrary();
    }

    // ─── Property 2 (Bug Condition) ─────────────────────────────────────

    /// <summary>
    /// Property 2: For any field declaration with an initializer expression
    /// (random primitive types, random field names, with/without static modifier),
    /// the class type should be registered in the semantic model and the field
    /// symbol should exist.
    ///
    /// **Validates: Requirements 2.2, 2.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(FieldInitializerPropertyTests) }, MaxTest = 50)]
    public bool FieldWithInitializer_ClassTypeRegistered_AndFieldSymbolExists(FieldWithInitializerScenario scenario)
    {
        var modifiers = scenario.IsStatic
            ? new ModifierNode[] { new PrivateModifierModifierNode(), new StaticModifierModifierNode() }
            : new ModifierNode[] { new PrivateModifierModifierNode() };

        var field = MakeFieldWithInitializer(
            scenario.FieldName,
            MakePrimitiveType(scenario.FieldType),
            modifiers);

        var ast = MakeClassWithMembers("TestClass",
            new FieldMemberMemberDeclarationNode { Field = field });

        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        // The class type should be registered in the semantic model
        if(!model.AllSymbols.Any(s => s.Name == "TestClass" && s.Kind == SymbolKind.Class))
        {
            return false;
        }

        // The field symbol should exist
        if(!model.AllSymbols.Any(s => s.Name == scenario.FieldName && s.Kind == SymbolKind.Field))
        {
            return false;
        }

        // No MIST_SEMANTIC_ERROR diagnostics from the field initializer
        if(diagnostics.Any(d => d.Id.Value == "MIST_SEMANTIC_ERROR"))
        {
            return false;
        }

        return true;
    }

    // ─── Property 5 (Preservation) ──────────────────────────────────────

    /// <summary>
    /// Property 5: For any field declaration WITHOUT an initializer
    /// (random primitive types, random field names), the class type should be
    /// registered and the field symbol should exist — same as before the fix.
    /// This is a preservation test verifying the fix did not break existing
    /// field-without-initializer behavior.
    ///
    /// **Validates: Requirements 3.2, 3.5**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(FieldInitializerPropertyTests) }, MaxTest = 50)]
    public bool FieldWithoutInitializer_ClassTypeRegistered_AndFieldSymbolExists(FieldWithoutInitializerScenario scenario)
    {
        var field = MakeFieldWithoutInitializer(
            scenario.FieldName,
            MakePrimitiveType(scenario.FieldType));

        var ast = MakeClassWithMembers("TestClass",
            new FieldMemberMemberDeclarationNode { Field = field });

        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        // The class type should be registered in the semantic model
        if(!model.AllSymbols.Any(s => s.Name == "TestClass" && s.Kind == SymbolKind.Class))
        {
            return false;
        }

        // The field symbol should exist
        if(!model.AllSymbols.Any(s => s.Name == scenario.FieldName && s.Kind == SymbolKind.Field))
        {
            return false;
        }

        // No MIST_SEMANTIC_ERROR diagnostics
        if(diagnostics.Any(d => d.Id.Value == "MIST_SEMANTIC_ERROR"))
        {
            return false;
        }

        return true;
    }
}