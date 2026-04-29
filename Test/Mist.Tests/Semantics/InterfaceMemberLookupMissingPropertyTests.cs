// Feature: interface-member-dispatch, Property 2: Interface member lookup emits diagnostic for missing members
// **Validates: Requirements 1.4**

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
/// Property-based tests verifying interface member lookup emits diagnostic
/// for missing members.
///
/// Property 2: For any InterfaceType hierarchy and for any member name that
/// does NOT exist in the target interface's MemberScope or any transitively
/// inherited interface's MemberScope, calling LookupMemberType SHALL emit
/// exactly one MIST_UNDEFINED_MEMBER diagnostic and SHALL return UnknownType.
/// </summary>
public class InterfaceMemberLookupMissingPropertyTests
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
        var members = methods.Select(m =>
            (InterfaceMemberDeclarationNode)new InterfaceMethodMemberInterfaceMemberDeclarationNode { Method = m })
            .ToImmutableArray();
        var decl = new InterfaceDeclarationNode
        {
            Name = MakeToken(name),
            Members = members,
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
    /// A test scenario: an interface hierarchy, a class implementing the leaf interface,
    /// and a member name that does NOT exist anywhere in the hierarchy.
    /// </summary>
    public record MissingMemberScenario(
        ImmutableArray<InterfaceInfo> Interfaces,
        string ClassName,
        string TargetInterfaceName,
        string MissingMemberName)
    {
        public override string ToString()
        {
            var ifaces = string.Join("; ", Interfaces);
            return $"[{ifaces}] class={ClassName} target={TargetInterfaceName} missing={MissingMemberName}";
        }
    }
#pragma warning restore CA1034

    // ─── Name pools ──────────────────────────────────────────────────────

    private static readonly string[] InterfaceNamePool =
        ["IAlpha", "IBeta", "IGamma", "IDelta", "IEpsilon"];

    /// <summary>
    /// Methods that ARE added to interfaces in the hierarchy.
    /// </summary>
    private static readonly string[] MethodNamePool =
        ["DoWork", "Process", "Execute", "Run", "Calculate", "Transform", "Validate", "Render"];

    /// <summary>
    /// Member names that are GUARANTEED to NOT exist in any interface.
    /// This pool is completely disjoint from MethodNamePool.
    /// </summary>
    private static readonly string[] MissingMemberNamePool =
        ["Foo", "Bar", "Baz", "Qux", "Quux", "Corge", "Grault", "Garply"];

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates a flat interface hierarchy (single interface with methods)
    /// and a missing member name that does not exist in the interface.
    /// </summary>
    private static Gen<MissingMemberScenario> GenFlatScenario()
    {
        return from methodCount in Gen.Choose(1, 4)
               from methods in Gen.Shuffle(MethodNamePool)
                   .Select(m => m.Take(methodCount).ToImmutableArray())
               from missingIdx in Gen.Choose(0, MissingMemberNamePool.Length - 1)
               let ifaceName = InterfaceNamePool[0]
               let iface = new InterfaceInfo(ifaceName, null, methods)
               select new MissingMemberScenario(
                   ImmutableArray.Create(iface),
                   "TestImpl",
                   ifaceName,
                   MissingMemberNamePool[missingIdx]);
    }

    /// <summary>
    /// Generates a chain hierarchy: IRoot -> IMiddle -> ILeaf (depth 2-3).
    /// The missing member name does not exist in any interface in the chain.
    /// </summary>
    private static Gen<MissingMemberScenario> GenChainScenario()
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

    private static Gen<MissingMemberScenario> BuildChainScenario(
        string[] names, string[] allMethods)
    {
        var depth = names.Length;
        return from splits in GenSplitPoints(allMethods.Length, depth)
               let interfaces = BuildChainInterfaces(names, allMethods, splits)
               from missingIdx in Gen.Choose(0, MissingMemberNamePool.Length - 1)
               select new MissingMemberScenario(
                   interfaces,
                   "TestImpl",
                   names[^1], // leaf interface as target
                   MissingMemberNamePool[missingIdx]);
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
    /// Generates a diamond hierarchy: IBase has methods, ILeft : IBase has its own methods.
    /// The missing member name does not exist in any interface.
    /// </summary>
    private static Gen<MissingMemberScenario> GenDiamondScenario()
    {
        return from baseMethods in Gen.Choose(1, 2)
               from leftMethods in Gen.Choose(0, 2)
               from shuffled in Gen.Shuffle(MethodNamePool)
                   .Select(m => m.Take(baseMethods + leftMethods).ToArray())
               let baseMethodNames = shuffled.Take(baseMethods).ToImmutableArray()
               let leftMethodNames = shuffled.Skip(baseMethods).Take(leftMethods).ToImmutableArray()
               let baseIface = new InterfaceInfo("IBase", null, baseMethodNames)
               let leftIface = new InterfaceInfo("ILeft", "IBase", leftMethodNames)
               from missingIdx in Gen.Choose(0, MissingMemberNamePool.Length - 1)
               select new MissingMemberScenario(
                   ImmutableArray.Create(baseIface, leftIface),
                   "TestImpl",
                   "ILeft",
                   MissingMemberNamePool[missingIdx]);
    }

    /// <summary>
    /// Generates an empty interface (no methods, no parents) scenario.
    /// Any member access should produce a diagnostic.
    /// </summary>
    private static Gen<MissingMemberScenario> GenEmptyInterfaceScenario()
    {
        return from missingIdx in Gen.Choose(0, MissingMemberNamePool.Length - 1)
               let ifaceName = InterfaceNamePool[0]
               let iface = new InterfaceInfo(ifaceName, null, ImmutableArray<string>.Empty)
               select new MissingMemberScenario(
                   ImmutableArray.Create(iface),
                   "TestImpl",
                   ifaceName,
                   MissingMemberNamePool[missingIdx]);
    }

    /// <summary>
    /// Generates split points to distribute N items across K buckets,
    /// each bucket getting at least 1 item.
    /// </summary>
    private static Gen<int[]> GenSplitPoints(int total, int buckets)
    {
        if (buckets <= 0 || total < buckets)
            return Gen.Constant(Array.Empty<int>());

        var remainder = total - buckets;
        return Gen.ArrayOf(Gen.Choose(0, Math.Max(remainder, 0)), buckets)
            .Select(extras =>
            {
                var sum = extras.Sum();
                if (sum == 0)
                {
                    var result = new int[buckets];
                    Array.Fill(result, 1);
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

                normalized[^1] += total - distributed;
                if (normalized[^1] < 1) normalized[^1] = 1;

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
            GenEmptyInterfaceScenario());
    }

    public static Arbitrary<MissingMemberScenario> ArbitraryMissingMemberScenario() =>
        GenScenario().ToArbitrary();

    // ─── AST construction ────────────────────────────────────────────────

    /// <summary>
    /// Builds a full CompilationUnitNode from a MissingMemberScenario.
    /// Creates the interface hierarchy, a class implementing the leaf interface
    /// with all required methods, and a Program class with Main that declares
    /// an interface-typed variable and accesses a member that does NOT exist.
    /// </summary>
    private static CompilationUnitNode BuildAst(MissingMemberScenario scenario)
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
        //    - Accesses: x.MissingMemberName;  (this member does NOT exist)
        var mainStatements = ImmutableArray.Create(
            MakeNamedTypeVarDecl(scenario.TargetInterfaceName, "x", scenario.ClassName),
            MakeExpressionStatement(MakeMemberAccessExpression("x", scenario.MissingMemberName)));

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
    /// Property 2: For any InterfaceType hierarchy and for any member name that
    /// does NOT exist in the target interface's MemberScope or any transitively
    /// inherited interface's MemberScope, calling LookupMemberType SHALL emit
    /// exactly one MIST_UNDEFINED_MEMBER diagnostic and SHALL return UnknownType.
    ///
    /// **Validates: Requirements 1.4**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(InterfaceMemberLookupMissingPropertyTests) }, MaxTest = 100)]
    public bool InterfaceMemberLookup_EmitsDiagnosticForMissingMembers(
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
    }
}
