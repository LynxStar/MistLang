using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Statements;

namespace Mist.ProgramAnalysis;

/// <summary>
/// Every node under a body root, in source order: expressions, statements, patterns, and the records
/// that hold them.
/// <para>
/// A TABLE KEYED BY NODE TYPE, rather than reflection or a switch. Reflection is out because the CLI
/// publishes trimmed, and a trimmed walker can lose the very properties it reads -- a shipped compiler
/// that silently skips subtrees, which no test of the untrimmed build would ever see. A table rather
/// than a switch because a table can be ENUMERATED: BodyWalkerCoverageTest holds its keys equal to
/// every concrete node type in the model, so a new node kind cannot escape analysis unnoticed. An
/// unknown node reaching the walker anyway is an internal error, thrown rather than skipped for the
/// same reason.
/// </para>
/// <para>
/// A refined type test's predicate is NOT a child. It is written once in its alias and reached from
/// every test of that refinement, so walking it from a use site would visit one declaration's code once
/// per use and report inside it as often.
/// </para>
/// </summary>
internal static class BodyWalker
{
    private static readonly Dictionary<Type, Func<object, IEnumerable<object?>>> Children = Build();

    internal static IReadOnlyCollection<Type> Kinds => Children.Keys;

    public static IEnumerable<object> DescendantsAndSelf(object root)
    {
        var pending = new Stack<object>();
        pending.Push(root);

        while(pending.Count > 0)
        {
            var node = pending.Pop();
            yield return node;

            if(!Children.TryGetValue(node.GetType(), out var children))
            {
                throw new InvalidOperationException(
                    $"ICE: BodyWalker has no row for {node.GetType().Name}, so nothing beneath it would be analysed.");
            }

            foreach(var child in children(node).Reverse())
            {
                if(child is not null)
                {
                    pending.Push(child);
                }
            }
        }
    }

    private static Dictionary<Type, Func<object, IEnumerable<object?>>> Build()
    {
        var table = new Dictionary<Type, Func<object, IEnumerable<object?>>>();

        void add<T>(Func<T, IEnumerable<object?>> children)
        {
            table[typeof(T)] = node => children((T)node);
        }

        add<AnonymousObjectExpression>(e => [.. e.Values]);
        add<ArrayCreationExpression>(e => [e.Size]);
        add<ArrayLiteralExpression>(e => [.. e.Elements]);
        add<AsExpression>(e => [e.Operand]);
        add<AssignmentExpression>(e => [e.Target, e.Value]);
        add<AsyncBlockExpression>(e => [e.Body]);
        add<AsyncExpression>(e => [e.Operand]);
        add<AwaitExpression>(e => [e.Operand]);
        add<BackingFieldReferenceExpression>(_ => []);
        add<BinaryExpression>(e => [e.Left, e.Right]);
        add<CastExpression>(e => [e.Operand]);
        add<CloneExpression>(e => [e.Source]);
        add<CoalesceExpression>(e => [e.Left, e.Right]);
        add<DefaultExpression>(_ => []);
        add<ErrorExpression>(_ => []);
        add<ExtensionPropertyAccessExpression>(e => [e.Receiver]);
        add<FieldReferenceExpression>(_ => []);
        add<FunctionReferenceExpression>(e => [e.Receiver]);
        add<IndexExpression>(e => [e.Receiver, e.Index]);
        add<InterpolatedStringExpression>(e => [.. e.Parts]);
        add<InterpolatedStringText>(_ => []);
        add<InterpolatedStringHole>(h => [h.Expression]);
        add<InvocationExpression>(e => [e.Receiver, .. e.Arguments]);
        add<IsPatternExpression>(e => [e.Scrutinee, e.Pattern]);
        add<LambdaExpression>(e => [e.Result, e.Body]);
        add<LiteralExpression>(_ => []);
        add<LocalReferenceExpression>(_ => []);
        add<MemberAccessExpression>(e => [e.Receiver]);
        add<MethodGroupExpression>(e => [e.Receiver]);
        add<MethodReferenceExpression>(_ => []);
        add<NamespaceReferenceExpression>(_ => []);
        add<NewExpression>(e => [.. e.Arguments, .. e.Initializer]);
        add<ObjectInitializerAssignment>(a => [a.Value]);
        add<PropertyReferenceExpression>(_ => []);
        add<RangeExpression>(e => [e.Start, e.End]);
        add<RegexLiteralExpression>(_ => []);
        add<SpreadExpression>(e => [e.Operand]);
        add<StackallocExpression>(e => [e.Size]);
        add<SwitchExpression>(e => [e.Scrutinee, .. e.Arms]);
        add<SwitchArm>(a => [a.Pattern, a.Guard, a.Result]);
        add<ThisExpression>(_ => []);
        add<TupleLiteralExpression>(e => [.. e.Elements]);
        add<TypeLiteralExpression>(_ => []);
        add<TypeReferenceExpression>(_ => []);
        add<UnaryExpression>(e => [e.Operand]);
        add<UnresolvedExpression>(_ => []);
        add<VoidLiteralExpression>(_ => []);

        add<Block>(b => [.. b.Statements]);
        add<BreakStatement>(_ => []);
        add<ExpressionStatement>(s => [s.Expression]);
        add<ForEachStatement>(s => [s.Collection, s.Body]);
        add<ForStatement>(s => [s.Initializer, s.Condition, s.Iterator, s.Body]);
        add<IfStatement>(s => [s.Condition, s.ThenBranch, s.ElseBranch]);
        add<LocalDeclarationStatement>(s => [s.Initializer]);
        add<ReturnStatement>(s => [s.Value]);
        add<TupleDeconstructionStatement>(s => [s.Initializer]);
        add<UnsupportedStatement>(_ => []);
        add<WhileStatement>(s => [s.Condition, s.Body]);
        add<WithBlockStatement>(s => [s.Value, s.Body]);

        add<CatchAllPattern>(_ => []);
        add<ComparisonPattern>(p => [p.Bound]);
        add<ConjunctionPattern>(p => [.. p.Operands]);
        add<DisjunctionPattern>(p => [.. p.Operands]);
        add<ListPattern>(p => [.. p.Elements]);
        add<ListPatternElement>(e => [e.Subpattern]);
        add<NegatedPattern>(p => [p.Operand]);
        add<PropertyPattern>(p => [.. p.Subpatterns]);
        add<PropertySubpattern>(s => [s.Pattern]);
        add<RegexPattern>(_ => []);
        add<TuplePattern>(p => [.. p.Elements]);
        add<TypeTestPattern>(_ => []);

        return table;
    }
}