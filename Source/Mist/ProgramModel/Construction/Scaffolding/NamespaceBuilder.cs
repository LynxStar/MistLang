using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Generated;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Construction.Scaffolding;

internal sealed class NamespaceBuilder
{
    private readonly BuilderContext context;

    public NamespaceBuilder(BuilderContext context)
    {
        this.context = context;
    }

    /// <summary>
    /// Returns the namespace the file's TOP-LEVEL items belong to, or null when validation failed
    /// and the scope should not be used.
    /// <para>
    /// The returned namespace is not recorded on the scope. It becomes a
    /// <see cref="NamespaceScope"/> beneath it, and only if the file turns out to have top-level
    /// items at all -- which is <c>TypeBuilder</c>'s to know, since it is the one walking them.
    /// </para>
    /// </summary>
    public string? Build(FileScope fileScope, CompilationUnitNode ast, SourceText sourceText, string namespaceRoot, string projectFolder)
    {
        if(ValidateNamespaceDeclarations(ast))
        {
            return null;
        }

        var fileNamespace = DetermineFileNamespace(ast, sourceText, namespaceRoot, projectFolder);

        RegisterNamespaces(ast, fileNamespace);
        PopulateImports(ast, sourceText, fileScope);

        return fileNamespace;
    }

    private bool ValidateNamespaceDeclarations(CompilationUnitNode ast)
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
                    context.Diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.DuplicateFileScopedNamespace, nsDecl, null);
                    hasErrors = true;
                }
                else if(hasBlockNamespace)
                {
                    context.Diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.MixedNamespaceStyles, nsDecl, null);
                    hasErrors = true;
                }

                hasFileScopedNamespace = true;
                CheckForFileScopedInsideBlock(nsDecl);
            }
            else if(nsDecl is BlockNamespaceNamespaceDeclarationNode)
            {
                if(hasFileScopedNamespace)
                {
                    context.Diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.MixedNamespaceStyles, nsDecl, null);
                    hasErrors = true;
                }

                hasBlockNamespace = true;
                CheckForFileScopedInsideBlock(nsDecl);
            }
        }

        return hasErrors;
    }

    private void CheckForFileScopedInsideBlock(NamespaceDeclarationNode nsDecl)
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
                context.Diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.FileScopedInsideBlock, nestedNsDecl, null);
            }
            else if(nestedNsDecl is BlockNamespaceNamespaceDeclarationNode)
            {
                CheckForFileScopedInsideBlock(nestedNsDecl);
            }
        }
    }

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

    /// <summary>
    /// Registers the namespaces the file DECLARES. The file's own namespace is deliberately not
    /// among them: a file whose declarations all sit inside blocks does not bring its inferred
    /// namespace into existence. Declaring a type registers its namespace, so a file that does have
    /// top-level items still gets one.
    /// </summary>
    private void RegisterNamespaces(CompilationUnitNode ast, string fileNamespace)
    {
        foreach(var item in ast.Items.OfType<NamespaceItemTopLevelItemNode>())
        {
            switch(item.NamespaceDeclaration)
            {
                case BlockNamespaceNamespaceDeclarationNode blockNs:
                    RegisterBlockNamespace(blockNs, "");
                    break;

                case FileScopedNamespaceNamespaceDeclarationNode fileScopedNs:
                    RegisterNestedBlocks(fileScopedNs.Body, fileNamespace);
                    break;
            }
        }
    }

    private void RegisterBlockNamespace(BlockNamespaceNamespaceDeclarationNode blockNs, string parentNamespace)
    {
        var namespaceName = BlockNamespaceName.Build(blockNs, parentNamespace);
        _ = context.ProgramModel.GetOrAddNamespace(namespaceName);
        RegisterNestedBlocks(blockNs.Body, namespaceName);
    }

    private void RegisterNestedBlocks(System.Collections.Immutable.ImmutableArray<NamespaceBodyItemNode> body, string parentNamespace)
    {
        foreach(var item in body)
        {
            if(item is NamespaceNamespaceItemNamespaceBodyItemNode nestedItem
                && nestedItem.NamespaceDeclaration is BlockNamespaceNamespaceDeclarationNode nestedBlock)
            {
                RegisterBlockNamespace(nestedBlock, parentNamespace);
            }
        }
    }

    private void PopulateImports(CompilationUnitNode ast, SourceText sourceText, FileScope fileScope)
    {
        var directives = ast.Usings.Concat(
            ast.Items.OfType<UsingItemTopLevelItemNode>().Select(item => item.UsingDeclaration));

        foreach(var directive in directives)
        {
            var usingName = ExtractQualifiedName(directive.Name, sourceText);
            fileScope.DeclareImport(usingName);
            context.PendingUsings.Add(new PendingUsing(directive, usingName));
        }
    }

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
                context.Diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.UnexpectedNamespaceNode, namespaceNode, null, new NameValue(namespaceNode.GetType().Name));
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