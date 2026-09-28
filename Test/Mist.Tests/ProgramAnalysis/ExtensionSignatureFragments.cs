using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

internal sealed class ExtensionSignatureFragments
{
    private int nextOffset = 20;

    public HandBuiltModel Built { get; } = new();

    public IMistType Integer => Built.Type("Int32");

    public IMistType String => Built.Type("String");

    public ExtensionBlock Block(IMistType? receiver = null, string namespaceName = "Probe", bool source = true)
    {
        var node = HandBuiltModel.Node(nextOffset++);
        var declaration = source ? new ExtensionBlockDeclarationNode { Span = node.Span, Origin = node.Origin } : null;
        return new ExtensionBlock("self", namespaceName, new ExtensionScope(null), declaration)
        {
            ResolvedReceiverType = receiver,
        };
    }

    public static TypeParameter Parameter(ExtensionBlock block, string name)
    {
        var parameter = new TypeParameter(name, block.TypeParametersList.Count, block.Scope, null);
        block.TypeParametersList.Add(parameter);
        return parameter;
    }

    public static TypeParameter Parameter(Method method, string name)
    {
        var parameter = new TypeParameter(name, method.TypeParametersList.Count, method.Scope, null);
        method.TypeParametersList.Add(parameter);
        return parameter;
    }

    public Method Method(ExtensionBlock block, string name, IMistType? result = null, params Parameter[] parameters)
    {
        var method = new Method(name, result ?? Integer, [.. parameters], default)
        {
            DeclarationNode = HandBuiltModel.Node(nextOffset++),
            DeclaringBlock = block,
            Scope = new Scope(block.Scope),
        };
        block.MethodsList.Add(method);
        return method;
    }

    public Property Property(ExtensionBlock block, string name, IMistType? type = null)
    {
        var property = new Property(name, type ?? Integer, default)
        {
            DeclarationNode = HandBuiltModel.Node(nextOffset++),
        };
        block.PropertiesList.Add(property);
        return property;
    }

    public static ConstructedType Construct(IMistType definition, params IMistType[] arguments)
    {
        return new ConstructedType(definition, arguments);
    }
}