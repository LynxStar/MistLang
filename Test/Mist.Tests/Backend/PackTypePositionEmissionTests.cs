using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

// How a pack written in a type position is spelled in each specialization, emitted from the model the
// binder builds for `Plain<params TArgs>`, `Indexed<Type[2]:TArgs>`, `Through<Type[2]:TBoth>` and their
// closings, and asserted on the emitted text, since a pack name left in a signature fails only in Roslyn.
[Trait("Tier", "2")]
public sealed class PackTypePositionEmissionTests
{
    private readonly HandBuiltModel built = new();
    private readonly ClassType plain = HandBuiltModel.Class("Plain<>");
    private readonly ClassType indexed = HandBuiltModel.Class("Indexed<>");
    private readonly ClassType through = HandBuiltModel.Class("Through<>");
    private readonly TypeParameter plainPack;
    private readonly TypeParameter indexedPack;
    private readonly TypeParameter throughPack;
    private readonly Method take;
    private readonly Method both;
    private readonly Constructor indexedConstructor;
    private readonly Field indexedSecond;
    private readonly Method last;

    public PackTypePositionEmissionTests()
    {
        plainPack = Pack(plain, "TArgs", length: null);
        indexedPack = Pack(indexed, "TArgs", length: 2);
        throughPack = Pack(through, "TBoth", length: 2);

        var whole = Tuple(new SpreadType(plainPack));
        var kept = AddField(plain, "Kept", whole);
        _ = AddConstructor(plain, [new Parameter("whole", whole)], Assign(kept, Local("whole", whole)));
        var copy = new LocalVariable("copy", whole);
        _ = AddMethod(plain, "Whole", whole, [],
            new LocalDeclarationStatement(copy, Read(kept)), new ReturnStatement(Ref(copy), whole));
        take = AddMethod(plain, "Take", built.Type("Int32"), [new Parameter("whole", whole)], Length(plainPack));
        both = AddMethod(plain, "Both", built.Type("Int32"), [Flattened("flat", whole), new Parameter("whole", whole)], Length(plainPack));
        var args = Local("args", whole);
        var relayed = new MemberAccessExpression(HandBuiltModel.Node(), new ThisExpression(HandBuiltModel.Node(), Closed(plain, new SpreadType(plainPack))), "Take", take.ReturnType);
        _ = AddMethod(plain, "Relay", built.Type("Int32"), [Flattened("args", whole)],
            new ReturnStatement(new InvocationExpression(HandBuiltModel.Node(), relayed, [args], take.ReturnType, take, [whole]), take.ReturnType));

        var first = AddField(indexed, "First", indexedPack.ElementAt(0));
        indexedSecond = AddField(indexed, "Second", indexedPack.ElementAt(1));
        indexedConstructor = AddConstructor(indexed,
            [new Parameter("first", indexedPack.ElementAt(0)), new Parameter("second", indexedPack.ElementAt(1))],
            Assign(first, Local("first", indexedPack.ElementAt(0))), Assign(indexedSecond, Local("second", indexedPack.ElementAt(1))));
        var copied = new LocalVariable("copy", indexedPack.ElementAt(1));
        _ = AddMethod(indexed, "Copy", indexedPack.ElementAt(1), [],
            new LocalDeclarationStatement(copied, Read(indexedSecond)), new ReturnStatement(Ref(copied), indexedPack.ElementAt(1)));

        var inner = Closed(indexed, new SpreadType(throughPack));
        var innerField = AddField(through, "Inner", inner);
        _ = AddConstructor(through,
            [new Parameter("first", throughPack.ElementAt(0)), new Parameter("second", throughPack.ElementAt(1))],
            Assign(innerField, new NewExpression(HandBuiltModel.Node(), inner,
                [Local("first", throughPack.ElementAt(0)), Local("second", throughPack.ElementAt(1))], indexedConstructor, [],
                [throughPack.ElementAt(0), throughPack.ElementAt(1)])));
        last = AddMethod(through, "Last", throughPack.ElementAt(1), [],
            new ReturnStatement(Second(Read(innerField), throughPack.ElementAt(1)), throughPack.ElementAt(1)));
    }

    [Theory]
    [InlineData(2, "public (T1, T2) Kept;")]
    [InlineData(2, "public Plain_2((T1, T2) whole)")]
    [InlineData(2, "public (T1, T2) Whole()")]
    [InlineData(2, "(T1, T2) copy = this.Kept;")]
    [InlineData(2, "public int Take((T1, T2) whole)")]
    [InlineData(2, "public int Both((T1, T2) flat, (T1, T2) whole)")]
    [InlineData(2, "return this.Take(args);")]
    [InlineData(1, "public int Take(System.ValueTuple<T1> whole)")]
    [InlineData(0, "public int Take(System.ValueTuple whole)")]
    public void ATupleOverAPack_IsSpelledWithTheSpecializationsElements(int width, string line)
    {
        Assert.Contains(line, Lines(Emit(plain, width)));
    }

    [Theory]
    [InlineData("int taken = plain.Take((2, \"y\"));")]
    [InlineData("int both = plain.Both((3, \"z\"), (4, \"w\"));")]
    public void ATupleParameterOverAPack_IsPassedTheTupleTheCallWrote(string line)
    {
        Assert.Contains(line, Lines(EmitClosings()));
    }

    [Theory]
    [InlineData("Indexed", "public Indexed_2(T1 first, T2 second)")]
    [InlineData("Indexed", "public T2 Copy()")]
    [InlineData("Indexed", "T2 copy = this.Second;")]
    [InlineData("Through", "public Probe.Indexed_2<T1, T2> Inner;")]
    [InlineData("Through", "public Through_2(T1 first, T2 second)")]
    [InlineData("Through", "public T2 Last()")]
    public void AnIndexedPackElement_IsSpelledAsTheSpecializationsParameterAtThatIndex(string typeName, string line)
    {
        Assert.Contains(line, Lines(Emit(typeName == "Indexed" ? indexed : through, 2)));
    }

    [Theory]
    [InlineData("string second = indexed.Second;")]
    [InlineData("string last = new Probe.Through_2<bool, string>(true, \"last\").Last();")]
    public void AnIndexedPackElement_IsTheClosingsTypeAtTheUse(string line)
    {
        Assert.Contains(line, Lines(EmitClosings()));
    }

    private string Emit(MemberType declared, int width)
    {
        var diagnostics = new DiagnosticBag();
        var writer = new CSharpWriter();

        using(Monomorph.For(declared.TypeParametersList, [width])!.Enter())
        {
            TypeEmitter.EmitTypeBody(declared, ConstraintClosureMap.Build(built.Model, diagnostics), built.Model.VariadicUses, writer, diagnostics);
        }

        Assert.Empty(diagnostics.ToImmutableArray().Where(d => d.Severity == DiagnosticSeverity.Error));

        return writer.ToString();
    }

    private string EmitClosings()
    {
        var int32 = built.Type("Int32");
        var text = built.Type("String");
        var boolean = built.Type("Boolean");
        var pair = Tuple(int32, text);

        var plainLocal = new LocalVariable("plain", Closed(plain, int32, text));
        var indexedLocal = new LocalVariable("indexed", Closed(indexed, int32, text));
        var throughType = Closed(through, boolean, text);
        var made = new NewExpression(HandBuiltModel.Node(), throughType,
            [HandBuiltModel.Literal(boolean, true), HandBuiltModel.Literal(text, "last")], Assert.Single(through.ConstructorsList), [], [boolean, text]);
        var body = HandBuiltModel.Block(
            new LocalDeclarationStatement(plainLocal, new NewExpression(HandBuiltModel.Node(), plainLocal.Type,
                [Pair(pair, 1, "x", targetTyped: true)], Assert.Single(plain.ConstructorsList), [], [pair])),
            new LocalDeclarationStatement(new LocalVariable("taken", int32),
                Call(Ref(plainLocal), take, [Pair(pair, 2, "y", targetTyped: true)], [pair])),
            new LocalDeclarationStatement(new LocalVariable("both", int32),
                Call(Ref(plainLocal), both, [HandBuiltModel.Literal(int32, 3), HandBuiltModel.Literal(text, "z"), Pair(pair, 4, "w", targetTyped: false)], [pair, pair])),
            new LocalDeclarationStatement(indexedLocal, new NewExpression(HandBuiltModel.Node(), indexedLocal.Type,
                [HandBuiltModel.Literal(int32, 7), HandBuiltModel.Literal(text, "seven")], indexedConstructor, [], [int32, text])),
            new LocalDeclarationStatement(new LocalVariable("second", text), Second(Ref(indexedLocal), text)),
            new LocalDeclarationStatement(new LocalVariable("last", text), Call(made, last, [], [])));
        var diagnostics = new DiagnosticBag();
        var writer = new CSharpWriter();

        BodyEmitter.EmitBlock(body, [], writer, diagnostics);

        Assert.Empty(diagnostics.ToImmutableArray().Where(d => d.Severity == DiagnosticSeverity.Error));

        return writer.ToString();
    }

    private static TypeParameter Pack(ClassType owner, string name, int? length)
    {
        var pack = new TypeParameter(name, 0, owner.Scope, owner)
        {
            PackKind = TypePackKind.Unbracketed,
            PackLength = length,
        };
        owner.TypeParametersList.Add(pack);
        owner.Modifiers = DeclarationFragments.Public;
        return pack;
    }

    private static Field AddField(ClassType owner, string name, IMistType type)
    {
        var field = new Field(name, type, OwnershipKind.Unrestricted, DeclarationFragments.Public) { DeclaringType = owner };
        owner.FieldsList.Add(field);
        return field;
    }

    private static Constructor AddConstructor(ClassType owner, List<Parameter> parameters, params Statement[] statements)
    {
        var constructor = new Constructor(owner.Name, owner, parameters, DeclarationFragments.Public)
        {
            DeclaringType = owner,
            Body = HandBuiltModel.Block(statements),
        };
        owner.ConstructorsList.Add(constructor);
        return constructor;
    }

    private static Method AddMethod(ClassType owner, string name, IMistType returnType, List<Parameter> parameters, params Statement[] statements)
    {
        var method = new Method(name, returnType, parameters, DeclarationFragments.Public)
        {
            DeclaringType = owner,
            Body = HandBuiltModel.Block(statements),
        };
        owner.MethodsList.Add(method);
        return method;
    }

    private static Parameter Flattened(string name, IMistType type)
    {
        return new Parameter(name, type) { IsFlattened = true };
    }

    private IMistType Tuple(params IMistType[] elements)
    {
        return built.Interner.GetOrCreateTupleType(elements);
    }

    private ConstructedType Closed(ClassType definition, params IMistType[] elements)
    {
        return built.Interner.GetOrCreateConstructedType(definition, [built.Interner.LiftPack(built.Type("Type"), new TypePackValue(elements))]);
    }

    private ReturnStatement Length(TypeParameter pack)
    {
        var length = new MemberAccessExpression(HandBuiltModel.Node(), new TypeReferenceExpression(HandBuiltModel.Node(), pack), "Length",
            built.Type("Int32"), member: new TypeResult(pack));
        return new ReturnStatement(length, built.Type("Int32"));
    }

    private MemberAccessExpression Second(Expression receiver, IMistType type)
    {
        return new MemberAccessExpression(HandBuiltModel.Node(), receiver, "Second", type,
            member: new FieldResult(indexedSecond, indexed, []));
    }

    private TupleLiteralExpression Pair(IMistType type, int number, string text, bool targetTyped)
    {
        return new TupleLiteralExpression(HandBuiltModel.Node(),
            [HandBuiltModel.Literal(built.Type("Int32"), number), HandBuiltModel.Literal(built.Type("String"), text)], [null, null], type)
        { IsTargetTyped = targetTyped };
    }

    private static InvocationExpression Call(Expression receiver, Method method, IReadOnlyList<Expression> arguments, IReadOnlyList<IMistType> parameterTypes)
    {
        var callee = new MemberAccessExpression(HandBuiltModel.Node(), receiver, method.Name, method.ReturnType);
        return new InvocationExpression(HandBuiltModel.Node(), callee, arguments, method.ReturnType, method, parameterTypes);
    }

    private static ExpressionStatement Assign(Field field, Expression value)
    {
        return new ExpressionStatement(new AssignmentExpression(HandBuiltModel.Node(), Read(field), value, field.Type));
    }

    private static FieldReferenceExpression Read(Field field)
    {
        return new FieldReferenceExpression(HandBuiltModel.Node(), field);
    }

    private static LocalReferenceExpression Local(string name, IMistType type)
    {
        return Ref(new LocalVariable(name, type));
    }

    private static LocalReferenceExpression Ref(LocalVariable local)
    {
        return new LocalReferenceExpression(HandBuiltModel.Node(), local);
    }

    private static List<string> Lines(string emitted)
    {
        return [.. emitted.Split('\n').Select(line => line.Trim())];
    }
}