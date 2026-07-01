using Mist.Common.Source;
using Mist.Generated;
using Mist.ProgramModel.Model;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Validates namespace declarations, determines the file namespace, registers
/// namespaces in the program model, and populates a file scope with the resolved
/// namespace and imports. One instance is created per compilation and reused
/// across all files.
/// </summary>
internal sealed class NamespaceBuilder
{
    private readonly BuilderContext context;

    public NamespaceBuilder(BuilderContext context)
    {
        this.context = context;
    }

    /// <summary>
    /// Validates, resolves, and populates the given file scope with namespace
    /// information from the AST. Sets the scope's namespace and imports.
    /// Returns false if validation failed (the scope should not be used).
    /// </summary>
    public bool Build(
        Scope fileScope,
        CompilationUnitNode ast,
        SourceText sourceText,
        string namespaceRoot,
        string projectFolder)
    {
        // Step 1: Validate namespace declarations.
        if(ValidateNamespaceDeclarations(ast, sourceText))
        {
            return false;
        }

        // Step 2: Determine the file's namespace.
        var fileNamespace = DetermineFileNamespace(ast, sourceText, namespaceRoot, projectFolder);

        // Step 3: Register namespaces in the program model.
        RegisterNamespaces(ast, sourceText, fileNamespace);

        // Step 4: Set the scope's namespace and populate imports.
        fileScope.SetNamespace(fileNamespace);
        fileScope.AddImportedNamespace(fileNamespace);
        PopulateImports(ast, sourceText, fileScope);

        return true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Namespace Validation
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Validates namespace declarations within a compilation unit:
    /// - MIST0301: duplicate file-scoped namespaces.
    /// - MIST0302: mixed file-scoped and block namespaces.
    /// - MIST0305: file-scoped namespace inside a block namespace.
    /// Returns true if any errors were found.
    /// </summary>
    private bool ValidateNamespaceDeclarations(CompilationUnitNode ast, SourceText sourceText)
    {
        var hasErrors = false;
        var fileScopedCount = 0;
        var hasBlockNamespace = false;
        var hasFileScopedNamespace = false;

        foreach(var item in ast.Items)
        {
            if(item is not NamespaceItemTopLevelItemNode nsItem)
            {
                continue;
            }

            var nsDecl = nsItem.NamespaceDeclaration;

            if(nsDecl is FileScopedNamespaceNamespaceDeclarationNode)
            {
                fileScopedCount++;

                if(fileScopedCount > 1)
                {
                    context.ReportError(
                        "MIST0301",
                        "Only one file-scoped namespace is permitted per file.",
                        nsDecl,
                        sourceText);
                    hasErrors = true;
                }
                else if(hasBlockNamespace)
                {
                    context.ReportError(
                        "MIST0302",
                        "File-scoped and block namespaces cannot be mixed in the same file.",
                        nsDecl,
                        sourceText);
                    hasErrors = true;
                }

                hasFileScopedNamespace = true;
                CheckForFileScopedInsideBlock(nsDecl, sourceText);
            }
            else if(nsDecl is BlockNamespaceNamespaceDeclarationNode)
            {
                if(hasFileScopedNamespace)
                {
                    context.ReportError(
                        "MIST0302",
                        "File-scoped and block namespaces cannot be mixed in the same file.",
                        nsDecl,
                        sourceText);
                    hasErrors = true;
                }

                hasBlockNamespace = true;
                CheckForFileScopedInsideBlock(nsDecl, sourceText);
            }
        }

        return hasErrors;
    }

    /// <summary>
    /// Recursively checks namespace bodies for file-scoped namespace declarations
    /// inside block namespaces (MIST0305).
    /// </summary>
    private void CheckForFileScopedInsideBlock(NamespaceDeclarationNode nsDecl, SourceText sourceText)
    {
        var bodyItems = GetNamespaceBody(nsDecl);

        foreach(var bodyItem in bodyItems)
        {
            if(bodyItem is not NamespaceNamespaceItemNamespaceBodyItemNode nestedNsItem)
            {
                continue;
            }

            var nestedNsDecl = nestedNsItem.NamespaceDeclaration;

            if(nestedNsDecl is FileScopedNamespaceNamespaceDeclarationNode)
            {
                context.ReportError(
                    "MIST0305",
                    "File-scoped namespace declarations cannot appear inside a block namespace.",
                    nestedNsDecl,
                    sourceText);
            }
            else if(nestedNsDecl is BlockNamespaceNamespaceDeclarationNode)
            {
                CheckForFileScopedInsideBlock(nestedNsDecl, sourceText);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Namespace Determination
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Determines the file's effective namespace. If the file declares a file-scoped
    /// namespace, that is used. Otherwise, the namespace is inferred from the file path.
    /// </summary>
    private string DetermineFileNamespace(
        CompilationUnitNode ast,
        SourceText sourceText,
        string namespaceRoot,
        string projectFolder)
    {
        foreach(var item in ast.Items)
        {
            if(item is NamespaceItemTopLevelItemNode nsItem
                && nsItem.NamespaceDeclaration is FileScopedNamespaceNamespaceDeclarationNode)
            {
                return ExtractNamespaceName(nsItem.NamespaceDeclaration, sourceText);
            }
        }

        return ComputeInferredNamespace(sourceText, namespaceRoot, projectFolder);
    }

    /// <summary>
    /// Computes the inferred namespace for a source file based on its location
    /// relative to the project folder.
    /// </summary>
    private static string ComputeInferredNamespace(SourceText sourceText, string namespaceRoot, string projectFolder)
    {
        var filePath = sourceText.Path;
        if(string.IsNullOrEmpty(filePath))
        {
            return namespaceRoot;
        }

        var fileDir = Path.GetDirectoryName(filePath);
        if(string.IsNullOrEmpty(fileDir))
        {
            return namespaceRoot;
        }

        var relativePath = Path.GetRelativePath(projectFolder, fileDir);
        if(relativePath == "." || string.IsNullOrEmpty(relativePath))
        {
            return namespaceRoot;
        }

        var segments = relativePath
            .Replace(Path.DirectorySeparatorChar, '.')
            .Replace(Path.AltDirectorySeparatorChar, '.');
        return $"{namespaceRoot}.{segments}";
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Namespace Registration
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Registers namespaces found in the compilation unit into the program model.
    /// </summary>
    private void RegisterNamespaces(CompilationUnitNode ast, SourceText sourceText, string fileNamespace)
    {
        _ = context.ProgramModel.GetOrAddNamespace(fileNamespace);

        foreach(var item in ast.Items)
        {
            if(item is NamespaceItemTopLevelItemNode nsItem
                && nsItem.NamespaceDeclaration is BlockNamespaceNamespaceDeclarationNode)
            {
                var blockNsName = ExtractNamespaceName(nsItem.NamespaceDeclaration, sourceText);
                _ = context.ProgramModel.GetOrAddNamespace(blockNsName);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Import Population
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Populates the file scope with using declarations from the AST.
    /// </summary>
    private static void PopulateImports(CompilationUnitNode ast, SourceText sourceText, Scope fileScope)
    {
        // Process using declarations before the namespace.
        foreach(var usingDecl in ast.Usings)
        {
            var usingName = ExtractQualifiedName(usingDecl.Name, sourceText);
            fileScope.AddImportedNamespace(usingName);
        }

        // Process using declarations after a file-scoped namespace.
        foreach(var item in ast.Items)
        {
            if(item is UsingItemTopLevelItemNode usingItem)
            {
                var usingName = ExtractQualifiedName(usingItem.UsingDeclaration.Name, sourceText);
                fileScope.AddImportedNamespace(usingName);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    private string ExtractNamespaceName(NamespaceDeclarationNode namespaceNode, SourceText sourceText)
    {
        QualifiedIdentifierNode? qualifiedId;

        switch(namespaceNode)
        {
            case FileScopedNamespaceNamespaceDeclarationNode fileScopedNs:
                qualifiedId = fileScopedNs.Name;
                break;
            case BlockNamespaceNamespaceDeclarationNode blockNs:
                qualifiedId = blockNs.Name;
                break;
            default:
                context.ReportError(
                    "MIST_INTERNAL_UNEXPECTED_NS_NODE",
                    $"Internal compiler error: unexpected namespace declaration node type '{namespaceNode.GetType().Name}'.",
                    namespaceNode,
                    sourceText);
                return "<error>";
        }

        var span = qualifiedId.Span;
        if(span.Length > 0 && span.Start + span.Length <= sourceText.Contents.Length)
        {
            var extracted = sourceText.Contents.Substring(span.Start, span.Length);
            if(IsValidQualifiedIdentifier(extracted))
            {
                return extracted;
            }
        }

        return qualifiedId.IDENTIFIER.Lexeme;
    }

    private static string ExtractQualifiedName(QualifiedIdentifierNode qualifiedId, SourceText sourceText)
    {
        var span = qualifiedId.Span;
        if(span.Length > 0 && span.Start + span.Length <= sourceText.Contents.Length)
        {
            var extracted = sourceText.Contents.Substring(span.Start, span.Length);
            if(IsValidQualifiedIdentifier(extracted))
            {
                return extracted;
            }
        }

        // Fallback: reconstruct the dotted name from the AST tokens.
        if(qualifiedId.IDENTIFIERs.IsDefaultOrEmpty)
        {
            return qualifiedId.IDENTIFIER.Lexeme;
        }

        var sb = new System.Text.StringBuilder(qualifiedId.IDENTIFIER.Lexeme);
        foreach(var token in qualifiedId.IDENTIFIERs)
        {
            if(!string.IsNullOrEmpty(token.Lexeme))
            {
                _ = sb.Append('.');
                _ = sb.Append(token.Lexeme);
            }
        }

        return sb.ToString();
    }

    private static bool IsValidQualifiedIdentifier(string text)
    {
        if(string.IsNullOrEmpty(text))
        {
            return false;
        }

        foreach(var ch in text)
        {
            if(!char.IsLetterOrDigit(ch) && ch != '_' && ch != '.')
            {
                return false;
            }
        }

        return true;
    }

    private static System.Collections.Immutable.ImmutableArray<NamespaceBodyItemNode> GetNamespaceBody(
        NamespaceDeclarationNode nsDecl)
    {
        return nsDecl switch
        {
            FileScopedNamespaceNamespaceDeclarationNode fileScopedNs => fileScopedNs.Body,
            BlockNamespaceNamespaceDeclarationNode blockNs => blockNs.Body,
            _ => System.Collections.Immutable.ImmutableArray<NamespaceBodyItemNode>.Empty
        };
    }
}