using Mist.Generated;
using Mist.Semantics.Contracts;

namespace Mist.Semantics.Model;

/// <summary>
/// Extracts and compares parameter signatures for overload resolution.
/// A parameter signature is the ordered list of parameter types,
/// excluding return type and parameter names.
/// </summary>
public static class ParameterSignature
{
    /// <summary>
    /// Extracts the parameter types from a method or constructor's declaring AST node.
    /// Returns an empty list for parameterless methods/constructors.
    /// </summary>
    public static IReadOnlyList<IType> Extract(ISymbol symbol, ISemanticModel model)
    {
        Storm.Runtime.SyntaxNode declaringNode;
        try
        {
            declaringNode = model.GetDeclaringNode(symbol);
        }
        catch
        {
            return Array.Empty<IType>();
        }

        var paramList = GetParameterList(declaringNode);
        if (paramList is null)
            return Array.Empty<IType>();

        var types = new List<IType>();
        foreach (var child in paramList.Children())
        {
            if (child is ParameterNode paramNode)
            {
                var paramSymbol = model.GetDeclaredSymbol(paramNode);
                if (paramSymbol?.DeclaredType is { } paramType)
                {
                    types.Add(paramType);
                }
            }
        }

        return types;
    }

    /// <summary>
    /// Extracts the parameter types from a method or constructor's declaring AST node
    /// using a <see cref="SymbolTable"/> directly. This overload is used during semantic
    /// model construction (before the <see cref="ISemanticModel"/> is built).
    /// Returns an empty list for parameterless methods/constructors.
    /// </summary>
    public static IReadOnlyList<IType> Extract(ISymbol symbol, SymbolTable symbolTable)
    {
        var declaringNode = symbolTable.GetDeclaringNode(symbol);
        if (declaringNode is null)
            return Array.Empty<IType>();

        var paramList = GetParameterList(declaringNode);
        if (paramList is null)
            return Array.Empty<IType>();

        var types = new List<IType>();
        foreach (var child in paramList.Children())
        {
            if (child is ParameterNode paramNode)
            {
                var paramSymbol = symbolTable.GetDeclaredSymbol(paramNode);
                if (paramSymbol?.DeclaredType is { } paramType)
                {
                    types.Add(paramType);
                }
            }
        }

        return types;
    }

    /// <summary>
    /// Returns true if two parameter signatures are identical
    /// (same count, same types in order by DisplayName).
    /// </summary>
    public static bool AreEqual(IReadOnlyList<IType> a, IReadOnlyList<IType> b)
    {
        if (a.Count != b.Count)
            return false;

        for (var i = 0; i < a.Count; i++)
        {
            if (a[i].DisplayName != b[i].DisplayName)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Extracts the ParameterListNode from a declaring AST node,
    /// handling MethodDeclarationNode, ConstructorDeclarationNode,
    /// InterfaceMethodDeclarationNode, OperatorDeclarationNode, and CastOperatorDeclarationNode.
    /// </summary>
    private static ParameterListNode? GetParameterList(Storm.Runtime.SyntaxNode declaringNode)
    {
        return declaringNode switch
        {
            MethodDeclarationNode method => method.Parameters,
            ConstructorDeclarationNode ctor => ctor.Parameters,
            InterfaceMethodDeclarationNode interfaceMethod => interfaceMethod.Parameters,
            OperatorDeclarationNode op => op.Parameters,
            CastOperatorDeclarationNode castOp => castOp.Parameters,
            _ => null,
        };
    }

    /// <summary>
    /// Extracts the readonly flags for each parameter of a method or constructor.
    /// Returns a list of booleans parallel to the parameter list, where <c>true</c>
    /// indicates the parameter has the <c>readonly</c> modifier.
    /// Returns an empty list for parameterless methods/constructors.
    /// </summary>
    public static IReadOnlyList<bool> ExtractIsReadonly(ISymbol symbol, ISemanticModel model)
    {
        Storm.Runtime.SyntaxNode declaringNode;
        try
        {
            declaringNode = model.GetDeclaringNode(symbol);
        }
        catch
        {
            return Array.Empty<bool>();
        }

        return ExtractIsReadonlyFromNode(declaringNode);
    }

    /// <summary>
    /// Extracts the readonly flags for each parameter of a method or constructor
    /// using a <see cref="SymbolTable"/> directly. This overload is used during semantic
    /// model construction (before the <see cref="ISemanticModel"/> is built).
    /// Returns an empty list for parameterless methods/constructors.
    /// </summary>
    public static IReadOnlyList<bool> ExtractIsReadonly(ISymbol symbol, SymbolTable symbolTable)
    {
        var declaringNode = symbolTable.GetDeclaringNode(symbol);
        if (declaringNode is null)
            return Array.Empty<bool>();

        return ExtractIsReadonlyFromNode(declaringNode);
    }

    /// <summary>
    /// Extracts readonly flags from a declaring node's parameter list.
    /// </summary>
    private static IReadOnlyList<bool> ExtractIsReadonlyFromNode(Storm.Runtime.SyntaxNode declaringNode)
    {
        var paramList = GetParameterList(declaringNode);
        if (paramList is null)
            return Array.Empty<bool>();

        var flags = new List<bool>();
        foreach (var child in paramList.Children())
        {
            if (child is ParameterNode paramNode)
            {
                flags.Add(paramNode.Modifier is not null || paramNode.OwnershipSpecifier is ReadonlyRefSpecifierOwnershipSpecifierNode);
            }
        }

        return flags;
    }
}
