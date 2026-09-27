using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.Tests.Helpers;

internal sealed class DeclarationFragments
{
    public HandBuiltModel Built { get; } = new();

    public static MemberModifiers Public => new() { Accessibility = Accessibility.Public };

    public List<MistType> Types { get; } = [];

    public List<TypeNaming> Namings { get; } = [];

    public List<ExtensionBlock> Extensions { get; } = [];

    public object? Root { get; set; }

    public MistType? Owner { get; set; }

    public static T At<T>(int offset = 10) where T : SyntaxNode, new()
    {
        var origin = HandBuiltModel.Node(offset);
        return new T { Span = origin.Span, Origin = origin.Origin };
    }

    public ClassType Class(string name, MistType? parent = null, Accessibility access = Accessibility.Public)
    {
        var type = new ClassType(name, "Refusal", new Scope(null), parent, At<BodyTypeTypeDeclarationNode>())
        {
            Modifiers = Public with { Accessibility = access },
        };
        Types.Add(type);
        parent?.NestedTypes.Add(type);
        return type;
    }

    public AbstractType Abstract(string name)
    {
        var type = new AbstractType(name, "Refusal", new Scope(null), null, At<BodyTypeTypeDeclarationNode>()) { Modifiers = Public };
        Types.Add(type);
        return type;
    }

    public TypeAlias Alias(string name, IMistType aliased, MistType? parent = null, Accessibility access = Accessibility.Public)
    {
        var type = new TypeAlias(name, "Refusal", new Scope(null), parent, At<TypeAliasDeclarationNode>())
        {
            AliasedType = aliased,
            Modifiers = Public with { Accessibility = access },
        };
        Types.Add(type);
        parent?.NestedTypes.Add(type);
        return type;
    }

    public static Method Method(MemberType owner, string name, IMistType returns, params IMistType[] parameters)
    {
        var method = new Method(name, returns, parameters.Select((type, i) => new Parameter($"p{i}", type)).ToList(), Public)
        {
            DeclarationNode = HandBuiltModel.Node(20),
            DeclaringType = owner,
            Scope = new Scope(owner.Scope),
        };
        owner.MethodsList.Add(method);
        return method;
    }

    public static TypeParameter Parameter(IHasTypeParameters owner, Scope scope, string name)
    {
        var parameter = new TypeParameter(name, owner.TypeParametersList.Count, scope, null);
        owner.TypeParametersList.Add(parameter);
        return parameter;
    }

    public IReadOnlyList<Diagnostic> Run(IProgramAnalyzer analyzer)
    {
        var diagnostics = new DiagnosticBag();
        var roots = Root is null ? Array.Empty<SourceRoot>() : [new SourceRoot(Root, Owner, MistAssembly.Synthetic, null)];
        var nodes = roots.SelectMany(root => BodyWalker.DescendantsAndSelf(root.Root)).ToList();
        var owners = new Dictionary<object, SourceRoot>(ReferenceEqualityComparer.Instance);
        var assigned = new Dictionary<object, AssignmentExpression>(ReferenceEqualityComparer.Instance);

        foreach(var node in nodes)
        {
            owners[node] = roots[0];

            if(node is AssignmentExpression assignment)
            {
                assigned[assignment.Target] = assignment;
            }
        }

        analyzer.Analyze(new AnalysisContext(Built.Model, new AnalysisScope(Types, roots, Extensions, Namings), nodes,
            owners, assigned, new HashSet<string>(StringComparer.Ordinal) { "hand-built.mist" }, diagnostics,
            analyzer.Id, CloneConversionRefusals.None));
        return diagnostics.ToImmutableArray();
    }

    public static Block Returning(Expression expression)
    {
        return HandBuiltModel.Block(new ReturnStatement(expression, expression.Type));
    }
}