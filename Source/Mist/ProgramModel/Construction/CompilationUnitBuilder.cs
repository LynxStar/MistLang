using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Generated;
using Mist.ProgramModel.Model;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Processes a single compilation unit (file) AST to extract namespace declarations,
/// validate them, and build the file-level scope.
/// </summary>
internal sealed class CompilationUnitBuilder
{
    private readonly Scope globalScope;
    private readonly Model.ProgramModel programModel;
    private readonly DiagnosticBag diagnostics;
    private readonly SourceText? sourceText;
    private readonly string namespaceRoot;
    private readonly string projectFolder;

    public CompilationUnitBuilder(
        Scope globalScope,
        Model.ProgramModel programModel,
        DiagnosticBag diagnostics,
        SourceText? sourceText,
        string namespaceRoot,
        string projectFolder)
    {
        this.globalScope = globalScope;
        this.programModel = programModel;
        this.diagnostics = diagnostics;
        this.sourceText = sourceText;
        this.namespaceRoot = namespaceRoot;
        this.projectFolder = projectFolder;
    }

    /// <summary>
    /// Processes the compilation unit: validates namespace declarations, determines
    /// the file namespace, registers namespaces, and builds the file-level scope.
    /// Returns the file scope, or null if namespace validation produced fatal errors.
    /// </summary>
    public Scope? Build(CompilationUnitNode compilationUnit)
    {
        // Step 1: Validate namespace declarations (MIST0301, MIST0302, MIST0305).
        var hasErrors = ValidateNamespaceDeclarations(compilationUnit);

        if(hasErrors)
        {
            return null;
        }

        // Step 2: Determine the file's namespace.
        var fileNamespace = DetermineFileNamespace(compilationUnit);

        // Step 3: Register namespace(s) in the program model.
        RegisterNamespaces(compilationUnit, fileNamespace);

        // Step 4: Build the file scope (parent = global scope, with imports).
        var fileScope = BuildFileScope(compilationUnit);

        return fileScope;
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
    private bool ValidateNamespaceDeclarations(CompilationUnitNode compilationUnit)
    {
        var hasErrors = false;
        var fileScopedCount = 0;
        var hasBlockNamespace = false;
        var hasFileScopedNamespace = false;

        foreach(var item in compilationUnit.Items)
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
                    diagnostics.Add(MakeDiagnostic(
                        "MIST0301",
                        "Only one file-scoped namespace is permitted per file.",
                        nsDecl));
                    hasErrors = true;
                }
                else if(hasBlockNamespace)
                {
                    diagnostics.Add(MakeDiagnostic(
                        "MIST0302",
                        "File-scoped and block namespaces cannot be mixed in the same file.",
                        nsDecl));
                    hasErrors = true;
                }

                hasFileScopedNamespace = true;
                CheckForFileScopedInsideBlock(nsDecl);
            }
            else if(nsDecl is BlockNamespaceNamespaceDeclarationNode)
            {
                if(hasFileScopedNamespace)
                {
                    diagnostics.Add(MakeDiagnostic(
                        "MIST0302",
                        "File-scoped and block namespaces cannot be mixed in the same file.",
                        nsDecl));
                    hasErrors = true;
                }

                hasBlockNamespace = true;
                CheckForFileScopedInsideBlock(nsDecl);
            }
        }

        return hasErrors;
    }

    /// <summary>
    /// Recursively checks namespace bodies for file-scoped namespace declarations
    /// inside block namespaces (MIST0305).
    /// </summary>
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
                diagnostics.Add(MakeDiagnostic(
                    "MIST0305",
                    "File-scoped namespace declarations cannot appear inside a block namespace.",
                    nestedNsDecl));
            }
            else if(nestedNsDecl is BlockNamespaceNamespaceDeclarationNode)
            {
                CheckForFileScopedInsideBlock(nestedNsDecl);
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
    private string DetermineFileNamespace(CompilationUnitNode compilationUnit)
    {
        foreach(var item in compilationUnit.Items)
        {
            if(item is NamespaceItemTopLevelItemNode nsItem
                && nsItem.NamespaceDeclaration is FileScopedNamespaceNamespaceDeclarationNode)
            {
                return ExtractNamespaceName(nsItem.NamespaceDeclaration);
            }
        }

        return ComputeInferredNamespace();
    }

    /// <summary>
    /// Computes the inferred namespace for a source file based on its location
    /// relative to the project folder.
    /// </summary>
    private string ComputeInferredNamespace()
    {
        var filePath = sourceText?.Path ?? "";
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
    private void RegisterNamespaces(CompilationUnitNode compilationUnit, string fileNamespace)
    {
        // Always register the file's namespace.
        _ = programModel.GetOrAddNamespace(fileNamespace);

        // Register any block namespaces declared in the file.
        foreach(var item in compilationUnit.Items)
        {
            if(item is NamespaceItemTopLevelItemNode nsItem
                && nsItem.NamespaceDeclaration is BlockNamespaceNamespaceDeclarationNode)
            {
                var blockNsName = ExtractNamespaceName(nsItem.NamespaceDeclaration);
                _ = programModel.GetOrAddNamespace(blockNsName);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // File Scope Construction
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Builds the file-level scope. The scope's parent is the global scope.
    /// It contains the file's own namespace and all using + global using imports.
    /// </summary>
    private Scope BuildFileScope(CompilationUnitNode compilationUnit)
    {
        var fileScope = new Scope(parent: globalScope);

        // Add the file's own namespace as implicitly imported.
        var ownNamespace = DetermineFileNamespace(compilationUnit);
        fileScope.AddImportedNamespace(ownNamespace);

        // Process using declarations.
        foreach(var usingDecl in compilationUnit.Usings)
        {
            var usingName = ExtractQualifiedName(usingDecl.Name);
            fileScope.AddImportedNamespace(usingName);
        }

        // Process using declarations that appear after a file-scoped namespace
        // (parsed as UsingItem top-level items).
        foreach(var item in compilationUnit.Items)
        {
            if(item is UsingItemTopLevelItemNode usingItem)
            {
                var usingName = ExtractQualifiedName(usingItem.UsingDeclaration.Name);
                fileScope.AddImportedNamespace(usingName);
            }
        }

        return fileScope;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    private string ExtractNamespaceName(NamespaceDeclarationNode namespaceNode)
    {
        var qualifiedId = namespaceNode switch
        {
            FileScopedNamespaceNamespaceDeclarationNode fileScopedNs => fileScopedNs.Name,
            BlockNamespaceNamespaceDeclarationNode blockNs => blockNs.Name,
            _ => null
        };

        if(qualifiedId is null)
        {
            return "<unknown>";
        }

        if(sourceText is not null)
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
        }

        return qualifiedId.IDENTIFIER.Lexeme;
    }

    private string ExtractQualifiedName(QualifiedIdentifierNode qualifiedId)
    {
        if(sourceText is not null)
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

    private Diagnostic MakeDiagnostic(string id, string message, Storm.Runtime.SyntaxNode node)
    {
        var location = default(LinePositionSpan);
        if(sourceText is not null)
        {
            var span = new SourceSpan(node.Span.Start, node.Span.Length);
            location = sourceText.ToLinePositionSpan(span);
        }

        return new Diagnostic(
            new DiagnosticId(id),
            DiagnosticSeverity.Error,
            PipelineStage.SemanticModelConstruction,
            message,
            Location: location,
            CompilationUnitPath: sourceText?.Path ?? "<unknown>",
            AnalyzerName: null);
    }
}