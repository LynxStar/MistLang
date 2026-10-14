// Tier 2 (Functional): the storage-write classification (unknown-type Requirement 9) over bodies built
// by hand -- effects read off each tree, edges substituted the way each site closes its callee, and the
// fixed point over them. No parsing and no binding: each root is the tree the binder would have left.

using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Binding.StorageWrites;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class StorageWriteSummaryTests
{
    private readonly BindingFragments fragments = new();

    private TypeInterner Interner => fragments.Built.Interner;

    private ClassType Int32 => fragments.Built.Type("Int32");

    private ClassType String => fragments.Built.Type("String");

    [Fact]
    public void AssigningAFieldWritesTheParametersItsTypeMentions()
    {
        var box = new Generic("Box", "T");
        var set = box.Method("Set", [new Parameter("value", box.T)], Body(Assign(box.ValueOf(This(box.Type)), Local("value", box.T))));
        var setCount = box.Method("SetCount", [new Parameter("n", Int32)], Body(Assign(box.CountOf(This(box.Type)), Local("n", Int32))));
        var bare = box.Method("Reset", [new Parameter("value", box.T)], Body(Assign(new FieldReferenceExpression(Node(), box.Value), Local("value", box.T))));
        var get = box.Method("Get", [], HandBuiltModel.Block(new ReturnStatement(box.ValueOf(This(box.Type)), box.T)));

        var writes = Solve(box.Roots(set, setCount, bare, get));

        Assert.Equal(Set(box.T), writes[(set, StorageAccess.Read)]);
        Assert.Empty(writes[(setCount, StorageAccess.Read)]);
        Assert.Equal(Set(box.T), writes[(bare, StorageAccess.Read)]);
        Assert.Empty(writes[(get, StorageAccess.Read)]);
    }

    [Fact]
    public void ACallInheritsItsCalleesClassificationThroughTheFixedPoint()
    {
        var box = new Generic("Box", "T");
        var set = box.Method("Set", [new Parameter("value", box.T)], Body(Assign(box.ValueOf(This(box.Type)), Local("value", box.T))));
        var forward = box.Method("Forward", [new Parameter("value", box.T)], Body(Call(Callee(This(box.Type), set), set, Local("value", box.T))));
        var relay = box.Method("Relay", [new Parameter("value", box.T)], Body(Call(Bare(forward), forward, Local("value", box.T))));
        var ping = box.Method("Ping", [], null);
        var pong = box.Method("Pong", [], Body(Call(Bare(ping), ping)));
        ping.Body!.Statements = [new ExpressionStatement(Call(Bare(pong), pong))];
        var mutual = box.Method("Mutual", [], null);
        var partner = box.Method("Partner", [], Body(Call(Bare(mutual), mutual), Assign(box.ValueOf(This(box.Type)), box.ValueOf(This(box.Type)))));
        mutual.Body!.Statements = [new ExpressionStatement(Call(Bare(partner), partner))];

        var writes = Solve(box.Roots(set, forward, relay, ping, pong, mutual, partner));

        Assert.Equal(Set(box.T), writes[(forward, StorageAccess.Read)]);
        Assert.Equal(Set(box.T), writes[(relay, StorageAccess.Read)]);
        Assert.Empty(writes[(ping, StorageAccess.Read)]);
        Assert.Empty(writes[(pong, StorageAccess.Read)]);
        Assert.Equal(Set(box.T), writes[(mutual, StorageAccess.Read)]);
        Assert.Equal(Set(box.T), writes[(partner, StorageAccess.Read)]);
    }

    /// <summary>
    /// Base names its parameter X and Derived names its own Y, and the base clause may wrap one in the
    /// other, so an inherited writer is attributed by the substitution the clause states, not by name.
    /// </summary>
    [Fact]
    public void AnInheritedCallIsSubstitutedThroughTheBaseClause()
    {
        var list = new Generic("List", "T");
        var baseType = new Generic("Base", "X");
        var keep = baseType.Method("Keep", [new Parameter("value", baseType.T)], Body(Assign(baseType.ValueOf(This(baseType.Type)), Local("value", baseType.T))));
        var derived = new Generic("Derived", "Y");
        derived.Type.BaseType = Interner.GetOrCreateConstructedType(baseType.Type, [derived.T]);
        var relay = derived.Method("Relay", [new Parameter("value", derived.T)], Body(Call(Bare(keep), keep, Local("value", derived.T))));
        var wrapped = new Generic("Wrapped", "Z");
        wrapped.Type.BaseType = Interner.GetOrCreateConstructedType(baseType.Type, [Interner.GetOrCreateConstructedType(list.Type, [wrapped.T])]);
        var listed = wrapped.Method("Relay", [], Body(Call(Callee(This(wrapped.Type), keep), keep, Local("items", Interner.GetOrCreateConstructedType(list.Type, [wrapped.T])))));
        var counted = wrapped.Method("Count", [], Body(Call(Callee(This(wrapped.Type), keep), keep, Local("items", Interner.GetOrCreateConstructedType(list.Type, [Int32])))));

        var writes = Solve([.. baseType.Roots(keep), .. derived.Roots(relay), .. wrapped.Roots(listed, counted)]);

        Assert.Equal(Set(derived.T), writes[(relay, StorageAccess.Read)]);
        Assert.Equal(Set(wrapped.T), writes[(listed, StorageAccess.Read)]);
        Assert.Equal(Set(wrapped.T), writes[(counted, StorageAccess.Read)]);
    }

    [Fact]
    public void TwoParametersAreClassifiedIndependently()
    {
        var pair = new Generic("Pair", "A", "B");
        var second = pair.Field("second", pair.Type.TypeParametersList[1]);
        var setFirst = pair.Method("SetFirst", [new Parameter("a", pair.T)], Body(Assign(pair.ValueOf(This(pair.Type)), Local("a", pair.T))));
        var setSecond = pair.Method("SetSecond", [new Parameter("b", second.Type)], Body(Assign(FieldOf(This(pair.Type), second, pair.Type), Local("b", second.Type))));

        var writes = Solve(pair.Roots(setFirst, setSecond));

        Assert.Equal(Set(pair.T), writes[(setFirst, StorageAccess.Read)]);
        Assert.Equal(Set(pair.Type.TypeParametersList[1]), writes[(setSecond, StorageAccess.Read)]);
    }

    /// <summary>
    /// An operator is reached by its declaration, and the operand closes its owner: a wrapper's V stands
    /// where the operator's owner names T, or X on a base the operand's type derives from.
    /// </summary>
    [Fact]
    public void AnOperatorCallIsAnEdgeToTheSelectedDeclaration()
    {
        var box = new Generic("Box", "T");
        var self = Interner.GetOrCreateConstructedType(box.Type, [box.T]);
        var plus = box.Operator(OperatorKind.Plus, self, [new Parameter("left", self), new Parameter("right", self)],
            Body(Assign(FieldOf(Local("left", self), box.Value, box.Type), FieldOf(Local("right", self), box.Value, box.Type))));
        var equal = box.Operator(OperatorKind.EqualEqual, Int32, [new Parameter("left", self), new Parameter("right", self)], Body());
        var derived = new Generic("Derived", "W");
        derived.Type.BaseType = Interner.GetOrCreateConstructedType(box.Type, [derived.T]);
        var wrapper = new Generic("Wrapper", "V");
        var inner = Interner.GetOrCreateConstructedType(box.Type, [wrapper.T]);
        var combine = wrapper.Method("Combine", [new Parameter("other", inner)],
            Body(Binary(Local("inner", inner), BinaryOperatorKind.Add, Local("other", inner), inner, plus)));
        var same = wrapper.Method("Same", [new Parameter("other", inner)],
            Body(Binary(Local("inner", inner), BinaryOperatorKind.Equal, Local("other", inner), Int32, equal)));
        var inherited = Interner.GetOrCreateConstructedType(derived.Type, [wrapper.T]);
        var merge = wrapper.Method("Merge", [new Parameter("other", inherited)],
            Body(Binary(Local("mine", inherited), BinaryOperatorKind.Add, Local("other", inherited), inner, plus)));

        var writes = Solve([.. box.Roots(plus, equal), .. wrapper.Roots(combine, same, merge)]);

        Assert.Equal(Set(wrapper.T), writes[(combine, StorageAccess.Read)]);
        Assert.Empty(writes[(same, StorageAccess.Read)]);
        Assert.Equal(Set(wrapper.T), writes[(merge, StorageAccess.Read)]);
    }

    [Fact]
    public void AccessorsAreClassifiedPerDirection()
    {
        var box = new Generic("Box", "T");
        var lazy = box.Property("Lazy", box.T,
            getter: Body(Assign(box.ValueOf(This(box.Type)), box.ValueOf(This(box.Type)))),
            setter: Body(Assign(box.CountOf(This(box.Type)), Local("value", Int32))));
        var reads = box.Method("Reads", [], Body(PropertyOf(This(box.Type), lazy, box.Type)));
        var assigns = box.Method("Assigns", [new Parameter("value", box.T)], Body(Assign(PropertyOf(This(box.Type), lazy, box.Type), Local("value", box.T))));
        var indexer = box.Indexer(getter: HandBuiltModel.Block(new ReturnStatement(box.ValueOf(This(box.Type)), box.T)), setter: Body(Assign(box.ValueOf(This(box.Type)), Local("value", box.T))));
        var peeks = box.Method("Peeks", [], Body(Index(This(box.Type), indexer, box.T)));
        var stores = box.Method("Stores", [new Parameter("value", box.T)], Body(Assign(Index(This(box.Type), indexer, box.T), Local("value", box.T))));

        var writes = Solve([.. box.AccessorRoots(lazy), .. box.AccessorRoots(indexer), .. box.Roots(reads, assigns, peeks, stores)]);

        Assert.Equal(Set(box.T), writes[(lazy, StorageAccess.Read)]);
        Assert.Empty(writes[(lazy, StorageAccess.Write)]);
        Assert.Equal(Set(box.T), writes[(reads, StorageAccess.Read)]);
        Assert.Empty(writes[(assigns, StorageAccess.Read)]);
        Assert.Empty(writes[(indexer, StorageAccess.Read)]);
        Assert.Equal(Set(box.T), writes[(indexer, StorageAccess.Write)]);
        Assert.Empty(writes[(peeks, StorageAccess.Read)]);
        Assert.Equal(Set(box.T), writes[(stores, StorageAccess.Read)]);
    }

    /// <summary>
    /// A callee with no body to read -- projected, intrinsic, abstract, or a function value -- writes
    /// whatever its arguments mention, the spec's conservative rule, and no more: an argument typed
    /// <c>string</c> is no write, however the callee is typed.
    /// </summary>
    [Fact]
    public void ACalleeWithoutABodyWritesWhatItsArgumentsMention()
    {
        var box = new Generic("Box", "T");
        var console = HandBuiltModel.Class("Console");
        var writeLine = new Method("WriteLine", Int32, [new Parameter("value", UnknownType.Instance)], DeclarationFragments.Public with { IsStatic = true });
        var print = box.Method("Print", [], Body(Call(Callee(new TypeReferenceExpression(Node(), console), writeLine), writeLine, box.ValueOf(This(box.Type)))));
        var describe = box.Method("Describe", [], Body(Call(Callee(new TypeReferenceExpression(Node(), console), writeLine), writeLine, HandBuiltModel.Literal(String, "box"))));
        var invoke = box.Method("Invoke", [], Body(new InvocationExpression(Node(), Local("callback", Int32), [box.ValueOf(This(box.Type))], Int32, method: null, parameterTypes: null)));

        var writes = Solve(box.Roots(print, describe, invoke));

        Assert.Equal(Set(box.T), writes[(print, StorageAccess.Read)]);
        Assert.Empty(writes[(describe, StorageAccess.Read)]);
        Assert.Equal(Set(box.T), writes[(invoke, StorageAccess.Read)]);
    }

    [Fact]
    public void AConstructorWritesItsParametersAndWhatItChainsTo()
    {
        var baseType = new Generic("Base", "X");
        var baseConstructor = baseType.Constructor([new Parameter("first", baseType.T)], Body());
        var derived = new Generic("Derived", "Y");
        derived.Type.BaseType = Interner.GetOrCreateConstructedType(baseType.Type, [derived.T]);
        var chained = derived.Constructor([], Body());
        chained.ChainedConstructor = baseConstructor;
        var plain = new Generic("Plain", "Z").Constructor([], Body());

        var writes = Solve([.. baseType.Roots(baseConstructor), .. derived.Roots(chained), new StorageWriteRoot(plain, StorageAccess.Read, plain.Body, plain.DeclaringType, Set(((IHasTypeParameters)plain.DeclaringType!).TypeParametersList[0]))]);

        Assert.Equal(Set(baseType.T), writes[(baseConstructor, StorageAccess.Read)]);
        Assert.Equal(Set(derived.T), writes[(chained, StorageAccess.Read)]);
        Assert.Empty(writes[(plain, StorageAccess.Read)]);
    }

    [Fact]
    public void ACoalescingAssignmentAndARefArgumentWrite()
    {
        var box = new Generic("Box", "T");
        var fill = box.Method("Fill", [new Parameter("value", box.T)], Body(new AssignmentExpression(Node(), box.ValueOf(This(box.Type)), Local("value", box.T), box.T, isCoalescing: true)));
        var take = box.Method("Take", [new Parameter("slot", box.T, new ParameterModifier(false, true, OwnershipKind.Unrestricted, ByRefDirection.Ref))], Body());
        var lend = box.Method("Lend", [new Parameter("slot", box.T)], Body());
        var borrow = box.Method("Borrow", [], Body(Call(Bare(take), take, box.ValueOf(This(box.Type)))));
        var share = box.Method("Share", [], Body(Call(Bare(lend), lend, box.ValueOf(This(box.Type)))));

        var writes = Solve(box.Roots(fill, take, lend, borrow, share));

        Assert.Equal(Set(box.T), writes[(fill, StorageAccess.Read)]);
        Assert.Equal(Set(box.T), writes[(borrow, StorageAccess.Read)]);
        Assert.Empty(writes[(share, StorageAccess.Read)]);
    }

    /// <summary>
    /// A generic method of a non-generic type is classified over its own parameter, and a call closes
    /// that parameter with the call's type argument -- so a helper that stores into a list it is handed
    /// writes the caller's T when the caller hands it a list of T.
    /// </summary>
    [Fact]
    public void AGenericMethodIsClassifiedOverItsOwnParameterAndClosedAtTheCall()
    {
        var list = new Generic("List", "T");
        var add = new Method("Add", Int32, [new Parameter("item", list.T)], DeclarationFragments.Public) { DeclaringType = list.Type, Scope = new MethodScope(list.Type.Scope) };
        list.Type.MethodsList.Add(add);
        var helper = HandBuiltModel.Class("Helper");
        var fill = new Method("Fill", Int32, [], DeclarationFragments.Public with { IsStatic = true }) { DeclaringType = helper, Scope = new MethodScope(helper.Scope), Body = HandBuiltModel.Block() };
        var u = BindingFragments.Parameter(fill, "U");
        var items = Interner.GetOrCreateConstructedType(list.Type, [u]);
        fill.ParametersList.Add(new Parameter("list", items));
        fill.ParametersList.Add(new Parameter("item", u));
        fill.Body!.Statements = [new ExpressionStatement(Call(Callee(Local("list", items), add), add, Local("item", u)))];
        helper.MethodsList.Add(fill);
        var box = new Generic("Box", "T");
        var mine = Interner.GetOrCreateConstructedType(list.Type, [box.T]);
        var store = box.Method("Store", [], Body(Call(Callee(new TypeReferenceExpression(Node(), helper), fill, box.T), fill, Local("items", mine), box.ValueOf(This(box.Type)))));
        var count = box.Method("Count", [], Body(Call(Callee(new TypeReferenceExpression(Node(), helper), fill, Int32), fill, Local("numbers", Interner.GetOrCreateConstructedType(list.Type, [Int32])), Local("n", Int32))));

        var writes = Solve([new StorageWriteRoot(fill, StorageAccess.Read, fill.Body, helper, Set(u)), .. box.Roots(store, count)]);

        Assert.Equal(Set(u), writes[(fill, StorageAccess.Read)]);
        Assert.Equal(Set(box.T), writes[(store, StorageAccess.Read)]);
        Assert.Empty(writes[(count, StorageAccess.Read)]);
    }

    private Dictionary<(object, StorageAccess), HashSet<TypeParameter>> Solve(IReadOnlyList<StorageWriteRoot> roots)
    {
        return StorageWriteSummaries.Solve(roots, fragments.Builder);
    }

    private static HashSet<TypeParameter> Set(params TypeParameter[] parameters)
    {
        return new HashSet<TypeParameter>(parameters, ReferenceEqualityComparer.Instance);
    }

    private static SyntaxNode Node()
    {
        return HandBuiltModel.Node();
    }

    private static ThisExpression This(MistType owner)
    {
        return new ThisExpression(Node(), owner);
    }

    private static LocalReferenceExpression Local(string name, IMistType type)
    {
        return new LocalReferenceExpression(Node(), new LocalVariable(name, type));
    }

    private static MemberAccessExpression FieldOf(Expression receiver, Field field, MemberType declaring)
    {
        return new MemberAccessExpression(Node(), receiver, field.Name, field.Type, member: new FieldResult(field, declaring, []));
    }

    private static MemberAccessExpression PropertyOf(Expression receiver, Property property, MemberType declaring)
    {
        return new MemberAccessExpression(Node(), receiver, property.Name, property.Type, member: new PropertyResult(property, declaring, []));
    }

    private static IndexExpression Index(Expression receiver, Indexer indexer, IMistType type)
    {
        return new IndexExpression(Node(), receiver, HandBuiltModel.Literal(indexer.ParametersList[0].Type, 0), type) { Indexer = indexer };
    }

    private static AssignmentExpression Assign(Expression target, Expression value)
    {
        return new AssignmentExpression(Node(), target, value, target.Type);
    }

    private static MemberAccessExpression Callee(Expression receiver, Method method, params IMistType[] typeArguments)
    {
        return new MemberAccessExpression(Node(), receiver, method.Name, method.ReturnType, typeArguments);
    }

    private static MethodReferenceExpression Bare(Method method)
    {
        return new MethodReferenceExpression(Node(), method);
    }

    private static InvocationExpression Call(Expression callee, Method method, params Expression[] arguments)
    {
        return new InvocationExpression(Node(), callee, arguments, method.ReturnType, method, method.ParametersList.ConvertAll(parameter => parameter.Type));
    }

    private static BinaryExpression Binary(Expression left, BinaryOperatorKind kind, Expression right, IMistType type, Operator selected)
    {
        return new BinaryExpression(Node(), left, kind, right, type) { Operator = selected };
    }

    private static Block Body(params Expression[] expressions)
    {
        return HandBuiltModel.Block(expressions.Select(expression => (Statement)new ExpressionStatement(expression)).ToArray());
    }

    /// <summary>
    /// A generic class with a <c>Value</c> field of its first parameter and an int <c>Count</c>, and the
    /// members a test adds to it, each rooted with the type's parameters in scope.
    /// </summary>
    private sealed class Generic
    {
        public Generic(string name, params string[] parameterNames)
        {
            Type = UnknownViewDecisionTests.Generic($"{name}<{new string(',', parameterNames.Length - 1)}>", null, parameterNames);
            Value = Field("Value", T);
            Count = Field("Count", new HandBuiltModel().Type("Int32"));
        }

        public ClassType Type { get; }

        public TypeParameter T => Type.TypeParametersList[0];

        public Field Value { get; }

        public Field Count { get; }

        public MemberAccessExpression ValueOf(Expression receiver)
        {
            return FieldOf(receiver, Value, Type);
        }

        public MemberAccessExpression CountOf(Expression receiver)
        {
            return FieldOf(receiver, Count, Type);
        }

        public Field Field(string name, IMistType type)
        {
            var field = new Field(name, type, OwnershipKind.Unrestricted, DeclarationFragments.Public) { DeclaringType = Type };
            Type.FieldsList.Add(field);
            return field;
        }

        public Method Method(string name, IReadOnlyList<Parameter> parameters, Block? body)
        {
            var method = new Method(name, new HandBuiltModel().Type("Int32"), [.. parameters], DeclarationFragments.Public)
            {
                DeclaringType = Type,
                Scope = new MethodScope(Type.Scope),
                Body = body ?? HandBuiltModel.Block(),
            };
            Type.MethodsList.Add(method);
            return method;
        }

        public Operator Operator(OperatorKind kind, IMistType returns, List<Parameter> parameters, Block body)
        {
            var declared = new Operator(kind, returns, parameters, DeclarationFragments.Public with { IsStatic = true })
            {
                DeclaringType = Type,
                Scope = new MethodScope(Type.Scope),
                Body = body,
            };
            Type.OperatorsList.Add(declared);
            return declared;
        }

        public Constructor Constructor(IReadOnlyList<Parameter> parameters, Block body)
        {
            var constructor = new Constructor(Type.Name, Type, [.. parameters], DeclarationFragments.Public)
            {
                DeclaringType = Type,
                Scope = new MethodScope(Type.Scope),
                Body = body,
            };
            Type.ConstructorsList.Add(constructor);
            return constructor;
        }

        public Property Property(string name, IMistType type, Block getter, Block setter)
        {
            var property = new Property(name, type, DeclarationFragments.Public)
            {
                DeclaringType = Type,
                Getter = new PropertyAccessor { Body = getter },
                Setter = new PropertyAccessor { Body = setter },
            };
            Type.PropertiesList.Add(property);
            return property;
        }

        public Indexer Indexer(Block getter, Block setter)
        {
            var indexer = new Indexer(T, [new Parameter("index", new HandBuiltModel().Type("Int32"))], DeclarationFragments.Public)
            {
                DeclaringType = Type,
                Getter = new PropertyAccessor { Body = getter },
                Setter = new PropertyAccessor { Body = setter },
            };
            Type.IndexersList.Add(indexer);
            return indexer;
        }

        public List<StorageWriteRoot> Roots(params Method[] methods)
        {
            var scope = new HashSet<TypeParameter>(Type.TypeParametersList, ReferenceEqualityComparer.Instance);

            return methods.Select(method => new StorageWriteRoot(method, StorageAccess.Read, method.Body, Type, scope)).ToList();
        }

        public List<StorageWriteRoot> AccessorRoots(object member)
        {
            var (getter, setter) = member switch
            {
                Property property => (property.Getter, property.Setter),
                Indexer indexer => (indexer.Getter, indexer.Setter),
                _ => throw new ArgumentOutOfRangeException(nameof(member)),
            };
            var scope = new HashSet<TypeParameter>(Type.TypeParametersList, ReferenceEqualityComparer.Instance);

            return
            [
                new StorageWriteRoot(member, StorageAccess.Read, getter!.Body, Type, scope),
                new StorageWriteRoot(member, StorageAccess.Write, setter!.Body, Type, scope),
            ];
        }
    }
}