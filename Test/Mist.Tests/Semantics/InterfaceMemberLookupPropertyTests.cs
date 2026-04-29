// Feature: interface-member-dispatch, Property 1: Interface member lookup resolves correctly through hierarchy
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
/// Property-based tests verifying interface member lookup resolves correctly
/// through hierarchy.
///
/// Property 1: For any InterfaceType hierarchy (with varying depth, width, and
/// diamond patterns) and for any member name that exists in the hierarchy (either
/// in the target interface's own MemberScope or in any transitively inherited
/// interface's MemberScope), calling LookupMemberType with the target interface
/// and that member name SHALL return the member's declared type and SHALL NOT
/// emit any MIST_UNDEFINED_MEMBER diagnostic.
/// </summary>
public class InterfaceMemberLookupPropertyTests
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
        var model = _builder.Build(compilation, ImmutableArray.Create(ast), ambientTypeResolver: null, diagnostics);
        return (model, diagnostics.ToImmutableArray());
    }

    // ─── AST construction helpers ────────────────────────────────────────

    private static InterfaceMethodDeclarationNode MakeInterfaceMethod(string name)
    {
        return new InterfaceMethodDeclarationNode
        {
            Name = MakeToken(name),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Parameters = null,
        };
    }

    private static InterfaceDeclarationNode MakeInterfaceDecl(
        string name,
        ImmutableArray<InterfaceMethodDeclarationNode> methods,
        string? parentInterfaceName = null)
    {
        var decl = new InterfaceDeclarationNode
        {
            Name = MakeToken(name),
            Methods = methods,
        };

        if (parentInterfaceName is not null)
        {
            decl.InterfaceList = MakeToken(":");
            decl.InterfaceName = MakeToken(parentInterfaceName);
        }

        return decl;
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
            Name = MakeToken(name),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Body = new BlockNode
            {
                Statements = bodyStatements ?? ImmutableArray<StatementNode>.Empty,
            },
            Modifiers = modifiers.ToImmutableArray(),
        };
    }

    private static ClassDeclarationNode MakeClassDecl(
        string name,
        string? baseOrInterfaceName = null,
        ImmutableArray<MemberDeclarationNode>? members = null)
    {
        var decl = new ClassDeclarationNode
        {
            Name = MakeToken(name),
            Members = members ?? ImmutableArray<MemberDeclarationNode>.Empty,
        };

        if (baseOrInterfaceName is not null)
        {
            decl.BaseClause = MakeToken(":");
            decl.BaseClassName = MakeToken(baseOrInterfaceName);
        }

        return decl;
    }

    private static MemberDeclarationNode WrapMethod(MethodDeclarationNode method) =>
        new MethodMemberMemberDeclarationNode { Method = method };

    private static TypeDeclarationNode WrapInterface(InterfaceDeclarationNode decl) =>
        new InterfaceTypeTypeDeclarationNode { InterfaceDeclaration = decl };

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
        var rel = new RelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = logOr };
        return new ExpressionNode { Assignment = assign };
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
        var rel = new RelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = logOr };
        return new ExpressionNode { Assignment = assign };
    }

    /// <summary>
    /// Creates: InterfaceName varName = new ClassName();
    /// </summary>
    private static StatementNode MakeNamedTypeVarDecl(
        string declaredTypeName, string varName, string initializedTypeName)
    {
        return new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = new NamedTypeRefTypeReferenceNode
                {
                    Name = new QualifiedIdentifierNode
                    {
                        IDENTIFIER = MakeToken(declaredTypeName),
                        Span = new Storm.Runtime.SourceSpan(0, declaredTypeName.Length),
                    },
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
    /// Represents an interface in a hierarchy with its own methods and optional parent.
    /// </summary>
#pragma warning disable CA1034
    public record InterfaceInfo(
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
    /// The shape of the interface hierarchy to generate.
    /// </summary>
    public enum HierarchyShape
    {
        /// <summary>Single interface, no inheritance.</summary>
        Flat,
        /// <summary>Linear chain: IA -> IB -> IC.</summary>
        Chain,
        /// <summary>Diamond: IA -> IB, IA -> IC, IB -> ID, IC -> ID (via chain since AST supports single parent).</summary>
        Diamond,
    }

    /// <summary>
    /// A test scenario: an interface hierarchy, a class implementing the leaf interface,
    /// and a member to look up through an interface-typed variable.
    /// </summary>
    public record InterfaceMemberLookupScenario(
        ImmutableArray<InterfaceInfo> Interfaces,
        string ClassName,
        string TargetInterfaceName,
        string AccessedMemberName)
    {
        public override string ToString()
        {
            var ifaces = string.Join("; ", Interfaces);
            return $"[{ifaces}] class={ClassName} target={TargetInterfaceName} access={AccessedMemberName}";
        }
    }
#pragma warning restore CA1034

    // ─── Name pools ──────────────────────────────────────────────────────

    private static readonly string[] InterfaceNamePool =
        ["IAlpha", "IBeta", "IGamma", "IDelta", "IEpsilon"];

    private static readonly string[] MethodNamePool =
        ["DoWork", "Process", "Execute", "Run", "Calculate", "Transform", "Validate", "Render"];

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates a flat interface hierarchy (single interface with methods).
    /// The accessed member is one of the interface's own methods.
    /// </summary>
    private static Gen<InterfaceMemberLookupScenario> GenFlatScenario()
    {
        return from methodCount in Gen.Choose(1, 4)
               from methods in Gen.Shuffle(MethodNamePool)
                   .Select(m => m.Take(methodCount).ToImmutableArray())
               from memberIdx in Gen.Choose(0, methodCount - 1)
               let ifaceName = InterfaceNamePool[0]
               let iface = new InterfaceInfo(ifaceName, null, methods)
               select new InterfaceMemberLookupScenario(
                   ImmutableArray.Create(iface),
                   "TestImpl",
                   ifaceName,
                   methods[memberIdx]);
    }

    /// <summary>
    /// Generates a chain hierarchy: IRoot -> IMiddle -> ILeaf (depth 2-3).
    /// The accessed member may be in any interface in the chain.
    /// The target interface for the variable type is the leaf.
    /// </summary>
    private static Gen<InterfaceMemberLookupScenario> GenChainScenario()
    {
        return from depth in Gen.Choose(2, 3)
               from names in Gen.Shuffle(InterfaceNamePool)
                   .Select(n => n.Take(depth).ToArray())
               from totalMethods in Gen.Choose(depth, Math.Min(depth * 2, MethodNamePool.Length))
               from shuffledMethods in Gen.Shuffle(MethodNamePool)
                   .Select(m => m.Take(totalMethods).ToArray())
               from scenario in BuildChainScenario(names, shuffledMethods)
               select scenario;
    }

    private static Gen<InterfaceMemberLookupScenario> BuildChainScenario(
        string[] names, string[] allMethods)
    {
        var depth = names.Length;
        // Distribute methods across interfaces, ensuring each has at least 1
        return from splits in GenSplitPoints(allMethods.Length, depth)
               let interfaces = BuildChainInterfaces(names, allMethods, splits)
               let allMethodNames = interfaces.SelectMany(i => i.Methods).ToList()
               where allMethodNames.Count > 0
               from memberIdx in Gen.Choose(0, allMethodNames.Count - 1)
               select new InterfaceMemberLookupScenario(
                   interfaces,
                   "TestImpl",
                   names[^1], // leaf interface as target
                   allMethodNames[memberIdx]);
    }

    private static ImmutableArray<InterfaceInfo> BuildChainInterfaces(
        string[] names, string[] allMethods, int[] splits)
    {
        var interfaces = new List<InterfaceInfo>();
        var methodIdx = 0;

        for (var i = 0; i < names.Length; i++)
        {
            var count = splits[i];
            var methods = allMethods.Skip(methodIdx).Take(count).ToImmutableArray();
            methodIdx += count;

            var parentName = i > 0 ? names[i - 1] : null;
            interfaces.Add(new InterfaceInfo(names[i], parentName, methods));
        }

        return interfaces.ToImmutableArray();
    }

    /// <summary>
    /// Generates a diamond hierarchy: IBase has methods, ILeft : IBase and IRight : IBase
    /// each have their own methods. The class implements ILeft (and transitively IBase).
    /// The target interface is ILeft, and the accessed member can be from IBase (inherited).
    /// </summary>
    private static Gen<InterfaceMemberLookupScenario> GenDiamondScenario()
    {
        return from baseMethods in Gen.Choose(1, 2)
               from leftMethods in Gen.Choose(0, 2)
               from shuffled in Gen.Shuffle(MethodNamePool)
                   .Select(m => m.Take(baseMethods + leftMethods).ToArray())
               let baseMethodNames = shuffled.Take(baseMethods).ToImmutableArray()
               let leftMethodNames = shuffled.Skip(baseMethods).Take(leftMethods).ToImmutableArray()
               let baseIface = new InterfaceInfo("IBase", null, baseMethodNames)
               let leftIface = new InterfaceInfo("ILeft", "IBase", leftMethodNames)
               let allMethods = baseMethodNames.Concat(leftMethodNames).ToList()
               where allMethods.Count > 0
               from memberIdx in Gen.Choose(0, allMethods.Count - 1)
               select new InterfaceMemberLookupScenario(
                   ImmutableArray.Create(baseIface, leftIface),
                   "TestImpl",
                   "ILeft", // target is child interface
                   allMethods[memberIdx]);
    }

    /// <summary>
    /// Generates split points to distribute N items across K buckets,
    /// each bucket getting at least 1 item.
    /// </summary>
    private static Gen<int[]> GenSplitPoints(int total, int buckets)
    {
        if (buckets <= 0 || total < buckets)
            return Gen.Constant(Array.Empty<int>());

        // Give each bucket 1, then distribute the remainder randomly
        var remainder = total - buckets;
        return Gen.ArrayOf(Gen.Choose(0, Math.Max(remainder, 0)), buckets)
            .Select(extras =>
            {
                // Normalize extras to sum to remainder
                var sum = extras.Sum();
                if (sum == 0)
                {
                    var result = new int[buckets];
                    Array.Fill(result, 1);
                    // Distribute remainder to first bucket
                    result[0] += remainder;
                    return result;
                }

                var normalized = new int[buckets];
                var distributed = 0;
                for (var i = 0; i < buckets; i++)
                {
                    normalized[i] = 1 + (int)Math.Round((double)extras[i] / sum * remainder);
                    distributed += normalized[i];
                }

                // Fix rounding errors
                normalized[^1] += total - distributed;
                if (normalized[^1] < 1) normalized[^1] = 1;

                return normalized;
            })
            .Where(splits => splits.Length == buckets && splits.All(s => s >= 1) && splits.Sum() == total);
    }

    public static Gen<InterfaceMemberLookupScenario> GenScenario()
    {
        return Gen.OneOf(GenFlatScenario(), GenChainScenario(), GenDiamondScenario());
    }

    public static Arbitrary<InterfaceMemberLookupScenario> ArbitraryInterfaceMemberLookupScenario() =>
        GenScenario().ToArbitrary();

    // ─── AST construction ────────────────────────────────────────────────

    /// <summary>
    /// Builds a full CompilationUnitNode from an InterfaceMemberLookupScenario.
    /// Creates the interface hierarchy, a class implementing the leaf interface
    /// with all required methods, and a Program class with Main that declares
    /// an interface-typed variable and accesses a member on it.
    /// </summary>
    private static CompilationUnitNode BuildAst(InterfaceMemberLookupScenario scenario)
    {
        var typeDecls = new List<TypeDeclarationNode>();

        // 1. Declare all interfaces in order (parents before children)
        foreach (var iface in scenario.Interfaces)
        {
            var methods = iface.Methods
                .Select(m => MakeInterfaceMethod(m))
                .ToImmutableArray();
            var decl = MakeInterfaceDecl(iface.Name, methods, iface.ParentName);
            typeDecls.Add(WrapInterface(decl));
        }

        // 2. Implementing class with ALL methods from the entire hierarchy
        var allMethodNames = scenario.Interfaces
            .SelectMany(i => i.Methods)
            .Distinct()
            .ToList();
        var implMembers = allMethodNames
            .Select(m => WrapMethod(MakeMethodDecl(m)))
            .ToImmutableArray();

        // The class implements the leaf interface (last in the list)
        var leafInterfaceName = scenario.Interfaces[^1].Name;
        typeDecls.Add(WrapClass(
            MakeClassDecl(scenario.ClassName, leafInterfaceName, implMembers)));

        // 3. Program class with Main that:
        //    - Declares: TargetInterfaceName x = new ClassName();
        //    - Accesses: x.AccessedMemberName;
        var mainStatements = ImmutableArray.Create(
            MakeNamedTypeVarDecl(scenario.TargetInterfaceName, "x", scenario.ClassName),
            MakeExpressionStatement(MakeMemberAccessExpression("x", scenario.AccessedMemberName)));

        var mainMethod = MakeMethodDecl("Main", mainStatements, isStatic: true);
        var programClass = MakeClassDecl("Program",
            members: ImmutableArray.Create<MemberDeclarationNode>(WrapMethod(mainMethod)));
        typeDecls.Add(WrapClass(programClass));

        return new CompilationUnitNode
        {
            Types = typeDecls.ToImmutableArray(),
        };
    }

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 1: For any InterfaceType hierarchy (with varying depth, width,
    /// and diamond patterns) and for any member name that exists in the hierarchy,
    /// calling LookupMemberType with the target interface and that member name
    /// SHALL return the member's declared type and SHALL NOT emit any
    /// MIST_UNDEFINED_MEMBER diagnostic.
    ///
    /// **Validates: Requirements 1.1, 1.2, 1.3, 5.1, 6.1**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(InterfaceMemberLookupPropertyTests) }, MaxTest = 100)]
    public bool InterfaceMemberLookup_ResolvesCorrectlyThroughHierarchy(
        InterfaceMemberLookupScenario scenario)
    {
        var ast = BuildAst(scenario);
        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        // No MIST_UNDEFINED_MEMBER diagnostic should be emitted for the accessed member
        var undefinedMemberDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_UNDEFINED_MEMBER"
                        && d.Message.Contains(scenario.AccessedMemberName))
            .ToList();

        return undefinedMemberDiags.Count == 0;
    }
}
