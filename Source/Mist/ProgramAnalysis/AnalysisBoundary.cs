using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.ProgramAnalysis;

// Membership follows the syntax carried by the judged model, including retained clone syntax;
// source coordinates are used only after a diagnostic has lost its syntax identity.
internal sealed class AnalysisBoundary
{
    private readonly HashSet<SyntaxNode> nodes = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Location> locations = [];

    public AnalysisBoundary(IEnumerable<SyntaxNode> syntax)
    {
        var pending = new Stack<SyntaxNode>(syntax);

        while(pending.TryPop(out var node))
        {
            if(!nodes.Add(node))
            {
                continue;
            }

            _ = locations.Add(DiagnosticHelper.ResolveLocation(node).Location);

            foreach(var child in node.Children().OfType<SyntaxNode>())
            {
                pending.Push(child);
            }
        }
    }

    public bool Contains(SyntaxNode node)
    {
        return nodes.Contains(node);
    }

    public bool Contains(Diagnostic diagnostic)
    {
        return locations.Contains(diagnostic.Location);
    }

    public static AnalysisBoundary ForBody(SourceRoot root)
    {
        return Create([], [root], [], []);
    }

    public static AnalysisBoundary Create(
        IReadOnlyList<MistType> types,
        IReadOnlyList<SourceRoot> bodies,
        IReadOnlyList<ExtensionBlock> extensions,
        IReadOnlyList<TypeNaming> namings)
    {
        var syntax = types.Select(Declaration)
            .Concat(types.OfType<ConcreteType>().Select(type => type.NewtypeDeclaration))
            .Concat(extensions.Select(extension => extension.DeclarationNode))
            .Concat(namings.Select(naming => naming.Node))
            .Concat(bodies.SelectMany(root => BodyWalker.DescendantsAndSelf(root.Root)).Select(NodeOf));

        return new AnalysisBoundary(syntax.OfType<SyntaxNode>());
    }

    private static SyntaxNode? Declaration(MistType type)
    {
        return type switch
        {
            MemberType member => member.DeclarationNode,
            EnumType enumeration => enumeration.DeclarationNode,
            TypeAlias alias => alias.DeclarationNode,
            Newtype newtype => newtype.DeclarationNode,
            _ => null,
        };
    }

    private static SyntaxNode? NodeOf(object node)
    {
        return node switch
        {
            Expression expression => expression.Node,
            Pattern pattern => pattern.Node,
            Block block => block.Node,
            ObjectInitializerAssignment assignment => assignment.Node,
            SwitchArm arm => arm.Node,
            PropertySubpattern property => property.Node,
            _ => null,
        };
    }
}