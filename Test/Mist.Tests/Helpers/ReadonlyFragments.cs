using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;

namespace Mist.Tests.Helpers;

internal sealed class ReadonlyFragments
{
    public static IReadOnlyList<Diagnostic> Analyze(string name)
    {
        var model = new HandBuiltModel();
        var owner = HandBuiltModel.Class("Counter");
        var integer = model.Type("Int32");
        var field = new Field("Count", integer, default, new MemberModifiers { IsReadonly = name.StartsWith("readonly-field-", StringComparison.Ordinal) })
        {
            DeclaringType = owner,
        };
        owner.FieldsList.Add(field);
        var parameter = new Parameter("c", owner, new ParameterModifier(true, false, default));
        var local = new LocalVariable("c", owner) { Parameter = parameter };
        var throughParameter = new LocalReferenceExpression(HandBuiltModel.Node(), local);
        var explicitThis = new ThisExpression(HandBuiltModel.Node(), owner);
        var bodyWrites = HandBuiltModel.Block(new ExpressionStatement(new AssignmentExpression(HandBuiltModel.Node(),
            new FieldReferenceExpression(HandBuiltModel.Node(), field), HandBuiltModel.Literal(integer, 1L), integer)));
        var mutable = new Method("Bump", integer, [], default) { Body = bodyWrites, DeclaringType = owner };
        var property = new Property("Level", integer, default)
        {
            Getter = new PropertyAccessor { IsAuto = true },
            Setter = new PropertyAccessor { IsAuto = true },
            DeclaringType = owner,
        };
        var indexer = new Indexer(integer, [new Parameter("index", integer)], default)
        {
            Getter = new PropertyAccessor { Body = bodyWrites },
            Setter = new PropertyAccessor { Body = bodyWrites },
            DeclaringType = owner,
        };
        Expression root;
        var isReadonly = name.StartsWith("readonly-method-", StringComparison.Ordinal)
            || name is "field-assigned-in-readonly-method" or "field-assigned-in-readonly-type";

        switch(name)
        {
            case "field-assigned-on-readonly-parameter":
                root = Write(Access(throughParameter, field));
                break;
            case "field-assigned-in-readonly-method":
            case "field-assigned-in-readonly-type":
            case "readonly-field-assigned-outside-constructor":
                root = Write(Access(explicitThis, field));
                break;
            case "readonly-field-assigned-from-outside-the-type":
                root = Write(Access(new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("c", owner)), field));
                break;
            case "readonly-block-in-constructor-assigns-field":
                var nested = HandBuiltModel.Block(new ExpressionStatement(Write(new FieldReferenceExpression(HandBuiltModel.Node(37), field))));
                nested.IsReadonly = true;
                var body = HandBuiltModel.Block(nested);
                var constructor = new Constructor("Counter", owner, [], new MemberModifiers { IsReadonly = true }) { Body = body, DeclaringType = owner };
                return model.Analyze(new ReadonlyAnalyzer(), body, owner: owner, member: constructor);
            case "readonly-method-calls-mutable-method":
            case "readonly-parameter-calls-mutable-method":
                var receiver = name == "readonly-parameter-calls-mutable-method" ? (Expression)throughParameter : explicitThis;
                root = new InvocationExpression(HandBuiltModel.Node(37), new MemberAccessExpression(HandBuiltModel.Node(), receiver, "Bump", integer), [], integer, mutable, []);
                break;
            case "readonly-method-calls-mutable-operator":
                root = new BinaryExpression(HandBuiltModel.Node(37), explicitThis, BinaryOperatorKind.Add, explicitThis, owner)
                {
                    Operator = new Operator(OperatorKind.Plus, owner, [new Parameter("a", owner), new Parameter("b", owner)], new MemberModifiers { IsStatic = true })
                    {
                        Body = bodyWrites,
                        DeclaringType = owner,
                    },
                };
                break;
            case "readonly-method-allocates-mutable":
                var allocating = new Constructor("Counter", owner, [], default)
                {
                    Body = HandBuiltModel.Block(new ExpressionStatement(new InvocationExpression(HandBuiltModel.Node(),
                        HandBuiltModel.Literal(integer), [], integer, mutable, []))),
                    DeclaringType = owner,
                };
                root = new NewExpression(HandBuiltModel.Node(37), owner, [], allocating, [], []);
                break;
            case "readonly-method-sets-property":
            case "readonly-method-coalesces-property":
                var set = new PropertyReferenceExpression(HandBuiltModel.Node(37), property);
                root = new AssignmentExpression(HandBuiltModel.Node(37), set, HandBuiltModel.Literal(integer, 1L), integer,
                    isCoalescing: name == "readonly-method-coalesces-property");
                break;
            case "readonly-parameter-sets-property":
                var parameterSet = new MemberAccessExpression(HandBuiltModel.Node(37), throughParameter, "Level", integer,
                    member: new PropertyResult(property, owner, []));
                root = Write(parameterSet);
                break;
            case "readonly-method-reads-mutable-indexer":
                root = new IndexExpression(HandBuiltModel.Node(37), explicitThis, HandBuiltModel.Literal(integer, 0L), integer) { Indexer = indexer };
                break;
            case "readonly-method-sets-indexer-on-local":
                var indexed = new IndexExpression(HandBuiltModel.Node(37), new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("grid", owner)),
                    HandBuiltModel.Literal(integer, 0L), integer)
                { Indexer = indexer };
                root = Write(indexed);
                break;
            case "readonly-method-writes-through-index":
                var array = model.Interner.GetOrCreateConstructedType(model.Type("Array<,>"), [integer]);
                var cells = new Field("cells", array, default, default) { DeclaringType = owner };
                root = Write(new IndexExpression(HandBuiltModel.Node(37), new FieldReferenceExpression(HandBuiltModel.Node(), cells), HandBuiltModel.Literal(integer, 0L), integer));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(name));
        }

        var method = new Method("Peek", integer, [], new MemberModifiers { IsReadonly = isReadonly })
        {
            Body = HandBuiltModel.Block(new ExpressionStatement(root)),
            DeclaringType = owner,
        };
        return model.Analyze(new ReadonlyAnalyzer(), method.Body, owner: owner, member: method);
    }

    private static MemberAccessExpression Access(Expression receiver, Field field)
    {
        return new MemberAccessExpression(HandBuiltModel.Node(37), receiver, field.Name, field.Type, member: new FieldResult(field, receiver.Type, []));
    }

    private static AssignmentExpression Write(Expression target)
    {
        return new AssignmentExpression(HandBuiltModel.Node(37), target, HandBuiltModel.Literal(target.Type, 1L), target.Type);
    }
}