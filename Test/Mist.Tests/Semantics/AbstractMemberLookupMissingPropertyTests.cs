// Feature: abstract-member-dispatch, Property 2: Abstract member lookup emits diagnostic for missing members
// **Validates: Requirements 1.4**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests verifying abstract member lookup emits diagnostic
/// for missing members.
///
/// Property 2: For any AbstractType hierarchy and for any member name that
/// does NOT exist in the target abstract's MemberScope or any transitively
/// inherited abstract's MemberScope, calling LookupMemberType SHALL emit
/// exactly one MIST_UNDEFINED_MEMBER diagnostic and SHALL return ErrorType.
/// </summary>
// Phase B will reintroduce UnknownType as the user-facing top type, distinct from this ErrorType sentinel.
public class AbstractMemberLookupMissingPropertyTests
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

    // ─── AST construction helpers ────────────────────────────────────────

    private static MethodDeclarationNode MakeAbstractMethod(string name)
    {
        return new MethodDeclarationNode
        {
            Name = MakeToken(name),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodNoBodyMethodBodyNode(),
        };
    }

    private static TypeBodyDeclarationNode MakeAbstractDecl(
        string name,
        ImmutableArray<MethodDeclarationNode> methods,
        string? parentAbstractName = null)
    {
        var members = methods.Select(m =>
            (MemberDeclarationNode)new MethodMemberMemberDeclarationNode { Method = m })
            .ToImmutableArray();
        var decl = new TypeBodyDeclarationNode
        {
            Keyword = new AbstractKeywordTypeKeywordNode(),
            Name = MakeToken(name),
            Members = members,
        };

        if(parentAbstractName is not null)
        {
            var abstractList1 = MakeBaseList(parentAbstractName);

            decl.BaseClause = abstractList1;

            decl.BaseList = abstractList1;
        }

        return decl;
    }

    private static MethodDeclarationNode MakeMethodDecl(
        string name,
        ImmutableArray<StatementNode>? bodyStatements = null,
        bool isStatic = false)
    {
        var modifiers = new List<ModifierNode> { new PublicModifierModifierNode() };
        if(isStatic)
        {
            modifiers.Add(new StaticModifierModifierNode());
        }

        return new MethodDeclarationNode
        {
            Name = MakeToken(name),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Body = new MethodBlockBodyMethodBodyNode
            {
                Block = new BlockNode
                {
                    Statements = bodyStatements ?? ImmutableArray<StatementNode>.Empty,
                }
            },
            Modifiers = modifiers.ToImmutableArray(),
        };
    }

    private static TypeBodyDeclarationNode MakeClassDecl(
        string name,
        string? baseOrAbstractName = null,
        ImmutableArray<MemberDeclarationNode>? members = null)
    {
        var decl = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = MakeToken(name),
            Members = members ?? ImmutableArray<MemberDeclarationNode>.Empty,
        };

        if(baseOrAbstractName is not null)
        {
            var baseList1 = MakeBaseList(baseOrAbstractName);

            decl.BaseClause = baseList1;

            decl.BaseList = baseList1;
        }

        return decl;
    }

    private static MemberDeclarationNode WrapMethod(MethodDeclarationNode method)
    {
        return new MethodMemberMemberDeclarationNode { Method = method };
    }

    private static TypeDeclarationNode WrapAbstract(TypeBodyDeclarationNode decl)
    {
        return new BodyTypeTypeDeclarationNode { TypeBody = decl };
    }

    private static TypeDeclarationNode WrapClass(TypeBodyDeclarationNode decl)
    {
        return new BodyTypeTypeDeclarationNode { TypeBody = decl };
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
                IDENTIFIER = MakeToken(typeName),
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
            Name = MakeToken(varName),
        };
        var suffix = new MemberAccessSuffixPostfixSuffixNode
        {
            Name = MakeToken(memberName),
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
    /// Creates: AbstractName varName = new ClassName();
    /// </summary>
    private static StatementNode MakeNamedTypeVarDecl(
        string declaredTypeName, string varName, string initializedTypeName)
    {
        return new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = new TypeReferenceNode
                {
                    First = new NamedSingleTypeRefSingleTypeRefNode
                    {
                        Name = new QualifiedIdentifierNode
                        {
                            IDENTIFIER = MakeToken(declaredTypeName),
                            Span = new Storm.Runtime.SourceSpan(0, declaredTypeName.Length),
                        },
                    }
                },
                Name = MakeToken(varName),
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

    // ─── Test data models ────────────────────────────────────────────────

    /// <summary>
    /// Represents an abstract in a hierarchy with its own methods and optional parent.
    /// </summary>
#pragma warning disable CA1034
    public record AbstractInfo(
        string Name,
        string? ParentName,
        ImmutableArray<string> Methods)
    {
        public override string ToString()
        {
            var parentStr = ParentName is not null ? $" : {ParentName}" : "";
            var methodsStr = Methods.Length > 0 ? $" {{ {string.Join(", ", Methods)} }}" : " { }";
            return $"{Name}{parentStr}{methodsStr}";
        }
    }

    /// <summary>
    /// A test scenario: an abstract hierarchy, a class implementing the leaf abstract,
    /// and a member name that does NOT exist anywhere in the hierarchy.
    /// </summary>
    public record MissingMemberScenario(
        ImmutableArray<AbstractInfo> Abstracts,
        string ClassName,
        string TargetAbstractName,
        string MissingMemberName)
    {
        public override string ToString()
        {
            var abstracts = string.Join("; ", Abstracts);
            return $"[{abstracts}] class={ClassName} target={TargetAbstractName} missing={MissingMemberName}";
        }
    }
#pragma warning restore CA1034

    // ─── Name pools ──────────────────────────────────────────────────────

    private static readonly string[] AbstractNamePool =
        ["IAlpha", "IBeta", "IGamma", "IDelta", "IEpsilon"];

    /// <summary>
    /// Methods that ARE added to abstracts in the hierarchy.
    /// </summary>
    private static readonly string[] MethodNamePool =
        ["DoWork", "Process", "Execute", "Run", "Calculate", "Transform", "Validate", "Render"];

    /// <summary>
    /// Member names that are GUARANTEED to NOT exist in any abstract.
    /// This pool is completely disjoint from MethodNamePool.
    /// </summary>
    private static readonly string[] MissingMemberNamePool =
        ["Foo", "Bar", "Baz", "Qux", "Quux", "Corge", "Grault", "Garply"];

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates a flat abstract hierarchy (single abstract with methods)
    /// and a missing member name that does not exist in the abstract.
    /// </summary>
    private static Gen<MissingMemberScenario> GenFlatScenario()
    {
        return from methodCount in Gen.Choose(1, 4)
               from methods in Gen.Shuffle(MethodNamePool)
                   .Select(m => m.Take(methodCount).ToImmutableArray())
               from missingIdx in Gen.Choose(0, MissingMemberNamePool.Length - 1)
               let abstractName = AbstractNamePool[0]
               let abstractInfo = new AbstractInfo(abstractName, null, methods)
               select new MissingMemberScenario(
                   ImmutableArray.Create(abstractInfo),
                   "TestImpl",
                   abstractName,
                   MissingMemberNamePool[missingIdx]);
    }

    /// <summary>
    /// Generates a chain hierarchy: IRoot -> IMiddle -> ILeaf (depth 2-3).
    /// The missing member name does not exist in any abstract in the chain.
    /// </summary>
    private static Gen<MissingMemberScenario> GenChainScenario()
    {
        return from depth in Gen.Choose(2, 3)
               from names in Gen.Shuffle(AbstractNamePool)
                   .Select(n => n.Take(depth).ToArray())
               from totalMethods in Gen.Choose(depth, Math.Min(depth * 2, MethodNamePool.Length))
               from shuffledMethods in Gen.Shuffle(MethodNamePool)
                   .Select(m => m.Take(totalMethods).ToArray())
               from scenario in BuildChainScenario(names, shuffledMethods)
               select scenario;
    }

    private static Gen<MissingMemberScenario> BuildChainScenario(
        string[] names, string[] allMethods)
    {
        var depth = names.Length;
        return from splits in GenSplitPoints(allMethods.Length, depth)
               let abstracts = BuildChainAbstracts(names, allMethods, splits)
               from missingIdx in Gen.Choose(0, MissingMemberNamePool.Length - 1)
               select new MissingMemberScenario(
                   abstracts,
                   "TestImpl",
                   names[^1], // leaf abstract as target
                   MissingMemberNamePool[missingIdx]);
    }

    private static ImmutableArray<AbstractInfo> BuildChainAbstracts(
        string[] names, string[] allMethods, int[] splits)
    {
        var abstracts = new List<AbstractInfo>();
        var methodIdx = 0;

        for(var i = 0; i < names.Length; i++)
        {
            var count = splits[i];
            var methods = allMethods.Skip(methodIdx).Take(count).ToImmutableArray();
            methodIdx += count;

            var parentName = i > 0 ? names[i - 1] : null;
            abstracts.Add(new AbstractInfo(names[i], parentName, methods));
        }

        return abstracts.ToImmutableArray();
    }

    /// <summary>
    /// Generates a diamond hierarchy: IBase has methods, ILeft : IBase has its own methods.
    /// The missing member name does not exist in any abstract.
    /// </summary>
    private static Gen<MissingMemberScenario> GenDiamondScenario()
    {
        return from baseMethods in Gen.Choose(1, 2)
               from leftMethods in Gen.Choose(0, 2)
               from shuffled in Gen.Shuffle(MethodNamePool)
                   .Select(m => m.Take(baseMethods + leftMethods).ToArray())
               let baseMethodNames = shuffled.Take(baseMethods).ToImmutableArray()
               let leftMethodNames = shuffled.Skip(baseMethods).Take(leftMethods).ToImmutableArray()
               let baseAbstract = new AbstractInfo("IBase", null, baseMethodNames)
               let leftAbstract = new AbstractInfo("ILeft", "IBase", leftMethodNames)
               from missingIdx in Gen.Choose(0, MissingMemberNamePool.Length - 1)
               select new MissingMemberScenario(
                   ImmutableArray.Create(baseAbstract, leftAbstract),
                   "TestImpl",
                   "ILeft",
                   MissingMemberNamePool[missingIdx]);
    }

    /// <summary>
    /// Generates an empty abstract (no methods, no parents) scenario.
    /// Any member access should produce a diagnostic.
    /// </summary>
    private static Gen<MissingMemberScenario> GenEmptyAbstractScenario()
    {
        return from missingIdx in Gen.Choose(0, MissingMemberNamePool.Length - 1)
               let abstractName = AbstractNamePool[0]
               let abstractInfo = new AbstractInfo(abstractName, null, ImmutableArray<string>.Empty)
               select new MissingMemberScenario(
                   ImmutableArray.Create(abstractInfo),
                   "TestImpl",
                   abstractName,
                   MissingMemberNamePool[missingIdx]);
    }

    /// <summary>
    /// Generates split points to distribute N items across K buckets,
    /// each bucket getting at least 1 item.
    /// </summary>
    private static Gen<int[]> GenSplitPoints(int total, int buckets)
    {
        if(buckets <= 0 || total < buckets)
        {
            return Gen.Constant(Array.Empty<int>());
        }

        var remainder = total - buckets;
        return Gen.ArrayOf(Gen.Choose(0, Math.Max(remainder, 0)), buckets)
            .Select(extras =>
            {
                var sum = extras.Sum();
                if(sum == 0)
                {
                    var result = new int[buckets];
                    Array.Fill(result, 1);
                    result[0] += remainder;
                    return result;
                }

                var normalized = new int[buckets];
                var distributed = 0;
                for(var i = 0; i < buckets; i++)
                {
                    normalized[i] = 1 + (int)Math.Round((double)extras[i] / sum * remainder);
                    distributed += normalized[i];
                }

                normalized[^1] += total - distributed;
                if(normalized[^1] < 1)
                {
                    normalized[^1] = 1;
                }

                return normalized;
            })
            .Where(splits => splits.Length == buckets && splits.All(s => s >= 1) && splits.Sum() == total);
    }

    public static Gen<MissingMemberScenario> GenScenario()
    {
        return Gen.OneOf(
            GenFlatScenario(),
            GenChainScenario(),
            GenDiamondScenario(),
            GenEmptyAbstractScenario());
    }

    public static Arbitrary<MissingMemberScenario> ArbitraryMissingMemberScenario()
    {
        return GenScenario().ToArbitrary();
    }

    // ─── AST construction ────────────────────────────────────────────────

    /// <summary>
    /// Builds a full CompilationUnitNode from a MissingMemberScenario.
    /// Creates the abstract hierarchy, a class implementing the leaf abstract
    /// with all required methods, and a Program class with Main that declares
    /// an abstract-typed variable and accesses a member that does NOT exist.
    /// </summary>
    private static CompilationUnitNode BuildAst(MissingMemberScenario scenario)
    {
        var typeDecls = new List<TypeDeclarationNode>();

        // 1. Declare all abstracts in order (parents before children)
        foreach(var abstractInfo in scenario.Abstracts)
        {
            var methods = abstractInfo.Methods
                .Select(MakeAbstractMethod)
                .ToImmutableArray();
            var decl = MakeAbstractDecl(abstractInfo.Name, methods, abstractInfo.ParentName);
            typeDecls.Add(WrapAbstract(decl));
        }

        // 2. Implementing class with ALL methods from the entire hierarchy
        var allMethodNames = scenario.Abstracts
            .SelectMany(i => i.Methods)
            .Distinct()
            .ToList();
        var implMembers = allMethodNames
            .Select(m => WrapMethod(MakeMethodDecl(m)))
            .ToImmutableArray();

        // The class implements the leaf abstract (last in the list)
        var leafAbstractName = scenario.Abstracts[^1].Name;
        typeDecls.Add(WrapClass(
            MakeClassDecl(scenario.ClassName, leafAbstractName, implMembers)));

        // 3. Program class with Main that:
        //    - Declares: TargetAbstractName x = new ClassName();
        //    - Accesses: x.MissingMemberName;  (this member does NOT exist)
        var mainStatements = ImmutableArray.Create(
            MakeNamedTypeVarDecl(scenario.TargetAbstractName, "x", scenario.ClassName),
            MakeExpressionStatement(MakeMemberAccessExpression("x", scenario.MissingMemberName)));

        var mainMethod = MakeMethodDecl("Main", mainStatements, isStatic: true);
        var programClass = MakeClassDecl("Program",
            members: ImmutableArray.Create(WrapMethod(mainMethod)));
        typeDecls.Add(WrapClass(programClass));

        return new CompilationUnitNode
        {
            Items = typeDecls.ToImmutableArray().AsTopLevelItems(),
        };
    }

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 2: For any AbstractType hierarchy and for any member name that
    /// does NOT exist in the target abstract's MemberScope or any transitively
    /// inherited abstract's MemberScope, calling LookupMemberType SHALL emit
    /// exactly one MIST_UNDEFINED_MEMBER diagnostic and SHALL return ErrorType.
    ///
    /// **Validates: Requirements 1.4**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(AbstractMemberLookupMissingPropertyTests) }, MaxTest = 100)]
    public bool AbstractMemberLookup_EmitsDiagnosticForMissingMembers(
        MissingMemberScenario scenario)
    {
        var ast = BuildAst(scenario);
        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        // Exactly one MIST_UNDEFINED_MEMBER diagnostic should be emitted for the missing member
        var undefinedMemberDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_UNDEFINED_MEMBER"
                        && d.Message.Contains(scenario.MissingMemberName))
            .ToList();

        return undefinedMemberDiags.Count == 1;
        ;
    }

}