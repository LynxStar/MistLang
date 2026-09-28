using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.ProgramAnalysis.Rules;

internal static class ExtensionSignatureDecision
{
    public static IEnumerable<ExtensionSignatureRepeat> Repeats(IEnumerable<ExtensionBlock> blocks)
    {
        var methods = new Dictionary<(string Namespace, string Receiver, string Name, string Signature), SyntaxNode>();
        var properties = new Dictionary<(string Namespace, string Receiver, string Name), SyntaxNode>();

        foreach(var block in blocks)
        {
            if(block.DeclarationNode is null || OverloadSignature.ExtensionReceiver(block) is not { } receiver)
            {
                continue;
            }

            var receiverName = TypeDisplay.Mist(block.ResolvedReceiverType!);

            foreach(var method in block.MethodsList)
            {
                if(method.DeclarationNode is not { } later || OverloadSignature.ExtensionMethod(block, method) is not { } signature)
                {
                    continue;
                }

                var key = (block.NamespaceName, receiver, method.Name, signature);
                if(methods.TryGetValue(key, out var earlier))
                {
                    var typeParameters = method.TypeParametersList.Count == 0
                        ? ""
                        : $"<{string.Join(", ", method.TypeParametersList.Select(parameter => parameter.Name))}>";
                    var display = $"{typeParameters}{OverloadFailure.DescribeSignatures([method])}";
                    yield return new ExtensionSignatureRepeat(later, earlier, method.Name, receiverName, display, IsProperty: false);
                }
                else
                {
                    methods.Add(key, later);
                }
            }

            foreach(var property in block.PropertiesList)
            {
                if(property.DeclarationNode is not { } later)
                {
                    continue;
                }

                var key = (block.NamespaceName, receiver, property.Name);
                if(properties.TryGetValue(key, out var earlier))
                {
                    yield return new ExtensionSignatureRepeat(later, earlier, property.Name, receiverName, property.Name, IsProperty: true);
                }
                else
                {
                    properties.Add(key, later);
                }
            }
        }
    }
}

internal sealed record ExtensionSignatureRepeat(
    SyntaxNode Later,
    SyntaxNode Earlier,
    string Name,
    string Receiver,
    string Signature,
    bool IsProperty);