using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;

namespace Mist.Tests.Helpers;

internal sealed partial class PurityFragments
{
    public string EmittedExtensionCall(string name)
    {
        var (list, _) = IntrinsicList();
        var block = new ExtensionBlock("self", "Probe", new Scope(null), new ExtensionBlockDeclarationNode()) { ResolvedReceiverType = list, CarrierName = "ListExtensions_Probe_0" };
        var arity = name == "Equals" ? 1 : 0;
        var extension = new Method(name, integer, [.. Enumerable.Range(0, arity).Select(i => new Parameter($"p{i}", integer))], Pure) { DeclaringBlock = block };
        block.MethodsList.Add(extension);
        var l = new LocalVariable("l", list);
        Expression[] arguments = [.. Enumerable.Range(0, arity).Select(_ => (Expression)Literal(integer, 1L))];
        return Emitted(new ExpressionStatement(Call(extension, Ref(l), 0, arguments)));
    }

    public string EmittedPinnedThis(string position)
    {
        var shape = Contract("Shape");
        var square = HandBuiltModel.Class("Square");
        var point = HandBuiltModel.Struct("Point");
        var point3 = HandBuiltModel.Struct("Point3");

        switch(position)
        {
            case "constructor":
                var wrapper = HandBuiltModel.Class("Wrapper");
                var pinned = new Constructor("Wrapper", wrapper, [new Parameter("s", shape)], Pure) { DeclaringType = wrapper, Body = HandBuiltModel.Block() };
                wrapper.ConstructorsList.Add(pinned);
                wrapper.ConstructorsList.Add(new Constructor("Wrapper", wrapper, [new Parameter("s", square)], Public) { DeclaringType = wrapper, Body = HandBuiltModel.Block() });
                return Emitted(new ExpressionStatement(new NewExpression(Node(), wrapper, [new ThisExpression(Node(), shape)], pinned, [], [shape])));

            case "indexer":
                var table = HandBuiltModel.Class("Table");
                var bound = new Indexer(integer, [new Parameter("s", shape)], Pure) { DeclaringType = table, Getter = new PropertyAccessor { Body = Returning(Literal(integer, 1L)) } };
                table.IndexersList.Add(bound);
                table.IndexersList.Add(new Indexer(integer, [new Parameter("s", square)], Public) { DeclaringType = table, Getter = new PropertyAccessor { Body = Returning(Literal(integer, 2L)) } });
                var t = new LocalVariable("t", table);
                return Emitted(new ExpressionStatement(new IndexExpression(Node(), Ref(t), new ThisExpression(Node(), shape), integer) { Indexer = bound }));

            case "operator":
                var weight = HandBuiltModel.Struct("Weight");
                var plus = new Operator(OperatorKind.Plus, integer, [new Parameter("w", weight), new Parameter("s", shape)], PureStatic) { DeclaringType = weight, Body = Returning(Literal(integer, 1L)) };
                weight.OperatorsList.Add(plus);
                var w = new LocalVariable("w", weight);
                return Emitted(new ExpressionStatement(new BinaryExpression(Node(), Ref(w), BinaryOperatorKind.Add, new ThisExpression(Node(), shape), integer) { Operator = plus }));

            default:
                var target = HandBuiltModel.Struct("Target");
                var fromPoint = new CastOperator(target, new Parameter("p", point), true, PureStatic) { DeclaringType = target, Body = Returning(Literal(target)) };
                target.CastOperatorsList.Add(fromPoint);
                target.CastOperatorsList.Add(new CastOperator(target, new Parameter("p", point3), true, Static) { DeclaringType = target, Body = Returning(Literal(target)) });
                var converted = new ImplicitConversionExpression(Node(), new ThisExpression(Node(), point), target, [new ConversionFunction(fromPoint, point, target)]);
                return Emitted(new LocalDeclarationStatement(new LocalVariable("made", target), converted));
        }
    }

    public static Expression? BoundCondition()
    {
        var bound = new BindingFragments();
        var flag = HandBuiltModel.Struct("Flag");
        _ = HandBuiltModel.Conversion(flag, flag, bound.Built.Type("Boolean"));
        bound.Register(flag);
        _ = bound.Local("f", flag);
        var node = BindingFragments.At<IfStatementNode>();
        node.Condition = BindingFragments.Expression(BindingFragments.Identifier("f"));
        node.ThenBranch = BindingFragments.At<BlockNode>();
        var statement = new IfStatementStatementNode { IfStatement = node };
        return (StatementBinder.BindStatement(statement, bound.Binding, bound.Builder) as IfStatement)?.Condition;
    }

    public static Expression? BoundArraySize()
    {
        var bound = new BindingFragments();
        var meters = HandBuiltModel.Struct("Meters");
        _ = HandBuiltModel.Conversion(meters, meters, bound.Built.Type("Int32"));
        bound.Register(meters);
        _ = bound.Local("m", meters);
        var node = BindingFragments.At<ArrayCreationExpressionPrimaryNode>();
        node.Type = new PrimitiveElementTypeRefElementTypeRefNode { PrimitiveType = new IntTypePrimitiveTypeNode() };
        node.Size = BindingFragments.Expression(BindingFragments.Identifier("m"));
        return (ExpressionBinder.BindPrimary(node, bound.Binding, bound.Builder) as ArrayCreationExpression)?.Size;
    }

    public static Expression? BoundVariantPattern()
    {
        var bound = new BindingFragments();
        var tier = HandBuiltModel.Enum("Tier", bound.Built.Type("Int32"));
        tier.MembersList.Add(new EnumMember("Gold", false, null));
        bound.Register(tier);
        var named = new QualifiedIdentifierNode { IDENTIFIER = BindingFragments.Token("Tier"), IDENTIFIERs = [BindingFragments.Token("Gold")] };
        var primary = BindingFragments.At<BareTypePatternPrimaryPatternNode>();
        primary.Type = new TypeReferenceNode { First = new NamedSingleTypeRefSingleTypeRefNode { Name = named } };
        var pattern = new PatternNode
        {
            OrPattern = new OrPatternNode { Left = new AndPatternNode { Left = new DirectPatternUnaryPatternNode { Primary = primary } } },
        };
        return (PatternBinder.Bind(pattern, tier, bound.Binding, bound.Builder) as ComparisonPattern)?.Bound;
    }

    private static string Emitted(Statement statement)
    {
        var writer = new CSharpWriter();
        BodyEmitter.EmitBlock(HandBuiltModel.Block(statement), writer, new DiagnosticBag());
        return writer.ToString();
    }
}