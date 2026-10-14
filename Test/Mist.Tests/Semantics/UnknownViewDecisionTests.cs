// Tier 1 (Unit): the decisions behind member filtering on a receiver closed with `unknown`
// (unknown-type Requirements 9 and 10), each called on types and members built directly: which type
// parameters a type mentions and whether it bears `unknown`, which written parameter a receiver's
// substitution binds to `unknown`, what a bodiless member may write by its signature, how a receiver's
// construction reaches a declaring type up the base chain, and which access an assignment writes through.

using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Construction.Binding.StorageWrites;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class UnknownViewDecisionTests
{
    private readonly HandBuiltModel built = new();

    private ClassType Int32 => built.Type("Int32");

    private ClassType String => built.Type("String");

    internal static ClassType Generic(string name, MistType? declaring, params string[] parameterNames)
    {
        var type = new ClassType(name, "Probe", declaring?.Scope ?? new Scope(null), declaring, null);

        for(var i = 0; i < parameterNames.Length; i++)
        {
            type.TypeParametersList.Add(new TypeParameter(parameterNames[i], i, type.Scope, type));
        }

        return type;
    }

    private static ClassType Array(out TypeParameter element)
    {
        var array = Generic("Array<,>", null, "TValue", "Length");
        element = array.TypeParametersList[0];
        array.TypeParametersList[1].IsDependent = true;
        return array;
    }

    private static HashSet<TypeParameter> Set(params TypeParameter[] parameters)
    {
        return new HashSet<TypeParameter>(parameters, ReferenceEqualityComparer.Instance);
    }

    [Fact]
    public void ATypeMentionsTheParametersItIsBuiltFrom()
    {
        var box = Generic("Box<>", null, "T");
        var pair = Generic("Pair<,>", null, "A", "B");
        var t = box.TypeParametersList[0];
        var a = pair.TypeParametersList[0];
        var b = pair.TypeParametersList[1];

        Assert.Equal(Set(t), TypeParameterOccurrences.In(new ConstructedType(pair, [new ConstructedType(box, [t]), Int32])));
        Assert.Equal(Set(a, b), TypeParameterOccurrences.In(built.Interner.GetOrCreateFunctionType([a], b)));
        Assert.Equal(Set(b), TypeParameterOccurrences.In(built.Interner.GetOrCreateUnionType([b, Int32])));
        Assert.Equal(Set(a), TypeParameterOccurrences.In(a));
        Assert.Empty(TypeParameterOccurrences.In(Int32));
    }

    /// <summary>
    /// A bare member type is its own instantiation -- what <c>this</c> is typed as inside its body -- so it
    /// mentions its own parameters and those of every type enclosing it.
    /// </summary>
    [Fact]
    public void ABareMemberTypeMentionsItsOwnAndEnclosingParameters()
    {
        var outer = Generic("Outer<>", null, "T");
        var inner = Generic("Inner<>", outer, "U");

        Assert.Equal(Set(outer.TypeParametersList[0], inner.TypeParametersList[0]), TypeParameterOccurrences.In(inner));
        Assert.Equal(Set(outer.TypeParametersList[0]), TypeParameterOccurrences.In(outer));
    }

    [Fact]
    public void AConstructionBearsUnknownAtAnyNonDependentDepth()
    {
        var box = Generic("Box<>", null, "T");
        var list = Generic("List<>", null, "T");
        var array = Array(out _);
        var length = built.Interner.GetOrCreateUnknownValue(Int32);

        Assert.True(TypeParameterOccurrences.BearsUnknown(UnknownType.Instance));
        Assert.True(TypeParameterOccurrences.BearsUnknown(new ConstructedType(box, [UnknownType.Instance])));
        Assert.True(TypeParameterOccurrences.BearsUnknown(new ConstructedType(box, [new ConstructedType(list, [UnknownType.Instance])])));
        Assert.True(TypeParameterOccurrences.BearsUnknown(new ConstructedType(array, [UnknownType.Instance, length])));
        Assert.True(TypeParameterOccurrences.BearsUnknown(built.Interner.GetOrCreateUnionType([Int32, UnknownType.Instance])));
        Assert.False(TypeParameterOccurrences.BearsUnknown(new ConstructedType(box, [Int32])));
        Assert.False(TypeParameterOccurrences.BearsUnknown(new ConstructedType(box, [box.TypeParametersList[0]])));
        Assert.False(TypeParameterOccurrences.BearsUnknown(Int32));
    }

    /// <summary>
    /// An array of unknown LENGTH is every unsized array in the language, so the top type at a dependent
    /// position must not close the array with <c>unknown</c>, directly or as another type's argument;
    /// the same distinction keeps array fields out of the storage ban.
    /// </summary>
    [Fact]
    public void AnUnknownLengthDoesNotCloseAnArrayWithUnknown()
    {
        var box = Generic("Box<>", null, "T");
        var array = Array(out _);
        var unsized = new ConstructedType(array, [Int32, UnknownType.Instance]);
        var measured = new ConstructedType(array, [Int32, built.Interner.GetOrCreateUnknownValue(Int32)]);

        Assert.False(TypeParameterOccurrences.BearsUnknown(unsized));
        Assert.False(TypeParameterOccurrences.BearsUnknown(measured));
        Assert.False(TypeParameterOccurrences.BearsUnknown(new ConstructedType(box, [unsized])));
        Assert.False(UnknownViews.IsClosedWithUnknown(unsized));
        Assert.True(UnknownViews.IsClosedWithUnknown(new ConstructedType(box, [UnknownType.Instance])));
        Assert.False(UnknownViews.IsClosedWithUnknown(UnknownType.Instance));
        Assert.False(UnknownViews.IsClosedWithUnknown(box));
    }

    [Fact]
    public void AnEnclosingInstantiationClosesANestedTypeWithUnknown()
    {
        var outer = Generic("Outer<>", null, "T");
        var inner = Generic("Inner", outer);

        Assert.True(UnknownViews.IsClosedWithUnknown(new ConstructedType(inner, [], new ConstructedType(outer, [UnknownType.Instance]))));
        Assert.False(UnknownViews.IsClosedWithUnknown(new ConstructedType(inner, [], new ConstructedType(outer, [Int32]))));
    }

    [Fact]
    public void TheWrittenParameterBoundToUnknownHidesTheMember()
    {
        var pair = Generic("Pair<,>", null, "A", "B");
        var a = pair.TypeParametersList[0];
        var b = pair.TypeParametersList[1];
        var substitution = TypeSubstitution.ForReceiver(new ConstructedType(pair, [Int32, UnknownType.Instance]));

        Assert.Same(b, UnknownViews.HiddenBy([a, b], substitution, built.Interner));
        Assert.Null(UnknownViews.HiddenBy([a], substitution, built.Interner));
        Assert.Null(UnknownViews.HiddenBy([], substitution, built.Interner));
        Assert.Null(UnknownViews.HiddenBy([a, b], TypeSubstitution.ForReceiver(new ConstructedType(pair, [Int32, String])), built.Interner));
    }

    [Fact]
    public void ANestedUnknownArgumentHidesTheMemberToo()
    {
        var box = Generic("Box<>", null, "T");
        var list = Generic("List<>", null, "T");
        var t = box.TypeParametersList[0];
        var substitution = TypeSubstitution.ForReceiver(new ConstructedType(box, [new ConstructedType(list, [UnknownType.Instance])]));

        Assert.Same(t, UnknownViews.HiddenBy([t], substitution, built.Interner));
    }

    [Fact]
    public void ABodilessMemberWritesWhatItsInputsAreTypedBy()
    {
        var list = Generic("List<>", null, "T");
        var t = list.TypeParametersList[0];
        var add = new Method("Add", Int32, [new Parameter("item", t)], DeclarationFragments.Public) { DeclaringType = list };
        var count = new Property("Count", Int32, DeclarationFragments.Public) { DeclaringType = list, Getter = new PropertyAccessor { IsAuto = true } };
        var clear = new Method("Clear", Int32, [], DeclarationFragments.Public) { DeclaringType = list };
        var outward = new ParameterModifier(false, true, OwnershipKind.Unrestricted, ByRefDirection.Out);
        var tryTake = new Method("TryTake", Int32, [new Parameter("found", t, outward)], DeclarationFragments.Public) { DeclaringType = list };
        var indexer = new Indexer(t, [new Parameter("index", Int32)], DeclarationFragments.Public) { DeclaringType = list };
        var keyed = new Indexer(Int32, [new Parameter("key", t)], DeclarationFragments.Public) { DeclaringType = list };
        var slot = new Field("slot", t, OwnershipKind.Unrestricted, DeclarationFragments.Public) { DeclaringType = list };
        var auto = new Property("Item", t, DeclarationFragments.Public) { DeclaringType = list };

        Assert.Equal(Set(t), StorageWriteSummaries.FromSignature(add, StorageAccess.Read));
        Assert.Empty(StorageWriteSummaries.FromSignature(count, StorageAccess.Read));
        Assert.Empty(StorageWriteSummaries.FromSignature(clear, StorageAccess.Read));
        Assert.Empty(StorageWriteSummaries.FromSignature(tryTake, StorageAccess.Read));
        Assert.Empty(StorageWriteSummaries.FromSignature(indexer, StorageAccess.Read));
        Assert.Equal(Set(t), StorageWriteSummaries.FromSignature(indexer, StorageAccess.Write));
        Assert.Equal(Set(t), StorageWriteSummaries.FromSignature(keyed, StorageAccess.Read));
        Assert.Empty(StorageWriteSummaries.FromSignature(slot, StorageAccess.Read));
        Assert.Equal(Set(t), StorageWriteSummaries.FromSignature(slot, StorageAccess.Write));
        Assert.Empty(StorageWriteSummaries.FromSignature(auto, StorageAccess.Read));
        Assert.Equal(Set(t), StorageWriteSummaries.FromSignature(auto, StorageAccess.Write));
    }

    [Fact]
    public void AnIntrinsicElementWriteIsHiddenByTheElementParameter()
    {
        var array = Array(out var element);
        var length = built.Interner.GetOrCreateUnknownValue(Int32);

        var hidden = UnknownViews.HiddenElement(new ConstructedType(array, [UnknownType.Instance, length]));

        Assert.True(hidden.HasValue);
        Assert.Equal("this[]", hidden.Value.Name);
        Assert.Equal("assigned", hidden.Value.Operation);
        Assert.Same(element, hidden.Value.Parameter);
        Assert.Null(UnknownViews.HiddenElement(new ConstructedType(array, [Int32, length])));
        Assert.Null(UnknownViews.HiddenElement(new ConstructedType(array, [Int32, UnknownType.Instance])));
    }

    [Fact]
    public void AReceiverReachesItsOwnDeclaringTypeDirectly()
    {
        var box = Generic("Box<>", null, "T");
        var t = box.TypeParametersList[0];

        var closed = Ancestry.SubstitutionTo(new ConstructedType(box, [Int32]), box, built.Interner);
        var open = Ancestry.SubstitutionTo(box, box, built.Interner);

        Assert.Same(Int32, closed!.Apply(t, built.Interner));
        Assert.Same(t, open!.Apply(t, built.Interner));
    }

    /// <summary>
    /// The base clause renames and rewraps: <c>Derived&lt;Y&gt; : Base&lt;List&lt;Y&gt;&gt;</c> binds Base's X to
    /// List of Derived's Y, and the receiver's argument closes Y, so X reads as a list of the argument.
    /// </summary>
    [Fact]
    public void AReceiverReachesABaseThroughItsConstructedBaseClause()
    {
        var list = Generic("List<>", null, "T");
        var baseType = Generic("Base<>", null, "X");
        var derived = Generic("Derived<>", null, "Y");
        var y = derived.TypeParametersList[0];
        derived.BaseType = new ConstructedType(baseType, [new ConstructedType(list, [y])]);

        var substitution = Ancestry.SubstitutionTo(new ConstructedType(derived, [Int32]), baseType, built.Interner);
        var openSubstitution = Ancestry.SubstitutionTo(derived, baseType, built.Interner);

        Assert.Equal("List<Int32>", substitution!.Apply(baseType.TypeParametersList[0], built.Interner).Name);
        Assert.Equal("List<Y>", openSubstitution!.Apply(baseType.TypeParametersList[0], built.Interner).Name);
    }

    [Fact]
    public void AReceiverReachesAnAbstractBaseAndAConstraint()
    {
        var sink = new AbstractType("ISink<>", "Probe", new Scope(null), null, null);
        var item = new TypeParameter("TItem", 0, sink.Scope, sink);
        sink.TypeParametersList.Add(item);
        var bucket = Generic("Bucket<>", null, "V");
        bucket.BaseAbstractTypes.Add(new ConstructedType(sink, [bucket.TypeParametersList[0]]));
        var constrained = new TypeParameter("U", 0, new MethodScope(new Scope(null)), null);
        constrained.ConstraintsList.Add(new TypeConstraint(new ConstructedType(sink, [String])));

        Assert.Same(String, Ancestry.SubstitutionTo(new ConstructedType(bucket, [String]), sink, built.Interner)!.Apply(item, built.Interner));
        Assert.Same(String, Ancestry.SubstitutionTo(constrained, sink, built.Interner)!.Apply(item, built.Interner));
        Assert.Null(Ancestry.SubstitutionTo(Int32, sink, built.Interner));
    }

    [Fact]
    public void ANestedTypeReachesItsEnclosingDeclarationThroughTheInstantiation()
    {
        var outer = Generic("Outer<>", null, "T");
        var inner = Generic("Inner", outer);
        var t = outer.TypeParametersList[0];

        var instantiated = Ancestry.SubstitutionTo(new ConstructedType(inner, [], new ConstructedType(outer, [Int32])), outer, built.Interner);
        var open = Ancestry.SubstitutionTo(inner, outer, built.Interner);

        Assert.Same(Int32, instantiated!.Apply(t, built.Interner));
        Assert.Same(t, open!.Apply(t, built.Interner));
    }

    [Fact]
    public void AnAssignmentWritesThroughTheLastAccessOfABarePostfixTarget()
    {
        var access = Access("Value");
        var target = BindingFragments.Expression(BindingFragments.Identifier("box"), Access("inner"), access).AsAssignmentOrNull()!.Target;
        var bare = BindingFragments.Identifier("local");
        var bareTarget = BindingFragments.Expression(bare).AsAssignmentOrNull()!.Target;

        Assert.Same(access, AssignmentTargets.AccessNodeOf(target));
        Assert.Same(bare, AssignmentTargets.AccessNodeOf(bareTarget));
    }

    [Fact]
    public void AnOperatorChainIsNotAStorageTarget()
    {
        var sum = BindingFragments.Expression(BindingFragments.Identifier("a"), Access("Value")).AsAssignmentOrNull()!.Target;
        var additive = Additive(sum);
        additive.Group0 = [new AdditiveNodeGroup0 { Operator = BindingFragments.Token("+"), Right = Additive(BindingFragments.Expression(BindingFragments.Identifier("b")).AsAssignmentOrNull()!.Target).Left }];

        Assert.Null(AssignmentTargets.AccessNodeOf(sum));
    }

    [Fact]
    public void AParenthesizedTargetWritesThroughItsInnerAccess()
    {
        var access = Access("Value");
        var parenthesized = BindingFragments.At<ParenthesizedExpressionPrimaryNode>();
        parenthesized.Inner = BindingFragments.Expression(BindingFragments.Identifier("box"), access);
        var target = BindingFragments.Expression(parenthesized).AsAssignmentOrNull()!.Target;

        Assert.Same(access, AssignmentTargets.AccessNodeOf(target));
    }

    private static MemberAccessSuffixPostfixSuffixNode Access(string name)
    {
        var access = BindingFragments.At<MemberAccessSuffixPostfixSuffixNode>();
        access.Name = BindingFragments.Token(name);
        return access;
    }

    private static AdditiveNode Additive(VoidCoalescingNode target)
    {
        return ((RelationalComparisonRelationalNode)target.Left.Left.Left.Left).Left;
    }
}