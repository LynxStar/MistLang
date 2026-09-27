using Mist.ProgramAnalysis.Rules;
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