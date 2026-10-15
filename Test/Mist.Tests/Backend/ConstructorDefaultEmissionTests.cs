using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.GenericDefaultShapes;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class ConstructorDefaultEmissionTests
{
    private static readonly MemberModifiers Public = new() { Accessibility = Accessibility.Public };
    private readonly GenericDefaultShapes shapes = new();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UninitializedStorage_ReceivesItsSemanticDefaultBeforeTheBody(bool isStruct)
    {
        var holder = shapes.Type("Holder", isStruct);
        _ = Slot(holder, "Item", shapes.Inner());
        var count = Slot(holder, "Count", shapes.Integer);
        var constructor = Declare(holder, parameters: [new Parameter("ignored", shapes.Integer)], body: HandBuiltModel.Block(new ExpressionStatement(
            new AssignmentExpression(HandBuiltModel.Node(), new FieldReferenceExpression(HandBuiltModel.Node(), count),
                HandBuiltModel.Literal(shapes.Integer, 9L), shapes.Integer))));

        var text = Emit(constructor, holder);

        Assert.Contains("this.Item = Inner.__CreateDefault();", text, StringComparison.Ordinal);
        Assert.Contains("this.Count = default(int);", text, StringComparison.Ordinal);
        Assert.True(text.IndexOf("this.Item =", StringComparison.Ordinal) < text.IndexOf("Count = 9;", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("string", "\"\"")]
    [InlineData("enum", "Mode.__CreateDefault()")]
    [InlineData("optional", "null")]
    [InlineData("constructed", "Probe.Box<Inner>.__CreateDefault(() => Inner.__CreateDefault())")]
    [InlineData("constrained", "__default_T()")]
    public void StorageKinds_UseTheExistingDefaultAndWitnessRoute(string kind, string expected)
    {
        var holder = shapes.Type("Holder");
        IMistType storageType = kind switch
        {
            "string" => shapes.String,
            "enum" => Mode(),
            "optional" => new UnionType([shapes.Integer, new HandBuiltModel().Type("Void")]),
            "constructed" => Construct(shapes.Box(), shapes.Inner()),
            "constrained" => Parameter(holder, defaultConstrained: true),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        _ = Slot(holder, "Item", storageType);

        var text = Emit(Constructor(holder, shapes.Integer), holder);

        Assert.Contains($"this.Item = {expected};", text, StringComparison.Ordinal);
        if(kind == "constrained")
        {
            Assert.True(text.IndexOf("this.__default_T = __default_T;", StringComparison.Ordinal)
                < text.IndexOf("this.Item =", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AutoProperties_IncludingGetOnly_ReceiveSemanticDefaults(bool getOnly)
    {
        var holder = shapes.Type("Holder");
        holder.PropertiesList.Add(Property("Item", shapes.Inner(), getOnly));

        Assert.Contains("this.Item = Inner.__CreateDefault();", Emit(Constructor(holder), holder), StringComparison.Ordinal);
    }

    [Fact]
    public void StaticAndComputedPropertiesAndNondefaultableStorage_AreNotInjected()
    {
        var holder = shapes.Type("Holder");
        holder.FieldsList.Add(new Field("Shared", shapes.Inner(), OwnershipKind.Unrestricted, Public with { IsStatic = true }));
        holder.PropertiesList.Add(Property("SharedProperty", shapes.Inner(), isStatic: true));
        holder.PropertiesList.Add(new Property("Computed", shapes.Inner(), Public)
        {
            Getter = new PropertyAccessor { Body = HandBuiltModel.Block() },
        });
        _ = Slot(holder, "Required", shapes.Connection());

        var text = Emit(Constructor(holder), holder);

        Assert.DoesNotContain("this.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WrittenInitializer_IsLeftToItsDeclaration()
    {
        var holder = shapes.Type("Holder");
        _ = Slot(holder, "Count", shapes.Integer, HandBuiltModel.Literal(shapes.Integer, 5L));
        var property = Property("Item", shapes.Inner());
        property.Initializer = new DefaultExpression(HandBuiltModel.Node(), property.Type);
        holder.PropertiesList.Add(property);

        Assert.DoesNotContain("this.", Emit(Constructor(holder), holder), StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultBlock_ChangesOnlyTheSynthesizedDefault()
    {
        var holder = shapes.Type("Holder");
        _ = Slot(holder, "Count", shapes.Integer);
        var constructor = Constructor(holder);
        Block(holder, new DefaultValueEntry("Count", HandBuiltModel.Literal(shapes.Integer, 7L)));
        var defaultWriter = new CSharpWriter();
        var anyEmitted = false;
        DefaultEmitter.Emit(holder, defaultWriter, new DiagnosticBag(), ref anyEmitted);

        Assert.Contains("this.Count = default(int);", Emit(constructor, holder), StringComparison.Ordinal);
        Assert.Contains("this.Count = 7;", defaultWriter.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("this.Count = default(int);", defaultWriter.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void DeferredInitializer_UsesTheWrittenValueEvenWhenTheDefaultBlockOverridesIt()
    {
        var holder = shapes.Type("Holder");
        var parameter = Parameter(holder, defaultConstrained: true);
        _ = Slot(holder, "Item", parameter, new DefaultExpression(HandBuiltModel.Node(), parameter));
        Block(holder, new DefaultValueEntry("Item", HandBuiltModel.Literal(parameter, null)));

        Assert.Contains("this.Item = __default_T();", Emit(Constructor(holder), holder), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThisChain_DoesNotRepeatStorageDefaultsOrDeferredInitializers(bool isStruct)
    {
        var holder = shapes.Type("Holder", isStruct);
        _ = Slot(holder, "Item", shapes.Inner());
        var parameter = Parameter(holder, defaultConstrained: true);
        _ = Slot(holder, "Deferred", parameter, new DefaultExpression(HandBuiltModel.Node(), parameter));
        var root = Constructor(holder, shapes.Integer);
        var delegating = Declare(holder, chain: ConstructorChainKind.This);
        delegating.ChainedConstructor = root;
        delegating.ChainArgumentsList.Add(HandBuiltModel.Literal(shapes.Integer, 1L));
        var text = Emit(delegating, holder, isStruct ? new Dictionary<StructType, string>() : null);

        Assert.DoesNotContain("this.Item =", text, StringComparison.Ordinal);
        Assert.DoesNotContain("this.Deferred =", text, StringComparison.Ordinal);
        Assert.Contains(" : this(__default_T, 1)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("this = new", text, StringComparison.Ordinal);
        Assert.Contains("this.Item = Inner.__CreateDefault();", Emit(root, holder), StringComparison.Ordinal);
    }

    [Fact]
    public void BaseClassChain_InitializesOnlyItsOwnStorage()
    {
        var parent = shapes.Type("Parent");
        _ = Slot(parent, "Inherited", shapes.Inner());
        var baseConstructor = Constructor(parent);
        var child = shapes.Type("Child", baseType: parent);
        _ = Slot(child, "Own", shapes.Inner());
        var constructor = Declare(child, chain: ConstructorChainKind.Base);
        constructor.ChainedConstructor = baseConstructor;

        var text = Emit(constructor, child);

        Assert.Contains(": base()", text, StringComparison.Ordinal);
        Assert.Contains("this.Own = Inner.__CreateDefault();", text, StringComparison.Ordinal);
        Assert.DoesNotContain("this.Inherited =", text, StringComparison.Ordinal);
    }

    [Fact]
    public void FlattenedStructBase_InitializesEachStorageSlotOnce()
    {
        var parent = shapes.Type("Parent", isStruct: true);
        _ = Slot(parent, "Inherited", shapes.Inner());
        var baseConstructor = Declare(parent, body: HandBuiltModel.Block());
        var child = shapes.Type("Child", isStruct: true, baseType: parent);
        _ = Slot(child, "Own", shapes.Inner());
        var constructor = Declare(child, chain: ConstructorChainKind.Base, body: HandBuiltModel.Block());
        constructor.ChainedConstructor = baseConstructor;
        var writer = new CSharpWriter();
        var bag = new DiagnosticBag();
        var model = new HandBuiltModel().Model;
        var anyEmitted = false;
        TypeEmitter.Emit(child, ConstraintClosureMap.Build(model, bag), model.VariadicUses, writer, bag, ref anyEmitted);
        var text = writer.ToString();

        Assert.Empty(bag.ToImmutableArray());
        Assert.Equal(1, text.Split("this.Inherited = Inner.__CreateDefault();", StringSplitOptions.None).Length - 1);
        Assert.Equal(1, text.Split("this.Own = Inner.__CreateDefault();", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void FlattenedClosedBase_ResolvesStorageDefaultsAfterSubstitution()
    {
        var parent = shapes.Type("Parent", isStruct: true);
        _ = Slot(parent, "Inherited", Parameter(parent));
        _ = Declare(parent, body: HandBuiltModel.Block());
        var child = shapes.Type("Child", isStruct: true, baseType: Construct(parent, shapes.Inner()));
        var constructor = Declare(child, body: HandBuiltModel.Block());

        Assert.Contains("this.Inherited = Inner.__CreateDefault();", Emit(constructor, child, new Dictionary<StructType, string>()),
            StringComparison.Ordinal);
    }

    [Fact]
    public void StaticConstructor_DoesNotInitializeInstanceStorage()
    {
        var holder = shapes.Type("Holder");
        _ = Slot(holder, "Item", shapes.Inner());
        var constructor = new Constructor("Holder", holder, [], Public with { IsStatic = true }) { DeclaringType = holder };

        Assert.DoesNotContain("this.", Emit(constructor, holder), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RefinementStorage_ReceivesZeroOnlyWhenItsPredicateAdmitsIt(bool admitsZero)
    {
        var holder = shapes.Type("Holder");
        var refined = new RefinedType(shapes.Integer, HandBuiltModel.Node(), "Count")
        {
            Predicate = new ComparisonPattern(HandBuiltModel.Node(), shapes.Integer,
                admitsZero ? ComparisonKind.GreaterThanOrEqual : ComparisonKind.GreaterThan, HandBuiltModel.Literal(shapes.Integer, 0L)),
        };
        holder.PropertiesList.Add(Property("Count", refined));

        var text = Emit(Constructor(holder), holder);

        Assert.Equal(admitsZero, text.Contains("this.Count = default(int);", StringComparison.Ordinal));
    }

    [Fact]
    public void StorageBehindACustomSetter_IsDefaultedByNameWhileTheBodyAssignsTheProperty()
    {
        var holder = shapes.Type("Holder");
        var property = CustomSetter(holder, "Count", shapes.Integer);
        var constructor = Declare(holder, body: HandBuiltModel.Block(new ExpressionStatement(
            new AssignmentExpression(HandBuiltModel.Node(), new PropertyReferenceExpression(HandBuiltModel.Node(), property),
                HandBuiltModel.Literal(shapes.Integer, 9L), shapes.Integer))));

        var text = Emit(constructor, holder);

        Assert.Contains("this.__Count_field = default(int);", text, StringComparison.Ordinal);
        Assert.Contains("Count = 9;", text, StringComparison.Ordinal);
        Assert.DoesNotContain("__Count_field = 9;", text, StringComparison.Ordinal);
        Assert.True(text.IndexOf("this.__Count_field =", StringComparison.Ordinal) < text.IndexOf("Count = 9;", StringComparison.Ordinal));
    }

    [Fact]
    public void DeferredInitializerBehindACustomSetter_IsAssignedByName()
    {
        var holder = shapes.Type("Holder");
        var parameter = Parameter(holder, defaultConstrained: true);
        var property = CustomSetter(holder, "Item", parameter);
        property.Initializer = new DefaultExpression(HandBuiltModel.Node(), parameter);

        Assert.Contains("this.__Item_field = __default_T();", Emit(Constructor(holder), holder), StringComparison.Ordinal);
    }

    [Fact]
    public void ThisChain_DoesNotRepeatStorageBehindACustomSetter()
    {
        var holder = shapes.Type("Holder");
        _ = CustomSetter(holder, "Count", shapes.Integer);
        var root = Constructor(holder, shapes.Integer);
        var delegating = Declare(holder, chain: ConstructorChainKind.This);
        delegating.ChainedConstructor = root;
        delegating.ChainArgumentsList.Add(HandBuiltModel.Literal(shapes.Integer, 1L));

        Assert.DoesNotContain("__Count_field", Emit(delegating, holder), StringComparison.Ordinal);
        Assert.Contains("this.__Count_field = default(int);", Emit(root, holder), StringComparison.Ordinal);
    }

    private static Property CustomSetter(ConcreteType owner, string name, IMistType type)
    {
        var property = new Property(name, type, Public)
        {
            Getter = new PropertyAccessor { IsAuto = true },
            Setter = new PropertyAccessor(),
            DeclaringType = owner,
        };
        var node = HandBuiltModel.Node();
        property.Setter!.ExpressionBody = new AssignmentExpression(node, new BackingFieldReferenceExpression(node, property), HandBuiltModel.Literal(type), type);
        property.UsesBackingField = true;
        owner.PropertiesList.Add(property);
        return property;
    }

    private EnumType Mode()
    {
        var mode = HandBuiltModel.Enum("Mode", shapes.Integer);
        mode.MembersList.Add(new EnumMember("Ready", true, HandBuiltModel.Literal(shapes.Integer, 7L)));
        return mode;
    }

    private static Property Property(string name, IMistType type, bool getOnly = false, bool isStatic = false)
    {
        return new Property(name, type, Public with { IsStatic = isStatic })
        {
            Getter = new PropertyAccessor { IsAuto = true },
            Setter = getOnly ? null : new PropertyAccessor { IsAuto = true },
        };
    }

    private static Constructor Declare(ConcreteType owner, ConstructorChainKind chain = ConstructorChainKind.None,
        List<Parameter>? parameters = null, Block? body = null)
    {
        var constructor = new Constructor(owner.Name, owner, parameters ?? [], Public)
        {
            DeclaringType = owner,
            ChainKind = chain,
            Body = body,
        };
        owner.ConstructorsList.Add(constructor);
        return constructor;
    }

    private static string Emit(Constructor constructor, ConcreteType owner, IReadOnlyDictionary<StructType, string>? names = null)
    {
        var writer = new CSharpWriter();
        var bag = new DiagnosticBag();
        ConstructorEmitter.Emit(constructor, owner, writer, bag, names);
        Assert.Empty(bag.ToImmutableArray());
        return writer.ToString();
    }
}