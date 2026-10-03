using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Helpers;

internal sealed partial class StructuredContextFragments
{
    private IReadOnlyList<Diagnostic> Accessors(string name)
    {
        var holder = HandBuiltModel.Class("Holder");
        var contract = new AbstractType("Labeled", "Probe", new Scope(null), null, null);
        var slot = new Property("Label", text, Public) { DeclaringType = contract, Getter = new PropertyAccessor() };
        contract.PropertiesList.Add(slot);
        holder.BaseAbstractTypes.Add(contract);
        types.AddRange([holder, contract]);
        var setterOwes = name.StartsWith("accessor-written", StringComparison.Ordinal);
        var shown = Accessor(holder, "Shown", getterOwes: !setterOwes, setterOwes);
        _ = Accessor(holder, "Label", getterOwes: true);
        var extended = Accessor(owner: null, "Tagged", getterOwes: true);

        NewExpression initialized(int offset)
        {
            return new NewExpression(
                HandBuiltModel.Node(), holder, [], constructor: null,
                [new ObjectInitializerAssignment(HandBuiltModel.Node(offset), "Shown", HandBuiltModel.Literal(text), text, member: new PropertyResult(shown, holder, []))],
                parameterTypes: null);
        }

        IsPatternExpression tested(int offset)
        {
            var subpattern = new PropertySubpattern(HandBuiltModel.Node(offset), "Shown", new CatchAllPattern(HandBuiltModel.Node(), text, null), text, new PropertyResult(shown, holder, []));

            return new IsPatternExpression(HandBuiltModel.Node(), HandBuiltModel.Literal(holder), new PropertyPattern(HandBuiltModel.Node(), holder, [subpattern]), integer);
        }

        Entry(name switch
        {
            "accessor-read-under-a-context" => [Under(text, Run(Read(shown, holder)))],
            "accessor-read-bare" => [Run(Read(shown, holder, Outer))],
            "accessor-nothing-reads" => [],
            "accessor-only-written-leaves-the-getter-unread" => [Run(Write(shown, holder, Outer))],
            "accessor-read-back-by-a-coalescing-assignment-bare" => [Run(Write(shown, holder, Outer, coalescing: true))],
            "accessor-written-under-a-context" => [Under(text, Run(Write(shown, holder)))],
            "accessor-written-bare" => [Run(Write(shown, holder, Outer))],
            "accessor-written-by-an-object-initializer-under-a-context" => [Under(text, Run(initialized(0)))],
            "accessor-written-by-an-object-initializer-bare" => [Run(initialized(Outer))],
            "accessor-read-by-a-property-pattern-under-a-context" => [Under(text, Run(tested(0)))],
            "accessor-read-by-a-property-pattern-bare" => [Run(tested(Outer))],
            "accessor-through-a-slot-under-a-context" => [Under(text, Run(Read(slot, contract)))],
            "accessor-through-a-slot-reached-bare" => [Run(Read(slot, contract, Outer))],
            "accessor-of-an-extension-block-under-a-context" => [Under(text, Run(Extended(extended)))],
            "accessor-of-an-extension-block-reached-bare" => [Run(Extended(extended, Outer))],
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        });

        return Judge();
    }

    private IReadOnlyList<Diagnostic> Indexers(string name)
    {
        var holder = HandBuiltModel.Class("Holder");
        types.Add(holder);
        var setterOwes = name.Contains("written", StringComparison.Ordinal);
        var indexer = new Indexer(text, [new Parameter("index", integer)], Public)
        {
            DeclaringType = holder,
            Getter = new PropertyAccessor { Body = Owing(!setterOwes) },
            Setter = new PropertyAccessor { Body = Owing(setterOwes) },
        };
        holder.IndexersList.Add(indexer);
        bodies.Add(new SourceRoot(indexer.Getter.Body!, holder, MistAssembly.Synthetic, indexer));
        bodies.Add(new SourceRoot(indexer.Setter.Body!, holder, MistAssembly.Synthetic, indexer));

        var count = new Property("Count", integer, Public) { DeclaringType = holder, Getter = new PropertyAccessor { Body = HandBuiltModel.Block() } };
        holder.PropertiesList.Add(count);
        bodies.Add(new SourceRoot(count.Getter.Body!, holder, MistAssembly.Synthetic, count));

        IndexExpression element(int offset)
        {
            return new IndexExpression(HandBuiltModel.Node(offset), HandBuiltModel.Literal(holder), HandBuiltModel.Literal(integer), text) { Indexer = indexer };
        }

        IsPatternExpression listed(int offset)
        {
            var pattern = new ListPattern(HandBuiltModel.Node(offset), [new ListPatternElement(new CatchAllPattern(HandBuiltModel.Node(), text, null), IsSlice: false, ScrutineeType: text)], holder)
            {
                Reads = new CountedIndexer(indexer, integer, count),
            };

            return new IsPatternExpression(HandBuiltModel.Node(), HandBuiltModel.Literal(holder), pattern, integer);
        }

        Entry(name switch
        {
            "indexer-read-under-a-context" => [Under(text, Run(element(0)))],
            "indexer-read-bare" => [Run(element(Outer))],
            "indexer-read-by-a-list-pattern-under-a-context" => [Under(text, Run(listed(0)))],
            "indexer-read-by-a-list-pattern-bare" => [Run(listed(Outer))],
            "indexer-written-under-a-context" => [Under(text, Run(new AssignmentExpression(HandBuiltModel.Node(), element(0), HandBuiltModel.Literal(text), text)))],
            "indexer-written-bare" => [Run(new AssignmentExpression(HandBuiltModel.Node(), element(Outer), HandBuiltModel.Literal(text), text))],
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        });

        return Judge();
    }

    private IReadOnlyList<Diagnostic> Operators(string name)
    {
        var vec = HandBuiltModel.Struct("Vec");
        types.Add(vec);
        var body = Owing(true);
        var plus = new Operator(OperatorKind.Plus, vec, [new Parameter("a", vec), new Parameter("b", vec)], Static) { DeclaringType = vec, Body = body };
        var negate = new Operator(OperatorKind.Minus, vec, [new Parameter("a", vec)], Static) { DeclaringType = vec, Body = body };
        var conversion = new CastOperator(text, new Parameter("value", vec), name.Contains("implicit", StringComparison.Ordinal), Static) { DeclaringType = vec, Body = body };
        vec.OperatorsList.AddRange([plus, negate]);
        vec.CastOperatorsList.Add(conversion);
        Method called = name.Split('-')[0] switch
        {
            "operator" when name.Contains("unary", StringComparison.Ordinal) => negate,
            "operator" => plus,
            _ => conversion,
        };
        bodies.Add(new SourceRoot(body, vec, MistAssembly.Synthetic, called));

        Expression applied(int offset)
        {
            var node = HandBuiltModel.Node(offset);
            var operand = HandBuiltModel.Literal(vec);
            var function = new ConversionFunction(conversion, vec, text);

            return called switch
            {
                Operator { IsUnary: true } => new UnaryExpression(node, UnaryOperatorKind.Negate, operand, vec) { Operator = negate },
                Operator => new BinaryExpression(node, operand, BinaryOperatorKind.Add, HandBuiltModel.Literal(vec), vec) { Operator = plus },
                CastOperator { IsImplicit: true } => new ImplicitConversionExpression(node, operand, text, [function]),
                _ => new CastExpression(node, operand, text, [function], text),
            };
        }

        Entry(name switch
        {
            "operator-applied-under-a-context" or "operator-unary-applied-under-a-context"
                or "conversion-cast-under-a-context" or "conversion-implicit-under-a-context" => [Under(text, Run(applied(0)))],
            "operator-applied-bare" or "operator-unary-applied-bare" or "conversion-cast-bare" or "conversion-implicit-bare" => [Run(applied(Outer))],
            "operator-nothing-applies" => [],
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        });

        return Judge();
    }

    private IReadOnlyList<Diagnostic> Drops(string name)
    {
        var token = Linear("Token");
        var box = Linear("Box");
        types.AddRange([token, box]);
        var owed = name == "drop-owed-a-key-at-a-locals-exit";
        var drop = Declare(token, "Drop", Public, Owing(!owed), owed ? [text] : []);
        _ = Declare(box, "Drop", Public, HandBuiltModel.Block());
        box.FieldsList.Add(new Field("token", token, OwnershipKind.Unrestricted, Public) { DeclaringType = box });
        var take = new Method("Take", text, [new Parameter("token", token)], Static) { DeclaringType = probe, Body = HandBuiltModel.Block(), DeclarationNode = HandBuiltModel.Node() };
        probe.MethodsList.Add(take);
        bodies.Add(new SourceRoot(take.Body!, probe, MistAssembly.Synthetic, take));

        Statement declared(ConcreteType type, int offset = 0)
        {
            return new LocalDeclarationStatement(new LocalVariable("held", type), HandBuiltModel.Literal(type, value: null, offset));
        }

        InvocationExpression taken(int offset)
        {
            return new InvocationExpression(
                HandBuiltModel.Node(offset), new MethodReferenceExpression(HandBuiltModel.Node(), take), [HandBuiltModel.Literal(token)], take.ReturnType, take, [token]);
        }

        Entry(name switch
        {
            "drop-owed-a-key-at-a-locals-exit" => [declared(token, Site)],
            "drop-of-a-local-under-a-context" => [Under(text, declared(token))],
            "drop-of-a-local-reached-bare" => [declared(token, Outer)],
            "drop-of-a-local-after-a-later-with" => [declared(token, Outer), Under(text)],
            "drop-of-a-with-blocks-value-under-an-outer-context" => [Under(text, new WithBlockStatement(HandBuiltModel.Literal(token), HandBuiltModel.Block()))],
            "drop-of-a-with-blocks-value-is-after-its-pop" =>
                [new WithBlockStatement(HandBuiltModel.Literal(token, value: null, Outer), HandBuiltModel.Block(), binding: null, keyType: text)],
            "drop-of-a-parameter-under-a-context" => [Under(text, Run(taken(0)))],
            "drop-of-a-parameter-reached-bare" => [Run(taken(Outer))],
            "drop-of-a-field-in-a-drop-body-under-a-context" => [Under(text, declared(box))],
            "drop-of-a-field-in-a-drop-body-reached-bare" => [declared(box, Outer)],
            "drop-called-explicitly-under-a-context" => [Under(text, Run(Call(drop)))],
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        });

        return Judge();
    }

    private IReadOnlyList<Diagnostic> Disposals(string name)
    {
        var disposable = new AbstractType("IDisposable", "System", new Scope(null), null, null) { Assembly = MistAssembly.Ambient };
        _ = Declare(disposable, "Dispose", Public, body: null);
        var resource = HandBuiltModel.Class("Resource");
        resource.BaseAbstractTypes.Add(disposable);
        var sequence = HandBuiltModel.Class("Sequence");
        types.AddRange([disposable, resource, sequence]);
        var currentOwes = name.StartsWith("loop-current", StringComparison.Ordinal);
        var dispose = Declare(resource, "Dispose", Public, Owing(!currentOwes));
        var moveNext = Declare(resource, "MoveNext", Public, HandBuiltModel.Block());
        var current = Accessor(resource, "Current", getterOwes: currentOwes);
        var getEnumerator = new Method("GetEnumerator", resource, [], Public) { DeclaringType = sequence, Body = HandBuiltModel.Block() };
        sequence.MethodsList.Add(getEnumerator);
        bodies.Add(new SourceRoot(getEnumerator.Body!, sequence, MistAssembly.Synthetic, getEnumerator));

        ForEachStatement loop(int offset)
        {
            return new ForEachStatement(new LocalVariable("item", text), HandBuiltModel.Literal(sequence, value: null, offset), HandBuiltModel.Block())
            {
                GetEnumerator = getEnumerator,
                MoveNext = moveNext,
                Current = current,
            };
        }

        Entry(name switch
        {
            "dispose-called-under-a-context" => [Under(text, Run(Call(dispose)))],
            "dispose-called-bare" => [Run(Call(dispose, Outer))],
            "loop-current-read-under-a-context" or "loop-enumerator-disposed-under-a-context" => [Under(text, loop(0))],
            "loop-current-read-bare" or "loop-enumerator-disposed-bare" => [loop(Outer)],
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        });

        return Judge();
    }

    private Property Accessor(MemberType? owner, string name, bool getterOwes, bool setterOwes = false)
    {
        var getter = new PropertyAccessor { Body = Owing(getterOwes) };
        var setter = new PropertyAccessor { Body = Owing(setterOwes) };
        var property = new Property(name, text, Public) { DeclaringType = owner, Getter = getter, Setter = setter };
        owner?.PropertiesList.Add(property);
        bodies.Add(new SourceRoot(getter.Body!, owner, MistAssembly.Synthetic, property));
        bodies.Add(new SourceRoot(setter.Body!, owner, MistAssembly.Synthetic, property));

        return property;
    }

    private Block Owing(bool owes)
    {
        return owes ? HandBuiltModel.Block(Run(Call(read, Site))) : HandBuiltModel.Block();
    }

    private static ClassType Linear(string name)
    {
        var type = HandBuiltModel.Class(name);
        type.Modifiers = Public with { Discipline = SubstructuralDiscipline.Linear };

        return type;
    }

    private static MemberAccessExpression Read(Property property, MemberType declaring, int offset = 0)
    {
        return new MemberAccessExpression(
            HandBuiltModel.Node(offset), HandBuiltModel.Literal(declaring), property.Name, property.Type, member: new PropertyResult(property, declaring, []));
    }

    private AssignmentExpression Write(Property property, MemberType declaring, int offset = 0, bool coalescing = false)
    {
        return new AssignmentExpression(HandBuiltModel.Node(), Read(property, declaring, offset), HandBuiltModel.Literal(text), text, coalescing);
    }

    private static ExtensionPropertyAccessExpression Extended(Property property, int offset = 0)
    {
        return new ExtensionPropertyAccessExpression(HandBuiltModel.Node(offset), HandBuiltModel.Literal(property.Type), property, "Carrier", property.Type);
    }
}