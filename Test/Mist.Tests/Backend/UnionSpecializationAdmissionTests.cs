using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

// Tier 2: UnionSpecialization's discovery chain over a hand-built model. It covers which instances a
// generic method declared in a specialized type closes, which construction a call through a closed
// derived receiver is named at, which overload keeps its name because it answers a slot inherited
// through another abstract or the first of two constructions of one abstract, and where a union over
// an extension method's pack is refused.
[Trait("Tier", "2")]
public sealed class UnionSpecializationAdmissionTests
{
    private const int BlockOffset = 4;

    private const int MethodOffset = 20;

    private static readonly MemberModifiers Public = new() { Accessibility = Accessibility.Public };

    private readonly HandBuiltModel built = new();

    [Fact]
    public void AMethodInstanceInASpecializedType_ClosesItsTypesAtEveryInstanceOfThatTypeAlreadyKnown()
    {
        var (pair, pick) = MethodInGenericType();

        using(UnionSpecialization.Enter(built.Model, new DiagnosticBag()))
        {
            var pairs = InstanceNames(pair);

            Assert.Contains("Pair__Standard_Boolean__Standard_Int32", pairs);
            Assert.Contains("Pair__Standard_Boolean__Standard_String", pairs);
            Assert.Equal(
                ["Pick__Standard_Boolean"],
                UnionSpecialization.InstancesOf(pick).Select(arguments => UnionSpecialization.Name("Pick", arguments)));
        }
    }

    [Fact]
    public void ATypeInstanceReachedAfterAMethodInstance_StillClosesThatMethodsTypesAtIt()
    {
        var (pair, _) = MethodInGenericType();

        using(UnionSpecialization.Enter(built.Model, new DiagnosticBag()))
        {
            Assert.Contains("Pair__Standard_Boolean__Standard_Int64", InstanceNames(pair));
        }
    }

    [Fact]
    public void ACallThroughAClosedDerivedReceiver_IsNamedAtTheConstructionItsBaseChainReaches()
    {
        var integer = built.Type("Int32");
        var lifted = Generic("Lifted<>", "K");
        var key = lifted.TypeParametersList[0];
        Writes(key);
        _ = Take(lifted, "key", key);
        var byNumber = Take(lifted, "number", integer);
        var derived = Generic("Derived<>", "D");
        derived.BaseType = built.Interner.GetOrCreateConstructedType(lifted, [derived.TypeParametersList[0]]);
        var closed = HandBuiltModel.Class("IntDerived", built.Interner.GetOrCreateConstructedType(derived, [integer]));
        Register(lifted, derived, closed);

        using(UnionSpecialization.Enter(built.Model, new DiagnosticBag()))
        {
            Assert.Equal("Take__o1", UnionSpecialization.CollidingName(byNumber, closed));
        }
    }

    [Fact]
    public void TheOverloadAnsweringASlotInheritedThroughAnotherAbstract_KeepsItsName()
    {
        var integer = built.Type("Int32");
        var taker = new AbstractType("ITaker<>", "Probe", new Scope(null), null, null);
        var slotKey = new TypeParameter("S", 0, taker.Scope, taker);
        taker.TypeParametersList.Add(slotKey);
        taker.MethodsList.Add(new Method("Take", integer, [new Parameter("key", slotKey)], Public) { DeclaringType = taker });
        var more = new AbstractType("IMore<>", "Probe", new Scope(null), null, null);
        var moreKey = new TypeParameter("M", 0, more.Scope, more);
        more.TypeParametersList.Add(moreKey);
        more.BaseAbstractTypes.Add(built.Interner.GetOrCreateConstructedType(taker, [moreKey]));
        var filler = Generic("Filler<>", "K");
        var key = filler.TypeParametersList[0];
        Writes(key);
        filler.BaseAbstractTypes.Add(built.Interner.GetOrCreateConstructedType(more, [key]));
        var byKey = Take(filler, "key", key);
        var byNumber = Take(filler, "number", integer);
        Register(taker, more, filler);
        var instance = built.Interner.GetOrCreateConstructedType(filler, [integer]);

        using(UnionSpecialization.Enter(built.Model, new DiagnosticBag()))
        {
            Assert.Null(UnionSpecialization.CollidingName(byKey, instance));
            Assert.Equal("Take__o1", UnionSpecialization.CollidingName(byNumber, instance));
        }
    }

    /// <summary>
    /// <c>Filler&lt;K&gt;</c> conforms to <c>IPair&lt;K, String&gt;</c> and then <c>IPair&lt;Int32, K&gt;</c>,
    /// whose slot is <c>Take(A)</c>, and declares <c>Take(K)</c> and <c>Take(Int32)</c>. At
    /// <c>Filler&lt;Int32&gt;</c> both read <c>Take(Int32)</c>; only the first construction of
    /// <c>IPair</c> is consulted, so <c>Take(K)</c> keeps its name and <c>Take(Int32)</c> is renamed,
    /// which keeps the two C# declarations distinct.
    /// </summary>
    [Fact]
    public void OfTwoConstructionsOfOneAbstract_OnlyTheFirstDecidesWhichOverloadKeepsItsName()
    {
        var integer = built.Type("Int32");
        var abstractPair = new AbstractType("IPair<,>", "Probe", new Scope(null), null, null);
        var first = new TypeParameter("A", 0, abstractPair.Scope, abstractPair);
        abstractPair.TypeParametersList.Add(first);
        abstractPair.TypeParametersList.Add(new TypeParameter("B", 1, abstractPair.Scope, abstractPair));
        abstractPair.MethodsList.Add(new Method("Take", integer, [new Parameter("key", first)], Public) { DeclaringType = abstractPair });
        var filler = Generic("Filler<>", "K");
        var key = filler.TypeParametersList[0];
        Writes(key);
        filler.BaseAbstractTypes.Add(built.Interner.GetOrCreateConstructedType(abstractPair, [key, built.Type("String")]));
        filler.BaseAbstractTypes.Add(built.Interner.GetOrCreateConstructedType(abstractPair, [integer, key]));
        var byKey = Take(filler, "key", key);
        var byNumber = Take(filler, "number", integer);
        Register(abstractPair, filler);
        var instance = built.Interner.GetOrCreateConstructedType(filler, [integer]);

        using(UnionSpecialization.Enter(built.Model, new DiagnosticBag()))
        {
            Assert.Null(UnionSpecialization.CollidingName(byKey, instance));
            Assert.Equal("Take__o1", UnionSpecialization.CollidingName(byNumber, instance));
        }
    }

    [Theory]
    [InlineData(true, MethodOffset)]
    [InlineData(false, BlockOffset)]
    public void AUnionOverAnExtensionMethodsPackElement_IsRefusedAtTheMethod_OrItsBlockWhenTheMethodHasNoNode(
        bool methodHasNode, int expectedOffset)
    {
        var text = new SourceText("hand-built.mist", new string(' ', 40));
        var unit = new CompilationUnit { SourcePath = text.Path };
        built.Model.AddSelfAssembly("Probe").CompilationUnits.Add(unit);
        var block = new ExtensionBlock(
            "self", "Probe", new Scope(null),
            new ExtensionBlockDeclarationNode { Span = new Storm.Runtime.SourceSpan(BlockOffset, 3), Origin = new ParsedOrigin(text) })
        {
            ResolvedReceiverType = HandBuiltModel.Class("Holder"),
            CarrierName = "HolderExtensions",
        };
        var pack = new TypeParameter("T", 0, block.Scope, null) { PackKind = TypePackKind.Unbracketed };
        var maybe = built.Interner.GetOrCreateUnionType([pack.ElementAt(0), built.Type("String")]);
        var method = new Method("Use", built.Type("Int32"), [new Parameter("maybe", maybe)], Public)
        {
            DeclaringBlock = block,
            DeclarationNode = methodHasNode
                ? new TrueLiteralExpressionPrimaryNode { Span = new Storm.Runtime.SourceSpan(MethodOffset, 5), Origin = new ParsedOrigin(text) }
                : null,
        };
        method.TypeParametersList.Add(pack);
        block.MethodsList.Add(method);
        unit.Extensions.Add(block);
        var diagnostics = new DiagnosticBag();

        using(UnionSpecialization.Enter(built.Model, diagnostics))
        {
            var refusal = Assert.Single(diagnostics.ToImmutableArray(), d => d.Number.Value == "MIST0026");
            var location = Assert.IsType<SourceRange>(refusal.Location);

            Assert.Equal(text.Path, location.Path);
            Assert.Equal(expectedOffset, location.Offset);
        }
    }

    /// <summary>
    /// <c>Pair&lt;A, B&gt;</c> writes <c>A | B</c>, <c>Holder&lt;K&gt;</c> writes <c>K | string</c> and declares
    /// <c>Pick&lt;T&gt;(Pair&lt;T, K&gt;)</c>, and <c>Maker&lt;M&gt;</c> writes <c>M | string</c> and names
    /// <c>Holder&lt;M&gt;</c>. The program interns <c>Holder&lt;int&gt;</c>, <c>Holder&lt;string&gt;</c> and
    /// <c>Maker&lt;long&gt;</c> and calls <c>Pick&lt;bool&gt;</c>, so <c>Holder&lt;long&gt;</c> is first reached
    /// while substituting <c>Maker&lt;long&gt;</c>, after <c>Pick&lt;bool&gt;</c> was admitted.
    /// </summary>
    private (ClassType Pair, Method Pick) MethodInGenericType()
    {
        var pair = Generic("Pair<,>", "A", "B");
        _ = built.Interner.GetOrCreateUnionType([pair.TypeParametersList[0], pair.TypeParametersList[1]]);
        var holder = Generic("Holder<>", "K");
        var key = holder.TypeParametersList[0];
        Writes(key);
        var own = new TypeParameter("T", 0, new Scope(holder.Scope), declaringType: null);
        var pick = new Method(
            "Pick", built.Type("Int32"),
            [new Parameter("pair", built.Interner.GetOrCreateConstructedType(pair, [own, key]))], Public)
        { DeclaringType = holder };
        pick.TypeParametersList.Add(own);
        holder.MethodsList.Add(pick);
        var maker = Generic("Maker<>", "M");
        Writes(maker.TypeParametersList[0]);
        _ = built.Interner.GetOrCreateConstructedType(holder, [maker.TypeParametersList[0]]);
        Register(pair, holder, maker);
        _ = built.Interner.GetOrCreateConstructedType(holder, [built.Type("Int32")]);
        _ = built.Interner.GetOrCreateConstructedType(holder, [built.Type("String")]);
        _ = built.Interner.GetOrCreateConstructedType(maker, [built.Type("Int64")]);
        built.Model.GenericCalls.Record(pick, [built.Type("Boolean")]);
        return (pair, pick);
    }

    private static ClassType Generic(string name, params string[] parameters)
    {
        var type = HandBuiltModel.Class(name);

        foreach(var parameter in parameters)
        {
            type.TypeParametersList.Add(new TypeParameter(parameter, type.TypeParametersList.Count, type.Scope, type));
        }

        return type;
    }

    /// <summary>
    /// The union a declaration writes over its own parameter, which no C# generic can spell and so
    /// makes the declaration specialized.
    /// </summary>
    private void Writes(TypeParameter parameter)
    {
        _ = built.Interner.GetOrCreateUnionType([parameter, built.Type("String")]);
    }

    private Method Take(ClassType owner, string parameter, IMistType type)
    {
        var take = new Method("Take", built.Type("Int32"), [new Parameter(parameter, type)], Public) { DeclaringType = owner };
        owner.MethodsList.Add(take);
        return take;
    }

    private void Register(params MistType[] types)
    {
        foreach(var type in types)
        {
            _ = built.Model.RegisterType(type);
        }
    }

    private static List<string> InstanceNames(MemberType definition)
    {
        return [.. UnionSpecialization.InstancesOf(definition).Select(UnionSpecialization.Name)];
    }
}