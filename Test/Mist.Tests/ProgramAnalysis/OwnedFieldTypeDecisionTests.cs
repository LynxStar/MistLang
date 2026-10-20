using Mist.Generated;
using Mist.ProgramAnalysis.Rules;
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

    private readonly HandBuiltModel model = new();
    private readonly DeclarationFragments fragment = new();
    private readonly StructType int32 = new("Int32", "Standard", new Scope(null), null, null);
    private readonly AbstractType clonable = new("IClonable<>", "Standard", new Scope(null), null, null);
    private readonly ClassType stringBuilder = new("StringBuilder", "System.Text", new Scope(null), null, null);

    [Fact]
    public void AnOwnedFieldTypeIsAdmitted()
    {
        var engine = Declared("Engine");
        engine.Modifiers = DeclarationFragments.Public with { IsOwned = true };
        Hold(engine, "Text", stringBuilder);

        Assert.Null(OwnedFieldTypes.WhyRefused(FieldOf(engine)));
    }

    [Fact]
    public void ATypeDeclaringCloneIsAdmittedWhateverItHolds()
    {
        var config = Declared("Config");
        Hold(config, "Text", stringBuilder);
        _ = DeclarationFragments.Method(config, "Clone", config);

        Assert.Null(OwnedFieldTypes.WhyRefused(FieldOf(config)));
    }

    [Fact]
    public void ATypeConformingToIClonableIsAdmitted()
    {
        var shell = new ClassType("Shell", "System", new Scope(null), null, null);
        shell.BaseAbstractTypes.Add(Clonable(shell));

        Assert.Null(OwnedFieldTypes.WhyRefused(FieldOf(shell)));
    }

    [Fact]
    public void ATypeWhoseFieldsAreAllClonableIsAdmittedByDerivation()
    {
        var text = new ClassType("String", "Standard", new Scope(null), null, null);
        text.BaseAbstractTypes.Add(Clonable(text));
        var widget = Declared("Widget");
        Hold(widget, "Value", int32);
        Hold(widget, "Label", text);

        Assert.Null(OwnedFieldTypes.WhyRefused(FieldOf(widget)));
    }

    [Fact]
    public void ATypeThatHoldsItselfIsDerivedFromItsOtherFields()
    {
        var node = Declared("Node");
        Hold(node, "Next", node);
        Hold(node, "Value", int32);

        Assert.Null(OwnedFieldTypes.WhyRefused(FieldOf(node)));
    }

    [Fact]
    public void ATypeWithANonClonableFieldIsRefused()
    {
        var log = Declared("Log");
        Hold(log, "Text", stringBuilder);

        Assert.Equal(NotOwnedOrClonable, OwnedFieldTypes.WhyRefused(FieldOf(log)));
    }

    [Fact]
    public void ANonClonableFieldTwoLevelsDownIsRefused()
    {
        var log = Declared("Log");
        Hold(log, "Text", stringBuilder);
        var journal = Declared("Journal");
        Hold(journal, "Count", int32);
        Hold(journal, "Entries", log);

        Assert.Equal(NotOwnedOrClonable, OwnedFieldTypes.WhyRefused(FieldOf(journal)));
    }

    [Fact]
    public void AClrShapedReferenceTypeWithoutCloneIsRefused()
    {
        Assert.Equal(NotOwnedOrClonable, OwnedFieldTypes.WhyRefused(FieldOf(stringBuilder)));
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

        Assert.Null(OwnedFieldTypes.WhyRefused(FieldOf(type)));
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

        Assert.Null(OwnedFieldTypes.WhyRefused(FieldOf(t)));
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

        Assert.Equal("a type parameter not constrained to an owned type or 'IClonable<T>'", OwnedFieldTypes.WhyRefused(FieldOf(t)));
    }

    [Fact]
    public void AGenericTypeIsDerivedThroughItsArguments()
    {
        var box = Declared("Box");
        var t = DeclarationFragments.Parameter(box, box.Scope, "T");
        Hold(box, "Value", t);

        Assert.Null(OwnedFieldTypes.WhyRefused(FieldOf(model.Interner.GetOrCreateConstructedType(box, [int32]))));
        Assert.Equal(NotOwnedOrClonable, OwnedFieldTypes.WhyRefused(FieldOf(model.Interner.GetOrCreateConstructedType(box, [stringBuilder]))));

        var nested = model.Interner.GetOrCreateConstructedType(box, [model.Interner.GetOrCreateConstructedType(box, [stringBuilder])]);
        Assert.Equal(NotOwnedOrClonable, OwnedFieldTypes.WhyRefused(FieldOf(nested)));
    }

    [Fact]
    public void AGenericFieldOverAnUnconstrainedParameterOfTheOwnedTypeIsRefused()
    {
        var box = Declared("Box");
        Hold(box, "Value", DeclarationFragments.Parameter(box, box.Scope, "T"));
        var holder = Declared("Holder");
        var u = DeclarationFragments.Parameter(holder, holder.Scope, "U");

        Assert.Equal(NotOwnedOrClonable, OwnedFieldTypes.WhyRefused(FieldOf(model.Interner.GetOrCreateConstructedType(box, [u]))));
    }

    [Fact]
    public void TheRefusalIsReportedAtTheFieldNamingItsTypeAndTheRestriction()
    {
        var holder = fragment.Class("Holder");
        holder.Modifiers = DeclarationFragments.Public with { IsOwned = true };
        holder.FieldsList.Add(new Field("Count", int32, OwnershipKind.Unrestricted, default) { DeclaringType = holder, DeclarationNode = HandBuiltModel.Node(20) });
        holder.FieldsList.Add(new Field("Text", stringBuilder, OwnershipKind.Unrestricted, default) { DeclaringType = holder, DeclarationNode = HandBuiltModel.Node(37) });

        var diagnostic = Assert.Single(fragment.Run(new OwnedFieldTypeAnalyzer()));

        Assert.Equal("MIST_PA_OWNED_FIELD_TYPE", diagnostic.Id.Value);
        Assert.Equal("hand-built.mist", diagnostic.CompilationUnitPath);
        Assert.Equal(1, diagnostic.Location.Start.Line);
        Assert.Equal(38, diagnostic.Location.Start.Column);
        Assert.Equal(
            "Field 'Text' of owned type 'Holder' has type 'StringBuilder', which is neither owned, clonable nor a value type. Every field of an owned type must be an owned type, implement 'IClonable<T>' (declared or derived from fields that all do), or be a value type, so that the owned type owns its whole graph.",
            diagnostic.Message);
    }

    private static ClassType Declared(string name)
    {
        return new ClassType(name, "Probe", new Scope(null), null, DeclarationFragments.At<BodyTypeTypeDeclarationNode>()) { Modifiers = DeclarationFragments.Public };
    }

    private static void Hold(ConcreteType holder, string name, IMistType type)
    {
        holder.FieldsList.Add(new Field(name, type, OwnershipKind.Unrestricted, default) { DeclaringType = holder });
    }

    private static Field FieldOf(IMistType type)
    {
        return new Field("Held", type, OwnershipKind.Unrestricted, default);
    }

    private ConstructedType Clonable(IMistType itself)
    {
        return model.Interner.GetOrCreateConstructedType(clonable, [itself]);
    }
}