using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Helpers;

internal sealed partial class PuritySpecFragments
{
    private IReadOnlyList<Diagnostic>? Inference(string name)
    {
        return name switch
        {
            "helper-reading-a-readonly-static" => ThroughHelper(Returning(Arithmetic(StaticRead(limit), BinaryOperatorKind.Add, Number(1)))),
            "helper-writing-an-array-it-allocated" => ThroughHelper(FillingAnArray()),
            "helper-calling-a-pure-helper" => ThroughHelper(Returning(Call(Method(host, "Helper2", integer, Static, Returning(Number(1)))))),
            "helper-reading-a-mutable-static" => ThroughHelper(Returning(StaticRead(counter))),
            "helper-writing-a-static" => ThroughHelper(HandBuiltModel.Block(Set(StaticRead(counter), Number(1)), Return(Number(1)))),
            "helper-calling-an-impure-helper" => ThroughHelper(Returning(Call(Method(host, "Helper2", integer, Static, Returning(StaticRead(counter)))))),
            "mutually-recursive-helpers" => MutuallyRecursiveHelpers(),
            "self-recursive-helper" => SelfRecursiveHelper(),
            "abstract-member-with-an-impure-implementation" => ThroughAnAbstract(
                Public,
                ("Square", Public, Returning(Number(4))),
                ("Noisy", Public, HandBuiltModel.Block(Set(StaticRead(counter), Number(1)), Return(Number(1))))),
            "abstract-member-whose-every-implementation-is-pure" => ThroughAnAbstract(
                Public,
                ("Square", Public, Returning(Number(4))),
                ("Round", Public, Returning(StaticRead(limit)))),
            "abstract-member-with-no-implementation" => ThroughAnAbstract(Public),
            "abstract-member-declared-pure" => ThroughAnAbstract(Pure, ("Square", Pure, Returning(Number(4)))),
            "auto-getter-over-stable-storage-read-through-an-external-receiver" => ThroughAnAutoGetter(settable: false, OwnershipKind.Unrestricted),
            "auto-getter-over-mutable-storage-read-through-a-frozen-input" => ThroughAnAutoGetter(settable: true, OwnershipKind.Frozen),
            "auto-getter-over-mutable-storage-read-through-a-fresh-local" => ThroughAnAutoGetterOfALocal(owned: false),
            "auto-getter-over-mutable-storage-set-and-read-through-an-owned-local" => ThroughAnAutoGetterOfALocal(owned: true),
            "auto-getter-over-mutable-storage-read-through-an-external-receiver" => ThroughAnAutoGetter(settable: true, OwnershipKind.Unrestricted),
            "getter-with-a-body-over-stable-fields" => ThroughAGetterOverStableFields(),
            "undeclared-constructor-assigning-its-own-fields" => ThroughAnUndeclaredConstructor(writesAStatic: false),
            "undeclared-constructor-writing-a-static" => ThroughAnUndeclaredConstructor(writesAStatic: true),
            _ => null,
        };
    }

    private IReadOnlyList<Diagnostic> ThroughHelper(Block helperBody)
    {
        var helper = Method(host, "Helper", integer, Static, helperBody);
        return ThroughCaller(Returning(Call(helper, null, 37)));
    }

    private IReadOnlyList<Diagnostic> ThroughCaller(Block callerBody, MistType[]? types = null, params LocalVariable[] inputs)
    {
        var caller = Method(host, "Caller", integer, PureStatic, callerBody, inputs);
        return Judge(new PurityAnalyzer(), caller, types ?? []);
    }

    private Block FillingAnArray()
    {
        var xs = Variable("xs", ArrayOfInt());
        return HandBuiltModel.Block(
            Declare(xs, new ArrayCreationExpression(Node(), integer, Number(2), xs.Type)),
            Set(Element(xs, integer), Number(1)),
            Return(Element(xs, integer)));
    }

    private IReadOnlyList<Diagnostic> MutuallyRecursiveHelpers()
    {
        var n = Input("n", integer);
        var m = Input("m", integer);
        var pingBody = HandBuiltModel.Block();
        var pongBody = HandBuiltModel.Block();
        var ping = Method(host, "Ping", integer, Static, pingBody, n);
        var pong = Method(host, "Pong", integer, Static, pongBody, m);
        pingBody.Statements =
        [
            new IfStatement(Compare(Ref(n), BinaryOperatorKind.Equal, Number(0)), Returning(Number(0)), null),
            Return(Call(pong, null, 0, Arithmetic(Ref(n), BinaryOperatorKind.Subtract, Number(1)))),
        ];
        pongBody.Statements = [Return(Call(ping, null, 0, Ref(m)))];
        return ThroughCaller(Returning(Call(ping, null, 37, Number(3))));
    }

    private IReadOnlyList<Diagnostic> SelfRecursiveHelper()
    {
        var n = Input("n", integer);
        var body = HandBuiltModel.Block();
        var fact = Method(host, "Fact", integer, Static, body, n);
        body.Statements =
        [
            new IfStatement(Compare(Ref(n), BinaryOperatorKind.LessThanOrEqual, Number(1)), Returning(Number(1)), null),
            Return(Arithmetic(Ref(n), BinaryOperatorKind.Multiply, Call(fact, null, 0, Arithmetic(Ref(n), BinaryOperatorKind.Subtract, Number(1))))),
        ];
        return ThroughCaller(Returning(Call(fact, null, 37, Number(3))));
    }

    private IReadOnlyList<Diagnostic> ThroughAnAbstract(MemberModifiers slot, params (string Name, MemberModifiers Modifiers, Block Body)[] implementations)
    {
        var shape = Contract("Shape");
        var area = Method(shape, "Area", integer, slot, null);
        var types = new List<MistType> { shape };

        foreach(var (name, modifiers, body) in implementations)
        {
            var implementer = Implementer(name, shape);
            _ = Method(implementer, "Area", integer, modifiers, body);
            types.Add(implementer);
        }

        var s = Input("s", shape);
        return ThroughCaller(Returning(Call(area, Ref(s), 37)), [.. types], s);
    }

    private IReadOnlyList<Diagnostic> ThroughAnAutoGetter(bool settable, OwnershipKind receiver)
    {
        var cell = HandBuiltModel.Class("Cell");
        var property = AutoProperty(cell, settable ? "Level" : "Fixed", integer, settable);
        var c = Input("c", cell, receiver);
        return ThroughCaller(Returning(Read(Ref(c), property, 37)), [cell], c);
    }

    private IReadOnlyList<Diagnostic> ThroughAnAutoGetterOfALocal(bool owned)
    {
        var cell = HandBuiltModel.Class("Cell");
        var level = AutoProperty(cell, "Level", integer, settable: true);
        var c = Variable("c", cell, owned ? OwnershipKind.Owned : OwnershipKind.Unrestricted);
        var statements = new List<Statement> { Declare(c, Alloc(cell, null)) };

        if(owned)
        {
            statements.Add(Set(Read(Ref(c), level), Number(2)));
        }

        statements.Add(Return(Read(Ref(c), level, 37)));
        return ThroughCaller(HandBuiltModel.Block([.. statements]), [cell]);
    }

    private IReadOnlyList<Diagnostic> ThroughAGetterOverStableFields()
    {
        var jar = HandBuiltModel.Class("Jar");
        var a = Field(jar, "A", integer, isReadonly: true);
        var seed = Input("a", integer);
        _ = Constructor(jar, Pure, HandBuiltModel.Block(Set(Own(a), Ref(seed))), seed);
        var twice = new Property("Twice", integer, Public)
        {
            DeclaringType = jar,
            Getter = new PropertyAccessor { Body = Returning(Arithmetic(Own(a), BinaryOperatorKind.Multiply, Number(2))) },
        };
        jar.PropertiesList.Add(twice);
        var j = Input("j", jar);
        return ThroughCaller(Returning(Read(Ref(j), twice, 37)), [jar], j);
    }

    private IReadOnlyList<Diagnostic> ThroughAnUndeclaredConstructor(bool writesAStatic)
    {
        var point = HandBuiltModel.Class("Point");
        var x = Field(point, "X", integer);
        var seed = Input("x", integer);
        var statements = new List<Statement> { Set(Own(x), Ref(seed)) };

        if(writesAStatic)
        {
            statements.Add(Set(StaticRead(counter), Ref(seed)));
        }

        var constructor = Constructor(point, Public, HandBuiltModel.Block([.. statements]), seed);
        var p = Variable("p", point);
        return ThroughCaller(HandBuiltModel.Block(Declare(p, Alloc(point, constructor, 37, Number(1))), Return(Access(Ref(p), x))), [point]);
    }
}