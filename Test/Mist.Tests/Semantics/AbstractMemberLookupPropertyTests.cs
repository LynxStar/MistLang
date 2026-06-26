// Feature: abstract-member-dispatch, Property 1: Abstract member lookup resolves correctly through hierarchy
// **Validates: Requirements 1.1, 1.2, 1.3, 5.1, 6.1**

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
/// Property-based tests verifying abstract member lookup resolves correctly
/// through hierarchy.
///
/// Property 1: For any AbstractType hierarchy (with varying depth, width, and
/// diamond patterns) and for any member name that exists in the hierarchy (either
/// in the target abstract's own MemberScope or in any transitively inherited
/// abstract's MemberScope), calling LookupMemberType with the target abstract
/// and that member name SHALL return the member's declared type and SHALL NOT
/// emit any MIST_UNDEFINED_MEMBER diagnostic.
/// </summary>
public class AbstractMemberLookupPropertyTests
{
    private readonly SemanticModelBuilder _builder = new();

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
        var model = _builder.Build(compilation, ImmutableArray.Create(ast), ambientTypeResolver: null, namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);
        return (model, diagnostics.ToImmutableArray());
    }

    // ─── AST construction helpers ────────────────────────────────────────

    private static AbstractMethodDeclarationNode MakeAbstractMethod(string name)
    {
        return new AbstractMethodDeclarationNode
        {
            Name = MakeToken(name),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
        };
    }

    private static AbstractDeclarationNode MakeAbstractDecl(
        string name,
        ImmutableArray<AbstractMethodDeclarationNode> methods,
        string? parentAbstractName = null)
    {
        var members = methods.Select(m =>
            (AbstractMemberDeclarationNode)new AbstractMethodMemberAbstractMemberDeclarationNode { Method = m })
            .ToImmutableArray();
        var decl = new AbstractDeclarationNode
        {
            Name = MakeToken(name),
            Members = members,
        };

        if(parentAbstractName is not null)
        {
            var _abstractList1 = MakeBaseList(parentAbstractName);

            decl.AbstractList = _abstractList1;

            decl.Abstracts = _abstractList1;
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

    private static ClassDeclarationNode MakeClassDecl(
        string name,
        string? baseOrAbstractName = null,
        ImmutableArray<MemberDeclarationNode>? members = null)
    {
        var decl = new ClassDeclarationNode
        {
            Name = MakeToken(name),
            Members = members ?? ImmutableArray<MemberDeclarationNode>.Empty,
        };

        if(baseOrAbstractName is not null)
        {
            var _baseList1 = MakeBaseList(baseOrAbstractName);

            decl.BaseClause = _baseList1;

            decl.BaseList = _baseList1;
        }

        return decl;
    }

    private static MemberDeclarationNode WrapMethod(MethodDeclarationNode method) =>
        new MethodMemberMemberDeclarationNode { Method = method };

    private static TypeDeclarationNode WrapAbstract(AbstractDeclarationNode decl) =>
        new AbstractTypeTypeDeclarationNode { AbstractDeclaration = decl };

    private static TypeDeclarationNode WrapClass(ClassDeclarationNode decl) =>
        new ClassTypeTypeDeclarationNode { ClassDeclaration = decl };

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
    /// The shape of the abstract hierarchy to generate.
    /// </summary>
    public enum HierarchyShape
    {
        /// <summary>Single abstract, no inheritance.</summary>
        Flat,
        /// <summary>Linear chain: IA -> IB -> IC.</summary>
        Chain,
        /// <summary>Diamond: IA -> IB, IA -> IC, IB -> ID, IC -> ID (via chain since AST supports single parent).</summary>
        Diamond,
    }

    /// <summary>
    /// A test scenario: an abstract hierarchy, a class implementing the leaf abstract,
    /// and a member to look up through an abstract-typed variable.
    /// </summary>
    public record AbstractMemberLookupScenario(
        ImmutableArray<AbstractInfo> Abstracts,
        string ClassName,
        string TargetAbstractName,
        string AccessedMemberName)
    {
        public override string ToString()
        {
            var abstracts = string.Join("; ", Abstracts);
            return $"[{abstracts}] class={ClassName} target={TargetAbstractName} access={AccessedMemberName}";
        }
    }
#pragma warning restore CA1034

    // ─── Name pools ──────────────────────────────────────────────────────

    private static readonly string[] AbstractNamePool =
        ["IAlpha", "IBeta", "IGamma", "IDelta", "IEpsilon"];

    private static readonly string[] MethodNamePool =
        ["DoWork", "Process", "Execute", "Run", "Calculate", "Transform", "Validate", "Render"];

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates a flat abstract hierarchy (single abstract with methods).
    /// The accessed member is one of the abstract's own methods.
    /// </summary>
    private static Gen<AbstractMemberLookupScenario> GenFlatScenario()
    {
        return from methodCount in Gen.Choose(1, 4)
               from methods in Gen.Shuffle(MethodNamePool)
                   .Select(m => m.Take(methodCount).ToImmutableArray())
               from memberIdx in Gen.Choose(0, methodCount - 1)
               let abstractName = AbstractNamePool[0]
               let abstractInfo = new AbstractInfo(abstractName, null, methods)
               select new AbstractMemberLookupScenario(
                   ImmutableArray.Create(abstractInfo),
                   "TestImpl",
                   abstractName,
                   methods[memberIdx]);
    }

    /// <summary>
    /// Generates a chain hierarchy: IRoot -> IMiddle -> ILeaf (depth 2-3).
    /// The accessed member may be in any abstract in the chain.
    /// The target abstract for the variable type is the leaf.
    /// </summary>
    private static Gen<AbstractMemberLookupScenario> GenChainScenario()
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

    private static Gen<AbstractMemberLookupScenario> BuildChainScenario(
        string[] names, string[] allMethods)
    {
        var depth = names.Length;
        // Distribute methods across abstracts, ensuring each has at least 1
        return from splits in GenSplitPoints(allMethods.Length, depth)
               let abstracts = BuildChainAbstracts(names, allMethods, splits)
               let allMethodNames = abstracts.SelectMany(i => i.Methods).ToList()
               where allMethodNames.Count > 0
               from memberIdx in Gen.Choose(0, allMethodNames.Count - 1)
               select new AbstractMemberLookupScenario(
                   abstracts,
                   "TestImpl",
                   names[^1], // leaf abstract as target
                   allMethodNames[memberIdx]);
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
    /// Generates a diamond hierarchy: IBase has methods, ILeft : IBase and IRight : IBase
    /// each have their own methods. The class implements ILeft (and transitively IBase).
    /// The target abstract is ILeft, and the accessed member can be from IBase (inherited).
    /// </summary>
    private static Gen<AbstractMemberLookupScenario> GenDiamondScenario()
    {
        return from baseMethods in Gen.Choose(1, 2)
               from leftMethods in Gen.Choose(0, 2)
               from shuffled in Gen.Shuffle(MethodNamePool)
                   .Select(m => m.Take(baseMethods + leftMethods).ToArray())
               let baseMethodNames = shuffled.Take(baseMethods).ToImmutableArray()
               let leftMethodNames = shuffled.Skip(baseMethods).Take(leftMethods).ToImmutableArray()
               let baseAbstract = new AbstractInfo("IBase", null, baseMethodNames)
               let leftAbstract = new AbstractInfo("ILeft", "IBase", leftMethodNames)
               let allMethods = baseMethodNames.Concat(leftMethodNames).ToList()
               where allMethods.Count > 0
               from memberIdx in Gen.Choose(0, allMethods.Count - 1)
               select new AbstractMemberLookupScenario(
                   ImmutableArray.Create(baseAbstract, leftAbstract),
                   "TestImpl",
                   "ILeft", // target is child abstract
                   allMethods[memberIdx]);
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

        // Give each bucket 1, then distribute the remainder randomly
        var remainder = total - buckets;
        return Gen.ArrayOf(Gen.Choose(0, Math.Max(remainder, 0)), buckets)
            .Select(extras =>
            {
                // Normalize extras to sum to remainder
                var sum = extras.Sum();
                if(sum == 0)
                {
                    var result = new int[buckets];
                    Array.Fill(result, 1);
                    // Distribute remainder to first bucket
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

                // Fix rounding errors
                normalized[^1] += total - distributed;
                if(normalized[^1] < 1)
                {
                    normalized[^1] = 1;
                }

                return normalized;
            })
            .Where(splits => splits.Length == buckets && splits.All(s => s >= 1) && splits.Sum() == total);
    }

    public static Gen<AbstractMemberLookupScenario> GenScenario()
    {
        return Gen.OneOf(GenFlatScenario(), GenChainScenario(), GenDiamondScenario());
    }

    public static Arbitrary<AbstractMemberLookupScenario> ArbitraryAbstractMemberLookupScenario() =>
        GenScenario().ToArbitrary();

    // ─── AST construction ────────────────────────────────────────────────

    /// <summary>
    /// Builds a full CompilationUnitNode from an AbstractMemberLookupScenario.
    /// Creates the abstract hierarchy, a class implementing the leaf abstract
    /// with all required methods, and a Program class with Main that declares
    /// an abstract-typed variable and accesses a member on it.
    /// </summary>
    private static CompilationUnitNode BuildAst(AbstractMemberLookupScenario scenario)
    {
        var typeDecls = new List<TypeDeclarationNode>();

        // 1. Declare all abstracts in order (parents before children)
        foreach(var abstractInfo in scenario.Abstracts)
        {
            var methods = abstractInfo.Methods
                .Select(m => MakeAbstractMethod(m))
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
        //    - Accesses: x.AccessedMemberName;
        var mainStatements = ImmutableArray.Create(
            MakeNamedTypeVarDecl(scenario.TargetAbstractName, "x", scenario.ClassName),
            MakeExpressionStatement(MakeMemberAccessExpression("x", scenario.AccessedMemberName)));

        var mainMethod = MakeMethodDecl("Main", mainStatements, isStatic: true);
        var programClass = MakeClassDecl("Program",
            members: ImmutableArray.Create<MemberDeclarationNode>(WrapMethod(mainMethod)));
        typeDecls.Add(WrapClass(programClass));

        return new CompilationUnitNode
        {
            Items = (typeDecls.ToImmutableArray()).AsTopLevelItems(),
        };
    }

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 1: For any AbstractType hierarchy (with varying depth, width,
    /// and diamond patterns) and for any member name that exists in the hierarchy,
    /// calling LookupMemberType with the target abstract and that member name
    /// SHALL return the member's declared type and SHALL NOT emit any
    /// MIST_UNDEFINED_MEMBER diagnostic.
    ///
    /// **Validates: Requirements 1.1, 1.2, 1.3, 5.1, 6.1**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(AbstractMemberLookupPropertyTests) }, MaxTest = 100)]
    public bool AbstractMemberLookup_ResolvesCorrectlyThroughHierarchy(
        AbstractMemberLookupScenario scenario)
    {
        var ast = BuildAst(scenario);
        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        // No MIST_UNDEFINED_MEMBER diagnostic should be emitted for the accessed member
        var undefinedMemberDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_UNDEFINED_MEMBER"
                        && d.Message.Contains(scenario.AccessedMemberName))
            .ToList();

        return undefinedMemberDiags.Count == 0;
        ;
    }

}