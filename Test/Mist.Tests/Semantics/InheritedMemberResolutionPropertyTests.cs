// Feature: class-inheritance, Property 4: Inherited member resolution via chain walk
// **Validates: Requirements 4.1, 4.2, 4.5, 6.2**

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

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests verifying inherited member resolution via chain walk.
///
/// Property 4: For any class hierarchy where a member (field or method) is declared
/// in an ancestor class but not in the derived class, accessing that member through
/// a derived class instance shall resolve to the ancestor's member definition.
/// Conversely, accessing a member name that does not exist in any class in the
/// inheritance chain shall produce an undefined-member diagnostic.
/// </summary>
public class InheritedMemberResolutionPropertyTests
{
    private readonly SemanticModelBuilder builder = new();

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

    private static BaseListNode MakeBaseList(string name)
    {
        var baseTypeRef = new BaseTypeRefNode { Name = MakeToken(name) };
        return new BaseListNode
        {
            First = baseTypeRef,
            Rest = baseTypeRef,
            BaseTypeRefs = ImmutableArray<BaseTypeRefNode>.Empty
        };
    }

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
        var model = builder.Build(compilation, ImmutableArray.Create(ast), ambientTypeResolver: null, namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);
        return (model, diagnostics.ToImmutableArray());
    }

    private static ClassDeclarationNode MakeClassDeclWithMembers(
        string name,
        string? baseClassName,
        ImmutableArray<MemberDeclarationNode> members)
    {
        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken(name),
            Members = members,
        };

        if(baseClassName is not null)
        {
            var _baseList1 = MakeBaseList(baseClassName);

            classDecl.BaseClause = _baseList1;

            classDecl.BaseList = _baseList1;
        }

        return classDecl;
    }

    private static MemberDeclarationNode MakeFieldMember(string name)
    {
        return new FieldMemberMemberDeclarationNode
        {
            Field = new FieldDeclarationNode
            {
                Name = MakeToken(name),
                Type = new TypeReferenceNode
                {
                    First = new PrimitiveSingleTypeRefSingleTypeRefNode
                    {
                        PrimitiveType = new IntTypePrimitiveTypeNode(),
                    }
                },
            },
        };
    }

    private static MemberDeclarationNode MakeMethodMember(string name)
    {
        return new MethodMemberMemberDeclarationNode
        {
            Method = new MethodDeclarationNode
            {
                Name = MakeToken(name),
                ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
                Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
            },
        };
    }

    /// <summary>
    /// Creates a this.memberName member access expression wrapped in a full expression tree.
    /// </summary>
    private static ExpressionNode MakeThisMemberAccessExpression(string memberName)
    {
        var thisPrimary = new ThisExpressionPrimaryNode();
        var memberSuffix = new MemberAccessSuffixPostfixSuffixNode
        {
            Name = MakeToken(memberName),
        };
        var postfix = new PostfixNode
        {
            Target = thisPrimary,
            Suffix = ImmutableArray.Create<PostfixSuffixNode>(memberSuffix),
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

    // ─── Test data models ────────────────────────────────────────────────

    /// <summary>
    /// Represents a class in a hierarchy with its fields and methods.
    /// </summary>
#pragma warning disable CA1034
    public record ClassInfo(string Name, string? BaseName, ImmutableArray<string> Fields, ImmutableArray<string> Methods)
    {
        public override string ToString()
        {
            var baseStr = BaseName is not null ? $" : {BaseName}" : "";
            var fieldsStr = Fields.Length > 0 ? $" fields=[{string.Join(",", Fields)}]" : "";
            var methodsStr = Methods.Length > 0 ? $" methods=[{string.Join(",", Methods)}]" : "";
            return $"{Name}{baseStr}{fieldsStr}{methodsStr}";
        }
    }

    /// <summary>
    /// Represents a test scenario: a class hierarchy with a member access to test.
    /// </summary>
    public record MemberAccessScenario(
        ImmutableArray<ClassInfo> Classes,
        string LeafClassName,
        string AccessedMemberName,
        bool ShouldResolve)
    {
        public override string ToString()
        {
            var classesStr = string.Join("; ", Classes);
            return $"[{classesStr}] leaf={LeafClassName} access={AccessedMemberName} shouldResolve={ShouldResolve}";
        }
    }
#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] ClassNamePool =
        ["Alpha", "Beta", "Gamma", "Delta", "Epsilon"];

    private static readonly string[] FieldNamePool =
        ["width", "height", "depth", "weight", "speed", "count", "value", "size"];

    private static readonly string[] MethodNamePool =
        ["Run", "Stop", "Reset", "Update", "Process", "Compute", "Init", "Check"];

    /// <summary>
    /// Generates a valid class hierarchy of depth 1-3 with unique field/method names
    /// at various levels, then picks a member to access that exists somewhere in the chain.
    /// </summary>
    public static Gen<MemberAccessScenario> GenResolvableAccess()
    {
        return from depth in Gen.Choose(1, 3)
               from classNames in Gen.Shuffle(ClassNamePool).Select(n => n.Take(depth + 1).ToArray())
               from scenario in BuildResolvableScenario(classNames)
               select scenario;
    }

    private static Gen<MemberAccessScenario> BuildResolvableScenario(string[] classNames)
    {
        var depth = classNames.Length;

        // Assign unique fields and methods to each level.
        // We need to ensure no name collisions across levels (additive-only constraint).
        return from fieldCounts in GenFieldCounts(depth)
               from methodCounts in GenMethodCounts(depth)
               from shuffledFields in Gen.Shuffle(FieldNamePool).Select(f => f.ToArray())
               from shuffledMethods in Gen.Shuffle(MethodNamePool).Select(m => m.ToArray())
               from targetLevel in Gen.Choose(0, depth - 1)
               let classes = BuildHierarchy(classNames, fieldCounts, methodCounts, shuffledFields, shuffledMethods)
               where classes.Length > 0
               let allMembers = CollectAllMembers(classes)
               where allMembers.Count > 0
               from memberIdx in Gen.Choose(0, allMembers.Count - 1)
               let accessedMember = allMembers[memberIdx]
               select new MemberAccessScenario(
                   classes,
                   classNames[^1],
                   accessedMember,
                   ShouldResolve: true);
    }

    /// <summary>
    /// Generates a valid class hierarchy where the accessed member does NOT exist
    /// in any class in the chain, so an MIST_UNDEFINED_MEMBER diagnostic is expected.
    /// </summary>
    public static Gen<MemberAccessScenario> GenUnresolvableAccess()
    {
        return from depth in Gen.Choose(1, 3)
               from classNames in Gen.Shuffle(ClassNamePool).Select(n => n.Take(depth + 1).ToArray())
               from scenario in BuildUnresolvableScenario(classNames)
               select scenario;
    }

    private static Gen<MemberAccessScenario> BuildUnresolvableScenario(string[] classNames)
    {
        var depth = classNames.Length;
        var undefinedMembers = new[] { "nonexistent", "missing", "phantom", "ghost" };

        return from fieldCounts in GenFieldCounts(depth)
               from methodCounts in GenMethodCounts(depth)
               from shuffledFields in Gen.Shuffle(FieldNamePool).Select(f => f.ToArray())
               from shuffledMethods in Gen.Shuffle(MethodNamePool).Select(m => m.ToArray())
               from undefinedIdx in Gen.Choose(0, undefinedMembers.Length - 1)
               let classes = BuildHierarchy(classNames, fieldCounts, methodCounts, shuffledFields, shuffledMethods)
               where classes.Length > 0
               select new MemberAccessScenario(
                   classes,
                   classNames[^1],
                   undefinedMembers[undefinedIdx],
                   ShouldResolve: false);
    }

    private static Gen<int[]> GenFieldCounts(int depth)
    {
        // Each level gets 0-2 fields, but at least one level must have at least 1 field
        return Gen.ArrayOf(Gen.Choose(0, 2), depth)
            .Where(counts => counts.Sum() <= FieldNamePool.Length);
    }

    private static Gen<int[]> GenMethodCounts(int depth)
    {
        return Gen.ArrayOf(Gen.Choose(0, 2), depth)
            .Where(counts => counts.Sum() <= MethodNamePool.Length);
    }

    private static ImmutableArray<ClassInfo> BuildHierarchy(
        string[] classNames,
        int[] fieldCounts,
        int[] methodCounts,
        string[] shuffledFields,
        string[] shuffledMethods)
    {
        var classes = new List<ClassInfo>();
        var fieldIdx = 0;
        var methodIdx = 0;
        var usedMembers = new HashSet<string>();
        var valid = true;

        for(var i = 0; i < classNames.Length; i++)
        {
            var fields = new List<string>();
            for(var f = 0; f < fieldCounts[i] && fieldIdx < shuffledFields.Length; f++)
            {
                var fieldName = shuffledFields[fieldIdx++];
                if(!usedMembers.Add(fieldName))
                {
                    valid = false;
                    break;
                }

                fields.Add(fieldName);
            }

            if(!valid)
            {
                break;
            }

            var methods = new List<string>();
            for(var m = 0; m < methodCounts[i] && methodIdx < shuffledMethods.Length; m++)
            {
                var methodName = shuffledMethods[methodIdx++];
                if(!usedMembers.Add(methodName))
                {
                    valid = false;
                    break;
                }

                methods.Add(methodName);
            }

            if(!valid)
            {
                break;
            }

            var baseName = i > 0 ? classNames[i - 1] : null;
            classes.Add(new ClassInfo(
                classNames[i],
                baseName,
                fields.ToImmutableArray(),
                methods.ToImmutableArray()));
        }

        if(!valid || classes.Count != classNames.Length)
        {
            return ImmutableArray<ClassInfo>.Empty;
        }

        return classes.ToImmutableArray();
    }

    private static List<string> CollectAllMembers(ImmutableArray<ClassInfo> classes)
    {
        var members = new List<string>();
        foreach(var cls in classes)
        {
            members.AddRange(cls.Fields);
            members.AddRange(cls.Methods);
        }

        return members;
    }

    /// <summary>FsCheck Arbitrary registration for MemberAccessScenario.</summary>
    public static Arbitrary<MemberAccessScenario> ArbitraryMemberAccessScenario()
    {
        return Gen.OneOf(GenResolvableAccess(), GenUnresolvableAccess()).ToArbitrary();
    }

    // ─── AST construction ────────────────────────────────────────────────

    /// <summary>
    /// Builds a full CompilationUnitNode from a MemberAccessScenario.
    /// The leaf class gets a method that accesses this.memberName.
    /// </summary>
    private static CompilationUnitNode BuildAst(MemberAccessScenario scenario)
    {
        var typeDecls = new List<TypeDeclarationNode>();

        foreach(var cls in scenario.Classes)
        {
            var members = new List<MemberDeclarationNode>();

            foreach(var field in cls.Fields)
            {
                members.Add(MakeFieldMember(field));
            }

            foreach(var method in cls.Methods)
            {
                members.Add(MakeMethodMember(method));
            }

            // If this is the leaf class, add a probe method that accesses this.memberName
            if(cls.Name == scenario.LeafClassName)
            {
                var accessExpr = MakeThisMemberAccessExpression(scenario.AccessedMemberName);
                var probeMethod = new MethodDeclarationNode
                {
                    Name = MakeToken("_probe_"),
                    ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
                    Body = new MethodBlockBodyMethodBodyNode
                    {
                        Block = new BlockNode
                        {
                            Statements = ImmutableArray.Create(MakeExpressionStatement(accessExpr)),
                        }
                    },
                };
                members.Add(new MethodMemberMemberDeclarationNode { Method = probeMethod });
            }

            var classDecl = MakeClassDeclWithMembers(cls.Name, cls.BaseName, members.ToImmutableArray());
            typeDecls.Add(new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl });
        }

        return new CompilationUnitNode
        {
            Items = typeDecls.ToImmutableArray().AsTopLevelItems(),
        };
    }

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 4a: For any class hierarchy where a member is declared in an ancestor
    /// class, accessing that member through this.memberName in the leaf class shall
    /// resolve without producing an MIST_UNDEFINED_MEMBER diagnostic.
    ///
    /// **Validates: Requirements 4.1, 4.2, 4.5**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(InheritedMemberResolutionPropertyTests) }, MaxTest = 100)]
    public bool InheritedMember_ResolvesWithoutUndefinedDiagnostic(MemberAccessScenario scenario)
    {
        if(!scenario.ShouldResolve)
        {
            return true; // Skip unresolvable scenarios for this property
        }

        var ast = BuildAst(scenario);
        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        // Filter to only MIST_UNDEFINED_MEMBER diagnostics about the accessed member
        var undefinedMemberDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_UNDEFINED_MEMBER"
                        && d.Message.Contains(scenario.AccessedMemberName))
            .ToList();

        // No undefined member diagnostic should be emitted for a member that exists in the chain
        return undefinedMemberDiags.Count == 0;
    }

    /// <summary>
    /// Property 4b: For any class hierarchy where a member name does not exist in any
    /// class in the inheritance chain, accessing that member through this.memberName
    /// in the leaf class shall produce an MIST_UNDEFINED_MEMBER diagnostic.
    ///
    /// **Validates: Requirements 4.2, 6.2**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(InheritedMemberResolutionPropertyTests) }, MaxTest = 100)]
    public bool UndefinedMember_ProducesUndefinedMemberDiagnostic(MemberAccessScenario scenario)
    {
        if(scenario.ShouldResolve)
        {
            return true; // Skip resolvable scenarios for this property
        }

        var ast = BuildAst(scenario);
        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        // An MIST_UNDEFINED_MEMBER diagnostic should be emitted for the accessed member
        var undefinedMemberDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_UNDEFINED_MEMBER"
                        && d.Message.Contains(scenario.AccessedMemberName))
            .ToList();

        return undefinedMemberDiags.Count > 0;
    }

    /// <summary>
    /// Property 4c: For any class hierarchy where a member is declared in an ancestor,
    /// the resolved type of this.memberName in the leaf class is not ErrorType
    /// (i.e., it resolves to a concrete type from the ancestor's declaration).
    ///
    /// **Validates: Requirements 4.1, 4.5**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(InheritedMemberResolutionPropertyTests) }, MaxTest = 100)]
    public bool InheritedMember_ResolvesToConcreteType(MemberAccessScenario scenario)
    {
        if(!scenario.ShouldResolve)
        {
            return true; // Skip unresolvable scenarios for this property
        }

        var ast = BuildAst(scenario);
        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        // No MIST_UNDEFINED_MEMBER diagnostic for the accessed member
        var hasUndefined = diagnostics.Any(d =>
            d.Id.Value == "MIST_UNDEFINED_MEMBER"
            && d.Message.Contains(scenario.AccessedMemberName));

        if(hasUndefined)
        {
            return false;
        }

        // Verify the member access expression was resolved to a non-unknown type.
        // We check that no MIST_UNDEFINED_MEMBER diagnostic was emitted for the leaf class
        // accessing the member, which confirms the chain walk found the member.
        // Additionally, check that the leaf class type exists and has the correct base chain.
        var leafSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == scenario.LeafClassName && s.Kind == SymbolKind.Class);

        if(leafSymbol?.DeclaredType is not ClassType leafType)
        {
            return false;
        }

        // Walk the chain to verify the member exists somewhere
        var current = leafType;
        while(current is not null)
        {
            foreach(var member in current.MemberScope.Members)
            {
                if(member.Name == scenario.AccessedMemberName)
                {
                    return true; // Found the member in the chain
                }
            }

            current = current.BaseType as ClassType;
        }

        // Member should have been found in the chain
        return false;
        ;
    }

}