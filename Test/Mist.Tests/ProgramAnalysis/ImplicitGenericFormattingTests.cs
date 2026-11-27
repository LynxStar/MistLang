using Mist.ProgramAnalysis;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class ImplicitGenericFormattingTests
{
    private readonly FormattingFragments f = new();

    [Fact]
    public void InterpolatingAnUnmarkedGenericFormatter_RequiresTheCallersTypeParameter()
    {
        var caller = InterpolatingCaller(f.Wrapper);

        Assert.Same(caller.TypeParametersList[0], Assert.Single(new FormattingCallbacks().Of(caller, caller.DeclaringType)));
    }

    [Fact]
    public void InterpolatingADeclaredReadonlyGenericFormatter_LeavesTheObligationWithItsPromise()
    {
        var caller = InterpolatingCaller(f.SealedWrapper);

        Assert.Empty(new FormattingCallbacks().Of(caller, caller.DeclaringType));
    }

    private Method InterpolatingCaller(ClassType formatter)
    {
        var scope = new Scope(f.Program.Scope);
        var typeParameter = new TypeParameter("T", 0, scope, null);
        var argument = new Parameter("wrapped", f.Constructed(formatter, typeParameter));
        var local = new LocalVariable(argument.Name, argument.Type) { Parameter = argument };
        var hole = new LocalReferenceExpression(HandBuiltModel.Node(), local);
        var caller = new Method("Show", f.Text, [argument], new MemberModifiers { IsStatic = true })
        {
            DeclaringType = f.Program,
            Scope = scope,
            Body = HandBuiltModel.Block(new ReturnStatement(f.Interpolate(hole), f.Text)),
        };
        typeParameter.DeclaringMethod = caller;
        caller.TypeParametersList.Add(typeParameter);
        return caller;
    }
}