using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

// Tier 2: conformance obligations over declarations and member bodies constructed directly.
[Trait("Tier", "2")]
public sealed class ConformanceRefusalTests
{
    [Theory]
    [InlineData("generic-slot-not-filled", "MIST_PA_SLOT_NOT_FILLED")]
    [InlineData("generic-slot-wrong-argument", "MIST_PA_SLOT_SIGNATURE_MISMATCH")]
    [InlineData("hidden-slot-not-filled", "MIST_PA_SLOT_NOT_FILLED")]
    [InlineData("inherited-slot-not-filled", "MIST_PA_SLOT_NOT_FILLED")]
    [InlineData("property-slot-accessor-missing", "MIST_PA_PROPERTY_SLOT_ACCESSOR_MISSING")]
    [InlineData("readonly-contract-getter-writes", "MIST_PA_READONLY_CONTRACT_NOT_MET")]
    [InlineData("readonly-contract-inherited-member-writes", "MIST_PA_READONLY_CONTRACT_NOT_MET")]
    [InlineData("readonly-contract-mapped-member-writes", "MIST_PA_READONLY_CONTRACT_NOT_MET")]
    [InlineData("readonly-contract-method-reaches-a-write", "MIST_PA_READONLY_CONTRACT_NOT_MET")]
    [InlineData("readonly-contract-method-writes", "MIST_PA_READONLY_CONTRACT_NOT_MET")]
    [InlineData("readonly-contract-on-one-slot", "MIST_PA_READONLY_CONTRACT_NOT_MET")]
    [InlineData("redundant-conformance", "MIST_PA_REDUNDANT_CONFORMANCE")]
    [InlineData("slot-not-filled", "MIST_PA_SLOT_NOT_FILLED")]
    [InlineData("slot-not-public", "MIST_PA_SLOT_NOT_FILLED")]
    [InlineData("slot-parameter-mismatch", "MIST_PA_SLOT_SIGNATURE_MISMATCH")]
    [InlineData("slot-return-mismatch", "MIST_PA_SLOT_SIGNATURE_MISMATCH")]
    [InlineData("type-slot-constraint-not-satisfied", "MIST_PA_TYPE_SLOT_CONSTRAINT_NOT_SATISFIED")]
    [InlineData("type-slot-unfilled", "MIST_PA_TYPE_SLOT_UNFILLED")]
    [InlineData("with-block-second-generic-base-unanswered", "MIST_PA_CONFORMANCE_SLOT_UNANSWERED")]
    [InlineData("with-block-slot-unanswered", "MIST_PA_CONFORMANCE_SLOT_UNANSWERED")]
    public void RefusesNamedCase(string name, string expected)
    {
        var (fragment, offset) = Build(name);
        var diagnostic = Assert.Single(fragment.Run(new ConformanceAnalyzer()));
        Assert.Equal(expected, diagnostic.Id.Value);
        Assert.Equal("hand-built.mist", diagnostic.CompilationUnitPath);
        Assert.Equal(offset + 1, diagnostic.Location.Start.Column);
    }

    [Theory]
    [InlineData("indexer-not-filled", "MIST_PA_SLOT_NOT_FILLED", "slot 'int this[int]' of 'IContract': it declares no public member named 'this[]'")]
    [InlineData("indexer-not-public", "MIST_PA_SLOT_NOT_FILLED", "slot 'int this[int]'")]
    [InlineData("indexer-other-type", "MIST_PA_SLOT_SIGNATURE_MISMATCH", "slot 'int this[int]' of 'IContract': its 'this[]' has a different signature")]
    [InlineData("indexer-other-parameter", "MIST_PA_SLOT_SIGNATURE_MISMATCH", "slot 'int this[int]'")]
    [InlineData("indexer-passed-by-ref", "MIST_PA_SLOT_SIGNATURE_MISMATCH", "slot 'int this[int]'")]
    [InlineData("indexer-setter-missing", "MIST_PA_PROPERTY_SLOT_ACCESSOR_MISSING", "'Thing.this[int]' does not implement the 'set' accessor")]
    [InlineData("indexer-getter-missing", "MIST_PA_PROPERTY_SLOT_ACCESSOR_MISSING", "'Thing.this[int]' does not implement the 'get' accessor")]
    [InlineData("indexer-inherited-from-a-base-closed-otherwise", "MIST_PA_SLOT_SIGNATURE_MISMATCH", "slot 'int this[int]'")]
    [InlineData("indexer-closed-generic-slot-other-type", "MIST_PA_SLOT_SIGNATURE_MISMATCH", "slot 'int this[int]'")]
    [InlineData("with-block-indexer-unanswered", "MIST_PA_CONFORMANCE_SLOT_UNANSWERED", "leaves slot 'int this[int]' unanswered")]
    [InlineData("parameter-passed-by-value", "MIST_PA_SLOT_SIGNATURE_MISMATCH", "slot 'int Act(ref int)'")]
    [InlineData("parameter-passed-readonly", "MIST_PA_SLOT_SIGNATURE_MISMATCH", "slot 'int Act(ref int)'")]
    [InlineData("parameter-inherited-by-value", "MIST_PA_SLOT_SIGNATURE_MISMATCH", "slot 'int Act(ref int)'")]
    [InlineData("parameter-closed-generic-by-value", "MIST_PA_SLOT_SIGNATURE_MISMATCH", "slot 'int Act(ref int)'")]
    [InlineData("parameter-mapped-readonly", "MIST_PA_CONFORMANCE_SLOT_UNANSWERED", "leaves slot 'int Act(ref int)' unanswered")]
    [InlineData("return-not-by-ref", "MIST_PA_SLOT_SIGNATURE_MISMATCH", "slot 'ref int Act(ref int)'")]
    [InlineData("generic-method-concrete-return", "MIST_PA_SLOT_SIGNATURE_MISMATCH", "slot 'T Identity<T>(T)'")]
    [InlineData("generic-method-concrete-parameter", "MIST_PA_SLOT_SIGNATURE_MISMATCH", "slot 'T Identity<T>(T)'")]
    [InlineData("generic-method-nested-concrete", "MIST_PA_SLOT_SIGNATURE_MISMATCH", "slot 'List<T> Identity<T>(T)'")]
    [InlineData("generic-method-not-generic", "MIST_PA_SLOT_SIGNATURE_MISMATCH", "slot 'T Identity<T>(T)'")]
    public void AnInexactMember_IsRefusedAtTheConformer(string name, string expected, string message)
    {
        var (fragment, offset) = Exact(name);
        var diagnostic = Assert.Single(fragment.Run(new ConformanceAnalyzer()));

        Assert.Equal(expected, diagnostic.Id.Value);
        Assert.Contains(message, diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(offset + 1, diagnostic.Location.Start.Column);
    }

    [Theory]
    [InlineData("indexer-answered")]
    [InlineData("indexer-get-slot-answered-by-get-and-set")]
    [InlineData("indexer-inherited")]
    [InlineData("indexer-inherited-from-a-base-closed-alike")]
    [InlineData("indexer-closed-generic-slot")]
    [InlineData("indexer-diamond")]
    [InlineData("with-block-indexer-answered")]
    [InlineData("parameter-passed-by-ref")]
    [InlineData("parameter-inherited-by-ref")]
    [InlineData("parameter-closed-generic-by-ref")]
    [InlineData("parameter-mapped-by-ref")]
    [InlineData("return-by-ref")]
    [InlineData("generic-method-renamed")]
    [InlineData("generic-method-nested-renamed")]
    public void AnExactMember_AnswersTheSlot(string name)
    {
        Assert.Empty(Exact(name).Fragment.Run(new ConformanceAnalyzer()));
    }

    [Theory]
    [InlineData("method", false)]
    [InlineData("property", false)]
    [InlineData("indexer", false)]
    [InlineData("method", true)]
    public void AConformerOverAnUnresolvedBase_OwesNoUnansweredSlot(string member, bool throughAnotherBase)
    {
        var fragment = new DeclarationFragments();
        var integer = fragment.Built.Type("Int32");
        var contract = fragment.Abstract("IContract");
        var concrete = fragment.Class("Thing");
        concrete.BaseAbstractTypes.Add(contract);
        var unresolved = throughAnotherBase ? fragment.Class("Middle") : concrete;
        unresolved.BaseUnresolved = true;
        concrete.BaseType = throughAnotherBase ? unresolved : null;

        switch(member)
        {
            case "method":
                _ = DeclarationFragments.Method(contract, "Describe", integer);
                _ = DeclarationFragments.Method(concrete, "Describe", integer, integer);
                break;
            case "property":
                contract.PropertiesList.Add(new Property("Size", integer, DeclarationFragments.Public) { DeclaringType = contract, Getter = new PropertyAccessor() });
                break;
            default:
                _ = Indexed(contract, integer, integer, getter: true, setter: false);
                break;
        }

        Assert.Empty(fragment.Run(new ConformanceAnalyzer()));
    }

    [Fact]
    public void UnresolvedMappingTargetOwesNoUnansweredSlot()
    {
        var fragment = new DeclarationFragments();
        var contract = fragment.Abstract("IShow");
        _ = DeclarationFragments.Method(contract, "Show", fragment.Built.Type("String"));
        var concrete = fragment.Class("Thing");
        concrete.ConformancesList.Add(new ConformanceDeclaration(concrete, contract, "Refusal", [], ["Show"], HandBuiltModel.Node(40)));
        Assert.Empty(fragment.Run(new ConformanceAnalyzer()));
    }

    [Fact]
    public void NonReadonlySlotMayWriteWhenAnotherSlotRequiresReadonly()
    {
        var fragment = new DeclarationFragments();
        var integer = fragment.Built.Type("Int32");
        var contract = fragment.Abstract("IThing");
        contract.MethodsList.Add(new Method("Peek", integer, [], DeclarationFragments.Public with { IsReadonly = true }));
        _ = DeclarationFragments.Method(contract, "Poke", integer);
        var concrete = fragment.Class("Thing");
        concrete.BaseAbstractTypes.Add(contract);
        concrete.MethodsList.Add(new Method("Peek", integer, [], DeclarationFragments.Public)
        {
            DeclaringType = concrete,
            Body = DeclarationFragments.Returning(HandBuiltModel.Literal(integer, 1L)),
        });
        _ = WritingMethod(concrete, "Poke", integer);
        Assert.Empty(fragment.Run(new ConformanceAnalyzer()));
    }

    private static (DeclarationFragments Fragment, int Offset) Build(string name)
    {
        var fragment = new DeclarationFragments();
        var integer = fragment.Built.Type("Int32");
        var text = fragment.Built.Type("String");
        var contract = fragment.Abstract("IContract");
        var concrete = fragment.Class("Thing");
        concrete.BaseAbstractTypes.Add(contract);

        if(name.StartsWith("readonly-contract-", StringComparison.Ordinal))
        {
            return Readonly(fragment, contract, concrete, integer, name);
        }

        switch(name)
        {
            case "generic-slot-not-filled":
            case "generic-slot-wrong-argument":
                var parameter = DeclarationFragments.Parameter(contract, contract.Scope, "T");
                _ = DeclarationFragments.Method(contract, "Get", parameter);
                _ = DeclarationFragments.Method(contract, "Put", fragment.Built.Type("Void"), parameter);
                concrete.BaseAbstractTypes.Clear();
                concrete.BaseAbstractTypes.Add(fragment.Built.Interner.GetOrCreateConstructedType(contract, [integer]));
                _ = DeclarationFragments.Method(concrete, "Get", integer);

                if(name == "generic-slot-wrong-argument")
                {
                    _ = DeclarationFragments.Method(concrete, "Put", fragment.Built.Type("Void"), text);
                }

                break;

            case "hidden-slot-not-filled":
            case "inherited-slot-not-filled":
                var parent = fragment.Abstract("IParent");
                _ = DeclarationFragments.Method(parent, "Name", text);
                contract.BaseAbstractTypes.Add(parent);

                if(name == "hidden-slot-not-filled")
                {
                    contract.MethodsList.Add(new Method("Name", text, [], DeclarationFragments.Public)
                    {
                        Body = DeclarationFragments.Returning(HandBuiltModel.Literal(text, "child")),
                    });
                }
                else
                {
                    _ = DeclarationFragments.Method(contract, "Detail", text);
                    _ = DeclarationFragments.Method(concrete, "Detail", text);
                }

                break;

            case "property-slot-accessor-missing":
                contract.PropertiesList.Add(new Property("Label", text, DeclarationFragments.Public)
                {
                    DeclaringType = contract,
                    Getter = new PropertyAccessor(),
                    Setter = new PropertyAccessor(),
                });
                concrete.PropertiesList.Add(new Property("Label", text, DeclarationFragments.Public)
                {
                    DeclaringType = concrete,
                    Getter = new PropertyAccessor { IsAuto = true },
                });
                break;

            case "type-slot-unfilled":
            case "type-slot-constraint-not-satisfied":
                var bound = fragment.Abstract("IDescribable");
                var slot = new TypeSlot("Kind", 0, contract.Scope, contract);
                slot.ConstraintsList.Add(new TypeConstraint(bound));
                contract.TypeSlotsList.Add(slot);

                if(name == "type-slot-constraint-not-satisfied")
                {
                    var nested = fragment.Class("Kind", concrete);
                    _ = DeclarationFragments.Method(contract, "Make", slot);
                    _ = DeclarationFragments.Method(concrete, "Make", nested);
                }

                break;

            case "with-block-slot-unanswered":
                var ancestor = fragment.Abstract("IDescribable");
                var describe = DeclarationFragments.Method(ancestor, "Describe", text);
                contract.BaseAbstractTypes.Add(ancestor);
                _ = DeclarationFragments.Method(contract, "Detail", text);
                var answer = DeclarationFragments.Method(concrete, "Text", text);
                concrete.BaseAbstractTypes.Clear();
                concrete.ConformancesList.Add(new ConformanceDeclaration(concrete, contract, "Refusal",
                    [new MethodMapping(answer, describe, ancestor)], [], HandBuiltModel.Node(40)));
                return (fragment, 40);

            case "with-block-second-generic-base-unanswered":
                var reads = fragment.Abstract("IRead");
                var element = DeclarationFragments.Parameter(reads, reads.Scope, "T");
                var read = DeclarationFragments.Method(reads, "Read", element);
                var numbers = fragment.Built.Interner.GetOrCreateConstructedType(reads, [integer]);
                contract.BaseAbstractTypes.Add(numbers);
                contract.BaseAbstractTypes.Add(fragment.Built.Interner.GetOrCreateConstructedType(reads, [text]));
                var readNumber = DeclarationFragments.Method(concrete, "ReadNumber", integer);
                concrete.BaseAbstractTypes.Clear();
                concrete.ConformancesList.Add(new ConformanceDeclaration(concrete, contract, "Refusal",
                    [new MethodMapping(readNumber, read, numbers)], [], HandBuiltModel.Node(40)));
                return (fragment, 40);

            default:
                var required = DeclarationFragments.Method(contract, "Describe", text);

                if(name is "slot-not-public" or "slot-parameter-mismatch" or "slot-return-mismatch" or "redundant-conformance")
                {
                    var method = new Method("Describe", name == "slot-return-mismatch" ? integer : text,
                        name == "slot-parameter-mismatch" ? [new Parameter("n", integer)] : [],
                        DeclarationFragments.Public with { Accessibility = name == "slot-not-public" ? Accessibility.Private : Accessibility.Public })
                    {
                        DeclaringType = concrete,
                    };
                    concrete.MethodsList.Add(method);

                    if(name == "redundant-conformance")
                    {
                        concrete.ConformancesList.Add(new ConformanceDeclaration(concrete, contract, "Refusal",
                            [new MethodMapping(method, required, contract)], [], HandBuiltModel.Node(40)));
                        return (fragment, 40);
                    }
                }

                break;
        }

        return (fragment, 10);
    }

    private static (DeclarationFragments Fragment, int Offset) Exact(string name)
    {
        var fragment = new DeclarationFragments();
        var interner = fragment.Built.Interner;
        IMistType integer = fragment.Built.Type("Int32");
        IMistType text = fragment.Built.Type("String");
        var contract = fragment.Abstract("IContract");
        var concrete = fragment.Class("Thing");
        concrete.BaseAbstractTypes.Add(contract);

        if(name.Contains("indexer", StringComparison.Ordinal))
        {
            var bothAccessors = name is "indexer-answered" or "indexer-setter-missing" or "indexer-getter-missing";
            var slotType = name.Contains("closed-generic", StringComparison.Ordinal) ? Generic(contract, "T", interner, integer, concrete) : integer;
            _ = Indexed(contract, slotType, integer, getter: true, setter: bothAccessors);
            var owner = name.StartsWith("indexer-inherited", StringComparison.Ordinal) ? fragment.Class("Base") : concrete;

            if(owner != concrete)
            {
                concrete.BaseType = owner;
            }

            var (type, key) = (integer, integer);

            if(name.StartsWith("indexer-inherited-from-a-base", StringComparison.Ordinal))
            {
                type = DeclarationFragments.Parameter(owner, owner.Scope, "U");
                concrete.BaseType = interner.GetOrCreateConstructedType(owner, [name.EndsWith("alike", StringComparison.Ordinal) ? integer : text]);
            }

            switch(name)
            {
                case "indexer-not-filled" or "with-block-indexer-unanswered":
                    break;
                case "indexer-not-public":
                    _ = Indexed(owner, type, key, getter: true, setter: false, Accessibility.Private);
                    break;
                case "indexer-other-type" or "indexer-closed-generic-slot-other-type":
                    _ = Indexed(owner, text, key, getter: true, setter: false);
                    break;
                case "indexer-other-parameter":
                    _ = Indexed(owner, type, text, getter: true, setter: false);
                    break;
                case "indexer-passed-by-ref":
                    owner.IndexersList.Add(new Indexer(type, [Passed("key", key, ByRefDirection.Ref)], DeclarationFragments.Public)
                    {
                        DeclaringType = owner,
                        Getter = new PropertyAccessor(),
                    });
                    break;
                case "indexer-getter-missing":
                    _ = Indexed(owner, type, key, getter: false, setter: true);
                    break;
                default:
                    _ = Indexed(owner, type, key, getter: true, setter: name is "indexer-answered" or "indexer-get-slot-answered-by-get-and-set");
                    break;
            }

            if(name == "indexer-diamond")
            {
                var left = fragment.Abstract("ILeft");
                var right = fragment.Abstract("IRight");
                left.BaseAbstractTypes.Add(contract);
                right.BaseAbstractTypes.Add(contract);
                concrete.BaseAbstractTypes.Clear();
                concrete.BaseAbstractTypes.AddRange([left, right]);
            }

            if(name.StartsWith("with-block", StringComparison.Ordinal))
            {
                concrete.BaseAbstractTypes.Clear();
                concrete.ConformancesList.Add(new ConformanceDeclaration(concrete, contract, "Refusal", [], [], HandBuiltModel.Node(40)));
                return (fragment, 40);
            }

            return (fragment, 10);
        }

        if(name.StartsWith("generic-method", StringComparison.Ordinal))
        {
            var list = fragment.Built.Type("List<>");
            var nested = name.Contains("nested", StringComparison.Ordinal);
            var t = new TypeParameter("T", 0, contract.Scope, contract);
            var slot = DeclarationFragments.Method(contract, "Identity", nested ? interner.GetOrCreateConstructedType(list, [t]) : t, t);
            slot.TypeParametersList.Add(t);

            var u = new TypeParameter("U", 0, concrete.Scope, concrete);
            var returned = name is "generic-method-concrete-return" or "generic-method-not-generic" ? integer : u;
            var filler = DeclarationFragments.Method(
                concrete, "Identity",
                nested ? interner.GetOrCreateConstructedType(list, [name.EndsWith("concrete", StringComparison.Ordinal) ? integer : u]) : returned,
                name is "generic-method-concrete-parameter" or "generic-method-not-generic" ? integer : u);

            if(name != "generic-method-not-generic")
            {
                filler.TypeParametersList.Add(u);
            }

            return (fragment, 10);
        }

        var takes = name.Contains("closed-generic", StringComparison.Ordinal) ? Generic(contract, "T", interner, integer, concrete) : integer;
        var refReturn = name.StartsWith("return", StringComparison.Ordinal);
        contract.MethodsList.Add(new Method("Act", integer, [Passed("x", takes, ByRefDirection.Ref)], DeclarationFragments.Public with { IsRef = refReturn })
        {
            DeclaringType = contract,
        });

        var passing = name.EndsWith("by-value", StringComparison.Ordinal) ? ByRefDirection.None
            : name.EndsWith("readonly", StringComparison.Ordinal) ? ByRefDirection.In
            : ByRefDirection.Ref;
        var holder = name.Contains("inherited", StringComparison.Ordinal) ? fragment.Class("Base") : concrete;
        var mapped = name.Contains("mapped", StringComparison.Ordinal);
        var answer = new Method(mapped ? "Poke" : "Act", integer, [Passed("y", integer, passing)], DeclarationFragments.Public with { IsRef = name == "return-by-ref" })
        {
            DeclaringType = holder,
        };
        holder.MethodsList.Add(answer);

        if(holder != concrete)
        {
            concrete.BaseType = holder;
        }

        if(mapped)
        {
            concrete.BaseAbstractTypes.Clear();
            concrete.ConformancesList.Add(new ConformanceDeclaration(concrete, contract, "Refusal",
                [new MethodMapping(answer, contract.MethodsList[0], contract)], [], HandBuiltModel.Node(40)));
            return (fragment, 40);
        }

        return (fragment, 10);
    }

    private static TypeParameter Generic(AbstractType contract, string name, TypeInterner interner, IMistType argument, ClassType concrete)
    {
        var parameter = new TypeParameter(name, 0, contract.Scope, contract);
        contract.TypeParametersList.Add(parameter);
        concrete.BaseAbstractTypes.Clear();
        concrete.BaseAbstractTypes.Add(interner.GetOrCreateConstructedType(contract, [argument]));
        return parameter;
    }

    private static Indexer Indexed(
        MemberType owner, IMistType type, IMistType key, bool getter, bool setter, Accessibility access = Accessibility.Public)
    {
        var indexer = new Indexer(type, [new Parameter("key", key)], DeclarationFragments.Public with { Accessibility = access })
        {
            DeclaringType = owner,
            Getter = getter ? new PropertyAccessor() : null,
            Setter = setter ? new PropertyAccessor() : null,
        };
        owner.IndexersList.Add(indexer);
        return indexer;
    }

    private static Parameter Passed(string name, IMistType type, ByRefDirection passing)
    {
        return new Parameter(name, type, new ParameterModifier(passing == ByRefDirection.In, passing != ByRefDirection.None, OwnershipKind.Unrestricted, passing));
    }

    private static (DeclarationFragments Fragment, int Offset) Readonly(
        DeclarationFragments fragment, AbstractType contract, ClassType concrete, IMistType integer, string name)
    {
        var slot = new Method("Peek", integer, [], DeclarationFragments.Public with { IsReadonly = true });
        contract.MethodsList.Add(slot);

        if(name == "readonly-contract-getter-writes")
        {
            contract.MethodsList.Clear();
            contract.PropertiesList.Add(new Property("Size", integer, DeclarationFragments.Public with { IsReadonly = true })
            {
                DeclaringType = contract,
                Getter = new PropertyAccessor(),
            });
            concrete.PropertiesList.Add(new Property("Size", integer, DeclarationFragments.Public)
            {
                DeclaringType = concrete,
                DeclarationNode = HandBuiltModel.Node(20),
                Getter = new PropertyAccessor { Body = Write(concrete, integer) },
            });
            return (fragment, 20);
        }

        var owner = name == "readonly-contract-inherited-member-writes" ? fragment.Class("Counter") : concrete;
        var method = WritingMethod(owner, name == "readonly-contract-mapped-member-writes" ? "Look" : "Peek", integer);

        if(name == "readonly-contract-inherited-member-writes")
        {
            concrete.BaseType = owner;
        }

        if(name == "readonly-contract-method-reaches-a-write")
        {
            owner.MethodsList.Clear();
            var bump = WritingMethod(owner, "Bump", integer);
            method = new Method("Peek", integer, [], DeclarationFragments.Public)
            {
                DeclaringType = concrete,
                DeclarationNode = HandBuiltModel.Node(20),
                Body = DeclarationFragments.Returning(new InvocationExpression(HandBuiltModel.Node(),
                    new ThisExpression(HandBuiltModel.Node(), concrete), [], integer, bump, [])),
            };
            owner.MethodsList.Add(method);
        }

        if(name == "readonly-contract-mapped-member-writes")
        {
            concrete.BaseAbstractTypes.Clear();
            concrete.ConformancesList.Add(new ConformanceDeclaration(concrete, contract, "Refusal",
                [new MethodMapping(method, slot, contract)], [], HandBuiltModel.Node(40)));
        }

        if(name == "readonly-contract-on-one-slot")
        {
            _ = DeclarationFragments.Method(contract, "Poke", integer);
            _ = WritingMethod(concrete, "Poke", integer);
        }

        return (fragment, 20);
    }

    private static Method WritingMethod(ConcreteType owner, string name, IMistType integer)
    {
        var method = new Method(name, integer, [], DeclarationFragments.Public)
        {
            DeclaringType = owner,
            DeclarationNode = HandBuiltModel.Node(20),
            Body = Write(owner, integer),
        };
        owner.MethodsList.Add(method);
        return method;
    }

    private static Block Write(ConcreteType owner, IMistType integer)
    {
        var field = new Field("count", integer, OwnershipKind.Unrestricted, DeclarationFragments.Public) { DeclaringType = owner };
        owner.FieldsList.Add(field);
        return HandBuiltModel.Block(new ExpressionStatement(new AssignmentExpression(HandBuiltModel.Node(),
            new FieldReferenceExpression(HandBuiltModel.Node(), field), HandBuiltModel.Literal(integer, 1L), integer)));
    }
}