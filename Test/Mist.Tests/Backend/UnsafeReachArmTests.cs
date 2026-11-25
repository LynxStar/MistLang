using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class UnsafeReachArmTests
{
    private readonly HandBuiltModel model = new();
    private readonly ClassType strip = HandBuiltModel.Class("Strip");
    private readonly ClassType copy = HandBuiltModel.Class("Copy");
    private readonly DiagnosticBag diagnostics = new();

    private IMistType Integer { get; } = new StructType("Int32", "Standard", new Scope(null), null, null);

    private IMistType Text => model.Type("String");

    [Fact]
    public void AConditionalReadOfAPrivateComputedProperty_BindsTheReceiverOnceAndReadsThroughItsGetter()
    {
        var note = Computed("Note", Accessibility.Private);
        var read = new MemberAccessExpression(HandBuiltModel.Node(7), Local("maybe", Optional(strip)), "Note", Optional(Integer),
            isConditional: true, member: new PropertyResult(note, strip, []));

        var (emitted, declared) = Emit(read);

        Assert.Equal("(maybe is { } __reached7 ? (int?)__Out_Probe_Strip_get_Note(__reached7) : null)", emitted);
        Assert.Contains("UnsafeAccessorKind.Method, Name = \"get_Note\")]", declared);
    }

    [Fact]
    public void AConditionalReadOfAPrivateAutoProperty_ReadsItsCompilerBackingField()
    {
        var count = Auto("Count", Accessibility.Private);
        var read = new MemberAccessExpression(HandBuiltModel.Node(3), Local("maybe", Optional(strip)), "Count", Optional(Integer),
            isConditional: true, member: new PropertyResult(count, strip, []));

        var (emitted, declared) = Emit(read);

        Assert.Equal("(maybe is { } __reached3 ? (int?)__Out_Probe_Strip_Count(__reached3) : null)", emitted);
        Assert.Contains("UnsafeAccessorKind.Field, Name = \"<Count>k__BackingField\")]", declared);
    }

    [Fact]
    public void AReadOfAPrivatePropertyThroughAnExplicitReceiver_PassesThatReceiverToTheGetter()
    {
        var note = Computed("Note", Accessibility.Private);
        var read = new MemberAccessExpression(HandBuiltModel.Node(), Local("other", strip), "Note", Integer, member: new PropertyResult(note, strip, []));

        Assert.Equal("__Out_Probe_Strip_get_Note(other)", Emit(read).Emitted);
    }

    [Fact]
    public void AConditionalCallOfAPrivateMethodWithAResult_BindsTheReceiverOnceAndLiftsTheResult()
    {
        var three = new Method("Three", Integer, [new Parameter("step", Integer)], new MemberModifiers { Accessibility = Accessibility.Private })
        {
            DeclaringType = strip,
        };
        var callee = new MemberAccessExpression(HandBuiltModel.Node(), Local("maybe", Optional(strip)), "Three", Optional(Integer), isConditional: true);
        var call = new InvocationExpression(HandBuiltModel.Node(5), callee, [HandBuiltModel.Literal(Integer, 1L)], Optional(Integer), three, null);

        var (emitted, declared) = Emit(call);

        Assert.Equal("(maybe is { } __reached5 ? (int?)__Out_Probe_Strip_Three(__reached5, 1) : null)", emitted);
        Assert.DoesNotContain("When", emitted, StringComparison.Ordinal);
        Assert.Contains("private static extern int __Out_Probe_Strip_Three(Strip __target, int step);", declared);
    }

    [Fact]
    public void ACallOfAPrivateMethodThroughAnExplicitReceiver_PassesThatReceiverFirst()
    {
        var doubled = new Method("Doubled", Integer, [new Parameter("by", Integer)], new MemberModifiers { Accessibility = Accessibility.Private })
        {
            DeclaringType = strip,
        };
        var callee = new MemberAccessExpression(HandBuiltModel.Node(), Local("other", strip), "Doubled", Integer);
        var call = new InvocationExpression(HandBuiltModel.Node(), callee, [HandBuiltModel.Literal(Integer, 2L)], Integer, doubled, null);

        Assert.Equal("__Out_Probe_Strip_Doubled(other, 2)", Emit(call).Emitted);
    }

    [Fact]
    public void ACallOfAPrivateStaticMethodThroughItsType_PassesANullTargetInsteadOfTheType()
    {
        var make = new Method("Make", Integer, [], new MemberModifiers { Accessibility = Accessibility.Private, IsStatic = true })
        {
            DeclaringType = strip,
        };
        var callee = new MemberAccessExpression(HandBuiltModel.Node(), new TypeReferenceExpression(HandBuiltModel.Node(), strip), "Make", Integer);
        var call = new InvocationExpression(HandBuiltModel.Node(), callee, [], Integer, make, null);

        var (emitted, declared) = Emit(call);

        Assert.Equal("__Out_Probe_Strip_Make(null)", emitted);
        Assert.Contains("UnsafeAccessorKind.StaticMethod, Name = \"Make\")]", declared);
    }

    [Fact]
    public void AnInitializerSettingOnlyReachableMembers_IsWrittenAsAnOrdinaryInitializer()
    {
        var name = new Field("Name", Text, OwnershipKind.Unrestricted, new MemberModifiers()) { DeclaringType = strip };
        var title = Auto("Title", Accessibility.Public);
        var creation = New(
            Entry("Name", HandBuiltModel.Literal(Text, "n"), new FieldResult(name, strip, [])),
            Entry("Title", HandBuiltModel.Literal(Integer, 1L), new PropertyResult(title, strip, [])));

        var (emitted, declared) = Emit(creation);

        Assert.DoesNotContain("__Out_", emitted, StringComparison.Ordinal);
        Assert.Contains("Name = \"n\"", emitted, StringComparison.Ordinal);
        Assert.Contains("Title = 1", emitted, StringComparison.Ordinal);
        Assert.Equal("", declared);
    }

    [Fact]
    public void AnInitializerSettingAnUnreachableMember_SetsEachEntryThroughItsOwnRoute()
    {
        var stripes = new Field("stripes", Integer, OwnershipKind.Unrestricted, new MemberModifiers { Accessibility = Accessibility.Private })
        {
            DeclaringType = strip,
        };
        var name = new Field("Name", Text, OwnershipKind.Unrestricted, new MemberModifiers()) { DeclaringType = strip };
        var note = Computed("Note", Accessibility.Private);
        var count = Auto("Count", Accessibility.Private);
        var label = Computed("Label", Accessibility.Public, setter: Accessibility.Private);
        var creation = New(
            Entry("stripes", HandBuiltModel.Literal(Integer, 1L), new FieldResult(stripes, strip, [])),
            Entry("Name", HandBuiltModel.Literal(Text, "n"), new FieldResult(name, strip, [])),
            Entry("Note", HandBuiltModel.Literal(Integer, 2L), new PropertyResult(note, strip, [])),
            Entry("Count", HandBuiltModel.Literal(Integer, 3L), new PropertyResult(count, strip, [])),
            Entry("Label", HandBuiltModel.Literal(Integer, 4L), new PropertyResult(label, strip, [])));

        var (emitted, declared) = Emit(creation);

        Assert.Equal(
            "__Out_Initialized(new Strip(), __initialized0 => { "
            + "__Out_Probe_Strip_stripes(__initialized0) = 1; "
            + "__initialized0.Name = \"n\"; "
            + "__Out_Probe_Strip_set_Note(__initialized0, 2); "
            + "__Out_Probe_Strip_Count(__initialized0) = 3; "
            + "__Out_Probe_Strip_set_Label(__initialized0, 4); })",
            emitted);
        Assert.Contains("UnsafeAccessorKind.Field, Name = \"<Count>k__BackingField\")]", declared);
        Assert.Contains("UnsafeAccessorKind.Method, Name = \"set_Label\")]", declared);
    }

    [Fact]
    public void EachInitializerInOneConversion_NamesItsOwnTarget()
    {
        var stripes = new Field("stripes", Integer, OwnershipKind.Unrestricted, new MemberModifiers { Accessibility = Accessibility.Private })
        {
            DeclaringType = strip,
        };
        var first = New(Entry("stripes", HandBuiltModel.Literal(Integer, 1L), new FieldResult(stripes, strip, [])));
        var second = New(Entry("stripes", HandBuiltModel.Literal(Integer, 2L), new FieldResult(stripes, strip, [])));

        using var promotion = AsyncPromotion.Enter(model.Model);
        using var reach = Enter();
        var emitted = $"{BodyEmitter.Render(first, diagnostics)} {BodyEmitter.Render(second, diagnostics)}";

        Assert.Contains("__initialized0 => { __Out_Probe_Strip_stripes(__initialized0) = 1; }", emitted, StringComparison.Ordinal);
        Assert.Contains("__initialized1 => { __Out_Probe_Strip_stripes(__initialized1) = 2; }", emitted, StringComparison.Ordinal);
        _ = Assert.Single(reach.Declarations, lines => lines.Any(line => line.Contains("__T __Out_Initialized<__T>", StringComparison.Ordinal)));
        Assert.Empty(diagnostics.ToImmutableArray());
    }

    public enum Relation
    {
        Unrelated,
        HomeNestedInDeclaring,
        HomeDerivesFromDeclaring,
        HomeAndReceiverDeriveFromDeclaring,
        OtherAssembly,
    }

    [Theory]
    [InlineData(Accessibility.Public, Relation.Unrelated, false, false)]
    [InlineData(Accessibility.Internal, Relation.Unrelated, false, false)]
    [InlineData(Accessibility.Internal, Relation.OtherAssembly, false, true)]
    [InlineData(Accessibility.Private, Relation.Unrelated, false, true)]
    [InlineData(Accessibility.Private, Relation.HomeNestedInDeclaring, false, false)]
    [InlineData(Accessibility.Protected, Relation.Unrelated, false, true)]
    [InlineData(Accessibility.Protected, Relation.Unrelated, true, true)]
    [InlineData(Accessibility.Protected, Relation.HomeDerivesFromDeclaring, false, true)]
    [InlineData(Accessibility.Protected, Relation.HomeDerivesFromDeclaring, true, false)]
    [InlineData(Accessibility.Protected, Relation.HomeAndReceiverDeriveFromDeclaring, false, false)]
    public void AFieldIsReadThroughAnAccessorExactlyWhereCSharpRefusesItAtTheConversion(
        Accessibility accessibility, Relation relation, bool isStatic, bool refused)
    {
        var declaring = HandBuiltModel.Class("Base");
        var home = relation switch
        {
            Relation.HomeNestedInDeclaring => new ClassType("Copy", "Probe", new Scope(null), declaring, null),
            Relation.HomeDerivesFromDeclaring or Relation.HomeAndReceiverDeriveFromDeclaring => HandBuiltModel.Class("Copy", declaring),
            _ => HandBuiltModel.Class("Copy"),
        };
        var receiverType = relation == Relation.HomeAndReceiverDeriveFromDeclaring ? HandBuiltModel.Class("Derived", home) : declaring;
        if(relation == Relation.OtherAssembly)
        {
            declaring.Assembly = new MistAssembly("Other", MistAssemblyKind.Reference);
        }

        var field = new Field("x", Integer, OwnershipKind.Unrestricted, new MemberModifiers { Accessibility = accessibility, IsStatic = isStatic })
        {
            DeclaringType = declaring,
        };
        Expression receiver = isStatic ? new TypeReferenceExpression(HandBuiltModel.Node(), declaring) : Local("other", receiverType);
        var read = new MemberAccessExpression(HandBuiltModel.Node(), receiver, "x", Integer, member: new FieldResult(field, declaring, []));

        using var reach = UnsafeReach.Enter(home, HandBuiltModel.Conversion(home, receiverType, home), "Out", diagnostics);
        var reached = UnsafeReach.Access(read, () => isStatic ? "Probe.Base" : "other");

        Assert.Equal(refused ? $"__Out_Probe_Base_x({(isStatic ? "null" : "other")})" : null, reached);
        Assert.Empty(diagnostics.ToImmutableArray());
    }

    [Fact]
    public void ACoalescingWriteThroughAPrivateIndexer_ReadsAndWritesThroughItsItemAccessors()
    {
        var indexer = new Indexer(Optional(Text), [new Parameter("index", Integer)], new MemberModifiers { Accessibility = Accessibility.Private })
        {
            DeclaringType = strip,
            Getter = new PropertyAccessor { Body = HandBuiltModel.Block() },
            Setter = new PropertyAccessor { Body = HandBuiltModel.Block() },
        };
        var element = new IndexExpression(HandBuiltModel.Node(), Local("other", strip), HandBuiltModel.Literal(Integer, 1L), Optional(Text)) { Indexer = indexer };
        var write = new AssignmentExpression(HandBuiltModel.Node(), element, HandBuiltModel.Literal(Text, "v"), Text, isCoalescing: true);

        var (emitted, declared) = Emit(write);

        Assert.Equal(
            "__Out_Coalesced(other, 1, (__t, __k) => __Out_Probe_Strip_get_Item(__t, __k), (__t, __k, __v) => __Out_Probe_Strip_set_Item(__t, __k, __v), () => \"v\")",
            emitted);
        Assert.Contains("UnsafeAccessorKind.Method, Name = \"get_Item\")]", declared);
        Assert.Contains("UnsafeAccessorKind.Method, Name = \"set_Item\")]", declared);
    }

    [Fact]
    public void ACoalescingWriteToAPrivateStaticComputedProperty_PassesATypedNullTarget()
    {
        var cache = Computed("Cache", Accessibility.Private, isStatic: true, type: Optional(Text));
        var write = new AssignmentExpression(
            HandBuiltModel.Node(), new PropertyReferenceExpression(HandBuiltModel.Node(), cache), HandBuiltModel.Literal(Text, "c"), Text, isCoalescing: true);

        var (emitted, declared) = Emit(write);

        Assert.Equal(
            "__Out_Coalesced((Strip?)null, (object?)null, (__t, __k) => __Out_Probe_Strip_get_Cache(__t), (__t, __k, __v) => __Out_Probe_Strip_set_Cache(__t, __v), () => \"c\")",
            emitted);
        Assert.Contains("UnsafeAccessorKind.StaticMethod, Name = \"set_Cache\")]", declared);
    }

    [Fact]
    public void ACompoundWriteToAPrivateComputedPropertyThroughAnExplicitReceiver_UpdatesThroughItsAccessors()
    {
        var note = Computed("Note", Accessibility.Private);
        var target = new MemberAccessExpression(HandBuiltModel.Node(), Local("other", strip), "Note", Integer, member: new PropertyResult(note, strip, []));

        var emitted = Emit(Compound(target)).Emitted;

        Assert.Equal(
            "__Out_Updated(other, (object?)null, (__t, __k) => __Out_Probe_Strip_get_Note(__t), (__t, __k, __v) => __Out_Probe_Strip_set_Note(__t, __v), __o => __o + (2), false)",
            emitted);
    }

    [Fact]
    public void ACompoundWriteToAPrivateStaticComputedProperty_PassesATypedNullTarget()
    {
        var total = Computed("Total", Accessibility.Private, isStatic: true);

        var emitted = Emit(Compound(new PropertyReferenceExpression(HandBuiltModel.Node(), total))).Emitted;

        Assert.StartsWith("__Out_Updated((Strip?)null, (object?)null, ", emitted, StringComparison.Ordinal);
    }

    [Fact]
    public void ACompoundWriteToAPrivateAutoProperty_UpdatesItsBackingFieldInPlace()
    {
        var count = Auto("Count", Accessibility.Private);
        var target = new MemberAccessExpression(HandBuiltModel.Node(), Local("other", strip), "Count", Integer, member: new PropertyResult(count, strip, []));

        var (emitted, declared) = Emit(Compound(target));

        Assert.Equal("__Out_Probe_Strip_Count(other) += 2", emitted);
        Assert.Contains("private static extern ref int __Out_Probe_Strip_Count(Strip __target);", declared);
        Assert.DoesNotContain("Updated", declared, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Accessibility.Private, Accessibility.Public, "Count")]
    [InlineData(Accessibility.Public, Accessibility.Private, "this[]")]
    [InlineData(Accessibility.Public, Accessibility.Public, null)]
    public void ASliceNamesTheFirstMemberItNeedsThatTheConversionCannotReach(Accessibility countAccess, Accessibility indexerAccess, string? unreached)
    {
        var count = Auto("Count", countAccess);
        var indexer = new Indexer(Integer, [new Parameter("index", Integer)], new MemberModifiers { Accessibility = indexerAccess })
        {
            DeclaringType = strip,
            Getter = new PropertyAccessor { IsAuto = true },
        };

        using var reach = Enter();

        Assert.Equal(unreached, UnsafeReach.Unreached(new CountedSlice(count, indexer, null), strip));
    }

    private UnsafeReach Enter()
    {
        return UnsafeReach.Enter(copy, HandBuiltModel.Conversion(copy, strip, copy), "Out", diagnostics);
    }

    private (string Emitted, string Declared) Emit(Expression expression)
    {
        BodyEmitter.ResetTemporaries();
        using var promotion = AsyncPromotion.Enter(model.Model);
        using var reach = Enter();
        var emitted = BodyEmitter.Render(expression, diagnostics);

        Assert.Empty(diagnostics.ToImmutableArray());
        return (emitted, string.Join("\n", reach.Declarations.SelectMany(lines => lines)));
    }

    private Property Computed(string name, Accessibility accessibility, Accessibility? setter = null, bool isStatic = false, IMistType? type = null)
    {
        return new Property(name, type ?? Integer, new MemberModifiers { Accessibility = accessibility, IsStatic = isStatic })
        {
            DeclaringType = strip,
            Getter = new PropertyAccessor { Body = HandBuiltModel.Block() },
            Setter = new PropertyAccessor { Body = HandBuiltModel.Block(), Accessibility = setter ?? Accessibility.Public },
        };
    }

    private Property Auto(string name, Accessibility accessibility)
    {
        return new Property(name, Integer, new MemberModifiers { Accessibility = accessibility })
        {
            DeclaringType = strip,
            Getter = new PropertyAccessor { IsAuto = true },
            Setter = new PropertyAccessor { IsAuto = true },
        };
    }

    private NewExpression New(params ObjectInitializerAssignment[] entries)
    {
        var constructor = new Constructor(".ctor", strip, [], new MemberModifiers()) { DeclaringType = strip };
        return new NewExpression(HandBuiltModel.Node(), strip, [], constructor, entries, []);
    }

    private static ObjectInitializerAssignment Entry(string name, Expression value, LookupResult member)
    {
        return new ObjectInitializerAssignment(HandBuiltModel.Node(), name, value, value.Type, member: member);
    }

    private CompoundAssignmentExpression Compound(Expression target)
    {
        return new CompoundAssignmentExpression(
            HandBuiltModel.Node(), target, BinaryOperatorKind.Add, HandBuiltModel.Literal(Integer, 2L), Integer, null, IncrementForm.None);
    }

    private IMistType Optional(IMistType type)
    {
        return model.Interner.GetOrCreateUnionType([type, model.Type("Void")]);
    }

    private static LocalReferenceExpression Local(string name, IMistType type)
    {
        return new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable(name, type));
    }
}