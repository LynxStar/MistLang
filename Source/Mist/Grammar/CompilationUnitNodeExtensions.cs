// Backward-compatible extension methods for CompilationUnitNode.
// The grammar changed from:
//   CompilationUnit = usings:UsingDeclaration* namespace:NamespaceDeclaration? types:TypeDeclaration*
// to:
//   CompilationUnit = usings:UsingDeclaration* items:TopLevelItem*
//
// These extensions provide the old Namespace/Types accessors and factory methods
// so existing code can work with minimal changes.

using System.Collections.Immutable;

namespace Mist.Generated;

/// <summary>
/// Extension methods on CompilationUnitNode for backward compatibility with the old grammar structure.
/// </summary>
public static class CompilationUnitNodeExtensions
{
    /// <summary>
    /// Gets the first namespace declaration from the Items list, or null if none exists.
    /// Equivalent to the old <c>cu.Namespace</c> property.
    /// </summary>
    public static NamespaceDeclarationNode? GetNamespace(this CompilationUnitNode cu)
    {
        foreach (var item in cu.Items)
        {
            if (item is NamespaceItemTopLevelItemNode nsItem)
                return nsItem.NamespaceDeclaration;
        }
        return null;
    }

    /// <summary>
    /// Gets all top-level type declarations from the Items list.
    /// Equivalent to the old <c>cu.Types</c> property.
    /// </summary>
    public static ImmutableArray<TypeDeclarationNode> GetTypes(this CompilationUnitNode cu)
    {
        var builder = ImmutableArray.CreateBuilder<TypeDeclarationNode>();
        foreach (var item in cu.Items)
        {
            if (item is TypeItemTopLevelItemNode typeItem)
                builder.Add(typeItem.TypeDeclaration);
        }
        return builder.ToImmutable();
    }

    /// <summary>
    /// Wraps a single TypeDeclarationNode into a TopLevelItemNode.
    /// </summary>
    public static TopLevelItemNode AsTopLevelItem(this TypeDeclarationNode typeDecl)
    {
        return new TypeItemTopLevelItemNode { TypeDeclaration = typeDecl };
    }

    /// <summary>
    /// Wraps a NamespaceDeclarationNode into a TopLevelItemNode.
    /// </summary>
    public static TopLevelItemNode AsTopLevelItem(this NamespaceDeclarationNode nsDecl)
    {
        return new NamespaceItemTopLevelItemNode { NamespaceDeclaration = nsDecl };
    }

    /// <summary>
    /// Converts an array of TypeDeclarationNode into an array of TopLevelItemNode.
    /// </summary>
    public static ImmutableArray<TopLevelItemNode> AsTopLevelItems(this ImmutableArray<TypeDeclarationNode> types)
    {
        var builder = ImmutableArray.CreateBuilder<TopLevelItemNode>(types.Length);
        foreach (var t in types)
            builder.Add(t.AsTopLevelItem());
        return builder.MoveToImmutable();
    }

    /// <summary>
    /// Creates a CompilationUnitNode with the old-style parameters for backward compatibility.
    /// </summary>
    public static CompilationUnitNode CreateCompilationUnit(
        ImmutableArray<UsingDeclarationNode>? usings = null,
        NamespaceDeclarationNode? ns = null,
        ImmutableArray<TypeDeclarationNode>? types = null)
    {
        var items = ImmutableArray.CreateBuilder<TopLevelItemNode>();

        if (ns is not null)
            items.Add(ns.AsTopLevelItem());

        if (types is not null)
        {
            foreach (var t in types.Value)
                items.Add(t.AsTopLevelItem());
        }

        return new CompilationUnitNode
        {
            Usings = usings ?? ImmutableArray<UsingDeclarationNode>.Empty,
            Items = items.ToImmutable(),
        };
    }
}
