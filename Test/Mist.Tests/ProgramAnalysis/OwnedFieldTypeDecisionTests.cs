using Mist.Generated;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class OwnedFieldTypeDecisionTests
{
    private const string NotOwnedOrClonable = "which is neither owned, clonable nor a value type";

    private static readonly CloneDerivation Clones = new([]);

    private readonly HandBuiltModel model = new();
    private readonly DeclarationFragments fragment = new();
    private readonly StructType int32 = new("Int32", "Standard", new Scope(null), null, null);
    private readonly AbstractType clonable = new("IClonable<>", "Standard", new Scope(null), null, null);
    private readonly ClassType stringBuilder = new("StringBuilder", "System.Text", new Scope(null), null, null);
    private readonly StructType nothing = new("Void", "Standard", new Scope(null), null, null);

    [Fact]
    public void AnOwnedFieldTypeIsAdmitted()
    {
        var engine = Declared("Engine");
        engine.Modifiers = DeclarationFragments.Public with { IsOwned = true };
        Hold(engine, "Text", stringBuilder);

        Assert.Null(OwnedFieldTypes.WhyRefused(engine, Clones));
    }

    [Fact]
    public void ATypeDeclaringCloneIsAdmittedWhateverItHolds()
    {
        var config = Declared("Config");
        Hold(config, "Text", stringBuilder);
        _ = DeclarationFragments.Method(config, "Clone", config);

        Assert.Null(OwnedFieldTypes.WhyRefused(config, Clones));
    }

    [Fact]
    public void ATypeConformingToIClonableIsAdmitted()
    {
        var shell = new ClassType("Shell", "System", new Scope(null), null, null);
        shell.BaseAbstractTypes.Add(Clonable(shell));

        Assert.Null(OwnedFieldTypes.WhyRefused(shell, Clones));
    }

    [Fact]
    public void ATypeWhoseFieldsAreAllClonableIsAdmittedByDerivation()
    {
        var text = new ClassType("String", "Standard", new Scope(null), null, null);
        text.BaseAbstractTypes.Add(Clonable(text));
        var widget = Declared("Widget");
        Hold(widget, "Value", int32);
        Hold(widget, "Label", text);

        Assert.Null(OwnedFieldTypes.WhyRefused(widget, Clones));
    }

    [Fact]
    public void ATypeThatHoldsItselfThroughAPlainFieldIsRefused()
    {
        var node = Declared("Node");
        Hold(node, "Next", node);
        Hold(node, "Value", int32);

        Assert.Equal(
            $"{NotOwnedOrClonable} ('Node' cannot be cloned: field 'Next' of 'Node' is not owned and holds 'Node', which can change; make that field 'owned' or give the type a 'Clone()' of its own)",
            OwnedFieldTypes.WhyRefused(node, Clones));
    }

    [Fact]
    public void ATypeThatHoldsItselfThroughAnOwnedFieldIsDerivedFromItsOtherFields()
    {
        var node = Declared("Node");
        Hold(node, "Next", node, owned: true);
        Hold(node, "Value", int32);

        Assert.Null(OwnedFieldTypes.WhyRefused(node, Clones));
    }

    [Fact]
    public void ATypeWithANonClonableFieldIsRefused()
    {
        var log = Declared("Log");
        Hold(log, "Text", stringBuilder);

        Assert.Equal(
            $"{NotOwnedOrClonable} ('Log' cannot be cloned: field 'Text' of 'Log' is not owned and holds 'StringBuilder', which can change; make that field 'owned' or give the type a 'Clone()' of its own)",
            OwnedFieldTypes.WhyRefused(log, Clones));
    }

    [Fact]
    public void ANonClonableFieldTwoLevelsDownIsRefusedNamingBothLevels()
    {
        var log = Declared("Log");
        Hold(log, "Text", stringBuilder);
        var journal = Declared("Journal");
        Hold(journal, "Count", int32);
        Hold(journal, "Entries", log, owned: true);

        Assert.Equal(
            $"{NotOwnedOrClonable} ('Journal' cannot be cloned: field 'Entries' of 'Journal' is owned but holds 'Log', which cannot be cloned (field 'Text' of 'Log' is not owned and holds 'StringBuilder', which can change); make that field 'owned' or give the type a 'Clone()' of its own)",
            OwnedFieldTypes.WhyRefused(journal, Clones));
    }

    [Fact]
    public void AFieldThatIsNotOwnedAndHoldsAChangeableClassIsRefusedEvenWhenThatClassIsClonable()
    {
        var log = Declared("Log");
        Hold(log, "Count", int32);
        var journal = Declared("Journal");
        Hold(journal, "Entries", log);

        Assert.Null(OwnedFieldTypes.WhyRefused(log, Clones));
        Assert.StartsWith($"{NotOwnedOrClonable} ('Journal' cannot be cloned: field 'Entries' of 'Journal' is not owned and holds 'Log'", OwnedFieldTypes.WhyRefused(journal, Clones));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AWideDeepGraphIsDerivedOncePerType(bool bottomRefused)
    {
        var level = Declared("A40");
        Hold(level, "Value", bottomRefused ? stringBuilder : int32);

        for(var i = 39; i >= 0; i--)
        {
            var above = Declared($"A{i}");
            Hold(above, "Left", level, owned: true);
            Hold(above, "Right", level, owned: true);
            level = above;
        }

        var reason = OwnedFieldTypes.WhyRefused(level, Clones);

        Assert.Equal(bottomRefused, reason?.StartsWith(NotOwnedOrClonable, StringComparison.Ordinal) == true);
        Assert.Equal(bottomRefused, reason is not null);
    }

    [Fact]
    public void ARefusalRetractsWhatWasAdmittedOnTheAssumptionItWasClonable()
    {
        var outer = Declared("Outer");
        var inner = Declared("Inner");
        Hold(outer, "Inner", inner, owned: true);
        Hold(outer, "Text", stringBuilder);
        Hold(inner, "Outer", outer, owned: true);

        Assert.StartsWith(NotOwnedOrClonable, OwnedFieldTypes.WhyRefused(outer, Clones));
        Assert.StartsWith(NotOwnedOrClonable, OwnedFieldTypes.WhyRefused(inner, Clones));
    }

    [Fact]
    public void AClrShapedReferenceTypeWithoutCloneIsRefused()
    {
        Assert.Equal(NotOwnedOrClonable, OwnedFieldTypes.WhyRefused(stringBuilder, Clones));
    }

    [Theory]
    [InlineData("struct")]
    [InlineData("primitive")]
    [InlineData("enum")]
    public void AValueTypeIsAdmittedWhateverItHolds(string kind)
    {
        var point = HandBuiltModel.Struct("Point");
        Hold(point, "Text", stringBuilder);
        IMistType type = kind switch
        {
            "struct" => point,
            "primitive" => int32,
            _ => HandBuiltModel.Enum("Color", int32),
        };

        Assert.Null(OwnedFieldTypes.WhyRefused(type, Clones));
    }

    [Theory]
    [InlineData("owned")]
    [InlineData("clonable")]
    [InlineData("struct")]
    public void AConstrainedTypeParameterIsAdmitted(string constraint)
    {
        var box = Declared("Box");
        var t = DeclarationFragments.Parameter(box, box.Scope, "T");
        t.ConstraintsList.Add(constraint switch
        {
            "owned" => new KeywordConstraint(TypeParameterConstraintKind.Owned),
            "struct" => new KeywordConstraint(TypeParameterConstraintKind.Struct),
            _ => new TypeConstraint(Clonable(t)),
        });

        Assert.Null(OwnedFieldTypes.WhyRefused(t, Clones));
    }

    [Theory]
    [InlineData("none")]
    [InlineData("class")]
    public void ATypeParameterNotConstrainedToOwnedOrClonableIsRefused(string constraint)
    {
        var box = Declared("Box");
        var t = DeclarationFragments.Parameter(box, box.Scope, "T");
        if(constraint == "class")
        {
            t.ConstraintsList.Add(new KeywordConstraint(TypeParameterConstraintKind.Class));
        }

        Assert.Equal("a type parameter not constrained to an owned type or 'IClonable<T>'", OwnedFieldTypes.WhyRefused(t, Clones));
    }

    [Fact]
    public void AGenericTypeIsDerivedThroughItsArguments()
    {
        var box = Declared("Box");
        var t = DeclarationFragments.Parameter(box, box.Scope, "T");
        Hold(box, "Value", t);

        Assert.Null(OwnedFieldTypes.WhyRefused(model.Interner.GetOrCreateConstructedType(box, [int32]), Clones));
        Assert.StartsWith(NotOwnedOrClonable, OwnedFieldTypes.WhyRefused(model.Interner.GetOrCreateConstructedType(box, [stringBuilder]), Clones));

        var nested = model.Interner.GetOrCreateConstructedType(box, [model.Interner.GetOrCreateConstructedType(box, [stringBuilder])]);
        Assert.StartsWith(NotOwnedOrClonable, OwnedFieldTypes.WhyRefused(nested, Clones));
    }

    [Fact]
    public void AGenericFieldOverAnUnconstrainedParameterOfTheOwnedTypeIsRefused()
    {
        var box = Declared("Box");
        Hold(box, "Value", DeclarationFragments.Parameter(box, box.Scope, "T"));
        var holder = Declared("Holder");
        var u = DeclarationFragments.Parameter(holder, holder.Scope, "U");

        Assert.StartsWith(NotOwnedOrClonable, OwnedFieldTypes.WhyRefused(model.Interner.GetOrCreateConstructedType(box, [u]), Clones));
    }

    [Fact]
    public void AnOptionalOrUnionTypeIsJudgedByItsConstituents()
    {
        var box = Declared("Box");
        var t = DeclarationFragments.Parameter(box, box.Scope, "T");

        Assert.Equal(NotOwnedOrClonable, OwnedFieldTypes.WhyRefused(new UnionType([stringBuilder, nothing]), Clones));
        Assert.Equal(NotOwnedOrClonable, OwnedFieldTypes.WhyRefused(new UnionType([int32, stringBuilder]), Clones));
        Assert.Equal("a type parameter not constrained to an owned type or 'IClonable<T>'", OwnedFieldTypes.WhyRefused(new UnionType([t, nothing]), Clones));
        Assert.Null(OwnedFieldTypes.WhyRefused(new UnionType([int32, nothing]), Clones));
    }

    [Fact]
    public void ATypeHoldingAnOptionalIsDerivedThroughItsConstituents()
    {
        var cell = Declared("Cell");
        Hold(cell, "Value", new UnionType([int32, nothing]));
        var log = Declared("Log");
        Hold(log, "Text", new UnionType([stringBuilder, nothing]));

        Assert.Null(OwnedFieldTypes.WhyRefused(cell, Clones));
        Assert.StartsWith(NotOwnedOrClonable, OwnedFieldTypes.WhyRefused(log, Clones));
    }

    [Fact]
    public void AnAutoPropertyOfTheOwnedTypeIsJudgedLikeAField()
    {
        var holder = Owned("Holder");
        var text = AutoProperty(holder, "Text", stringBuilder);
        _ = AutoProperty(holder, "Count", int32);

        var violation = Assert.Single(OwnedFieldTypes.Of(holder, Clones));

        Assert.Same(text, violation.Slot.Property);
        Assert.Null(violation.InheritedFrom);
    }

    [Fact]
    public void AFieldInheritedFromANonOwnedBaseIsJudged()
    {
        var root = Declared("Root");
        Hold(root, "Text", stringBuilder);
        var middle = Declared("Middle");
        middle.BaseType = root;
        Hold(middle, "Count", int32);
        var holder = Owned("Holder");
        holder.BaseType = middle;

        var violation = Assert.Single(OwnedFieldTypes.Of(holder, Clones));

        Assert.Equal("Text", violation.Slot.Name);
        Assert.Same(root, violation.InheritedFrom);
    }

    [Fact]
    public void AnOwnedBaseJudgesItsOwnFields()
    {
        var engine = Owned("Engine");
        Hold(engine, "Text", stringBuilder);
        var holder = Owned("Holder");
        holder.BaseType = engine;

        Assert.Empty(OwnedFieldTypes.Of(holder, Clones));
    }

    [Fact]
    public void AFieldInheritedFromAGenericBaseIsJudgedAtTheBaseArguments()
    {
        var box = Declared("Box");
        Hold(box, "Value", DeclarationFragments.Parameter(box, box.Scope, "T"));
        var admitted = Owned("Admitted");
        admitted.BaseType = model.Interner.GetOrCreateConstructedType(box, [int32]);
        var refused = Owned("Refused");
        refused.BaseType = model.Interner.GetOrCreateConstructedType(box, [stringBuilder]);

        Assert.Empty(OwnedFieldTypes.Of(admitted, Clones));
        var violation = Assert.Single(OwnedFieldTypes.Of(refused, Clones));
        Assert.Same(stringBuilder, violation.Type);
        Assert.Equal(NotOwnedOrClonable, violation.Reason);
    }

    [Fact]
    public void AnInheritedRefusalIsReportedAtTheOwnedTypeNamingTheBase()
    {
        var root = fragment.Class("Root");
        root.FieldsList.Add(new Field("Text", stringBuilder, OwnershipKind.Unrestricted, default) { DeclaringType = root, DeclarationNode = HandBuiltModel.Node(37) });
        var holder = fragment.Class("Holder");
        holder.Modifiers = DeclarationFragments.Public with { IsOwned = true };
        holder.BaseType = root;

        var diagnostic = Assert.Single(fragment.Run(new OwnedFieldTypeAnalyzer()));

        Assert.Equal(11, diagnostic.Position().Start.Column);
        Assert.Equal(
            "Field 'Root.Text' of owned type 'Holder' has type 'StringBuilder', which is neither owned, clonable nor a value type. Every field of an owned type must be an owned type, implement 'IClonable<T>' (declared or derived from fields that all do), or be a value type, so that the owned type owns its whole graph.",
            diagnostic.Message);
    }

    [Fact]
    public void TheRefusalIsReportedAtTheFieldNamingItsTypeAndTheRestriction()
    {
        var holder = fragment.Class("Holder");
        holder.Modifiers = DeclarationFragments.Public with { IsOwned = true };
        holder.FieldsList.Add(new Field("Count", int32, OwnershipKind.Unrestricted, default) { DeclaringType = holder, DeclarationNode = HandBuiltModel.Node(20) });
        holder.FieldsList.Add(new Field("Text", stringBuilder, OwnershipKind.Unrestricted, default) { DeclaringType = holder, DeclarationNode = HandBuiltModel.Node(37) });

        var diagnostic = Assert.Single(fragment.Run(new OwnedFieldTypeAnalyzer()));

        Assert.Equal("MIST0237", diagnostic.Number.Value);
        Assert.Equal("hand-built.mist", diagnostic.Location.Path);
        Assert.Equal(1, diagnostic.Position().Start.Line);
        Assert.Equal(38, diagnostic.Position().Start.Column);
        Assert.Equal(
            "Field 'Text' of owned type 'Holder' has type 'StringBuilder', which is neither owned, clonable nor a value type. Every field of an owned type must be an owned type, implement 'IClonable<T>' (declared or derived from fields that all do), or be a value type, so that the owned type owns its whole graph.",
            diagnostic.Message);
    }

    private static ClassType Declared(string name)
    {
        return new ClassType(name, "Probe", new Scope(null), null, DeclarationFragments.At<BodyTypeTypeDeclarationNode>()) { Modifiers = DeclarationFragments.Public };
    }

    private static ClassType Owned(string name)
    {
        var type = Declared(name);
        type.Modifiers = DeclarationFragments.Public with { IsOwned = true };
        return type;
    }

    private static Property AutoProperty(ConcreteType holder, string name, IMistType type)
    {
        var property = new Property(name, type, DeclarationFragments.Public)
        {
            Getter = new PropertyAccessor { IsAuto = true },
            Setter = new PropertyAccessor { IsAuto = true },
            DeclaringType = holder,
        };
        holder.PropertiesList.Add(property);
        return property;
    }

    private static void Hold(ConcreteType holder, string name, IMistType type, bool owned = false)
    {
        holder.FieldsList.Add(new Field(name, type, owned ? OwnershipKind.Owned : OwnershipKind.Unrestricted, default) { DeclaringType = holder });
    }

    private ConstructedType Clonable(IMistType itself)
    {
        return model.Interner.GetOrCreateConstructedType(clonable, [itself]);
    }
}