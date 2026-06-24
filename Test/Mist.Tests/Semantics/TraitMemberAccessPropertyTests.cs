// Feature: traits, Property 7: Trait member access
// **Validates: Requirements 8.1, 8.2, 8.3**

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
using Mist.Semantics.Model.Types;
using Mist.Tests.Generators;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests verifying trait member access through LookupMemberType.
///
/// Property 7: For any trait-typed variable and for any member name, if the member
/// name exists in the trait's member scope, LookupMemberType SHALL return the
/// member's declared type without emitting a diagnostic. If the member name does
/// not exist in the trait's member scope, LookupMemberType SHALL emit a
/// MIST_UNDEFINED_MEMBER diagnostic.
/// </summary>
public class TraitMemberAccessPropertyTests
{
    private readonly SemanticModelBuilder _builder = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static Storm.Runtime.Token Tok(string lexeme) =>
        MistTypeGenerator.MakeToken(lexeme);

    private static MistCompilation MakeCompilation(string source = "")
    {
        var sourceText = new SourceText("test.mist", source);
        return new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
    }

    private (ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics) BuildModelWithDiagnostics(
        Storm.Runtime.SyntaxNode ast,
        string source = "")
    {
        var compilation = MakeCompilation(source);
        var diagnostics = new DiagnosticBag();
        var model = _builder.Build(
            compilation,
            ImmutableArray.Create(ast),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            diagnostics);
        return (model, diagnostics.ToImmutableArray());
    }

    private static TypeReferenceNode MakePrimitiveTypeRef(string typeName)
    {
        return typeName switch
        {
            "int" => new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode
            {
                PrimitiveType = new IntTypePrimitiveTypeNode(),
            } },
            "string" => new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode
            {
                PrimitiveType = new StringTypePrimitiveTypeNode(),
            } },
            "bool" => new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode
            {
                PrimitiveType = new BoolTypePrimitiveTypeNode(),
            } },
            _ => new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode
            {
                PrimitiveType = new IntTypePrimitiveTypeNode(),
            } },
        };
    }

    // ─── Expression / statement builders ─────────────────────────────────

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
        return new AssignmentFormExpressionNode { Assignment = assign };
    }

    private static ExpressionNode MakeNewObjectExpression(string typeName)
    {
        var primary = new ObjectCreationExpressionPrimaryNode
        {
            Type = new QualifiedIdentifierNode
            {
                IDENTIFIER = Tok(typeName),
                Span = new Storm.Runtime.SourceSpan(0, typeName.Length),
            },
            Arguments = null,
        };
        return WrapPrimaryAsExpression(primary);
    }

    /// <summary>
    /// Creates a member access expression: varName.memberName
    /// </summary>
    private static ExpressionNode MakeMemberAccessExpression(string varName, string memberName)
    {
        var primary = new IdentifierExpressionPrimaryNode
        {
            Name = Tok(varName),
        };
        var suffix = new MemberAccessSuffixPostfixSuffixNode
        {
            Name = Tok(memberName),
        };
        var postfix = new PostfixNode
        {
            Target = primary,
            Suffix = ImmutableArray.Create<PostfixSuffixNode>(suffix),
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

    /// <summary>
    /// Creates: TraitName varName = new ClassName();
    /// </summary>
    private static StatementNode MakeNamedTypeVarDecl(
        string declaredTypeName, string varName, string initializedTypeName)
    {
        return new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = new TypeReferenceNode { First = new NamedSingleTypeRefSingleTypeRefNode
                {
                    Name = new QualifiedIdentifierNode
                    {
                        IDENTIFIER = Tok(declaredTypeName),
                        Span = new Storm.Runtime.SourceSpan(0, declaredTypeName.Length),
                    },
                } },
                Name = Tok(varName),
                Initializer = MakeNewObjectExpression(initializedTypeName),
            },
        };
    }

    private static StatementNode MakeExpressionStatement(ExpressionNode expr)
    {
        return new ExpressionStatementStatementNode
        {
            ExpressionStatement = new ExpressionStatementNode
            {
                Expression = expr,
            },
        };
    }

    private static MethodDeclarationNode MakeMethodDecl(
        string name,
        ImmutableArray<StatementNode>? bodyStatements = null,
        bool isStatic = false)
    {
        var modifiers = new List<ModifierNode> { new PublicModifierModifierNode() };
        if (isStatic)
            modifiers.Add(new StaticModifierModifierNode());

        return new MethodDeclarationNode
        {
            Name = Tok(name),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Body = new MethodBlockBodyMethodBodyNode
            {
                Block = new BlockNode
                {
                    Statements = bodyStatements ?? ImmutableArray<StatementNode>.Empty,
                },
            },
            Modifiers = modifiers.ToImmutableArray(),
        };
    }

    private static MemberDeclarationNode WrapMethod(MethodDeclarationNode method) =>
        new MethodMemberMemberDeclarationNode { Method = method };

    // ─── AST construction helpers ────────────────────────────────────────

    /// <summary>
    /// Creates a class with public field members that structurally satisfy a trait.
    /// </summary>
    private static ClassDeclarationNode MakeClassWithPublicFields(
        string className, params (string typeName, string fieldName)[] fields)
    {
        var members = fields.Select(f =>
            (MemberDeclarationNode)new FieldMemberMemberDeclarationNode
            {
                Field = new FieldDeclarationNode
                {
                    Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
                    Type = MakePrimitiveTypeRef(f.typeName),
                    Name = Tok(f.fieldName),
                },
            }).ToImmutableArray();

        return new ClassDeclarationNode { Name = Tok(className), Members = members };
    }

    /// <summary>
    /// Creates a trait with field members.
    /// </summary>
    private static TraitDeclarationNode MakeTraitWithFields(
        string traitName, params (string typeName, string fieldName)[] fields)
    {
        var members = fields.Select(f =>
            (TraitMemberDeclarationNode)new TraitFieldMemberTraitMemberDeclarationNode
            {
                Field = new TraitFieldDeclarationNode
                {
                    Type = MakePrimitiveTypeRef(f.typeName),
                    Name = Tok(f.fieldName),
                },
            }).ToImmutableArray();

        return new TraitDeclarationNode { Name = Tok(traitName), Members = members };
    }

    /// <summary>
    /// Creates a trait with method members.
    /// </summary>
    private static TraitDeclarationNode MakeTraitWithMethods(
        string traitName,
        params (string returnType, string methodName, (string type, string name)[]? parameters)[] methods)
    {
        var members = methods.Select(m =>
        {
            ParameterListNode? paramList = null;
            if (m.parameters is { Length: > 0 })
            {
                var first = m.parameters[0];
                paramList = new ParameterListNode
                {
                    Parameter = new ParameterNode
                    {
                        Type = MakePrimitiveTypeRef(first.type),
                        Name = Tok(first.name),
                    },
                };
            }

            return (TraitMemberDeclarationNode)new TraitMethodMemberTraitMemberDeclarationNode
            {
                Method = new TraitMethodDeclarationNode
                {
                    ReturnType = m.returnType == "void"
                        ? new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() }
                        : MakePrimitiveTypeRef(m.returnType),
                    Name = Tok(m.methodName),
                    Parameters = paramList,
                },
            };
        }).ToImmutableArray();

        return new TraitDeclarationNode { Name = Tok(traitName), Members = members };
    }

    /// <summary>
    /// Creates a trait with property members.
    /// </summary>
    private static TraitDeclarationNode MakeTraitWithProperties(
        string traitName,
        params (string typeName, string propName, bool hasGet, bool hasSet)[] properties)
    {
        var members = properties.Select(p =>
        {
            var accessors = new List<AbstractAccessorDeclarationNode>();
            if (p.hasGet)
                accessors.Add(new AbstractGetAccessorAbstractAccessorDeclarationNode());
            if (p.hasSet)
                accessors.Add(new AbstractSetAccessorAbstractAccessorDeclarationNode());

            return (TraitMemberDeclarationNode)new TraitPropertyMemberTraitMemberDeclarationNode
            {
                Property = new TraitPropertyDeclarationNode
                {
                    Type = MakePrimitiveTypeRef(p.typeName),
                    Name = Tok(p.propName),
                    Accessors = accessors.ToImmutableArray(),
                },
            };
        }).ToImmutableArray();

        return new TraitDeclarationNode { Name = Tok(traitName), Members = members };
    }

    /// <summary>
    /// Creates a class with public methods.
    /// </summary>
    private static ClassDeclarationNode MakeClassWithPublicMethods(
        string className,
        params (string returnType, string methodName, (string type, string name)[]? parameters)[] methods)
    {
        var members = methods.Select(m =>
        {
            ParameterListNode? paramList = null;
            if (m.parameters is { Length: > 0 })
            {
                var first = m.parameters[0];
                paramList = new ParameterListNode
                {
                    Parameter = new ParameterNode
                    {
                        Type = MakePrimitiveTypeRef(first.type),
                        Name = Tok(first.name),
                    },
                };
            }

            return (MemberDeclarationNode)new MethodMemberMemberDeclarationNode
            {
                Method = new MethodDeclarationNode
                {
                    Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
                    ReturnType = m.returnType == "void"
                        ? new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() }
                        : MakePrimitiveTypeRef(m.returnType),
                    Name = Tok(m.methodName),
                    Parameters = paramList,
                    Body = new MethodBlockBodyMethodBodyNode
                    {
                        Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty },
                    },
                },
            };
        }).ToImmutableArray();

        return new ClassDeclarationNode { Name = Tok(className), Members = members };
    }

    /// <summary>
    /// Creates a class with public properties.
    /// </summary>
    private static ClassDeclarationNode MakeClassWithPublicProperties(
        string className,
        params (string typeName, string propName, bool hasGet, bool hasSet)[] properties)
    {
        var members = properties.Select(p =>
        {
            var accessors = new List<AccessorDeclarationNode>();
            if (p.hasGet)
                accessors.Add(new AutoGetAccessorAccessorDeclarationNode());
            if (p.hasSet)
                accessors.Add(new AutoSetAccessorAccessorDeclarationNode());

            return (MemberDeclarationNode)new PropertyMemberMemberDeclarationNode
            {
                Property = new BlockPropertyPropertyDeclarationNode
                {
                    Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
                    Type = MakePrimitiveTypeRef(p.typeName),
                    Name = Tok(p.propName),
                    Accessors = accessors.ToImmutableArray(),
                },
            };
        }).ToImmutableArray();

        return new ClassDeclarationNode { Name = Tok(className), Members = members };
    }

    // ─── Name pools ──────────────────────────────────────────────────────

    private static readonly string[] ClassNames =
        ["Alpha", "Beta", "Gamma", "Delta", "Epsilon"];

    private static readonly string[] TraitNames =
        ["Printable", "Drawable", "Serializable", "Hashable", "Comparable"];

    private static readonly string[] FieldNames =
        ["value", "count", "size", "width", "height"];

    private static readonly string[] MethodNames =
        ["Compute", "Process", "Execute", "Transform", "Validate"];

    private static readonly string[] PropertyNames =
        ["Name", "Label", "Title", "Status", "Score"];

    private static readonly string[] PrimitiveTypes = ["int", "string", "bool"];

    /// <summary>
    /// Member names that are GUARANTEED to NOT exist in any trait.
    /// This pool is completely disjoint from FieldNames, MethodNames, and PropertyNames.
    /// </summary>
    private static readonly string[] MissingMemberNamePool =
        ["Foo", "Bar", "Baz", "Qux", "Quux", "Corge", "Grault", "Garply"];

    // ═══════════════════════════════════════════════════════════════════════
    // Scenario types
    // ═══════════════════════════════════════════════════════════════════════

#pragma warning disable CA1034

    /// <summary>
    /// The kind of trait member being tested.
    /// </summary>
    public enum TraitMemberKind { Field, Method, Property }

    /// <summary>
    /// A scenario where a trait-typed variable accesses an existing member.
    /// The class structurally satisfies the trait so the variable declaration is valid.
    /// </summary>
#pragma warning disable CA1819 // Properties should not return arrays — record used as test data
    public record ExistingMemberScenario(
        string ClassName,
        string TraitName,
        TraitMemberKind MemberKind,
        string AccessedMemberName,
        string MemberType,
        (string type, string name)[]? MethodParams,
        bool PropHasGet,
        bool PropHasSet)
    {
        public override string ToString()
        {
            return $"trait {TraitName} {{ {MemberKind} {MemberType} {AccessedMemberName} }} " +
                   $"class {ClassName} accesses .{AccessedMemberName}";
        }
    }
#pragma warning restore CA1819

    /// <summary>
    /// A scenario where a trait-typed variable accesses a member that does NOT exist.
    /// </summary>
    public record MissingMemberScenario(
        string ClassName,
        string TraitName,
        string ExistingFieldType,
        string ExistingFieldName,
        string MissingMemberName)
    {
        public override string ToString()
        {
            return $"trait {TraitName} {{ {ExistingFieldType} {ExistingFieldName} }} " +
                   $"class {ClassName} accesses .{MissingMemberName} (missing)";
        }
    }

#pragma warning restore CA1034

    // ═══════════════════════════════════════════════════════════════════════
    // Generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a scenario where a trait has a field member and the class
    /// structurally satisfies it. The accessed member exists on the trait.
    /// </summary>
    private static Gen<ExistingMemberScenario> GenFieldAccessScenario()
    {
        return from className in Gen.Elements(ClassNames)
               from traitName in Gen.Elements(TraitNames)
               from fieldType in Gen.Elements(PrimitiveTypes)
               from fieldName in Gen.Elements(FieldNames)
               select new ExistingMemberScenario(
                   className, traitName, TraitMemberKind.Field,
                   fieldName, fieldType, null, false, false);
    }

    /// <summary>
    /// Generates a scenario where a trait has a method member and the class
    /// structurally satisfies it. The accessed member exists on the trait.
    /// </summary>
    private static Gen<ExistingMemberScenario> GenMethodAccessScenario()
    {
        return from className in Gen.Elements(ClassNames)
               from traitName in Gen.Elements(TraitNames)
               from returnType in Gen.Elements("void", "int", "string", "bool")
               from methodName in Gen.Elements(MethodNames)
               from hasParam in Gen.Elements(true, false)
               from paramType in Gen.Elements(PrimitiveTypes)
               select new ExistingMemberScenario(
                   className, traitName, TraitMemberKind.Method,
                   methodName, returnType,
                   hasParam ? new[] { (paramType, "arg") } : null,
                   false, false);
    }

    /// <summary>
    /// Generates a scenario where a trait has a property member and the class
    /// structurally satisfies it. The accessed member exists on the trait.
    /// </summary>
    private static Gen<ExistingMemberScenario> GenPropertyAccessScenario()
    {
        return from className in Gen.Elements(ClassNames)
               from traitName in Gen.Elements(TraitNames)
               from propType in Gen.Elements(PrimitiveTypes)
               from propName in Gen.Elements(PropertyNames)
               from hasGet in Gen.Elements(true, false)
               from hasSet in Gen.Elements(true, false)
               where hasGet || hasSet
               select new ExistingMemberScenario(
                   className, traitName, TraitMemberKind.Property,
                   propName, propType, null, hasGet, hasSet);
    }

    /// <summary>
    /// Generates a scenario where a trait-typed variable accesses a member
    /// that does NOT exist in the trait's member scope.
    /// </summary>
    private static Gen<MissingMemberScenario> GenMissingMemberScenario()
    {
        return from className in Gen.Elements(ClassNames)
               from traitName in Gen.Elements(TraitNames)
               from fieldType in Gen.Elements(PrimitiveTypes)
               from fieldName in Gen.Elements(FieldNames)
               from missingName in Gen.Elements(MissingMemberNamePool)
               select new MissingMemberScenario(
                   className, traitName, fieldType, fieldName, missingName);
    }

    public static Arbitrary<ExistingMemberScenario> ArbitraryExistingMember() =>
        Gen.OneOf(GenFieldAccessScenario(), GenMethodAccessScenario(), GenPropertyAccessScenario())
            .ToArbitrary();

    public static Arbitrary<MissingMemberScenario> ArbitraryMissingMember() =>
        GenMissingMemberScenario().ToArbitrary();

    // ═══════════════════════════════════════════════════════════════════════
    // AST construction
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Builds a CompilationUnitNode for an existing member scenario:
    /// - A trait with the specified member
    /// - A class that structurally satisfies the trait
    /// - A Program class with Main that declares a trait-typed variable and accesses the member
    /// </summary>
    private static CompilationUnitNode BuildExistingMemberAst(ExistingMemberScenario scenario)
    {
        var items = new List<TopLevelItemNode>();

        // 1. Declare the trait
        TraitDeclarationNode traitDecl;
        switch (scenario.MemberKind)
        {
            case TraitMemberKind.Field:
                traitDecl = MakeTraitWithFields(scenario.TraitName,
                    (scenario.MemberType, scenario.AccessedMemberName));
                break;
            case TraitMemberKind.Method:
                traitDecl = MakeTraitWithMethods(scenario.TraitName,
                    (scenario.MemberType, scenario.AccessedMemberName, scenario.MethodParams));
                break;
            case TraitMemberKind.Property:
                traitDecl = MakeTraitWithProperties(scenario.TraitName,
                    (scenario.MemberType, scenario.AccessedMemberName,
                     scenario.PropHasGet, scenario.PropHasSet));
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        items.Add(new TypeItemTopLevelItemNode
        {
            TypeDeclaration = new TraitTypeTypeDeclarationNode { TraitDeclaration = traitDecl },
        });

        // 2. Declare the class that structurally satisfies the trait
        ClassDeclarationNode classDecl;
        switch (scenario.MemberKind)
        {
            case TraitMemberKind.Field:
                classDecl = MakeClassWithPublicFields(scenario.ClassName,
                    (scenario.MemberType, scenario.AccessedMemberName));
                break;
            case TraitMemberKind.Method:
                classDecl = MakeClassWithPublicMethods(scenario.ClassName,
                    (scenario.MemberType, scenario.AccessedMemberName, scenario.MethodParams));
                break;
            case TraitMemberKind.Property:
                classDecl = MakeClassWithPublicProperties(scenario.ClassName,
                    (scenario.MemberType, scenario.AccessedMemberName,
                     scenario.PropHasGet, scenario.PropHasSet));
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        items.Add(new TypeItemTopLevelItemNode
        {
            TypeDeclaration = new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl },
        });

        // 3. Program class with Main:
        //    TraitName x = new ClassName();
        //    x.AccessedMemberName;
        var mainStatements = ImmutableArray.Create(
            MakeNamedTypeVarDecl(scenario.TraitName, "x", scenario.ClassName),
            MakeExpressionStatement(MakeMemberAccessExpression("x", scenario.AccessedMemberName)));

        var mainMethod = MakeMethodDecl("Main", mainStatements, isStatic: true);
        var programClass = new ClassDeclarationNode
        {
            Name = Tok("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(WrapMethod(mainMethod)),
        };

        items.Add(new TypeItemTopLevelItemNode
        {
            TypeDeclaration = new ClassTypeTypeDeclarationNode { ClassDeclaration = programClass },
        });

        return new CompilationUnitNode { Items = items.ToImmutableArray() };
    }

    /// <summary>
    /// Builds a CompilationUnitNode for a missing member scenario:
    /// - A trait with a field member
    /// - A class that structurally satisfies the trait
    /// - A Program class with Main that declares a trait-typed variable and accesses a non-existent member
    /// </summary>
    private static CompilationUnitNode BuildMissingMemberAst(MissingMemberScenario scenario)
    {
        var items = new List<TopLevelItemNode>();

        // 1. Declare the trait with a field
        var traitDecl = MakeTraitWithFields(scenario.TraitName,
            (scenario.ExistingFieldType, scenario.ExistingFieldName));

        items.Add(new TypeItemTopLevelItemNode
        {
            TypeDeclaration = new TraitTypeTypeDeclarationNode { TraitDeclaration = traitDecl },
        });

        // 2. Declare the class that structurally satisfies the trait
        var classDecl = MakeClassWithPublicFields(scenario.ClassName,
            (scenario.ExistingFieldType, scenario.ExistingFieldName));

        items.Add(new TypeItemTopLevelItemNode
        {
            TypeDeclaration = new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl },
        });

        // 3. Program class with Main:
        //    TraitName x = new ClassName();
        //    x.MissingMemberName;  (this member does NOT exist on the trait)
        var mainStatements = ImmutableArray.Create(
            MakeNamedTypeVarDecl(scenario.TraitName, "x", scenario.ClassName),
            MakeExpressionStatement(MakeMemberAccessExpression("x", scenario.MissingMemberName)));

        var mainMethod = MakeMethodDecl("Main", mainStatements, isStatic: true);
        var programClass = new ClassDeclarationNode
        {
            Name = Tok("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(WrapMethod(mainMethod)),
        };

        items.Add(new TypeItemTopLevelItemNode
        {
            TypeDeclaration = new ClassTypeTypeDeclarationNode { ClassDeclaration = programClass },
        });

        return new CompilationUnitNode { Items = items.ToImmutableArray() };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property tests
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 7a: For any trait-typed variable and for any member name that
    /// exists in the trait's member scope, LookupMemberType SHALL return the
    /// member's declared type without emitting a MIST_UNDEFINED_MEMBER diagnostic.
    ///
    /// **Validates: Requirements 8.1, 8.2**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(TraitMemberAccessPropertyTests) }, MaxTest = 100)]
    public bool TraitMemberAccess_ExistingMember_NoDiagnostic(ExistingMemberScenario scenario)
    {
        var ast = BuildExistingMemberAst(scenario);
        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        // No MIST_UNDEFINED_MEMBER diagnostic should be emitted for the accessed member
        var undefinedMemberDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_UNDEFINED_MEMBER"
                        && d.Message.Contains(scenario.AccessedMemberName))
            .ToList();

        return undefinedMemberDiags.Count == 0;
    }

    /// <summary>
    /// Property 7b: For any trait-typed variable and for any member name that
    /// does NOT exist in the trait's member scope, LookupMemberType SHALL emit
    /// exactly one MIST_UNDEFINED_MEMBER diagnostic.
    ///
    /// **Validates: Requirements 8.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(TraitMemberAccessPropertyTests) }, MaxTest = 100)]
    public bool TraitMemberAccess_MissingMember_EmitsDiagnostic(MissingMemberScenario scenario)
    {
        var ast = BuildMissingMemberAst(scenario);
        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        // Exactly one MIST_UNDEFINED_MEMBER diagnostic should be emitted for the missing member
        var undefinedMemberDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_UNDEFINED_MEMBER"
                        && d.Message.Contains(scenario.MissingMemberName))
            .ToList();

        return undefinedMemberDiags.Count == 1;
    }
}
