using System.Text.RegularExpressions;
using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class StructImplicitBaseEmissionTests
{
    private static readonly MemberModifiers Public = DeclarationFragments.Public;
    private readonly HandBuiltModel model = new();

    [Fact]
    public void UnchainedDerivedConstructor_CallsTheBaseInitializerBeforeItsOwnBody()
    {
        var meter = Struct("Meter");
        var reading = Field(meter, "reading");
        _ = Declare(meter, reading, HandBuiltModel.Literal(reading.Type, 7L));
        var dial = Struct("Dial", meter);
        var face = Field(dial, "face");
        var parameter = new Parameter("face", face.Type);
        _ = Declare(dial, face, new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("face", face.Type)), parameters: [parameter]);

        var text = Emit(dial);

        Assert.Contains("__Meter_ctor();\n        this.face = face;", text, StringComparison.Ordinal);
        Assert.Contains("reading = 7;", text, StringComparison.Ordinal);
        Assert.Equal(1, Count(text, "__Meter_ctor();"));
    }

    [Fact]
    public void ThreeLevelImplicitChain_CallsEachAncestorInitializerOnce()
    {
        var root = Struct("Root");
        var rootField = Field(root, "rootValue");
        _ = Declare(root, rootField, HandBuiltModel.Literal(rootField.Type, 7L));
        var middle = Struct("Middle", root);
        var middleField = Field(middle, "middleValue");
        _ = Declare(middle, middleField, HandBuiltModel.Literal(middleField.Type, 5L));
        var leaf = Struct("Leaf", middle);
        var leafField = Field(leaf, "leafValue");
        _ = Declare(leaf, leafField, HandBuiltModel.Literal(leafField.Type, 3L));

        var text = Emit(leaf);

        Assert.Equal(1, Count(text, "__Middle_ctor();"));
        Assert.Equal(1, Count(text, "__Root_ctor();"));
        Assert.Contains("__Middle_ctor();\n        this.leafValue = 3;", text, StringComparison.Ordinal);
        Assert.Contains("__Root_ctor();\n        this.middleValue = 5;", text, StringComparison.Ordinal);
        Assert.Contains("rootValue = 7;", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ExplicitThisAndBaseChains_DelegateWithoutReinitializingTheRoot()
    {
        var root = Struct("Root");
        var rootField = Field(root, "rootValue");
        var rootConstructor = Declare(root, rootField, HandBuiltModel.Literal(rootField.Type, 7L));
        var middle = Struct("Middle", root);
        var middleField = Field(middle, "middleValue");
        var target = Declare(middle, middleField, HandBuiltModel.Literal(middleField.Type, 5L),
            chain: ConstructorChainKind.Base, parameters: [new Parameter("value", middleField.Type)]);
        target.ChainedConstructor = rootConstructor;
        var delegateConstructor = Declare(middle, chain: ConstructorChainKind.This);
        delegateConstructor.ChainedConstructor = target;
        delegateConstructor.ChainArgumentsList.Add(HandBuiltModel.Literal(middleField.Type, 4L));
        var leaf = Struct("Leaf", middle);
        var leafField = Field(leaf, "leafValue");
        var leafConstructor = Declare(leaf, leafField, HandBuiltModel.Literal(leafField.Type, 3L), chain: ConstructorChainKind.Base);
        leafConstructor.ChainedConstructor = delegateConstructor;

        var text = Emit(leaf);

        Assert.Equal(1, Count(text, "__Middle_ctor();"));
        Assert.Equal(1, Count(text, "__Middle_ctor(4);"));
        Assert.Equal(1, Count(text, "__Root_ctor();"));
        Assert.Contains("__Root_ctor();\n        this.middleValue = 5;", text, StringComparison.Ordinal);
        Assert.Contains("__Middle_ctor();\n        this.leafValue = 3;", text, StringComparison.Ordinal);
    }

    [Fact]
    public void DerivedThisChain_ConstructsItsSiblingBeforeItsOwnBody()
    {
        var meter = Struct("Meter");
        var reading = Field(meter, "reading");
        _ = Declare(meter, reading, HandBuiltModel.Literal(reading.Type, 7L));
        var dial = Struct("Dial", meter);
        var face = Field(dial, "face");
        var sibling = Declare(dial, face, HandBuiltModel.Literal(face.Type, 3L),
            parameters: [new Parameter("value", face.Type)]);
        var delegating = Declare(dial, chain: ConstructorChainKind.This);
        delegating.ChainedConstructor = sibling;
        delegating.ChainArgumentsList.Add(HandBuiltModel.Literal(face.Type, 4L));

        var text = Emit(dial);

        Assert.Contains("public Dial() : this(4)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("this = new Dial", text, StringComparison.Ordinal);
        Assert.Equal(1, Count(text, "__Meter_ctor();"));
        Assert.Contains("__Meter_ctor();\n        this.face = 3;", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AccessibleParamsConstructor_IsPinnedWhenAHiddenParameterlessOverloadExists()
    {
        var meter = Struct("Meter");
        var reading = Field(meter, "reading");
        _ = Declare(meter, reading, HandBuiltModel.Literal(reading.Type, 99L),
            modifiers: Public with { Accessibility = Accessibility.Private });
        var array = model.Interner.GetOrCreateConstructedType(model.Type("Array<,>"), [reading.Type]);
        _ = Declare(meter, reading, HandBuiltModel.Literal(reading.Type, 7L),
            parameters: [new Parameter("readings", array) { IsFlattened = true }]);
        var dial = Struct("Dial", meter);
        _ = Declare(dial);

        var text = Emit(dial);

        Assert.Equal(1, Count(text, "__Meter_ctor(readings: new int[] { });"));
    }

    [Fact]
    public void ImplicitCallAndInitializerSignature_SubstituteAGenericBaseArgument()
    {
        var meter = new StructType("Meter<>", "Probe", new Scope(null), null, null);
        var typeParameter = new TypeParameter("T", 0, meter.Scope, meter);
        meter.TypeParametersList.Add(typeParameter);
        var array = model.Interner.GetOrCreateConstructedType(model.Type("Array<,>"), [typeParameter]);
        _ = Declare(meter, parameters: [new Parameter("readings", array) { IsFlattened = true }]);
        var dial = Struct("Dial", model.Interner.GetOrCreateConstructedType(meter, [model.Type("Int32")]));
        _ = Declare(dial);

        var text = Emit(dial);

        Assert.Contains("__Meter_ctor(readings: new int[] { });", text, StringComparison.Ordinal);
        Assert.Contains("__Meter_ctor(params int[] readings)", text, StringComparison.Ordinal);
    }

    private static StructType Struct(string name, MistType? baseType = null)
    {
        var type = HandBuiltModel.Struct(name);
        type.BaseType = baseType;
        return type;
    }

    private Field Field(StructType owner, string name)
    {
        var field = new Field(name, model.Type("Int32"), OwnershipKind.Unrestricted, Public) { DeclaringType = owner };
        owner.FieldsList.Add(field);
        return field;
    }

    private static Constructor Declare(
        StructType owner,
        Field? field = null,
        Expression? value = null,
        ConstructorChainKind chain = ConstructorChainKind.None,
        List<Parameter>? parameters = null,
        MemberModifiers? modifiers = null)
    {
        var statements = field is null || value is null
            ? Array.Empty<Statement>()
            : [new ExpressionStatement(new AssignmentExpression(HandBuiltModel.Node(),
                new FieldReferenceExpression(HandBuiltModel.Node(), field), value, field.Type))];
        var constructor = new Constructor(owner.Name, owner, parameters ?? [], modifiers ?? Public)
        {
            DeclaringType = owner,
            ChainKind = chain,
            Body = HandBuiltModel.Block(statements),
        };
        owner.ConstructorsList.Add(constructor);
        return constructor;
    }

    private string Emit(StructType type)
    {
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();
        var anyEmitted = false;
        TypeEmitter.Emit(type, ConstraintClosureMap.Build(model.Model, diagnostics), model.Model.VariadicUses,
            writer, diagnostics, ref anyEmitted);
        Assert.Empty(diagnostics.ToImmutableArray());
        return writer.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static int Count(string text, string call)
    {
        return Regex.Matches(text, Regex.Escape(call)).Count;
    }
}