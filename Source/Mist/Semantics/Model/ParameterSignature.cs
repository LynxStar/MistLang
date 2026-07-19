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
    /// For CLR Backend symbols (which have no AST node), reads parameter types from
    /// the <see cref="Symbol.Parameters"/> collection instead.
    /// Returns an empty list for parameterless methods/constructors.
    /// </summary>
    public static IReadOnlyList<IType> Extract(ISymbol symbol, ISemanticModel model)
    {
        // CLR Backend symbols store parameters directly on the Symbol instance
        // because they have no AST declaring node.
        if(symbol is Symbol { Parameters.Count: > 0 } backendSym)
        {
            var backendTypes = new List<IType>(backendSym.Parameters.Count);
            foreach(var param in backendSym.Parameters)
            {
                if(param.DeclaredType is { } paramType)
                {
                    backendTypes.Add(paramType);
                }
            }

            return backendTypes;
        }

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
        if(paramList is null)
        {
            return Array.Empty<IType>();
        }

        var types = new List<IType>();
        foreach(var child in paramList.Children())
        {
            if(child is ParameterNode paramNode)
            {
                var paramSymbol = model.GetDeclaredSymbol(paramNode);
                if(paramSymbol?.DeclaredType is { } paramType)
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
        // CLR Backend symbols (including ambient extensions) store parameters
        // directly on the Symbol instance because they have no AST declaring node.
        if(symbol is Symbol { Parameters.Count: > 0 } backendSym
            && symbolTable.GetDeclaringNode(symbol) is null)
        {
            var backendTypes = new List<IType>(backendSym.Parameters.Count);
            foreach(var param in backendSym.Parameters)
            {
                if(param.DeclaredType is { } paramType)
                {
                    backendTypes.Add(paramType);
                }
            }

            return backendTypes;
        }

        var declaringNode = symbolTable.GetDeclaringNode(symbol);
        if(declaringNode is null)
        {
            return Array.Empty<IType>();
        }

        var paramList = GetParameterList(declaringNode);
        if(paramList is null)
        {
            return Array.Empty<IType>();
        }

        var types = new List<IType>();
        foreach(var child in paramList.Children())
        {
            if(child is ParameterNode paramNode)
            {
                var paramSymbol = symbolTable.GetDeclaredSymbol(paramNode);
                if(paramSymbol?.DeclaredType is { } paramType)
                {
                    types.Add(paramType);
                }
            }
        }

        // Fallback: if the AST-based path returned empty but the symbol has
        // pre-resolved parameters (from the signature pre-pass), use those.
        if(types.Count == 0 && symbol is Symbol { Parameters.Count: > 0 } preResolvedSym)
        {
            var fallbackTypes = new List<IType>(preResolvedSym.Parameters.Count);
            foreach(var param in preResolvedSym.Parameters)
            {
                if(param.DeclaredType is { } paramType)
                {
                    fallbackTypes.Add(paramType);
                }
            }

            return fallbackTypes;
        }

        return types;
    }

    /// <summary>
    /// Extracts the RefKind values for each parameter of a method or constructor.
    /// Returns a list parallel to the parameter list indicating the ref kind of each parameter.
    /// Uses the <see cref="ISemanticModel"/> to look up parameter symbols.
    /// For CLR Backend symbols (which have no AST node), reads RefKind from
    /// the <see cref="Symbol.Parameters"/> collection directly.
    /// </summary>
    public static IReadOnlyList<RefKind> ExtractRefKinds(ISymbol symbol, ISemanticModel model)
    {
        // CLR Backend symbols store parameters directly on the Symbol instance
        // because they have no AST declaring node.
        if(symbol is Symbol { Parameters.Count: > 0 } backendSym)
        {
            // Check if this is a backend symbol by attempting to get its declaring node.
            // Backend symbols will throw or return a node that doesn't have a parameter list.
            Storm.Runtime.SyntaxNode? declaringNode = null;
            try
            {
                declaringNode = model.GetDeclaringNode(symbol);
            }
            catch
            {
                // No declaring node — this is a backend symbol.
            }

            if(declaringNode is null || GetParameterList(declaringNode) is null)
            {
                // Backend symbol: read RefKind directly from parameter symbols.
                var backendRefKinds = new List<RefKind>(backendSym.Parameters.Count);
                foreach(var param in backendSym.Parameters)
                {
                    backendRefKinds.Add(param is Symbol paramSym ? paramSym.RefKind : RefKind.None);
                }

                return backendRefKinds;
            }
        }

        Storm.Runtime.SyntaxNode node;
        try
        {
            node = model.GetDeclaringNode(symbol);
        }
        catch
        {
            return Array.Empty<RefKind>();
        }

        var paramList = GetParameterList(node);
        if(paramList is null)
        {
            return Array.Empty<RefKind>();
        }

        var refKinds = new List<RefKind>();
        foreach(var child in paramList.Children())
        {
            if(child is ParameterNode paramNode)
            {
                var paramSymbol = model.GetDeclaredSymbol(paramNode);
                if(paramSymbol is Symbol sym)
                {
                    refKinds.Add(sym.RefKind);
                }
                else
                {
                    refKinds.Add(RefKind.None);
                }
            }
        }

        return refKinds;
    }

    /// <summary>
    /// Extracts the RefKind values for each parameter of a method or constructor
    /// using a <see cref="SymbolTable"/> directly. This overload is used during semantic
    /// model construction (before the <see cref="ISemanticModel"/> is built).
    /// </summary>
    public static IReadOnlyList<RefKind> ExtractRefKinds(ISymbol symbol, SymbolTable symbolTable)
    {
        // CLR Backend symbols (including ambient extensions) store parameters
        // directly on the Symbol instance because they have no AST declaring node.
        if(symbol is Symbol { Parameters.Count: > 0 } backendSym
            && symbolTable.GetDeclaringNode(symbol) is null)
        {
            var backendRefKinds = new List<RefKind>(backendSym.Parameters.Count);
            foreach(var param in backendSym.Parameters)
            {
                backendRefKinds.Add(param is Symbol paramSym ? paramSym.RefKind : RefKind.None);
            }

            return backendRefKinds;
        }

        var declaringNode = symbolTable.GetDeclaringNode(symbol);
        if(declaringNode is null)
        {
            return Array.Empty<RefKind>();
        }

        var paramList = GetParameterList(declaringNode);
        if(paramList is null)
        {
            return Array.Empty<RefKind>();
        }

        var refKinds = new List<RefKind>();
        foreach(var child in paramList.Children())
        {
            if(child is ParameterNode paramNode)
            {
                var paramSymbol = symbolTable.GetDeclaredSymbol(paramNode);
                if(paramSymbol is Symbol sym)
                {
                    refKinds.Add(sym.RefKind);
                }
                else
                {
                    refKinds.Add(RefKind.None);
                }
            }
        }

        return refKinds;
    }

    /// <summary>
    /// Returns true if two parameter signatures are identical
    /// (same count, same types in order by DisplayName, and same RefKind values).
    /// Two methods with the same parameter types but different RefKind values
    /// on one or more parameters are distinct overloads (Requirement 7.4).
    /// </summary>
    public static bool AreEqual(IReadOnlyList<IType> a, IReadOnlyList<IType> b)
    {
        if(a.Count != b.Count)
        {
            return false;
        }

        for(var i = 0; i < a.Count; i++)
        {
            if(a[i].DisplayName != b[i].DisplayName)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns true if two parameter signatures are identical considering both
    /// types and ref kinds. Two methods with the same parameter types but different
    /// RefKind values on one or more parameters are distinct overloads (Requirement 7.4).
    /// </summary>
    public static bool AreEqualWithRefKind(
        IReadOnlyList<IType> aTypes, IReadOnlyList<RefKind> aRefKinds,
        IReadOnlyList<IType> bTypes, IReadOnlyList<RefKind> bRefKinds)
    {
        if(aTypes.Count != bTypes.Count)
        {
            return false;
        }

        for(var i = 0; i < aTypes.Count; i++)
        {
            if(aTypes[i].DisplayName != bTypes[i].DisplayName)
            {
                return false;
            }

            if(aRefKinds.Count > i && bRefKinds.Count > i)
            {
                if(aRefKinds[i] != bRefKinds[i])
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Extracts the ParameterListNode from a declaring AST node,
    /// handling MethodDeclarationNode, ConstructorDeclarationNode,
    /// OperatorDeclarationNode, and CastOperatorDeclarationNode.
    /// </summary>
    internal static ParameterListNode? GetParameterList(Storm.Runtime.SyntaxNode declaringNode)
    {
        return declaringNode switch
        {
            MethodDeclarationNode method => method.Parameters,
            ConstructorDeclarationNode ctor => ctor.Parameters,
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
        if(declaringNode is null)
        {
            return Array.Empty<bool>();
        }

        return ExtractIsReadonlyFromNode(declaringNode);
    }

    /// <summary>
    /// Extracts readonly flags from a declaring node's parameter list.
    /// </summary>
    private static IReadOnlyList<bool> ExtractIsReadonlyFromNode(Storm.Runtime.SyntaxNode declaringNode)
    {
        var paramList = GetParameterList(declaringNode);
        if(paramList is null)
        {
            return Array.Empty<bool>();
        }

        var flags = new List<bool>();
        foreach(var child in paramList.Children())
        {
            if(child is ParameterNode paramNode)
            {
                flags.Add(paramNode.Modifier is not null || paramNode.OwnershipSpecifier is ReadonlyRefSpecifierOwnershipSpecifierNode);
            }
        }

        return flags;
    }
}