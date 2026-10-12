using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

// Tier 2: ConformanceAnalyzer over a hand-built `abstract IShelf { type Holder<T> : <bound>; }` and a
// conformer whose nested generic alias `type Holder<...> = <target>` has the arity and target a case names.
[Trait("Tier", "2")]
public sealed class ConformanceGenericAliasFillerTests
{
    [Theory]
    [InlineData("IHolder", "Box", 1, "")]
    [InlineData("IHolder", "Plain", 1, "MIST_PA_TYPE_SLOT_CONSTRAINT_NOT_SATISFIED")]
    [InlineData("class", "Box", 1, "")]
    [InlineData("struct", "Box", 1, "MIST_PA_TYPE_SLOT_CONSTRAINT_NOT_SATISFIED")]
    [InlineData("IHolder", "Box", 0, "MIST_PA_TYPE_SLOT_UNFILLED")]
    [InlineData("IHolder", "Box", 2, "MIST_PA_TYPE_SLOT_UNFILLED")]
    public void AGenericAliasOfTheSlotsArity_FillsIt_AndIsJudgedByItsExpansionOverTheSlotsParameters(
        string bound, string target, int arity, string expected)
    {
        var fragment = new DeclarationFragments();
        var holder = Generic(fragment.Abstract("IHolder<>"), out _);
        var shelf = fragment.Abstract("IShelf");
        var slot = new TypeSlot("Holder<>", 0, shelf.Scope, shelf);
        var slotted = new TypeParameter("T", 0, slot.Scope, slot);
        slot.TypeParametersList.Add(slotted);
        slot.ConstraintsList.Add(bound switch
        {
            "class" => new KeywordConstraint(TypeParameterConstraintKind.Class),
            "struct" => new KeywordConstraint(TypeParameterConstraintKind.Struct),
            _ => new TypeConstraint(new ConstructedType(holder, [slotted])),
        });
        shelf.TypeSlotsList.Add(slot);
        var box = Generic(fragment.Class("Box<>"), out var boxed);
        box.BaseAbstractTypes.Add(new ConstructedType(holder, [boxed]));
        var plain = Generic(fragment.Class("Plain<>"), out _);

        var conformer = fragment.Class("Shelf");
        conformer.BaseAbstractTypes.Add(shelf);
        var alias = fragment.Alias(arity switch { 0 => "Holder", 1 => "Holder<>", _ => "Holder<,>" }, fragment.Built.Type("Int32"), conformer);

        for(var i = 0; i < arity; i++)
        {
            alias.TypeParametersList.Add(new TypeParameter($"A{i}", i, alias.Scope, alias));
        }

        alias.AliasedType = new ConstructedType(target == "Box" ? box : plain, [arity == 0 ? fragment.Built.Type("Int32") : alias.TypeParametersList[0]]);

        var findings = fragment.Run(new ConformanceAnalyzer());

        Assert.Equal(expected, string.Join(", ", findings.Select(finding => finding.Id.Value)));
        Assert.All(findings.Where(finding => finding.Id.Value == "MIST_PA_TYPE_SLOT_CONSTRAINT_NOT_SATISFIED"),
            finding => Assert.Contains("Nested type 'Holder' does not satisfy constraint", finding.Message, StringComparison.Ordinal));
    }

    private static T Generic<T>(T type, out TypeParameter parameter) where T : MemberType
    {
        parameter = new TypeParameter("T", 0, type.Scope, type);
        type.TypeParametersList.Add(parameter);
        return type;
    }
}