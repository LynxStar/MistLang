using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramAnalysis;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Helpers;

/// <summary>
/// Hand-built programs for the structured-context rule: several bodies at once, because what the rule
/// judges is who runs a body and what is ambient there. The node a refusal should land on sits at
/// <see cref="Site"/>, and the call the entry point makes at <see cref="Outer"/>.
/// </summary>
internal sealed partial class StructuredContextFragments
{
    public const int Site = 37;
    public const int Outer = 53;

    private static readonly MemberModifiers Public = new() { Accessibility = Accessibility.Public };
    private static readonly MemberModifiers Static = Public with { IsStatic = true };

    private readonly HandBuiltModel model = new();
    private readonly ClassType text;
    private readonly ClassType integer;
    private readonly ClassType probe = HandBuiltModel.Class("Probe");
    private readonly List<SourceRoot> bodies = [];
    private readonly List<MistType> types = [];
    private readonly Method read;

    public StructuredContextFragments()
    {
        text = model.Type("String");
        integer = model.Type("Int32");
        types.Add(probe);
        read = Declare(probe, "Read", Static, HandBuiltModel.Block(), text);
    }

    public IReadOnlyList<Diagnostic> Analyze(string name)
    {
        return name switch
        {
            _ when name.StartsWith("call-", StringComparison.Ordinal) => Calls(name),
            _ when name.StartsWith("transparent-", StringComparison.Ordinal) || name.StartsWith("cycle-", StringComparison.Ordinal) => Transparent(name),
            _ when name.StartsWith("runtime-called-", StringComparison.Ordinal) => RuntimeCalled(name["runtime-called-".Length..]),
            _ when name.StartsWith("slot-", StringComparison.Ordinal) => Slots(name),
            _ when name.StartsWith("value-", StringComparison.Ordinal) => Values(name),
            _ when name.StartsWith("constructor-", StringComparison.Ordinal) => Constructors(name),
            _ when name.StartsWith("name-", StringComparison.Ordinal) => Names(name),
            _ when name.StartsWith("clause-", StringComparison.Ordinal) => Clauses(name),
            _ when name.StartsWith("implementation-", StringComparison.Ordinal) => Implementations(name),
            _ when name.StartsWith("generic-", StringComparison.Ordinal) => Generics(name),
            _ when name.StartsWith("scope-", StringComparison.Ordinal) => Scopes(name),
            _ when name.StartsWith("implicit-", StringComparison.Ordinal) => Implicit(name),
            _ when name.StartsWith("accessor-", StringComparison.Ordinal) => Accessors(name),
            _ when name.StartsWith("indexer-", StringComparison.Ordinal) => Indexers(name),
            _ when name.StartsWith("operator-", StringComparison.Ordinal) || name.StartsWith("conversion-", StringComparison.Ordinal) => Operators(name),
            _ when name.StartsWith("drop-", StringComparison.Ordinal) => Drops(name),
            _ when name.StartsWith("dispose-", StringComparison.Ordinal) || name.StartsWith("loop-", StringComparison.Ordinal) => Disposals(name),
            "initializer-of-an-instance-field" => Initializer(Public),
            "initializer-of-a-static-field" => Initializer(Static),
            "anonymous-key" => AnonymousKey(),
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };
    }

    private IReadOnlyList<Diagnostic> Calls(string name)
    {
        var config = HandBuiltModel.Class("Config");
        var detailed = HandBuiltModel.Class("Detailed", config);
        var alias = new TypeAlias("Name", "Probe", new Scope(null), null, new TypeAliasDeclarationNode()) { AliasedType = text };
        var refined = HandBuiltModel.Refined(text, "Short");
        var readConfig = Declare(probe, "ReadConfig", Static, HandBuiltModel.Block(), config);
        var readShort = Declare(probe, "ReadShort", Static, HandBuiltModel.Block(), refined);
        var both = Declare(probe, "Both", Static, HandBuiltModel.Block(), text, integer);
        var forward = Declare(probe, "Forward", Static, HandBuiltModel.Block(Run(Call(read, Site))), text);

        Entry(name switch
        {
            "call-with-no-context" => [Run(Call(read, Site))],
            "call-under-a-with-block" => [Under(text, Run(Call(read, Site)))],
            "call-after-the-with-block-ends" => [Under(text), Run(Call(read, Site))],
            "call-inside-the-value-a-with-pushes" => [new WithBlockStatement(Call(read, Site), HandBuiltModel.Block(), binding: null, keyType: text)],
            "call-under-a-context-of-another-type" => [Under(integer, Run(Call(read, Site)))],
            "call-under-a-context-of-a-derived-type" => [Under(detailed, Run(Call(readConfig, Site)))],
            "call-under-a-union-holding-the-key" => [Under(model.Interner.GetOrCreateUnionType([text, integer]), Run(Call(read, Site)))],
            "call-under-an-alias-of-the-key" => [Under(alias, Run(Call(read, Site)))],
            "call-under-a-refinement-of-the-key" => [Under(refined, Run(Call(read, Site)))],
            "call-of-a-refined-clause-under-its-base" => [Under(text, Run(Call(readShort, Site)))],
            "call-owed-two-keys-under-one" => [Under(text, Run(Call(both, Site)))],
            "call-owed-two-keys-under-both" => [Under(text, Under(integer, Run(Call(both, Site))))],
            "call-from-a-member-with-the-clause" => [Under(text, Run(Call(forward)))],
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        });

        return Judge();
    }

    private IReadOnlyList<Diagnostic> Transparent(string name)
    {
        var middle = Declare(probe, "Middle", Static, HandBuiltModel.Block(Run(Call(read, Site))));
        var ping = Declare(probe, "Ping", Static, HandBuiltModel.Block());
        var pong = Declare(probe, "Pong", Static, HandBuiltModel.Block(Run(Call(ping)), Run(Call(read, Site))));
        ping.Body!.Statements = [Run(Call(pong))];

        switch(name)
        {
            case "transparent-method-reached-bare":
                Entry(Run(Call(middle, Outer)));
                break;
            case "transparent-method-under-a-context":
                Entry(Under(text, Run(Call(middle))));
                break;
            case "transparent-method-nothing-calls":
                break;
            case "transparent-method-with-one-bare-caller":
                var provided = Declare(probe, "Provided", Static, HandBuiltModel.Block(Under(text, Run(Call(middle)))));
                var bare = Declare(probe, "Bare", Static, HandBuiltModel.Block(Run(Call(middle))));
                Entry(Run(Call(provided)), Run(Call(bare, Outer)));
                break;
            case "cycle-reached-bare":
                Entry(Run(Call(ping, Outer)));
                break;
            case "cycle-under-a-context":
                Entry(Under(text, Run(Call(ping))));
                break;
            case "cycle-nothing-reaches":
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(name));
        }

        return Judge();
    }

    private IReadOnlyList<Diagnostic> RuntimeCalled(string member)
    {
        var thing = HandBuiltModel.Class("Thing");
        types.Add(thing);
        var method = new Method(member, text, [], Public)
        {
            DeclaringType = thing,
            Body = HandBuiltModel.Block(Run(Call(read, Site))),
        };
        thing.MethodsList.Add(method);
        bodies.Add(new SourceRoot(method.Body, thing, MistAssembly.Synthetic, method));
        Entry(Under(text, Run(Call(method))));

        return Judge();
    }

    private IReadOnlyList<Diagnostic> Slots(string name)
    {
        var contract = new AbstractType("Source", "Probe", new Scope(null), null, null);
        var pull = Declare(contract, "Pull", Public, body: null, name == "slot-call-with-no-context" ? [text] : []);
        var tap = HandBuiltModel.Class("Tap");
        tap.BaseAbstractTypes.Add(contract);
        types.AddRange([contract, tap]);
        _ = Declare(tap, "Pull", Public, HandBuiltModel.Block(Run(Call(read, Site))), name == "slot-call-with-no-context" ? [text] : []);

        switch(name)
        {
            case "slot-call-with-no-context":
                Entry(Run(Call(pull, Site)));
                break;
            case "slot-answered-transparently-reached-bare":
                Entry(Run(Call(pull, Outer)));
                break;
            case "slot-answered-transparently-under-a-context":
                Entry(Under(text, Run(Call(pull))));
                break;
            case "slot-of-the-runtime-answered-transparently":
                contract.Assembly = MistAssembly.Ambient;
                Entry(Under(text, Run(Call(pull))));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(name));
        }

        return Judge();
    }

    private IReadOnlyList<Diagnostic> Values(string name)
    {
        var function = model.Interner.GetOrCreateFunctionType([], text);
        var held = new LocalVariable("held", function);
        var inner = new Method("Inner", text, [], Public) { Body = HandBuiltModel.Block(Run(Call(read, Site))) };
        Statement lambda = new LocalDeclarationStatement(held, new LambdaExpression(HandBuiltModel.Node(), [], Call(read, Site), body: null, isStatic: false, function));
        Statement named = new LocalDeclarationStatement(held, new FunctionReferenceExpression(HandBuiltModel.Node(Site), receiver: null, read, function));
        Statement declared = new LocalFunctionStatement(inner, []);

        Entry(name switch
        {
            "value-lambda-with-no-context" => [lambda],
            "value-lambda-under-a-context" => [Under(text, lambda)],
            "value-method-named-with-no-context" => [named],
            "value-method-named-under-a-context" => [Under(text, named)],
            "value-local-function-called-bare" => [declared, Run(Call(inner, Outer))],
            "value-local-function-called-under-a-later-context" => [declared, Under(text, Run(Call(inner)))],
            "value-local-function-declared-under-a-context" => [Under(text, declared, Run(Call(inner)))],
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        });

        return Judge();
    }

    private IReadOnlyList<Diagnostic> Constructors(string name)
    {
        var greeter = HandBuiltModel.Class("Greeter");
        var loud = HandBuiltModel.Class("Loud", greeter);
        types.AddRange([greeter, loud]);
        var greet = Construct(greeter, chained: null, at: 0, text);
        var chained = Construct(loud, greet, Site, name == "constructor-chain-from-a-constructor-with-the-clause" ? [text] : []);

        Entry(name switch
        {
            "constructor-with-no-context" => [Run(New(greet, Site))],
            "constructor-under-a-with-block" => [Under(text, Run(New(greet, Site)))],
            "constructor-chain-reached-bare" => [Run(New(chained, Outer))],
            "constructor-chain-under-a-context" => [Under(text, Run(New(chained)))],
            "constructor-chain-from-a-constructor-with-the-clause" => [Under(text, Run(New(chained)))],
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        });

        return Judge();
    }

    private IReadOnlyList<Diagnostic> Names(string name)
    {
        var bound = new LocalVariable("s", text) { IsContextBound = name != "name-of-an-ordinary-local-assigned" };
        var byRef = new Method("Fill", text, [new Parameter("slot", text, new ParameterModifier(false, true, OwnershipKind.Unrestricted, ByRefDirection.Ref))], Static) { DeclaringType = probe };
        var byValue = new Method("Show", text, [new Parameter("shown", text)], Static) { DeclaringType = probe };
        probe.MethodsList.AddRange([byRef, byValue]);

        Statement statement = name switch
        {
            "name-of-a-clause-assigned" or "name-of-an-ordinary-local-assigned" =>
                Run(new AssignmentExpression(HandBuiltModel.Node(Site), Ref(bound), HandBuiltModel.Literal(text), text)),
            "name-of-a-clause-passed-by-ref" => Run(Pass(byRef, Ref(bound, Site))),
            "name-of-a-clause-passed-by-value" => Run(Pass(byValue, Ref(bound, Site))),
            "name-of-a-clause-aliased-by-a-ref-local" => new LocalDeclarationStatement(new LocalVariable("alias", text) { IsRef = true }, Ref(bound, Site)),
            "name-of-a-clause-aliased-by-a-readonly-ref-local" =>
                new LocalDeclarationStatement(new LocalVariable("alias", text) { IsRef = true, IsReadonly = true }, Ref(bound, Site)),
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };

        _ = Declare(probe, "Use", Static, HandBuiltModel.Block(statement), text);

        return Judge();
    }

    private IReadOnlyList<Diagnostic> Clauses(string name)
    {
        var alias = new TypeAlias("Name", "Probe", new Scope(null), null, new TypeAliasDeclarationNode()) { AliasedType = text };
        var greeter = HandBuiltModel.Class("Greeter");
        types.Add(greeter);

        if(name == "clause-key-bound-twice-and-called-with-no-context")
        {
            Entry(Run(Call(Declare(probe, "Twice", Static, HandBuiltModel.Block(), text, text), Site)));
            return Judge();
        }

        _ = name switch
        {
            "clause-key-bound-twice" => Declare(probe, "Twice", Static, HandBuiltModel.Block(), text, text),
            "clause-key-bound-twice-through-an-alias" => Declare(probe, "Twice", Static, HandBuiltModel.Block(), text, alias),
            "clause-key-bound-twice-on-a-constructor" => Construct(greeter, chained: null, at: 0, text, text),
            "clause-keys-differ" => Declare(probe, "Both", Static, HandBuiltModel.Block(), text, integer),
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };

        return Judge();
    }

    private IReadOnlyList<Diagnostic> Implementations(string name)
    {
        var (slot, answering) = name switch
        {
            "implementation-omits-the-slots-clause" => (new IMistType[] { text }, Array.Empty<IMistType>()),
            "implementation-adds-a-clause" => ([], [text]),
            "implementation-binds-another-type" => ([text], [integer]),
            "implementation-repeats-the-slots-clause" => ([text], new IMistType[] { text }),
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };

        var contract = new AbstractType("Source", "Probe", new Scope(null), null, null);
        _ = Declare(contract, "Pull", Public, body: null, slot);
        var tap = HandBuiltModel.Class("Tap");
        tap.BaseAbstractTypes.Add(contract);
        types.AddRange([contract, tap]);
        _ = Declare(tap, "Pull", Public, HandBuiltModel.Block(), answering);

        return Judge();
    }

    private IReadOnlyList<Diagnostic> Generics(string name)
    {
        var element = new TypeParameter("T", 0, probe.Scope, probe);
        var get = Declare(probe, "Get", Static, HandBuiltModel.Block(), element);

        Method through(params IMistType[] own)
        {
            return Declare(probe, "Mid", Static, HandBuiltModel.Block(Run(Call(get, Site, element))), own);
        }

        Entry(name switch
        {
            "generic-key-closed-by-the-call-under-a-context" => [Under(integer, Run(Call(get, Site, integer)))],
            "generic-key-closed-by-the-call-with-no-context" => [Run(Call(get, Site, integer))],
            "generic-key-in-a-transparent-method" => [Under(integer, Run(Call(through())))],
            "generic-key-in-a-method-with-the-clause" => [Under(integer, Run(Call(through(element), 0, integer)))],
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        });

        return Judge();
    }

    private IReadOnlyList<Diagnostic> Scopes(string name)
    {
        var middle = Declare(probe, "Middle", Static, HandBuiltModel.Block(Run(Call(read, Site))));
        var judged = bodies[^1];
        Entry(name == "scope-judged-alone-with-a-bare-caller-outside" ? Run(Call(middle, Outer)) : Under(text, Run(Call(middle))));

        return Judge([judged]);
    }

    private IReadOnlyList<Diagnostic> Implicit(string name)
    {
        var bag = HandBuiltModel.Class("Bag");
        var owed = Declare(bag, "Owed", Public, HandBuiltModel.Block(), text);
        var plain = Declare(bag, "Plain", Public, HandBuiltModel.Block());
        var collection = HandBuiltModel.Literal(bag, value: null, Site);

        ForEachStatement loop(Method getEnumerator, Method moveNext)
        {
            return new ForEachStatement(new LocalVariable("item", text), collection, HandBuiltModel.Block()) { GetEnumerator = getEnumerator, MoveNext = moveNext };
        }

        var filled = new NewExpression(
            HandBuiltModel.Node(), bag, [], constructor: null,
            [new ObjectInitializerAssignment(HandBuiltModel.Node(Site), memberName: null, HandBuiltModel.Literal(text), targetType: null, owed)], parameterTypes: null);

        Entry(name switch
        {
            "implicit-enumerator-with-no-context" => [loop(owed, plain)],
            "implicit-move-next-with-no-context" => [loop(plain, owed)],
            "implicit-enumerator-under-a-context" => [Under(text, loop(owed, owed))],
            "implicit-add-with-no-context" => [Run(filled)],
            "implicit-add-under-a-context" => [Under(text, Run(filled))],
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        });

        return Judge();
    }

    private IReadOnlyList<Diagnostic> Initializer(MemberModifiers modifiers)
    {
        var field = new Field("Name", text, OwnershipKind.Unrestricted, modifiers) { DeclaringType = probe, Initializer = Call(read, Site) };
        probe.FieldsList.Add(field);
        bodies.Add(new SourceRoot(field.Initializer, probe, MistAssembly.Synthetic, field));

        return Judge();
    }

    private IReadOnlyList<Diagnostic> AnonymousKey()
    {
        var anonymous = model.Interner.GetOrCreateAnonymousType(["A"], [integer]);
        Entry(new WithBlockStatement(new AnonymousObjectExpression(HandBuiltModel.Node(Site), [HandBuiltModel.Literal(integer)], anonymous), HandBuiltModel.Block()));

        return Judge();
    }

    private void Entry(params Statement[] statements)
    {
        _ = Declare(probe, "Main", Static, HandBuiltModel.Block(statements));
    }

    private Method Declare(MemberType owner, string name, MemberModifiers modifiers, Block? body, params IMistType[] clauses)
    {
        var method = new Method(name, text, [], modifiers) { DeclaringType = owner, Body = body, DeclarationNode = HandBuiltModel.Node(Site) };
        Bind(method, clauses);
        owner.MethodsList.Add(method);

        if(body is not null)
        {
            bodies.Add(new SourceRoot(body, owner, MistAssembly.Synthetic, method));
        }

        return method;
    }

    private Constructor Construct(ClassType owner, Constructor? chained, int at, params IMistType[] clauses)
    {
        var constructor = new Constructor(owner.Name, owner, [], Public)
        {
            DeclaringType = owner,
            Body = HandBuiltModel.Block(),
            DeclarationNode = HandBuiltModel.Node(at),
            ChainKind = chained is null ? ConstructorChainKind.None : ConstructorChainKind.Base,
            ChainedConstructor = chained,
        };

        Bind(constructor, clauses);
        owner.ConstructorsList.Add(constructor);
        bodies.Add(new SourceRoot(constructor.Body, owner, MistAssembly.Synthetic, constructor));
        return constructor;
    }

    private static void Bind(Method method, IMistType[] clauses)
    {
        for(var i = 0; i < clauses.Length; i++)
        {
            method.ContextBindingsList.Add(new ContextBinding(clauses[i], $"c{i}") { Node = HandBuiltModel.Node(i == clauses.Length - 1 ? Site : i) });
        }
    }

    private static InvocationExpression Call(Method callee, int offset = 0, params IMistType[] closed)
    {
        var call = Pass(callee, offset);

        return closed.Length == 0
            ? call
            : new InvocationExpression(call.Node, call.Receiver, [], callee.ReturnType, callee, []) { ContextTypes = closed };
    }

    private static InvocationExpression Pass(Method callee, Expression argument)
    {
        return new InvocationExpression(
            HandBuiltModel.Node(), new MethodReferenceExpression(HandBuiltModel.Node(), callee), [argument], callee.ReturnType, callee,
            [.. callee.ParametersList.Select(parameter => parameter.Type)]);
    }

    private static InvocationExpression Pass(Method callee, int offset)
    {
        return new InvocationExpression(
            HandBuiltModel.Node(offset), new MethodReferenceExpression(HandBuiltModel.Node(), callee), [], callee.ReturnType, callee, []);
    }

    private static NewExpression New(Constructor constructor, int offset = 0)
    {
        return new NewExpression(HandBuiltModel.Node(offset), constructor.ReturnType, [], constructor, [], []);
    }

    private static LocalReferenceExpression Ref(LocalVariable local, int offset = 0)
    {
        return new LocalReferenceExpression(HandBuiltModel.Node(offset), local);
    }

    private static ExpressionStatement Run(Expression expression)
    {
        return new ExpressionStatement(expression);
    }

    private static WithBlockStatement Under(IMistType key, params Statement[] body)
    {
        return new WithBlockStatement(HandBuiltModel.Literal(key), HandBuiltModel.Block(body), binding: null, keyType: key);
    }

    private IReadOnlyList<Diagnostic> Judge(IReadOnlyList<SourceRoot>? alone = null)
    {
        var scope = new AnalysisScope(alone is null ? types : [], alone ?? bodies, [], []);
        var nodes = new List<object>();
        var roots = new Dictionary<object, SourceRoot>(ReferenceEqualityComparer.Instance);
        var assignments = new Dictionary<object, AssignmentExpression>(ReferenceEqualityComparer.Instance);

        foreach(var body in scope.Bodies)
        {
            foreach(var node in BodyWalker.DescendantsAndSelf(body.Root))
            {
                nodes.Add(node);
                roots[node] = body;

                if(node is AssignmentExpression assignment)
                {
                    assignments[assignment.Target] = assignment;
                }
            }
        }

        var diagnostics = new DiagnosticBag();
        var analyzer = new ContextAnalyzer();

        analyzer.Analyze(new AnalysisContext(
            model.Model, scope, nodes, roots, assignments, new HashSet<string>(StringComparer.Ordinal) { "hand-built.mist" },
            diagnostics, analyzer.Id, CloneConversionRefusals.None, program: new AnalysisScope(types, bodies, [], [])));

        return diagnostics.ToImmutableArray();
    }
}