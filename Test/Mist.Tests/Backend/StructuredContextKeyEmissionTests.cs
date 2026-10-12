using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class StructuredContextKeyEmissionTests
{
    private readonly HandBuiltModel model = new();
    private readonly ClassType text;
    private readonly StructType integer = new("Int32", "Standard", new Scope(null), null, null);

    public StructuredContextKeyEmissionTests()
    {
        text = model.Type("String");
    }

    [Fact]
    [Trait("Tier", "1")]
    public void OptionalKeysHaveIdentityWithoutChangingValueSpellings()
    {
        Assert.Equal("string", TypeNameEmitter.Emit(text));
        Assert.Equal("string?", TypeNameEmitter.Emit(Optional(text)));
        Assert.Equal("int?", TypeNameEmitter.Emit(Optional(integer)));
        Assert.Equal("string", TypeNameEmitter.EmitContextKey(text));
        Assert.Equal("MistContext.Optional<string>", TypeNameEmitter.EmitContextKey(Optional(text)));
        Assert.Equal("int?", TypeNameEmitter.EmitContextKey(Optional(integer)));
        Assert.Equal("string?", TypeNameEmitter.Emit(Optional(text)));
    }

    [Fact]
    [Trait("Tier", "1")]
    public void AliasesRefinementsAndTupleNamesNormalizeInsideOptionalKeys()
    {
        var alias = new TypeAlias("Name", "Probe", new Scope(null), null, new TypeAliasDeclarationNode()) { AliasedType = text };
        var refined = HandBuiltModel.Refined(text, "Short");
        var optional = Optional(text);
        var pair = new TupleType([optional, integer]);

        Assert.Equal(TypeNameEmitter.EmitContextKey(optional), TypeNameEmitter.EmitContextKey(Optional(alias)));
        Assert.Equal(TypeNameEmitter.EmitContextKey(optional), TypeNameEmitter.EmitContextKey(Optional(refined)));
        Assert.Equal(TypeNameEmitter.EmitContextKey(optional), TypeNameEmitter.EmitContextKey(HandBuiltModel.Refined(optional)));
        Assert.Equal(TypeNameEmitter.EmitContextKey(pair), TypeNameEmitter.EmitContextKey(new NamedTupleType(pair, ["name", "count"])));
        Assert.Equal("(string? name, int count)", TypeNameEmitter.Emit(new NamedTupleType(pair, ["name", "count"])));
        var numberAlias = new TypeAlias("Count", "Probe", new Scope(null), null, new TypeAliasDeclarationNode()) { AliasedType = integer };
        Assert.Equal(TypeNameEmitter.EmitContextKey(Optional(integer)), TypeNameEmitter.EmitContextKey(Optional(numberAlias)));
        Assert.Equal(TypeNameEmitter.EmitContextKey(Optional(integer)), TypeNameEmitter.EmitContextKey(Optional(HandBuiltModel.Refined(numberAlias))));
    }

    [Fact]
    [Trait("Tier", "1")]
    public void OptionalMarkersComposeThroughTuplesCallablesAndConstructions()
    {
        var optional = Optional(text);
        var nested = new TupleType([optional, new FunctionType([optional], Optional(integer), isPure: true)]);
        var holder = HandBuiltModel.Class("Holder<>");

        Assert.Equal("(MistContext.Optional<string>, MistContext.Pure<System.Func<MistContext.Optional<string>, int?>>)",
            TypeNameEmitter.EmitContextKey(nested));
        Assert.Equal("(MistContext.Optional<string>, MistContext.Pure<System.Func<MistContext.Optional<string>, int?>>)?",
            TypeNameEmitter.EmitContextKey(Optional(nested)));
        Assert.Equal("Probe.Holder<MistContext.Optional<string>>", TypeNameEmitter.EmitContextKey(new ConstructedType(holder, [optional])));
        Assert.Equal("System.Func<string?, int?>", TypeNameEmitter.Emit(new FunctionType([optional], Optional(integer), isPure: true)));
        Assert.NotEqual(TypeNameEmitter.EmitContextKey(new FunctionType([optional], text)), TypeNameEmitter.EmitContextKey(new FunctionType([text], optional)));
    }

    [Fact]
    [Trait("Tier", "1")]
    public void ClosedGenericKeysRetainOptionalIdentityInTheirArguments()
    {
        var holder = HandBuiltModel.Class("Holder<>");
        var parameter = new TypeParameter("T", 0, new Scope(null), null);
        holder.TypeParametersList.Add(parameter);
        var constructed = new ConstructedType(holder, [parameter]);
        var closed = new ConstructedType(holder, [Optional(text)]);

        using(TypeNameEmitter.Substituting(TypeSubstitution.ForReceiver(closed)))
        {
            Assert.Equal("Probe.Holder<MistContext.Optional<string>>", TypeNameEmitter.EmitContextKey(constructed));
            Assert.Equal("MistContext.Optional<string>", TypeNameEmitter.EmitContextKey(parameter));
            Assert.Equal("Probe.Holder<string?>", TypeNameEmitter.Emit(constructed));
        }

        Assert.Equal("T", TypeNameEmitter.EmitContextKey(parameter));
    }

    [Fact]
    [Trait("Tier", "2")]
    public void PushPopAndEntryPeekUseTheSameKeyWhileTheCastUsesTheValueType()
    {
        var optional = Optional(text);
        var body = HandBuiltModel.Block(new WithBlockStatement(
            HandBuiltModel.Literal(optional), HandBuiltModel.Block(), binding: null, keyType: optional));
        var method = new Method("Read", optional, [], new MemberModifiers()) { Body = body };
        method.ContextBindingsList.Add(new ContextBinding(optional, "value"));
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();

        BodyEmitter.EmitMethodBody(method, body, writer, diagnostics);

        Assert.Empty(diagnostics.ToImmutableArray());
        Assert.Contains("var value = (string?)MistContext.Peek<MistContext.Optional<string>>();", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("MistContext.Push<MistContext.Optional<string>>(", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("MistContext.Pop<MistContext.Optional<string>>();", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Tier", "2")]
    public void AnOpenClauseReadsItsKeyParameterAndAWithBlockUnderItFilesByTheSameParameter()
    {
        var parameter = new TypeParameter("T", 0, new Scope(null), null);
        var body = HandBuiltModel.Block(new WithBlockStatement(
            HandBuiltModel.Literal(parameter), HandBuiltModel.Block(), binding: null, keyType: parameter));
        var method = new Method("Read", parameter, [], new MemberModifiers()) { Body = body, DeclaringType = HandBuiltModel.Class("Owner") };
        method.TypeParametersList.Add(parameter);
        method.ContextBindingsList.Add(new ContextBinding(parameter, "value"));
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();

        BodyEmitter.EmitMethodBody(method, body, writer, diagnostics);

        Assert.Empty(diagnostics.ToImmutableArray());
        Assert.Equal("T Read<T>(System.Type __context_0)", MethodEmitter.EmitSignature(method, diagnostics));
        Assert.Contains("var value = (T)MistContext.Peek(__context_0);", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("MistContext.Push(__context_0, ", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("MistContext.Pop(__context_0);", writer.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Peek<", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Tier", "2")]
    public void AClosedClauseOnAGenericMethodKeepsTheGenericPeekAndNoParameter()
    {
        var parameter = new TypeParameter("T", 0, new Scope(null), null);
        var method = new Method("Read", text, [], new MemberModifiers()) { Body = HandBuiltModel.Block(), DeclaringType = HandBuiltModel.Class("Owner") };
        method.TypeParametersList.Add(parameter);
        method.ContextBindingsList.Add(new ContextBinding(Optional(text), "value"));
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();

        BodyEmitter.EmitMethodBody(method, method.Body!, writer, diagnostics);

        Assert.Equal("string Read<T>()", MethodEmitter.EmitSignature(method, diagnostics));
        Assert.Contains("var value = (string?)MistContext.Peek<MistContext.Optional<string>>();", writer.ToString(), StringComparison.Ordinal);
    }

    private UnionType Optional(IMistType inner)
    {
        return new UnionType([inner, model.Type("Void")]);
    }
}