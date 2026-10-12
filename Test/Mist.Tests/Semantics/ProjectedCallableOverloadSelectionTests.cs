using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class ProjectedCallableOverloadSelectionTests
{
    [Theory]
    [InlineData("Select", false)]
    [InlineData("Select", true)]
    [InlineData("Where", false)]
    public void AProjectedLinqExtensionSelectsTheClosedUnarySignature(string name, bool explicitResult)
    {
        var f = new BindingFragments();
        var integer = f.Built.Type("Int32");
        var boolean = f.Built.Type("Boolean");
        var enumerable = new AbstractType("IEnumerable<>", "Standard", new Scope(null), null, null) { Assembly = MistAssembly.Ambient };
        enumerable.TypeParametersList.Add(new TypeParameter("T", 0, enumerable.Scope, enumerable));
        var methods = new[] { false, true }.Select(indexed => Projected(f, enumerable, name, indexed, integer, boolean)).ToList();
        var body = name == "Select"
            ? BindingFragments.Expression(BindingFragments.Identifier("x"))
            : BindingFragments.Expression(new TrueLiteralExpressionPrimaryNode());
        var lambda = new UnboundLambdaExpression(BindingFragments.Lambda(body));

        var choice = OverloadResolution.Select(methods, new Expression[] { lambda }, null,
            explicitResult ? [integer] : null, f.Built.Interner, lambdas: new LambdaTrials(f.Binding, f.Builder));

        var selected = Assert.IsType<Method>(choice.Selected);
        var callback = Assert.IsType<FunctionType>(choice.Substitution.Apply(Assert.Single(selected.ParametersList).Type, f.Built.Interner));
        Assert.Same(integer, Assert.Single(callback.ParameterTypes));
        Assert.Same(name == "Select" ? integer : boolean, callback.ReturnType);
        var result = Assert.IsType<ConstructedType>(choice.Substitution.Apply(selected.ReturnType, f.Built.Interner));
        Assert.Same(integer, Assert.Single(result.TypeArguments));
        if(name == "Select")
        {
            Assert.Same(integer, choice.Substitution.Apply(Assert.Single(selected.TypeParametersList), f.Built.Interner));
        }

        var settled = Assert.IsType<LambdaExpression>(LambdaTrials.BindAgainst(lambda, callback, f.Binding, f.Builder));
        Assert.Same(integer, Assert.Single(settled.Parameters).Type);
        Assert.Equal(callback.ReturnType.FullyQualifiedName, Assert.IsType<FunctionType>(settled.Type).ReturnType.FullyQualifiedName);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    /// <summary>
    /// One LINQ overload as the ambient provider projects it: the receiver's <c>TSource</c> is the
    /// block's, <c>TResult</c> keeps the CLR's flat ordinal of 1 on the method, and the callback is a
    /// function type with the receiver element first and, for the indexed overload, an <c>int</c> second.
    /// </summary>
    private static Method Projected(BindingFragments f, AbstractType enumerable, string name, bool indexed, IMistType integer, IMistType boolean)
    {
        var block = new ExtensionBlock("self", "System.Linq", new ExtensionScope(null), null);
        var source = new TypeParameter("TSource", 0, block.Scope, null!);
        block.TypeParametersList.Add(source);
        block.ResolvedReceiverType = f.Built.Interner.GetOrCreateConstructedType(enumerable, [source]);
        var scope = new MethodScope(block.Scope) { IsStatic = true };
        var own = new List<TypeParameter>();
        var result = boolean;
        var returns = block.ResolvedReceiverType;

        if(name == "Select")
        {
            var projected = new TypeParameter("TResult", 1, scope, null!);
            scope.DeclareType("TResult", projected);
            own.Add(projected);
            result = projected;
            returns = f.Built.Interner.GetOrCreateConstructedType(enumerable, [projected]);
        }

        IReadOnlyList<IMistType> inputs = indexed ? [source, integer] : [source];
        var method = new Method(
            name, returns, [new Parameter("selector", f.Built.Interner.GetOrCreateFunctionType(inputs, result))],
            new MemberModifiers { Accessibility = Accessibility.Public, IsClrProjected = true, ClrResultIsNonNull = true })
        {
            Scope = scope,
        };
        method.TypeParametersList.AddRange(own);
        block.MethodsList.Add(method);

        return ExtensionSpecialization.Close(method, block, [integer], f.Built.Interner);
    }
}