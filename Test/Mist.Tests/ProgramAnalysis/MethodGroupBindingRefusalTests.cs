using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

// Tier 2: directly settle hand-built method groups against function slots and report refusals.
[Trait("Tier", "2")]
public sealed class MethodGroupBindingRefusalTests
{
    [Theory]
    [InlineData("generic-method-group-the-target-cannot-close", 2)]
    [InlineData("generic-method-value-with-type-arguments-it-cannot-take", 3)]
    [InlineData("method-group-with-no-function-target", 1)]
    public void TargetMustCloseExactlyTheDeclaredSignature(string refusal, int expectedCount)
    {
        var f = new BindingFragments();
        var intType = f.Built.Type("Int32");
        var longType = f.Built.Type("Int64");
        if(refusal == "method-group-with-no-function-target")
        {
            refuse(f.Function("Twice", intType, intType), intType);
        }
        else if(refusal == "generic-method-group-the-target-cannot-close")
        {
            var first = f.Function("First", intType);
            var parameter = BindingFragments.Parameter(first, "T");
            first = generic("First", parameter, parameter, parameter);
            first.TypeParametersList.Add(parameter);
            refuse(first, new FunctionType([intType, longType], longType));
            var either = f.Function("Either", intType);
            var t = BindingFragments.Parameter(either, "T");
            var r = BindingFragments.Parameter(either, "R");
            either.ParametersList.Add(new Parameter("value", f.Built.Interner.GetOrCreateUnionType([t, r])));
            refuse(either, new FunctionType([f.Built.Interner.GetOrCreateUnionType([intType, f.Built.Type("String")])], intType));
            _ = Assert.IsType<FunctionReferenceExpression>(MethodGroupConversion.Convert(Group(first), new FunctionType([intType, intType], intType), f.Built.Interner));
        }
        else
        {
            var prototype = f.Function("Same", intType);
            var t = BindingFragments.Parameter(prototype, "T");
            var same = generic("Same", t, t);
            same.TypeParametersList.Add(t);
            refuse(same, new FunctionType([intType], intType), [intType, f.Built.Type("String")]);
            refuse(same, new FunctionType([longType], longType), [intType]);
            var good = Group(same, [intType]);
            _ = Assert.IsType<FunctionReferenceExpression>(MethodGroupConversion.Convert(good, new FunctionType([intType], intType), f.Built.Interner));
            var owner = HandBuiltModel.Class("Api");
            owner.FieldsList.Add(new Field("Count", intType, OwnershipKind.Unrestricted, DeclarationFragments.Public) { DeclaringType = owner });
            _ = f.Local("api", owner);
            var nonMethod = BindingFragments.At<GenericMethodGroupSuffixPostfixSuffixNode>();
            nonMethod.Name = BindingFragments.Token("Count");
            nonMethod.TypeArgs = BindingFragments.TypeArguments("Int32");
            _ = f.Bind(BindingFragments.Identifier("api"), nonMethod);
        }

        var diagnostics = f.Diagnostics.ToImmutableArray();
        Assert.Equal(expectedCount, diagnostics.Length);
        Assert.All(diagnostics.Take(refusal == "generic-method-value-with-type-arguments-it-cannot-take" ? 2 : expectedCount), diagnostic =>
        {
            Assert.Equal("MIST_PM_METHOD_GROUP_NOT_CONVERTED", diagnostic.Number.Value);
            Assert.Equal(31, diagnostic.Position().Start.Column);
        });
        if(refusal == "generic-method-value-with-type-arguments-it-cannot-take")
        {
            Assert.Equal("MIST_PM_TYPE_ARGUMENTS_ON_A_NON_METHOD", diagnostics[2].Number.Value);
        }

        void refuse(Method method, IMistType target, IReadOnlyList<IMistType>? stated = null)
        {
            var settled = MethodGroupConversion.Settle([Group(method, stated)], [new Parameter("callback", target)], TypeSubstitution.Empty, f.Builder);
            _ = Assert.IsType<ErrorExpression>(Assert.Single(settled));
        }

        Method generic(string name, IMistType returns, params IMistType[] parameters)
        {
            return f.Function(name, returns, parameters);
        }
    }

    [Fact]
    public void MissingSelectedParameterSilencesAGroupWhoseCallAlreadyFailed()
    {
        var f = new BindingFragments();
        var settled = MethodGroupConversion.Settle([Group(f.Function("Twice", f.Built.Type("Int32"), f.Built.Type("Int32")))], null, TypeSubstitution.Empty, f.Builder);
        _ = Assert.IsType<ErrorExpression>(Assert.Single(settled));
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    private static MethodGroupExpression Group(Method method, IReadOnlyList<IMistType>? stated = null)
    {
        return new(HandBuiltModel.Node(30), null, method.Name, [method], null)
        {
            StatedTypeArguments = stated,
        };
    }
}