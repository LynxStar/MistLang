using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class OwnedValueStoreTests
{
    [Theory]
    [InlineData("initializer", false)]
    [InlineData("member", false)]
    [InlineData("direct", false)]
    [InlineData("static", false)]
    [InlineData("coalescing", false)]
    [InlineData("direct-coalescing", false)]
    [InlineData("initializer", true)]
    [InlineData("member", true)]
    [InlineData("coalescing", true)]
    public void AnOwnedBindingCannotEscapeIntoANonOwningField(string store, bool inferred)
    {
        var f = new Stores();
        var local = new LocalVariable("value", f.Item) { Ownership = inferred ? OwnershipKind.Unrestricted : OwnershipKind.Owned, InfersOwnership = inferred };
        var value = new LocalReferenceExpression(HandBuiltModel.Node(37), local);
        var statements = new List<Statement>();
        Expression initial = Stores.Fresh(f.Item);
        if(inferred)
        {
            var owner = new Stores(owning: true, type: f.Item);
            var holder = new LocalVariable("owner", owner.Holder) { Ownership = OwnershipKind.Owned };
            statements.Add(new LocalDeclarationStatement(holder, Stores.Fresh(owner.Holder)));
            initial = owner.Access(new LocalReferenceExpression(HandBuiltModel.Node(), holder), owner.Field);
        }

        statements.Add(new LocalDeclarationStatement(local, initial));
        statements.Add(f.Store(store, value));
        var body = HandBuiltModel.Block([.. statements]);
        Assert.Equal(ThisTier.Owned, LocalOwnership.For(body).Of(local));

        var diagnostic = Assert.Single(f.Model.Analyze(new OwnershipAnalyzer(), body));

        Assert.Equal("MIST0486", diagnostic.Number.Value);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(37, Assert.IsType<SourceRange>(diagnostic.Location).Offset);
        Assert.Contains("value", diagnostic.Message);
        Assert.Contains("Value", diagnostic.Message);
    }

    [Theory]
    [InlineData("initializer")]
    [InlineData("member")]
    [InlineData("coalescing")]
    public void ARejectedStoreDoesNotMakeALaterOwnedMoveLookShared(string store)
    {
        var f = new Stores();
        var local = new LocalVariable("value", f.Item) { Ownership = OwnershipKind.Owned };
        var moved = new LocalVariable("moved", f.Item) { Ownership = OwnershipKind.Owned };
        var body = HandBuiltModel.Block(new LocalDeclarationStatement(local, Stores.Fresh(f.Item)),
            f.Store(store, new LocalReferenceExpression(HandBuiltModel.Node(37), local)),
            new LocalDeclarationStatement(moved, new LocalReferenceExpression(HandBuiltModel.Node(73), local)));

        var diagnostic = Assert.Single(f.Model.Analyze(new OwnershipAnalyzer(), body));

        Assert.Equal("MIST0486", diagnostic.Number.Value);
        Assert.Equal(37, Assert.IsType<SourceRange>(diagnostic.Location).Offset);
    }

    [Theory]
    [InlineData("initializer")]
    [InlineData("member")]
    [InlineData("coalescing")]
    public void AnOwnedFieldCannotEscapeIntoANonOwningField(string store)
    {
        var f = new Stores(owning: true);
        var local = new LocalVariable("owner", f.Holder) { Ownership = OwnershipKind.Owned };
        var value = f.Access(new LocalReferenceExpression(HandBuiltModel.Node(), local), f.Field, 37);
        var target = new Stores(type: f.Item);
        var body = HandBuiltModel.Block(new LocalDeclarationStatement(local, Stores.Fresh(f.Holder)), target.Store(store, value));

        var diagnostic = Assert.Single(f.Model.Analyze(new OwnershipAnalyzer(), body));

        Assert.Equal("MIST0486", diagnostic.Number.Value);
        Assert.Equal(37, Assert.IsType<SourceRange>(diagnostic.Location).Offset);
    }

    [Theory]
    [InlineData("initializer", false)]
    [InlineData("member", false)]
    [InlineData("initializer", true)]
    [InlineData("member", true)]
    [InlineData("coalescing", false)]
    [InlineData("coalescing", true)]
    public void AnOwnedFieldOfThisCannotEscapeIntoANonOwningField(string store, bool explicitThis)
    {
        var f = new Stores(owning: true);
        Expression value = explicitThis
            ? f.Access(new ThisExpression(HandBuiltModel.Node(), f.Holder), f.Field, 37)
            : new FieldReferenceExpression(HandBuiltModel.Node(37), f.Field);
        var target = new Stores(type: f.Item);
        var body = HandBuiltModel.Block(target.Store(store, value));

        var diagnostic = Assert.Single(f.Model.Analyze(new OwnershipAnalyzer(), body));

        Assert.Equal("MIST0486", diagnostic.Number.Value);
        Assert.Equal(37, Assert.IsType<SourceRange>(diagnostic.Location).Offset);
    }

    [Theory]
    [InlineData("initializer", false)]
    [InlineData("member", false)]
    [InlineData("coalescing", false)]
    [InlineData("initializer", true)]
    [InlineData("member", true)]
    [InlineData("coalescing", true)]
    public void AnOwnedFieldNestedUnderThisCannotEscapeIntoANonOwningField(string store, bool explicitThis)
    {
        var (owner, middle) = Nested(middleOwned: true);
        var target = new Stores(type: middle.Item);
        var body = HandBuiltModel.Block(target.Store(store, middle.Access(Receiver(owner, explicitThis), middle.Field, 37)));

        var diagnostic = Assert.Single(target.Model.Analyze(new OwnershipAnalyzer(), body));

        Assert.Equal("MIST0486", diagnostic.Number.Value);
        Assert.Equal(37, Assert.IsType<SourceRange>(diagnostic.Location).Offset);
        Assert.Contains("Value.Value", diagnostic.Message);
    }

    [Theory]
    [InlineData("initializer", false)]
    [InlineData("member", false)]
    [InlineData("initializer", true)]
    [InlineData("member", true)]
    public void AnOwnedFieldReachedThroughANonOwningFieldOfThisIsAdmitted(string store, bool explicitThis)
    {
        var (owner, middle) = Nested(middleOwned: false);
        var target = new Stores(type: middle.Item);
        var body = HandBuiltModel.Block(target.Store(store, middle.Access(Receiver(owner, explicitThis), middle.Field, 37)));

        Assert.Empty(target.Model.Analyze(new OwnershipAnalyzer(), body));
    }

    [Theory]
    [InlineData("initializer")]
    [InlineData("member")]
    [InlineData("coalescing")]
    public void AnOwnedReceiverCannotEscapeIntoANonOwningField(string store)
    {
        var f = new Stores();
        var body = HandBuiltModel.Block(f.Store(store, new ThisExpression(HandBuiltModel.Node(37), f.Item)));

        var diagnostic = Assert.Single(f.Model.Analyze(new OwnershipAnalyzer(), body, member: f.Method(ThisTier.Owned)));

        Assert.Equal("MIST0486", diagnostic.Number.Value);
        Assert.Equal(37, Assert.IsType<SourceRange>(diagnostic.Location).Offset);
        Assert.Contains("'this'", diagnostic.Message);
        Assert.Contains("'Value'", diagnostic.Message);
    }

    [Theory]
    [InlineData("initializer", "undeclared")]
    [InlineData("member", "undeclared")]
    [InlineData("initializer", "struct")]
    [InlineData("member", "struct")]
    [InlineData("initializer", "owning")]
    [InlineData("member", "owning")]
    public void AReceiverThatLeavesNoOwnedAliasIsAdmitted(string store, string receiver)
    {
        var f = new Stores(owning: receiver == "owning", type: receiver == "struct" ? HandBuiltModel.Struct("Point") : null);
        var body = HandBuiltModel.Block(f.Store(store, new ThisExpression(HandBuiltModel.Node(37), f.Item)));

        Assert.Empty(f.Model.Analyze(new OwnershipAnalyzer(), body, member: f.Method(receiver == "undeclared" ? null : ThisTier.Owned)));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void AnOwnedBindingCannotEscapeThroughARefLocalOverANonOwningField(bool coalescing, bool chained)
    {
        var f = new Stores();
        var local = new LocalVariable("value", f.Item) { Ownership = OwnershipKind.Owned };
        var holder = new LocalVariable("holder", f.Holder);
        var slot = new LocalVariable("slot", f.Item) { IsRef = true };
        var statements = new List<Statement>
        {
            new LocalDeclarationStatement(local, Stores.Fresh(f.Item)),
            new LocalDeclarationStatement(holder, Stores.Fresh(f.Holder)),
            new LocalDeclarationStatement(slot, f.Access(new LocalReferenceExpression(HandBuiltModel.Node(), holder), f.Field)),
        };
        if(chained)
        {
            var again = new LocalVariable("again", f.Item) { IsRef = true };
            statements.Add(new LocalDeclarationStatement(again, new LocalReferenceExpression(HandBuiltModel.Node(), slot)));
            slot = again;
        }

        statements.Add(Stores.Write(slot, new LocalReferenceExpression(HandBuiltModel.Node(37), local), coalescing));
        statements.Add(new LocalDeclarationStatement(new LocalVariable("moved", f.Item) { Ownership = OwnershipKind.Owned }, new LocalReferenceExpression(HandBuiltModel.Node(73), local)));

        var diagnostic = Assert.Single(f.Model.Analyze(new OwnershipAnalyzer(), HandBuiltModel.Block([.. statements])));

        Assert.Equal("MIST0486", diagnostic.Number.Value);
        Assert.Equal(37, Assert.IsType<SourceRange>(diagnostic.Location).Offset);
        Assert.Contains("'value'", diagnostic.Message);
        Assert.Contains("'Value'", diagnostic.Message);
    }

    [Theory]
    [InlineData("local", "member")]
    [InlineData("local", "initializer")]
    [InlineData("local", "coalescing")]
    [InlineData("chained", "member")]
    [InlineData("chained", "initializer")]
    [InlineData("owning-field", "member")]
    [InlineData("owning-field", "initializer")]
    public void AnOwnedValueReadThroughARefLocalCannotEscapeIntoANonOwningField(string aliased, string store)
    {
        var owner = new Stores(owning: true);
        var f = new Stores(type: owner.Item);
        var local = new LocalVariable("value", f.Item) { Ownership = OwnershipKind.Owned };
        var holder = new LocalVariable("owner", owner.Holder) { Ownership = OwnershipKind.Owned };
        var slot = new LocalVariable("slot", f.Item) { IsRef = true };
        var inner = new List<Statement>
        {
            new LocalDeclarationStatement(slot, aliased == "owning-field"
                ? owner.Access(new LocalReferenceExpression(HandBuiltModel.Node(), holder), owner.Field)
                : new LocalReferenceExpression(HandBuiltModel.Node(), local)),
        };
        if(aliased == "chained")
        {
            var again = new LocalVariable("again", f.Item) { IsRef = true };
            inner.Add(new LocalDeclarationStatement(again, new LocalReferenceExpression(HandBuiltModel.Node(), slot)));
            slot = again;
        }

        inner.Add(f.Store(store, new LocalReferenceExpression(HandBuiltModel.Node(37), slot)));
        var body = HandBuiltModel.Block(new LocalDeclarationStatement(local, Stores.Fresh(f.Item)),
            new LocalDeclarationStatement(holder, Stores.Fresh(owner.Holder)),
            HandBuiltModel.Block([.. inner]),
            new LocalDeclarationStatement(new LocalVariable("moved", f.Item) { Ownership = OwnershipKind.Owned }, new LocalReferenceExpression(HandBuiltModel.Node(73), local)));

        var diagnostic = Assert.Single(f.Model.Analyze(new OwnershipAnalyzer(), body));

        Assert.Equal("MIST0486", diagnostic.Number.Value);
        Assert.Equal(37, Assert.IsType<SourceRange>(diagnostic.Location).Offset);
        Assert.Contains(aliased == "owning-field" ? "'owner.Value'" : "'value'", diagnostic.Message);
    }

    [Theory]
    [InlineData("shared", "member")]
    [InlineData("shared", "initializer")]
    [InlineData("non-owning-field", "member")]
    [InlineData("non-owning-field", "initializer")]
    public void AValueReadThroughARefLocalThatLeavesNoOwnedAliasIsAdmitted(string aliased, string store)
    {
        var plain = new Stores();
        var f = new Stores(type: plain.Item);
        var local = new LocalVariable("value", f.Item) { Ownership = aliased == "shared" ? OwnershipKind.Unrestricted : OwnershipKind.Owned };
        var holder = new LocalVariable("holder", plain.Holder);
        var slot = new LocalVariable("slot", f.Item) { IsRef = true };
        var body = HandBuiltModel.Block(new LocalDeclarationStatement(local, Stores.Fresh(f.Item)),
            new LocalDeclarationStatement(holder, Stores.Fresh(plain.Holder)),
            new LocalDeclarationStatement(slot, aliased == "non-owning-field"
                ? plain.Access(new LocalReferenceExpression(HandBuiltModel.Node(), holder), plain.Field)
                : new LocalReferenceExpression(HandBuiltModel.Node(), local)),
            f.Store(store, new LocalReferenceExpression(HandBuiltModel.Node(37), slot)));

        Assert.Empty(f.Model.Analyze(new OwnershipAnalyzer(), body));
    }

    [Theory]
    [InlineData("local", "member")]
    [InlineData("local", "initializer")]
    [InlineData("local", "coalescing")]
    [InlineData("chained", "member")]
    [InlineData("chained", "initializer")]
    [InlineData("owning-field", "member")]
    [InlineData("owning-field", "initializer")]
    [InlineData("nested", "member")]
    [InlineData("nested", "initializer")]
    public void AnOwnedFieldReadThroughARefLocalReceiverCannotEscapeIntoANonOwningField(string aliased, string store)
    {
        var (outer, middle) = Nested(middleOwned: true);
        var f = new Stores(type: middle.Item);
        var top = aliased is "owning-field" or "nested";
        var holder = new LocalVariable("owner", top ? outer.Holder : middle.Holder) { Ownership = OwnershipKind.Owned };
        var slot = new LocalVariable("slot", aliased == "nested" ? outer.Holder : middle.Holder) { IsRef = true };
        Expression named = new LocalReferenceExpression(HandBuiltModel.Node(), holder);
        var inner = new List<Statement> { new LocalDeclarationStatement(slot, aliased == "owning-field" ? outer.Access(named, outer.Field) : named) };
        if(aliased == "chained")
        {
            var again = new LocalVariable("again", middle.Holder) { IsRef = true };
            inner.Add(new LocalDeclarationStatement(again, new LocalReferenceExpression(HandBuiltModel.Node(), slot)));
            slot = again;
        }

        Expression receiver = new LocalReferenceExpression(HandBuiltModel.Node(), slot);
        inner.Add(f.Store(store, middle.Access(aliased == "nested" ? outer.Access(receiver, outer.Field) : receiver, middle.Field, 37)));
        var body = HandBuiltModel.Block(new LocalDeclarationStatement(holder, Stores.Fresh(holder.Type)),
            HandBuiltModel.Block([.. inner]),
            new LocalDeclarationStatement(new LocalVariable("moved", holder.Type) { Ownership = OwnershipKind.Owned }, new LocalReferenceExpression(HandBuiltModel.Node(73), holder)));

        var diagnostic = Assert.Single(f.Model.Analyze(new OwnershipAnalyzer(), body));

        Assert.Equal("MIST0486", diagnostic.Number.Value);
        Assert.Equal(37, Assert.IsType<SourceRange>(diagnostic.Location).Offset);
        Assert.Contains(top ? "'owner.Value.Value'" : "'owner.Value'", diagnostic.Message);
    }

    [Theory]
    [InlineData("shared-holder", "member")]
    [InlineData("shared-holder", "initializer")]
    [InlineData("non-owning-field", "member")]
    [InlineData("non-owning-field", "initializer")]
    public void AFieldReadThroughARefLocalReceiverThatLeavesNoOwnedAliasIsAdmitted(string aliased, string store)
    {
        var source = new Stores(owning: aliased == "shared-holder");
        var f = new Stores(type: source.Item);
        var holder = new LocalVariable("owner", source.Holder) { Ownership = aliased == "shared-holder" ? OwnershipKind.Unrestricted : OwnershipKind.Owned };
        var slot = new LocalVariable("slot", source.Holder) { IsRef = true };
        var body = HandBuiltModel.Block(new LocalDeclarationStatement(holder, Stores.Fresh(source.Holder)),
            new LocalDeclarationStatement(slot, new LocalReferenceExpression(HandBuiltModel.Node(), holder)),
            f.Store(store, source.Access(new LocalReferenceExpression(HandBuiltModel.Node(), slot), source.Field, 37)));

        Assert.Empty(f.Model.Analyze(new OwnershipAnalyzer(), body));
    }

    [Theory]
    [InlineData("local", false)]
    [InlineData("local", true)]
    [InlineData("ref-local", false)]
    [InlineData("ref-local", true)]
    [InlineData("ref-owning-field", false)]
    [InlineData("ref-owning-field", true)]
    public void AWriteToALocalThatStandsForNoNonOwningFieldIsAdmitted(string target, bool coalescing)
    {
        var f = new Stores(owning: true);
        var local = new LocalVariable("value", f.Item) { Ownership = OwnershipKind.Owned };
        var other = new LocalVariable("other", f.Item) { Ownership = OwnershipKind.Owned };
        var holder = new LocalVariable("holder", f.Holder) { Ownership = OwnershipKind.Owned };
        var slot = new LocalVariable("slot", f.Item) { IsRef = true };
        var statements = new List<Statement>
        {
            new LocalDeclarationStatement(local, Stores.Fresh(f.Item)),
            new LocalDeclarationStatement(other, Stores.Fresh(f.Item)),
            new LocalDeclarationStatement(holder, Stores.Fresh(f.Holder)),
        };
        if(target != "local")
        {
            Expression aliased = target == "ref-local"
                ? new LocalReferenceExpression(HandBuiltModel.Node(), other)
                : f.Access(new LocalReferenceExpression(HandBuiltModel.Node(), holder), f.Field);
            statements.Add(new LocalDeclarationStatement(slot, aliased));
        }

        statements.Add(Stores.Write(target == "local" ? other : slot, new LocalReferenceExpression(HandBuiltModel.Node(37), local), coalescing));

        Assert.Empty(f.Model.Analyze(new OwnershipAnalyzer(), HandBuiltModel.Block([.. statements])));
    }

    [Theory]
    [InlineData("initializer")]
    [InlineData("member")]
    [InlineData("coalescing")]
    public void AnOwningFieldTakesTheValueAndMakesALaterReadFail(string store)
    {
        var f = new Stores(owning: true);
        var local = new LocalVariable("value", f.Item) { Ownership = OwnershipKind.Owned };
        var value = new LocalReferenceExpression(HandBuiltModel.Node(37), local);
        var later = new LocalReferenceExpression(HandBuiltModel.Node(73), local);
        var declaration = new LocalDeclarationStatement(local, Stores.Fresh(f.Item));
        var stored = f.Store(store, value);

        Assert.Empty(f.Model.Analyze(new OwnershipAnalyzer(), HandBuiltModel.Block(declaration, stored)));
        var diagnostic = Assert.Single(f.Model.Analyze(new OwnershipAnalyzer(), HandBuiltModel.Block(declaration, stored, new ExpressionStatement(later))));
        Assert.Equal("MIST0227", diagnostic.Number.Value);
        Assert.Equal(73, Assert.IsType<SourceRange>(diagnostic.Location).Offset);
    }

    [Theory]
    [InlineData("initializer", "fresh")]
    [InlineData("member", "fresh")]
    [InlineData("initializer", "shared")]
    [InlineData("member", "shared")]
    [InlineData("initializer", "immutable")]
    [InlineData("member", "immutable")]
    [InlineData("initializer", "immutable-erased")]
    [InlineData("member", "immutable-erased")]
    [InlineData("initializer", "struct")]
    [InlineData("member", "struct")]
    [InlineData("initializer", "clone")]
    [InlineData("member", "clone")]
    public void AStoreThatDoesNotRetainAnOwnedAliasIsAdmitted(string store, string source)
    {
        var model = new HandBuiltModel();
        IMistType type = source switch
        {
            "immutable" or "immutable-erased" => model.Type("String"),
            "struct" => HandBuiltModel.Struct("Point"),
            _ => HandBuiltModel.Class("Item"),
        };
        var f = new Stores(type: source == "immutable-erased" ? model.Type("Object") : type);
        var local = new LocalVariable("value", type) { Ownership = source == "shared" ? OwnershipKind.Unrestricted : OwnershipKind.Owned };
        Expression value = new LocalReferenceExpression(HandBuiltModel.Node(37), local);
        value = source switch
        {
            "fresh" => Stores.Fresh(type),
            "clone" => new CloneExpression(HandBuiltModel.Node(37), value),
            "immutable-erased" => new ImplicitConversionExpression(HandBuiltModel.Node(37), value, f.Item, []),
            _ => value,
        };
        var body = HandBuiltModel.Block(new LocalDeclarationStatement(local, Stores.Fresh(type)), f.Store(store, value));

        Assert.Empty(f.Model.Analyze(new OwnershipAnalyzer(), body));
    }

    private static (Stores Owner, Stores Middle) Nested(bool middleOwned)
    {
        var middle = new Stores(owning: true);
        return (new Stores(owning: middleOwned, type: middle.Holder), middle);
    }

    private static Expression Receiver(Stores owner, bool explicitThis)
    {
        return explicitThis
            ? owner.Access(new ThisExpression(HandBuiltModel.Node(), owner.Holder), owner.Field)
            : new FieldReferenceExpression(HandBuiltModel.Node(), owner.Field);
    }

    private sealed class Stores
    {
        public Stores(bool owning = false, IMistType? type = null)
        {
            Item = type ?? HandBuiltModel.Class("Item");
            Holder = HandBuiltModel.Class("Holder");
            Field = new Field("Value", Item, owning ? OwnershipKind.Owned : OwnershipKind.Unrestricted, default) { DeclaringType = Holder };
            Holder.FieldsList.Add(Field);
        }

        public HandBuiltModel Model { get; } = new();

        public IMistType Item { get; }

        public ClassType Holder { get; }

        public Field Field { get; }

        public static NewExpression Fresh(IMistType type)
        {
            return new NewExpression(HandBuiltModel.Node(), type, [], null, [], []);
        }

        public Method Method(ThisTier? receiver)
        {
            return new Method("Store", Model.Type("Void"), [], default) { DeclaringType = Holder, DeclaredThisTier = receiver };
        }

        public static ExpressionStatement Write(LocalVariable target, Expression value, bool coalescing)
        {
            return new ExpressionStatement(new AssignmentExpression(HandBuiltModel.Node(), new LocalReferenceExpression(HandBuiltModel.Node(), target), value, target.Type, isCoalescing: coalescing));
        }

        public MemberAccessExpression Access(Expression receiver, Field field, int offset = 0)
        {
            return new MemberAccessExpression(HandBuiltModel.Node(offset), receiver, field.Name, field.Type, member: new FieldResult(field, Holder, []));
        }

        public ExpressionStatement Store(string store, Expression value)
        {
            if(store == "initializer")
            {
                var entry = new ObjectInitializerAssignment(HandBuiltModel.Node(31), Field.Name, value, Field.Type, member: new FieldResult(Field, Holder, []));
                return new ExpressionStatement(new NewExpression(HandBuiltModel.Node(), Holder, [], null, [entry], []));
            }

            var field = store == "static" ? new Field(Field.Name, Item, OwnershipKind.Unrestricted, new MemberModifiers { IsStatic = true }) { DeclaringType = Holder } : Field;
            Expression target = store is "direct" or "static" or "direct-coalescing" ? new FieldReferenceExpression(HandBuiltModel.Node(), field) : Access(Fresh(Holder), field);
            return new ExpressionStatement(new AssignmentExpression(HandBuiltModel.Node(), target, value, Field.Type, isCoalescing: store.EndsWith("coalescing", StringComparison.Ordinal)));
        }
    }
}