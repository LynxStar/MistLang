using Mist.Generated;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class DelegateInvocationBindingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ADelegateCallRecordsItsClosedParametersAndResultWithoutChangingItsReceiver(bool generic)
    {
        var f = new BindingFragments();
        var argument = f.Built.Type("Int32");
        var result = f.Built.Type("Boolean");
        var definition = Delegate("Predicate", argument, result);
        IMistType callable = definition;
        if(generic)
        {
            var parameter = new TypeParameter("T", 0, definition.Scope, definition);
            definition.TypeParametersList.Add(parameter);
            definition.MethodsList[0].ParametersList[0].Type = parameter;
            callable = f.Built.Interner.GetOrCreateConstructedType(definition, [argument]);
        }

        _ = f.Local("callback", callable);
        _ = f.Local("input", argument);

        var call = Assert.IsType<InvocationExpression>(Call(f, "input"));

        Assert.Same(callable, call.Receiver.Type);
        Assert.Null(call.Method);
        Assert.Same(definition.MethodsList[0], call.InvokeMethod);
        Assert.Same(result, call.Type);
        Assert.Same(argument, Assert.Single(call.ParameterTypes!));
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void ADelegateCallCarriesItsInvokePassingModeAlongsideTheClosedTypes()
    {
        var f = new BindingFragments();
        var intType = f.Built.Type("Int32");
        var definition = Delegate("Mutate", intType, intType);
        var invoke = new Method("Invoke", intType,
            [new Parameter("value", intType, new ParameterModifier(false, true, OwnershipKind.Unrestricted, ByRefDirection.Ref))],
            new MemberModifiers { IsClrProjected = true, ClrResultIsNonNull = true });
        definition.MethodsList.Clear();
        definition.MethodsList.Add(invoke);
        _ = f.Local("callback", definition);
        _ = f.Local("input", intType);
        var suffix = BindingFragments.At<InvocationSuffixPostfixSuffixNode>();
        suffix.Arguments = new ArgumentListNode
        {
            Argument = new RefArgumentArgumentNode { Expression = BindingFragments.Expression(BindingFragments.Identifier("input")) },
        };

        var call = Assert.IsType<InvocationExpression>(f.Bind(BindingFragments.Identifier("callback"), suffix));

        Assert.Null(call.Method);
        Assert.Same(invoke, call.InvokeMethod);
        Assert.Equal(ByRefDirection.Ref, Assert.Single(call.Parameters!).Modifier.Direction);
        Assert.Same(intType, Assert.Single(call.ParameterTypes!));
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AGenericDelegateResultUsesItsClosedClrNullabilityContract(bool nonNull)
    {
        var f = new BindingFragments();
        var definition = Delegate("Result", f.Built.Type("Int32"), f.Built.Type("String"));
        var result = new TypeParameter("T", 0, definition.Scope, definition);
        definition.TypeParametersList.Add(result);
        definition.MethodsList.Clear();
        definition.MethodsList.Add(new Method("Invoke", result, [new Parameter("input", f.Built.Type("Int32"))],
            new MemberModifiers { IsClrProjected = true, ClrResultIsNonNull = nonNull }));
        _ = f.Local("callback", f.Built.Interner.GetOrCreateConstructedType(definition, [f.Built.Type("String")]));
        _ = f.Local("input", f.Built.Type("Int32"));

        var call = Assert.IsType<InvocationExpression>(Call(f, "input"));

        if(nonNull)
        {
            Assert.Same(f.Built.Type("String"), call.Type);
        }
        else
        {
            Assert.Contains(f.Built.Type("String"), Assert.IsType<UnionType>(call.Type).Constituents);
        }

        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void GenericInvokeClosesNestedConstructionsNamedTuplesAndFunctionTypes()
    {
        var f = new BindingFragments();
        var definition = Delegate("Nested", f.Built.Type("Int32"), f.Built.Type("Boolean"));
        var parameter = new TypeParameter("T", 0, definition.Scope, definition);
        definition.TypeParametersList.Add(parameter);
        var tuple = new NamedTupleType(new TupleType([parameter, f.Built.Type("Boolean")]), ["Value", "Flag"]);
        var list = HandBuiltModel.Class("List<>");
        var nested = new ConstructedType(list, [tuple]);
        definition.MethodsList.Clear();
        _ = BindingFragments.Member(definition, "Invoke", new FunctionType([nested], tuple), nested);
        var callable = f.Built.Interner.GetOrCreateConstructedType(definition, [f.Built.Type("Int32")]);

        var shape = Assert.IsType<FunctionType>(DelegateShape.Of(callable));

        var input = Assert.IsType<ConstructedType>(Assert.Single(shape.ParameterTypes));
        var named = Assert.IsType<NamedTupleType>(Assert.Single(input.TypeArguments));
        Assert.Equal(["Value", "Flag"], named.ElementNames);
        Assert.Same(f.Built.Type("Int32"), named.ElementTypes[0]);
        var output = Assert.IsType<FunctionType>(shape.ReturnType);
        Assert.Equal(input.FullyQualifiedName, Assert.Single(output.ParameterTypes).FullyQualifiedName);
        Assert.Same(f.Built.Type("Int32"), Assert.IsType<NamedTupleType>(output.ReturnType).ElementTypes[0]);
        Assert.Same(parameter, tuple.ElementTypes[0]);
    }

    [Fact]
    public void ADelegateParameterTargetsALambdaArgumentWithItsClosedInputType()
    {
        var f = new BindingFragments();
        var intType = f.Built.Type("Int32");
        var input = new FunctionType([intType], intType);
        _ = f.Local("callback", Delegate("Apply", input, intType));
        var lambda = new LambdaFormExpressionNode
        {
            Lambda = BindingFragments.Lambda(BindingFragments.Expression(BindingFragments.Identifier("x"))),
        };
        var suffix = BindingFragments.At<InvocationSuffixPostfixSuffixNode>();
        suffix.Arguments = BindingFragments.Arguments(lambda);

        var call = Assert.IsType<InvocationExpression>(f.Bind(BindingFragments.Identifier("callback"), suffix));

        var bound = Assert.IsType<LambdaExpression>(Assert.Single(call.Arguments));
        Assert.Same(intType, Assert.Single(bound.Parameters).Type);
        Assert.Same(intType, call.Type);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AnIncorrectDelegateArgumentCountIsRefusedAtTheCall(int count)
    {
        var f = new BindingFragments();
        _ = f.Local("callback", Delegate("Predicate", f.Built.Type("Int32"), f.Built.Type("Boolean")));
        _ = f.Local("input", f.Built.Type("Int32"));
        var suffix = BindingFragments.At<InvocationSuffixPostfixSuffixNode>();
        suffix.Arguments = BindingFragments.Arguments([.. Enumerable.Repeat(BindingFragments.Expression(BindingFragments.Identifier("input")), count)]);

        var error = Assert.IsType<ErrorExpression>(f.Bind(BindingFragments.Identifier("callback"), suffix));

        Assert.Same(suffix, error.Node);
        Assert.Equal("MIST_PM_FUNCTION_CALL_ARITY", Assert.Single(f.Diagnostics.ToImmutableArray()).Id.Value);
    }

    [Fact]
    public void AnIncompatibleDelegateArgumentIsJudgedAgainstTheRecordedParameter()
    {
        var f = new BindingFragments();
        var callable = Delegate("Predicate", f.Built.Type("Int32"), f.Built.Type("Boolean"));
        var call = new InvocationExpression(HandBuiltModel.Node(), HandBuiltModel.Literal(callable),
            [HandBuiltModel.Literal(f.Built.Type("String"))], f.Built.Type("Boolean"), null, [f.Built.Type("Int32")]);

        var diagnostics = f.Built.Analyze(new AssignabilityAnalyzer(), call);

        Assert.Equal("MIST_PA_NO_IMPLICIT_CONVERSION", Assert.Single(diagnostics).Id.Value);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    private static Expression Call(BindingFragments f, string argument)
    {
        var suffix = BindingFragments.At<InvocationSuffixPostfixSuffixNode>();
        suffix.Arguments = BindingFragments.Arguments(BindingFragments.Expression(BindingFragments.Identifier(argument)));
        return f.Bind(BindingFragments.Identifier("callback"), suffix);
    }

    private static ClassType Delegate(string name, IMistType parameter, IMistType result)
    {
        var definition = HandBuiltModel.Class(name,
            new ClassType("MulticastDelegate", "System", null, null, null));
        _ = BindingFragments.Member(definition, "Invoke", result, parameter);
        return definition;
    }
}