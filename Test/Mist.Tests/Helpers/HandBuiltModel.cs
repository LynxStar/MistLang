using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Generated;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Model = Mist.ProgramModel.Model.ProgramModel;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.Tests.Helpers;

internal sealed class HandBuiltModel
{
    private readonly Dictionary<string, ClassType> primitives = new(StringComparer.Ordinal);

    public Model Model { get; } = new();

    public TypeInterner Interner => Model.TypeInterner;

    public ClassType Type(string name)
    {
        if(!primitives.TryGetValue(name, out var type))
        {
            type = new ClassType(name, "Standard", new Scope(null), null, null);
            primitives.Add(name, type);
        }

        return type;
    }

    public static ClassType Class(string name, MistType? baseType = null)
    {
        return new ClassType(name, "Probe", new Scope(null), null, null) { BaseType = baseType };
    }

    public static StructType Struct(string name)
    {
        return new StructType(name, "Probe", new Scope(null), null, null);
    }

    public static EnumType Enum(string name, IMistType underlying)
    {
        return new EnumType(name, "Probe", new Scope(null), null, null) { UnderlyingType = underlying };
    }

    public static RefinedType Refined(IMistType baseType, string? name = null)
    {
        return new RefinedType(baseType, Node(), name);
    }

    public static Newtype Newtype(string name, IMistType source)
    {
        return new Newtype(name, "Probe", new Scope(null), null, new NewtypeDeclarationNode()) { SourceType = source };
    }

    public static CastOperator Conversion(ConcreteType owner, IMistType from, IMistType to, bool isImplicit = true, bool isIntrinsic = false)
    {
        var conversion = new CastOperator(to, new Parameter("value", from), isImplicit,
            new MemberModifiers { IsStatic = true, IsIntrinsic = isIntrinsic })
        { DeclaringType = owner };
        owner.CastOperatorsList.Add(conversion);
        return conversion;
    }

    public static SyntaxNode Node(int offset = 0, string path = "hand-built.mist")
    {
        return new TrueLiteralExpressionPrimaryNode
        {
            Span = new Storm.Runtime.SourceSpan(offset, 1),
            Origin = new ParsedOrigin(new SourceText(path, new string(' ', offset + 1))),
        };
    }

    public static LiteralExpression Literal(IMistType type, object? value = null, int offset = 0)
    {
        return new LiteralExpression(Node(offset), value, type);
    }

    public static Block Block(params Statement[] statements)
    {
        return new Block(new BlockNode()) { Statements = statements };
    }

    public AnalysisContext Context(
        IProgramAnalyzer analyzer, DiagnosticBag diagnostics, object? root = null,
        IReadOnlyList<MistType>? types = null, MistType? owner = null, object? member = null,
        IReadOnlyList<ExtensionBlock>? extensions = null)
    {
        var bodies = root is null ? Array.Empty<SourceRoot>() : [new SourceRoot(root, owner, MistAssembly.Synthetic, member)];
        var nodes = bodies.SelectMany(body => BodyWalker.DescendantsAndSelf(body.Root)).ToList();
        var roots = new Dictionary<object, SourceRoot>(ReferenceEqualityComparer.Instance);
        var assignments = new Dictionary<object, AssignmentExpression>(ReferenceEqualityComparer.Instance);
        foreach(var node in nodes)
        {
            roots[node] = bodies[0];
            if(node is AssignmentExpression assignment)
            {
                assignments[assignment.Target] = assignment;
            }
        }

        return new AnalysisContext(Model, new AnalysisScope(types ?? [], bodies, extensions ?? [], []), nodes, roots, assignments,
            new HashSet<string>(StringComparer.Ordinal) { "hand-built.mist" }, diagnostics, analyzer.Id, CloneConversionRefusals.None);
    }

    public IReadOnlyList<Diagnostic> Analyze(
        IProgramAnalyzer analyzer, object? root = null, IReadOnlyList<MistType>? types = null,
        MistType? owner = null, object? member = null, IReadOnlyList<ExtensionBlock>? extensions = null)
    {
        var diagnostics = new DiagnosticBag();
        analyzer.Analyze(Context(analyzer, diagnostics, root, types, owner, member, extensions));
        return diagnostics.ToImmutableArray();
    }
}