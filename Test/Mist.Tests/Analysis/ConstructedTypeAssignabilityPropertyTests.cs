// Feature: generics-type-arguments, Property 10: Constructed type assignability
// **Validates: Requirements 11.1, 11.3, 11.4**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests verifying constructed type assignability.
///
/// Property 10: For any two ConstructedType instances built from the same generic
/// definition with the same type arguments, assignment from one to the other SHALL
/// be accepted by the type checker. Additionally, if a ConstructedType inherits from
/// another ConstructedType (e.g., OrderedList&lt;int&gt; extends List&lt;int&gt;),
/// assignment from the derived to the base SHALL be accepted. If a ConstructedType
/// implements a constructed interface (e.g., Box&lt;int&gt; implements IContainer&lt;int&gt;),
/// assignment from the implementing type to the interface SHALL be accepted.
/// </summary>
public class ConstructedTypeAssignabilityPropertyTests
{
    private readonly SemanticModelBuilder _builder = new();
    private readonly TypeCheckerAnalyzer _analyzer = new();

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

    private ISemanticModel BuildModel(Storm.Runtime.SyntaxNode ast, string source = "")
    {
        var compilation = MakeCompilation(source);
        var diagnostics = new DiagnosticBag();
        return _builder.Build(compilation, ImmutableArray.Create(ast), ambientTypeResolver: null, namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);
    }

    private ImmutableArray<Diagnostic> RunTypeChecker(ISemanticModel model)
    {
        var bag = new DiagnosticBag();
        _analyzer.Analyze(model, bag);
        return bag.ToImmutableArray();
    }

    // ─── AST construction helpers ────────────────────────────────────────

    private static TypeParameterListNode MakeTypeParamList(string[] names)
    {
        var first = new RegularTypeParameterTypeParameterNode { Name = MakeToken(names[0]) };
        var rest = names.Skip(1)
            .Select(n => (TypeParameterNode)new RegularTypeParameterTypeParameterNode { Name = MakeToken(n) })
            .ToImmutableArray();
        return new TypeParameterListNode
        {
            TypeParameter = first,
            TypeParameters = rest,
        };
    }

    private static TypeArgumentListNode MakeTypeArgList(TypeReferenceNode[] typeRefs)
    {
        return new TypeArgumentListNode
        {
            TypeArgument = new TypeReferenceTypeArgumentTypeArgumentNode { Type = typeRefs[0] },
            TypeArguments = typeRefs.Skip(1).Select(t => (TypeArgumentNode)new TypeReferenceTypeArgumentTypeArgumentNode { Type = t }).ToImmutableArray(),
        };
    }

    private static TypeReferenceNode MakeNamedTypeRef(string name, TypeArgumentListNode? typeArgs = null)
    {
        return new TypeReferenceNode { First = new NamedSingleTypeRefSingleTypeRefNode
        {
            Name = new QualifiedIdentifierNode { IDENTIFIER = MakeToken(name) },
            TypeArgs = typeArgs,
        } };
    }

    private static TypeReferenceNode MakePrimitiveTypeRef(string primitiveName)
    {
        PrimitiveTypeNode primitiveNode = primitiveName switch
        {
            "int" => new IntTypePrimitiveTypeNode(),
            "string" => new StringTypePrimitiveTypeNode(),
            "bool" => new BoolTypePrimitiveTypeNode(),
            "float" => new FloatTypePrimitiveTypeNode(),
            _ => new IntTypePrimitiveTypeNode(),
        };
        return new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = primitiveNode } };
    }

    private static ExpressionNode WrapPrimaryAsExpression(PrimaryNode primary)
    {
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
        return new ExpressionNode { Assignment = assign };
    }

    private static ExpressionNode MakeNewGenericObjectExpression(string typeName, TypeArgumentListNode typeArgs)
    {
        var primary = new ObjectCreationExpressionPrimaryNode
        {
            Type = new QualifiedIdentifierNode
            {
                IDENTIFIER = MakeToken(typeName),
                Span = new Storm.Runtime.SourceSpan(0, typeName.Length),
            },
            TypeArgs = typeArgs,
            Arguments = null,
        };
        return WrapPrimaryAsExpression(primary);
    }

    private static ExpressionNode MakeIdentifierExpression(string name)
    {
        var primary = new IdentifierExpressionPrimaryNode
        {
            Name = MakeToken(name),
        };
        return WrapPrimaryAsExpression(primary);
    }

    private static StatementNode MakeTypedVarDecl(TypeReferenceNode typeRef, string varName, ExpressionNode initializer)
    {
        return new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = typeRef,
                Name = MakeToken(varName),
                Initializer = initializer,
            },
        };
    }

    private static BaseListNode MakeBaseList(BaseTypeRefNode[] entries)
    {
        return new BaseListNode
        {
            First = entries[0],
            Rest = entries[0],
            BaseTypeRefs = entries.Skip(1).ToImmutableArray(),
        };
    }

    private static BaseTypeRefNode MakeBaseTypeRef(string name, TypeArgumentListNode? typeArgs = null)
    {
        return new BaseTypeRefNode { Name = MakeToken(name), TypeArgs = typeArgs };
    }

    // ─── Test data models ────────────────────────────────────────────────

#pragma warning disable CA1034

    public enum AssignabilityKind
    {
        /// <summary>Same constructed type: Box&lt;int&gt; a = new Box&lt;int&gt;();</summary>
        SameConstructedType,
        /// <summary>Derived to base: Derived&lt;int&gt; d = ...; Base&lt;int&gt; b = d;</summary>
        DerivedToBase,
        /// <summary>Implementing type to interface: Box&lt;int&gt; b = ...; IContainer&lt;int&gt; c = b;</summary>
        ImplementsInterface,
    }

    /// <summary>
    /// Scenario for testing constructed type assignability.
    /// </summary>
    public record AssignabilityScenario(
        AssignabilityKind Kind,
        string GenericTypeName,
        string ConcreteType,
        string? DerivedTypeName,
        string? InterfaceName)
    {
        public override string ToString()
        {
            return Kind switch
            {
                AssignabilityKind.SameConstructedType =>
                    $"{GenericTypeName}<{ConcreteType}> a = new {GenericTypeName}<{ConcreteType}>();",
                AssignabilityKind.DerivedToBase =>
                    $"{DerivedTypeName}<{ConcreteType}> d = ...; {GenericTypeName}<{ConcreteType}> b = d;",
                AssignabilityKind.ImplementsInterface =>
                    $"{GenericTypeName}<{ConcreteType}> b = ...; {InterfaceName}<{ConcreteType}> i = b;",
                _ => "unknown",
            };
        }
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] GenericNamePool = ["Box", "Container", "Wrapper"];
    private static readonly string[] DerivedNamePool = ["SpecialBox", "ExtendedContainer", "SubWrapper"];
    private static readonly string[] InterfaceNamePool = ["IHolder", "IStore", "IKeeper"];
    private static readonly string[] PrimitivePool = ["int", "string", "bool", "float"];

    public static Gen<AssignabilityScenario> GenAssignabilityScenario()
    {
        return Gen.OneOf(
            GenSameConstructedType(),
            GenDerivedToBase(),
            GenImplementsInterface());
    }

    private static Gen<AssignabilityScenario> GenSameConstructedType()
    {
        return from genericName in Gen.Elements(GenericNamePool)
               from concreteType in Gen.Elements(PrimitivePool)
               select new AssignabilityScenario(
                   AssignabilityKind.SameConstructedType,
                   genericName, concreteType, null, null);
    }

    private static Gen<AssignabilityScenario> GenDerivedToBase()
    {
        return from genericName in Gen.Elements(GenericNamePool)
               from derivedName in Gen.Elements(DerivedNamePool)
               from concreteType in Gen.Elements(PrimitivePool)
               select new AssignabilityScenario(
                   AssignabilityKind.DerivedToBase,
                   genericName, concreteType, derivedName, null);
    }

    private static Gen<AssignabilityScenario> GenImplementsInterface()
    {
        return from genericName in Gen.Elements(GenericNamePool)
               from interfaceName in Gen.Elements(InterfaceNamePool)
               from concreteType in Gen.Elements(PrimitivePool)
               select new AssignabilityScenario(
                   AssignabilityKind.ImplementsInterface,
                   genericName, concreteType, null, interfaceName);
    }

    public static Arbitrary<AssignabilityScenario> ArbitraryAssignabilityScenario() =>
        GenAssignabilityScenario().ToArbitrary();

    // ─── AST construction ────────────────────────────────────────────────

    private static CompilationUnitNode BuildAst(AssignabilityScenario scenario)
    {
        return scenario.Kind switch
        {
            AssignabilityKind.SameConstructedType => BuildSameConstructedTypeAst(scenario),
            AssignabilityKind.DerivedToBase => BuildDerivedToBaseAst(scenario),
            AssignabilityKind.ImplementsInterface => BuildImplementsInterfaceAst(scenario),
            _ => throw new InvalidOperationException(),
        };
    }

    /// <summary>
    /// Builds: class GenericName&lt;T&gt; { }
    ///         class Program { static void Main() { GenericName&lt;concrete&gt; a = new GenericName&lt;concrete&gt;(); GenericName&lt;concrete&gt; b = a; } }
    /// </summary>
    private static CompilationUnitNode BuildSameConstructedTypeAst(AssignabilityScenario scenario)
    {
        var typeDecls = new List<TypeDeclarationNode>();

        // Generic class
        var genericClassDecl = new ClassDeclarationNode
        {
            Name = MakeToken(scenario.GenericTypeName),
            TypeParams = MakeTypeParamList(["T"]),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };
        typeDecls.Add(new ClassTypeTypeDeclarationNode { ClassDeclaration = genericClassDecl });

        // Main body
        var typeArgList = MakeTypeArgList([MakePrimitiveTypeRef(scenario.ConcreteType)]);
        var constructedTypeRef = MakeNamedTypeRef(scenario.GenericTypeName, typeArgList);
        var newExpr = MakeNewGenericObjectExpression(scenario.GenericTypeName, typeArgList);

        // GenericName<concrete> a = new GenericName<concrete>();
        var varDeclA = MakeTypedVarDecl(constructedTypeRef, "a", newExpr);

        // GenericName<concrete> b = a;
        var typeArgList2 = MakeTypeArgList([MakePrimitiveTypeRef(scenario.ConcreteType)]);
        var constructedTypeRef2 = MakeNamedTypeRef(scenario.GenericTypeName, typeArgList2);
        var varDeclB = MakeTypedVarDecl(constructedTypeRef2, "b", MakeIdentifierExpression("a"));

        var mainStatements = ImmutableArray.Create(varDeclA, varDeclB);
        typeDecls.Add(new ClassTypeTypeDeclarationNode { ClassDeclaration = MakeProgramClass(mainStatements) });

        return new CompilationUnitNode { Items = (typeDecls.ToImmutableArray()).AsTopLevelItems() };
    }

    /// <summary>
    /// Builds: class Base&lt;T&gt; { }
    ///         class Derived&lt;T&gt; : Base&lt;T&gt; { }
    ///         class Program { static void Main() { Derived&lt;concrete&gt; d = new Derived&lt;concrete&gt;(); Base&lt;concrete&gt; b = d; } }
    /// </summary>
    private static CompilationUnitNode BuildDerivedToBaseAst(AssignabilityScenario scenario)
    {
        var typeDecls = new List<TypeDeclarationNode>();

        // Base generic class
        var baseClassDecl = new ClassDeclarationNode
        {
            Name = MakeToken(scenario.GenericTypeName),
            TypeParams = MakeTypeParamList(["T"]),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };
        typeDecls.Add(new ClassTypeTypeDeclarationNode { ClassDeclaration = baseClassDecl });

        // Derived generic class: Derived<T> : Base<T>
        var baseTypeArgList = MakeTypeArgList([MakeNamedTypeRef("T")]);
        var baseList = MakeBaseList([MakeBaseTypeRef(scenario.GenericTypeName, baseTypeArgList)]);
        var derivedClassDecl = new ClassDeclarationNode
        {
            Name = MakeToken(scenario.DerivedTypeName!),
            TypeParams = MakeTypeParamList(["T"]),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
            BaseClause = baseList,
            BaseList = baseList,
        };
        typeDecls.Add(new ClassTypeTypeDeclarationNode { ClassDeclaration = derivedClassDecl });

        // Main body
        var derivedTypeArgList = MakeTypeArgList([MakePrimitiveTypeRef(scenario.ConcreteType)]);
        var derivedTypeRef = MakeNamedTypeRef(scenario.DerivedTypeName!, derivedTypeArgList);
        var newDerivedExpr = MakeNewGenericObjectExpression(scenario.DerivedTypeName!, derivedTypeArgList);

        // Derived<concrete> d = new Derived<concrete>();
        var varDeclD = MakeTypedVarDecl(derivedTypeRef, "d", newDerivedExpr);

        // Base<concrete> b = d;
        var baseTypeArgList2 = MakeTypeArgList([MakePrimitiveTypeRef(scenario.ConcreteType)]);
        var baseTypeRef = MakeNamedTypeRef(scenario.GenericTypeName, baseTypeArgList2);
        var varDeclB = MakeTypedVarDecl(baseTypeRef, "b", MakeIdentifierExpression("d"));

        var mainStatements = ImmutableArray.Create(varDeclD, varDeclB);
        typeDecls.Add(new ClassTypeTypeDeclarationNode { ClassDeclaration = MakeProgramClass(mainStatements) });

        return new CompilationUnitNode { Items = (typeDecls.ToImmutableArray()).AsTopLevelItems() };
    }

    /// <summary>
    /// Builds: interface IName&lt;T&gt; { }
    ///         class GenericName&lt;T&gt; : IName&lt;T&gt; { }
    ///         class Program { static void Main() { GenericName&lt;concrete&gt; b = new GenericName&lt;concrete&gt;(); IName&lt;concrete&gt; i = b; } }
    /// </summary>
    private static CompilationUnitNode BuildImplementsInterfaceAst(AssignabilityScenario scenario)
    {
        var typeDecls = new List<TypeDeclarationNode>();

        // Generic interface
        var interfaceDecl = new InterfaceDeclarationNode
        {
            Name = MakeToken(scenario.InterfaceName!),
            TypeParams = MakeTypeParamList(["T"]),
            Members = ImmutableArray<InterfaceMemberDeclarationNode>.Empty,
        };
        typeDecls.Add(new InterfaceTypeTypeDeclarationNode { InterfaceDeclaration = interfaceDecl });

        // Generic class implementing the interface: GenericName<T> : IName<T>
        var ifaceTypeArgList = MakeTypeArgList([MakeNamedTypeRef("T")]);
        var baseList = MakeBaseList([MakeBaseTypeRef(scenario.InterfaceName!, ifaceTypeArgList)]);
        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken(scenario.GenericTypeName),
            TypeParams = MakeTypeParamList(["T"]),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
            BaseClause = baseList,
            BaseList = baseList,
        };
        typeDecls.Add(new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl });

        // Main body
        var classTypeArgList = MakeTypeArgList([MakePrimitiveTypeRef(scenario.ConcreteType)]);
        var classTypeRef = MakeNamedTypeRef(scenario.GenericTypeName, classTypeArgList);
        var newClassExpr = MakeNewGenericObjectExpression(scenario.GenericTypeName, classTypeArgList);

        // GenericName<concrete> b = new GenericName<concrete>();
        var varDeclB = MakeTypedVarDecl(classTypeRef, "b", newClassExpr);

        // IName<concrete> i = b;
        var ifaceTypeArgList2 = MakeTypeArgList([MakePrimitiveTypeRef(scenario.ConcreteType)]);
        var ifaceTypeRef = MakeNamedTypeRef(scenario.InterfaceName!, ifaceTypeArgList2);
        var varDeclI = MakeTypedVarDecl(ifaceTypeRef, "i", MakeIdentifierExpression("b"));

        var mainStatements = ImmutableArray.Create(varDeclB, varDeclI);
        typeDecls.Add(new ClassTypeTypeDeclarationNode { ClassDeclaration = MakeProgramClass(mainStatements) });

        return new CompilationUnitNode { Items = (typeDecls.ToImmutableArray()).AsTopLevelItems() };
    }

    private static ClassDeclarationNode MakeProgramClass(ImmutableArray<StatementNode> mainStatements)
    {
        var mainMethod = new MethodDeclarationNode
        {
            Name = MakeToken("Main"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = mainStatements } },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };

        return new ClassDeclarationNode
        {
            Name = MakeToken("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
        };
    }

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 10: For any two ConstructedType instances built from the same generic
    /// definition with the same type arguments, assignment from one to the other SHALL
    /// be accepted. Derived-to-base and interface implementation assignments SHALL also
    /// be accepted.
    ///
    /// **Validates: Requirements 11.1, 11.3, 11.4**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(ConstructedTypeAssignabilityPropertyTests) }, MaxTest = 100)]
    public bool ConstructedTypeAssignment_ProducesNoTypeMismatchDiagnostic(AssignabilityScenario scenario)
    {
        var ast = BuildAst(scenario);
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        // Filter to MIST_TC_0001 diagnostics (type mismatch / cannot assign)
        var typeMismatchDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_TC_0001")
            .ToList();

        // No type mismatch diagnostic should be emitted for valid constructed type assignments
        return typeMismatchDiags.Count == 0;
    }
}
