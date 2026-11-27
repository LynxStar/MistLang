using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class ContextWitnessArgumentDecisionTests
{
    [Fact]
    public void OpenClauseUsesTheCallerKeyAtItsOriginalOrdinal()
    {
        var model = new HandBuiltModel();
        var text = model.Type("String");
        var optionalText = new UnionType([text, model.Type("Void")]);
        var owner = HandBuiltModel.Class("Consumer");
        var method = new Method("Get", text, [], new MemberModifiers { IsStatic = true })
        {
            DeclaringType = owner,
            Scope = new MethodScope(owner.Scope) { IsStatic = true },
        };
        var parameter = new TypeParameter("T", 0, method.Scope, owner) { DeclaringMethod = method };
        method.TypeParametersList.Add(parameter);
        method.ContextBindingsList.Add(new ContextBinding(text, "required"));
        method.ContextBindingsList.Add(new ContextBinding(parameter, "value"));

        Assert.Equal(["typeof(MistContext.Optional<string>)"], ContextKeyWitnesses.Arguments(method, [text, optionalText]));
    }
}