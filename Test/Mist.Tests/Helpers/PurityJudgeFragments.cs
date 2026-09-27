using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Rules;
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

internal sealed class PurityJudgeFragments
{
    private static readonly MemberModifiers Public = new() { Accessibility = Accessibility.Public };
    private static readonly MemberModifiers Pure = Public with { IsPure = true, IsReadonly = true };
    private static readonly MemberModifiers Local = Public with { IsLocal = true };
    private static readonly MemberModifiers LocalReadonly = Local with { IsReadonly = true };
    private static readonly MemberModifiers Readonly = Public with { IsReadonly = true };
    private static readonly MemberModifiers Static = Public with { IsStatic = true };

    private readonly HandBuiltModel model = new();
    private readonly ClassType integer;
    private readonly ClassType text;
    private readonly ClassType unit;
    private readonly ConstructedType integers;
    private readonly Operator plus;
    private readonly ClassType host = HandBuiltModel.Class("Host");
    private readonly ClassType bag = HandBuiltModel.Class("Bag");
    private readonly ClassType vec = HandBuiltModel.Class("Vec");
    private readonly StructType pt = HandBuiltModel.Struct("Pt");
    private readonly ClassType holder = HandBuiltModel.Class("Holder");
    private readonly ClassType loud = HandBuiltModel.Class("Loud");
    private readonly ClassType res = HandBuiltModel.Class("Res");
    private readonly ClassType globals = HandBuiltModel.Class("Globals");
    private readonly Field count;
    private readonly Field seed;
    private readonly Field mine;
    private readonly Field shared;
    private readonly Field stored;
    private readonly Field axis;
    private readonly Field coordinate;
    private readonly Field item;
    private readonly Field caption;
    private readonly Field point;
    private readonly Field n;
    private readonly Field volume;
    private readonly Field counter;
    private readonly Field limit;
    private readonly Field pool;
    private readonly Constructor bagConstructor;
    private readonly Constructor vecConstructor;
    private readonly Constructor loudConstructor;
    private readonly Method bagAdd;
    private readonly Method bagSize;
    private readonly Method bagPeek;
    private readonly Method bagPoke;
    private readonly Method hostAdd;
    private readonly Method hostSize;
    private readonly Method hostPeek;
    private readonly Method hostPoke;
    private readonly Method doubled;
    private readonly Method keep;
    private readonly Method label;

    public PurityJudgeFragments()
    {
        integer = Primitive("Int32");
        text = Primitive("String");
        unit = Primitive("Void");
        integers = model.Interner.GetOrCreateConstructedType(model.Type("Array<,>"), [integer, model.Interner.GetOrCreateUnknownValue(integer)]);
        plus = new Operator(OperatorKind.Plus, integer, [new Parameter("a", integer), new Parameter("b", integer)], Pure with { IsStatic = true, IsIntrinsic = true })
        {
            DeclaringType = integer,
        };
        integer.OperatorsList.Add(plus);
        counter = Field(globals, "Counter", integer, Static);
        limit = Field(globals, "Limit", integer, Static with { IsReadonly = true });
        pool = Field(globals, "Pool", bag, Static with { IsReadonly = true });
        stored = Field(bag, "Count", integer, Public);
        bagConstructor = Constructor(bag, Pure);
        (bagAdd, bagSize, bagPeek, bagPoke) = Counting(bag, stored);
        axis = Field(vec, "X", integer, Readonly);
        vec.Modifiers = Pure;
        var start = Input("x", integer);
        vecConstructor = Constructor(vec, Pure, HandBuiltModel.Block(Set(Bare(axis), Ref(start))), start);
        doubled = Method(vec, "Doubled", integer, Pure, Returning(Sum(Bare(axis), Bare(axis))));
        coordinate = Field(pt, "X", integer, Public);
        item = Field(holder, "Item", bag, Public);
        caption = Field(holder, "Text", text, Public);
        point = Field(holder, "P", pt, Public);
        n = Field(holder, "N", integer, Public);
        var kept = Input("b", bag);
        keep = Method(holder, "Keep", unit, Local, HandBuiltModel.Block(Set(Bare(item), Ref(kept))), kept);
        var labelled = Input("s", text);
        label = Method(holder, "Label", unit, Local, HandBuiltModel.Block(Set(Bare(caption), Ref(labelled))), labelled);
        volume = Field(loud, "V", integer, Public);
        loudConstructor = Constructor(loud, Public, HandBuiltModel.Block(Set(StaticRead(counter), Lit(1))));
        _ = Method(res, "Drop", unit, Public, HandBuiltModel.Block(Set(StaticRead(counter), Lit(1))));
        count = Field(host, "Count", integer, Public);
        seed = Field(host, "Fixed", integer, Readonly);
        mine = Field(host, "Mine", bag, Public, OwnershipKind.Owned);
        shared = Field(host, "Shared", bag, Public);
        (hostAdd, hostSize, hostPeek, hostPoke) = Counting(host, count);
    }

    public IReadOnlyList<Diagnostic> Analyze(string member)
    {
        var signature = member.IndexOf(" M(", StringComparison.Ordinal);
        var header = member[..signature];
        var modifiers = header.Contains("pure", StringComparison.Ordinal) ? Pure
            : header.Contains("readonly", StringComparison.Ordinal) ? LocalReadonly
            : Local;
        var returns = header.EndsWith("void", StringComparison.Ordinal) ? unit : integer;
        var (inputs, statements) = Body(member[(signature + 1)..]);
        var method = Method(host, "M", returns, modifiers, HandBuiltModel.Block(statements), inputs);
        return model.Analyze(new PurityAnalyzer(), method.Body, [host, bag, vec, pt, holder, loud, res, globals], host, method);
    }

    private (LocalVariable[] Inputs, Statement[] Statements) Body(string key)
    {
        var b = new LocalVariable("b", bag);
        var ownedBag = new LocalVariable("b", bag) { Ownership = OwnershipKind.Owned };
        var f = Input("f", bag, OwnershipKind.Frozen);
        var x = Input("x", bag);
        var other = Input("other", bag);
        var v = Input("v", vec);
        var frozenVec = Input("v", vec, OwnershipKind.Frozen);
        var freshVec = new LocalVariable("v", vec);
        var s = Input("s", text);
        var p = Input("p", pt);
        var xs = new LocalVariable("xs", integers);
        var handed = Input("xs", integers);
        var t = new LocalVariable("t", integer);
        var element = new LocalVariable("x", integer);
        var h = new LocalVariable("h", holder);
        var l = new LocalVariable("l", loud);
        var r = new LocalVariable("r", res);
        var greeting = new LocalVariable("s", text);
        var promised = Input("f", Function(pure: true));
        var plain = Input("f", Function(pure: false));
        var held = new LocalVariable("f", Function(pure: false));
        var argument = new LocalVariable("n", integer);

        return key switch
        {
            "M() { var b = new Bag(); b.Count = 1; return b.Count; }" => ([], [FreshBag(b), Set(Access(Ref(b), stored), Lit(1)), Return(Access(Ref(b), stored))]),
            "M() { owned var b = new Bag(); b.Count = 1; return b.Count; }" => ([], [FreshBag(ownedBag), Set(Access(Ref(ownedBag), stored), Lit(1)), Return(Access(Ref(ownedBag), stored))]),
            "M() { var b = new Bag(); b.Count = 1; }" => ([], [FreshBag(b), Set(Access(Ref(b), stored), Lit(1))]),
            "M() { int[] xs = [0, 0]; xs[0] = 1; return xs[0]; }" => ([], [Declare(xs, Elements(0, 0)), Set(Index(xs), Lit(1)), Return(Index(xs))]),
            "M() { int[] xs = [0, 0]; xs[0] = 1; }" => ([], [Declare(xs, Elements(0, 0)), Set(Index(xs), Lit(1))]),
            "M() { Count = 1; return 1; }" => ([], [Set(Bare(count), Lit(1)), Return(Lit(1))]),
            "M() { Count = 1; }" => ([], [Set(Bare(count), Lit(1))]),
            "M() { Mine.Count = 1; }" => ([], [Set(Access(Bare(mine), stored), Lit(1))]),
            "M() { Shared.Count = 1; }" => ([], [Set(Access(Bare(shared), stored), Lit(1))]),
            "M() { Globals.Counter = 1; return 1; }" => ([], [Set(StaticRead(counter), Lit(1)), Return(Lit(1))]),
            "M() { Globals.Counter = 1; }" => ([], [Set(StaticRead(counter), Lit(1))]),
            "M(frozen Bag f) { f.Count = 1; return 1; }" => ([f], [Set(Access(Ref(f), stored), Lit(1)), Return(Lit(1))]),
            "M(frozen Bag f) { f.Count = 1; }" => ([f], [Set(Access(Ref(f), stored), Lit(1))]),
            "M(Bag x) { x.Count = 1; return 1; }" => ([x], [Set(Access(Ref(x), stored), Lit(1)), Return(Lit(1))]),
            "M(Bag x) { x.Count = 1; }" => ([x], [Set(Access(Ref(x), stored), Lit(1))]),
            "M() { return Fixed; }" => ([], [Return(Bare(seed))]),
            "M() { return Globals.Limit; }" => ([], [Return(StaticRead(limit))]),
            "M(Vec v) { return v.X; }" => ([v], [Return(Access(Ref(v), axis))]),
            "M(Pt p) { return p.X; }" => ([p], [Return(Access(Ref(p), coordinate))]),
            "M() { var b = new Bag(); return b.Count; }" => ([], [FreshBag(b), Return(Access(Ref(b), stored))]),
            "M() { int[] xs = [1]; return xs[0]; }" => ([], [Declare(xs, Elements(1)), Return(Index(xs))]),
            "M(frozen Bag f) { return f.Count; }" => ([f], [Return(Access(Ref(f), stored))]),
            "M() { return Count; }" => ([], [Return(Bare(count))]),
            "M() { return Globals.Counter; }" => ([], [Return(StaticRead(counter))]),
            "M(Bag x) { return x.Count; }" => ([x], [Return(Access(Ref(x), stored))]),
            "M() { return Shared.Count; }" => ([], [Return(Access(Bare(shared), stored))]),
            "M() { return Mine.Count; }" => ([], [Return(Access(Bare(mine), stored))]),
            "M(Vec v) { return v.Doubled(); }" => ([v], [Return(Call(doubled, Ref(v)))]),
            "M(frozen Vec v) { return v.Doubled(); }" => ([frozenVec], [Return(Call(doubled, Ref(frozenVec)))]),
            "M() { var v = new Vec(1); return v.Doubled(); }" => ([], [Declare(freshVec, New(vec, vecConstructor, Lit(1))), Return(Call(doubled, Ref(freshVec)))]),
            "M() { var v = new Vec(1); return v.X; }" => ([], [Declare(freshVec, New(vec, vecConstructor, Lit(1))), Return(Access(Ref(freshVec), axis))]),
            "M() { var b = new Bag(); return b.Size(); }" => ([], [FreshBag(b), Return(Call(bagSize, Ref(b)))]),
            "M(frozen Bag f) { return f.Size(); }" => ([f], [Return(Call(bagSize, Ref(f)))]),
            "M(Bag x) { return x.Size(); }" => ([x], [Return(Call(bagSize, Ref(x)))]),
            "M() { return Mine.Size(); }" => ([], [Return(Call(bagSize, Bare(mine)))]),
            "M() { return Shared.Size(); }" => ([], [Return(Call(bagSize, Bare(shared)))]),
            "M() { return Size(); }" => ([], [Return(Call(hostSize, null))]),
            "M() { var b = new Bag(); b.Add(1); return b.Count; }" => ([], [FreshBag(b), Statement(Call(bagAdd, Ref(b), Lit(1))), Return(Access(Ref(b), stored))]),
            "M() { var b = new Bag(); b.Add(1); }" => ([], [FreshBag(b), Statement(Call(bagAdd, Ref(b), Lit(1)))]),
            "M() { var b = new Bag(); b.Add(Globals.Limit); return 1; }" => ([], [FreshBag(b), Statement(Call(bagAdd, Ref(b), StaticRead(limit))), Return(Lit(1))]),
            "M(frozen Bag f) { f.Add(1); return 1; }" => ([f], [Statement(Call(bagAdd, Ref(f), Lit(1))), Return(Lit(1))]),
            "M(Bag x) { x.Add(1); return 1; }" => ([x], [Statement(Call(bagAdd, Ref(x), Lit(1))), Return(Lit(1))]),
            "M() { Add(1); }" => ([], [Statement(Call(hostAdd, null, Lit(1)))]),
            "M() { Mine.Add(1); }" => ([], [Statement(Call(bagAdd, Bare(mine), Lit(1)))]),
            "M() { Shared.Add(1); }" => ([], [Statement(Call(bagAdd, Bare(shared), Lit(1)))]),
            "M() { var b = new Bag(); return b.Peek(); }" => ([], [FreshBag(b), Return(Call(bagPeek, Ref(b)))]),
            "M(frozen Bag f) { return f.Peek(); }" => ([f], [Return(Call(bagPeek, Ref(f)))]),
            "M(Bag x) { return x.Peek(); }" => ([x], [Return(Call(bagPeek, Ref(x)))]),
            "M() { return Peek(); }" => ([], [Return(Call(hostPeek, null))]),
            "M() { return Shared.Peek(); }" => ([], [Return(Call(bagPeek, Bare(shared)))]),
            "M() { var b = new Bag(); b.Poke(); return 1; }" => ([], [FreshBag(b), Statement(Call(bagPoke, Ref(b))), Return(Lit(1))]),
            "M() { var b = new Bag(); b.Poke(); }" => ([], [FreshBag(b), Statement(Call(bagPoke, Ref(b)))]),
            "M(frozen Bag f) { f.Poke(); return 1; }" => ([f], [Statement(Call(bagPoke, Ref(f))), Return(Lit(1))]),
            "M(Bag x) { x.Poke(); return 1; }" => ([x], [Statement(Call(bagPoke, Ref(x))), Return(Lit(1))]),
            "M() { Poke(); }" => ([], [Statement(Call(hostPoke, null))]),
            "M() { Shared.Poke(); }" => ([], [Statement(Call(bagPoke, Bare(shared)))]),
            "M(pure func<int, int> f) { return f(1); }" => ([promised], [Return(Invoke(promised, Lit(1)))]),
            "M(func<int, int> f) { return f(1); }" => ([plain], [Return(Invoke(plain, Lit(1)))]),
            "M() { var h = new Holder(); return h.N; }" => ([], [Declare(h, New(holder, null)), Return(Access(Ref(h), n))]),
            "M() { int[] xs = new int[2]; return xs[0]; }" => ([], [Declare(xs, new ArrayCreationExpression(Node(), integer, Lit(2), integers)), Return(Index(xs))]),
            "M() { var f = (int n) => n + 1; return 1; }" => ([], [Declare(held, new LambdaExpression(Node(), [argument], Sum(Ref(argument), Lit(1)), null, false, held.Type)), Return(Lit(1))]),
            "M() { var l = new Loud(); return l.V; }" => ([], [Declare(l, New(loud, loudConstructor)), Return(Access(Ref(l), volume))]),
            "M(frozen Bag f) { var h = new Holder(); h.Item = f; return 1; }" => ([f], [Declare(h, New(holder, null)), Set(Access(Ref(h), item), Ref(f)), Return(Lit(1))]),
            "M(frozen Bag f) { var h = new Holder(); h.Keep(f); return 1; }" => ([f], [Declare(h, New(holder, null)), Statement(Call(keep, Ref(h), Ref(f))), Return(Lit(1))]),
            "M(frozen Bag f) { var h = new Holder(); h.Keep(f); }" => ([f], [Declare(h, New(holder, null)), Statement(Call(keep, Ref(h), Ref(f)))]),
            "M() { var h = new Holder(); h.Item = Globals.Pool; return 1; }" => ([], [Declare(h, New(holder, null)), Set(Access(Ref(h), item), StaticRead(pool)), Return(Lit(1))]),
            "M() { Shared = Globals.Pool; }" => ([], [Set(Bare(shared), StaticRead(pool))]),
            "M(Bag other) { Shared = other; }" => ([other], [Set(Bare(shared), Ref(other))]),
            "M(Bag other) { Mine = other; }" => ([other], [Set(Bare(mine), Ref(other))]),
            "M(string s) { var h = new Holder(); h.Text = s; return 1; }" => ([s], [Declare(h, New(holder, null)), Set(Access(Ref(h), caption), Ref(s)), Return(Lit(1))]),
            "M(string s) { var h = new Holder(); h.Text = s; }" => ([s], [Declare(h, New(holder, null)), Set(Access(Ref(h), caption), Ref(s))]),
            "M(Pt p) { var h = new Holder(); h.P = p; return 1; }" => ([p], [Declare(h, New(holder, null)), Set(Access(Ref(h), point), Ref(p)), Return(Lit(1))]),
            "M(Pt p) { var h = new Holder(); h.P = p; }" => ([p], [Declare(h, New(holder, null)), Set(Access(Ref(h), point), Ref(p))]),
            "M(string s) { var h = new Holder(); h.Label(s); return 1; }" => ([s], [Declare(h, New(holder, null)), Statement(Call(label, Ref(h), Ref(s))), Return(Lit(1))]),
            "M() { int[] xs = [1, 2]; var t = 0; foreach (var x in xs) { t = t + x; } return t; }" => ([], [Declare(xs, Elements(1, 2)), Declare(t, Lit(0)), Loop(element, xs, t), Return(Ref(t))]),
            "M(int[] xs) { var t = 0; foreach (var x in xs) { t = t + x; } return t; }" => ([handed], [Declare(t, Lit(0)), Loop(element, handed, t), Return(Ref(t))]),
            "M() { with (var r = new Res()) { } return 1; }" => ([], [new WithBlockStatement(New(res, null), HandBuiltModel.Block(), r), Return(Lit(1))]),
            "M() { var s = $\"limit={Globals.Limit}\"; return Globals.Limit; }" => ([], [Declare(greeting, Interpolated("limit=", StaticRead(limit))), Return(StaticRead(limit))]),
            _ => throw new ArgumentOutOfRangeException(nameof(key)),
        };
    }

    private (Method Add, Method Size, Method Peek, Method Poke) Counting(ClassType owner, Field tally)
    {
        var amount = Input("x", integer);
        var add = Method(owner, "Add", unit, Local, HandBuiltModel.Block(Set(Bare(tally), Sum(Bare(tally), Lit(1)))), amount);
        var size = Method(owner, "Size", integer, LocalReadonly, Returning(Bare(tally)));
        var peek = Method(owner, "Peek", integer, Readonly, Returning(Sum(Bare(tally), StaticRead(counter))));
        var poke = Method(owner, "Poke", unit, Public, HandBuiltModel.Block(Set(Bare(tally), Lit(0)), Set(StaticRead(counter), Lit(1))));
        return (add, size, peek, poke);
    }

    private ClassType Primitive(string name)
    {
        var type = model.Type(name);
        type.Modifiers = new MemberModifiers { IsReadonly = true, IsIntrinsic = true };
        return type;
    }

    private FunctionType Function(bool pure)
    {
        return model.Interner.GetOrCreateFunctionType([integer], integer, pure);
    }

    private static Field Field(ConcreteType owner, string name, IMistType type, MemberModifiers modifiers, OwnershipKind ownership = OwnershipKind.Unrestricted)
    {
        var field = new Field(name, type, ownership, modifiers) { DeclaringType = owner };
        owner.FieldsList.Add(field);
        return field;
    }

    private static Constructor Constructor(ConcreteType owner, MemberModifiers modifiers, Block? body = null, params LocalVariable[] inputs)
    {
        var constructor = new Constructor(owner.Name, owner, [.. inputs.Select(input => input.Parameter!)], modifiers)
        {
            DeclaringType = owner,
            Body = body ?? HandBuiltModel.Block(),
        };
        owner.ConstructorsList.Add(constructor);
        return constructor;
    }

    private static Method Method(MemberType owner, string name, IMistType returns, MemberModifiers modifiers, Block? body, params LocalVariable[] inputs)
    {
        var method = new Method(name, returns, [.. inputs.Select(input => input.Parameter!)], modifiers) { DeclaringType = owner, Body = body };
        owner.MethodsList.Add(method);
        return method;
    }

    private static LocalVariable Input(string name, IMistType type, OwnershipKind ownership = OwnershipKind.Unrestricted)
    {
        return new LocalVariable(name, type) { Ownership = ownership, Parameter = new Parameter(name, type, new ParameterModifier(false, false, ownership)) };
    }

    private static SyntaxNode Node()
    {
        return HandBuiltModel.Node();
    }

    private LiteralExpression Lit(long value)
    {
        return HandBuiltModel.Literal(integer, value);
    }

    private static LocalReferenceExpression Ref(LocalVariable local)
    {
        return new LocalReferenceExpression(Node(), local);
    }

    private static FieldReferenceExpression Bare(Field field)
    {
        return new FieldReferenceExpression(Node(), field);
    }

    private static MemberAccessExpression Access(Expression receiver, Field field)
    {
        return new MemberAccessExpression(Node(), receiver, field.Name, field.Type, member: new FieldResult(field, receiver.Type, []));
    }

    private static MemberAccessExpression StaticRead(Field field)
    {
        var owner = field.DeclaringType!;
        return new MemberAccessExpression(Node(), new TypeReferenceExpression(Node(), owner), field.Name, field.Type, member: new FieldResult(field, owner, []));
    }

    private static ExpressionStatement Set(Expression target, Expression value)
    {
        return new ExpressionStatement(new AssignmentExpression(Node(), target, value, target.Type));
    }

    private static ExpressionStatement Statement(Expression expression)
    {
        return new ExpressionStatement(expression);
    }

    private static LocalDeclarationStatement Declare(LocalVariable local, Expression initializer)
    {
        return new LocalDeclarationStatement(local, initializer);
    }

    private LocalDeclarationStatement FreshBag(LocalVariable local)
    {
        return Declare(local, New(bag, bagConstructor));
    }

    private static ReturnStatement Return(Expression value)
    {
        return new ReturnStatement(value, value.Type);
    }

    private static Block Returning(Expression value)
    {
        return HandBuiltModel.Block(Return(value));
    }

    private BinaryExpression Sum(Expression left, Expression right)
    {
        return new BinaryExpression(Node(), left, BinaryOperatorKind.Add, right, integer) { Operator = plus };
    }

    private static NewExpression New(ConcreteType type, Constructor? constructor, params Expression[] arguments)
    {
        return new NewExpression(Node(), type, arguments, constructor, [], constructor is null ? null : [.. constructor.ParametersList.Select(parameter => parameter.Type)]);
    }

    private static InvocationExpression Call(Method method, Expression? receiver, params Expression[] arguments)
    {
        Expression callee = receiver is null
            ? new MethodReferenceExpression(Node(), method)
            : new MemberAccessExpression(Node(), receiver, method.Name, method.ReturnType);
        return new InvocationExpression(Node(), callee, arguments, method.ReturnType, method, [.. method.ParametersList.Select(parameter => parameter.Type)]);
    }

    private InvocationExpression Invoke(LocalVariable function, params Expression[] arguments)
    {
        return new InvocationExpression(Node(), Ref(function), arguments, integer, null, [integer]);
    }

    private IndexExpression Index(LocalVariable array)
    {
        return new IndexExpression(Node(), Ref(array), Lit(0), integer);
    }

    private ArrayLiteralExpression Elements(params long[] values)
    {
        return new ArrayLiteralExpression(Node(), integer, [.. values.Select(Lit)], integers);
    }

    private ForEachStatement Loop(LocalVariable element, LocalVariable collection, LocalVariable total)
    {
        return new ForEachStatement(element, Ref(collection), HandBuiltModel.Block(Set(Ref(total), Sum(Ref(total), Ref(element)))));
    }

    private InterpolatedStringExpression Interpolated(string prefix, Expression hole)
    {
        return new InterpolatedStringExpression(Node(), [new InterpolatedStringText(prefix), new InterpolatedStringHole(hole)], text);
    }
}