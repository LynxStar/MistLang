using System;
using System.Collections.Generic;
using System.IO;
using Storm.SourceGenerator.Diagnostics;
using Storm.SourceGenerator.Frontend;

namespace Storm.SourceGenerator.Resolve
{
    /// <summary>
    /// Result of resolving a root Storm grammar file and all its transitive imports.
    /// </summary>
    public sealed class ImportResolutionResult
    {
        /// <summary>
        /// All parsed Storm files keyed by their canonical (absolute) path,
        /// in the order they were first visited (DFS pre-order).
        /// </summary>
        public IReadOnlyDictionary<string, StormFile> FilesByPath { get; }

        /// <summary>
        /// The canonical path of the root file.
        /// </summary>
        public string RootPath { get; }

        /// <summary>
        /// Diagnostics produced during resolution (e.g. unresolved imports).
        /// </summary>
        public IReadOnlyList<StormDiagnostic> Diagnostics { get; }

        /// <summary>
        /// Whether resolution completed without errors.
        /// </summary>
        public bool Succeeded { get; }

        /// <summary>
        /// Ordered list of (canonical path, StormFile) pairs in DFS pre-order of first visit.
        /// </summary>
        public IReadOnlyList<KeyValuePair<string, StormFile>> OrderedFiles { get; }

        public ImportResolutionResult(
            string rootPath,
            IReadOnlyDictionary<string, StormFile> filesByPath,
            IReadOnlyList<KeyValuePair<string, StormFile>> orderedFiles,
            IReadOnlyList<StormDiagnostic> diagnostics,
            bool succeeded)
        {
            RootPath = rootPath;
            FilesByPath = filesByPath;
            OrderedFiles = orderedFiles;
            Diagnostics = diagnostics;
            Succeeded = succeeded;
        }
    }

    /// <summary>
    /// Resolves Storm grammar imports starting from a root file using DFS with cycle detection.
    /// Each import path is resolved once per build regardless of how many times the same file
    /// is imported (R6.7 — cycles are harmless).
    /// </summary>
    public sealed class StormImportResolver
    {
        private readonly Func<string, string?> _readFile;
        private readonly Dictionary<string, StormFile> _filesByPath;
        private readonly List<KeyValuePair<string, StormFile>> _orderedFiles;
        private readonly HashSet<string> _visitedPaths;
        private readonly List<StormDiagnostic> _diagnostics;
        private bool _failed;

        /// <summary>
        /// Creates a new import resolver.
        /// </summary>
        /// <param name="readFile">
        /// A delegate that reads a file given its canonical path.
        /// Returns the file contents, or null if the file cannot be read.
        /// </param>
        public StormImportResolver(Func<string, string?> readFile)
        {
            _readFile = readFile ?? throw new ArgumentNullException(nameof(readFile));
            _filesByPath = new Dictionary<string, StormFile>(StringComparer.OrdinalIgnoreCase);
            _orderedFiles = new List<KeyValuePair<string, StormFile>>();
            _visitedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _diagnostics = new List<StormDiagnostic>();
        }

        /// <summary>
        /// Resolves all imports starting from the given root file path.
        /// </summary>
        /// <param name="rootPath">The canonical path to the root Storm grammar file.</param>
        /// <returns>The resolution result containing all parsed files and diagnostics.</returns>
        public ImportResolutionResult Resolve(string rootPath)
        {
            if (rootPath == null) throw new ArgumentNullException(nameof(rootPath));

            var canonicalRoot = CanonicalizePath(rootPath);
            ResolveFile(canonicalRoot, null, 0, 0);

            return new ImportResolutionResult(
                canonicalRoot,
                _filesByPath,
                _orderedFiles,
                _diagnostics,
                !_failed);
        }

        /// <summary>
        /// Recursively resolves a single file and its imports via DFS.
        /// </summary>
        /// <param name="canonicalPath">The canonical path of the file to resolve.</param>
        /// <param name="importingFilePath">The path of the file that imported this one (null for root).</param>
        /// <param name="importLine">The line of the import declaration (for diagnostics).</param>
        /// <param name="importColumn">The column of the import declaration (for diagnostics).</param>
        private void ResolveFile(string canonicalPath, string? importingFilePath, int importLine, int importColumn)
        {
            // Cycle detection: if already visited, skip (R6.7)
            if (!_visitedPaths.Add(canonicalPath))
            {
                return;
            }

            // Read the file
            var contents = _readFile(canonicalPath);
            if (contents == null)
            {
                _diagnostics.Add(new StormDiagnostic(
                    StormDiagnosticIds.STORM_IMPORT_NOT_FOUND,
                    StormDiagnosticSeverity.Error,
                    $"Cannot resolve import path '{canonicalPath}'",
                    importingFilePath,
                    importLine,
                    importColumn));
                _failed = true;
                return;
            }

            // Parse the file
            var source = new StormSourceText(canonicalPath, contents);
            var parseDiagnostics = new List<StormDiagnostic>();
            var tokens = StormLexer.Tokenize(source, parseDiagnostics);
            var stormFile = StormParser.Parse(tokens, source, parseDiagnostics);

            // Propagate any parse diagnostics
            if (parseDiagnostics.Count > 0)
            {
                _diagnostics.AddRange(parseDiagnostics);
                foreach (var diag in parseDiagnostics)
                {
                    if (diag.Severity == StormDiagnosticSeverity.Error)
                    {
                        _failed = true;
                    }
                }
            }

            // Store the parsed file
            _filesByPath[canonicalPath] = stormFile;
            _orderedFiles.Add(new KeyValuePair<string, StormFile>(canonicalPath, stormFile));

            // Recursively resolve imports (DFS pre-order)
            foreach (var import in stormFile.Imports)
            {
                var resolvedPath = ResolveImportPath(canonicalPath, import.Path);
                ResolveFile(resolvedPath, canonicalPath, import.Line, import.Column);
            }
        }

        /// <summary>
        /// Resolves an import path relative to the importing file's directory (R6.9).
        /// </summary>
        private static string ResolveImportPath(string importingFilePath, string importPath)
        {
            var importingDir = GetDirectoryName(importingFilePath);
            var combined = CombinePath(importingDir, importPath);
            return CanonicalizePath(combined);
        }

        /// <summary>
        /// Canonicalizes a file path by resolving . and .. segments and normalizing separators.
        /// </summary>
        public static string CanonicalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            // Normalize separators to forward slash for consistency
            path = path.Replace('\\', '/');

            // Split into segments and resolve . and ..
            var parts = path.Split('/');
            var stack = new List<string>();

            foreach (var part in parts)
            {
                if (part == "." || part == "")
                {
                    // Skip current-directory references and empty segments
                    // (but preserve leading empty segment for absolute paths)
                    if (stack.Count == 0 && part == "")
                    {
                        stack.Add("");
                    }
                    continue;
                }
                else if (part == "..")
                {
                    if (stack.Count > 0 && stack[stack.Count - 1] != "" && stack[stack.Count - 1] != "..")
                    {
                        stack.RemoveAt(stack.Count - 1);
                    }
                    else
                    {
                        stack.Add(part);
                    }
                }
                else
                {
                    stack.Add(part);
                }
            }

            var result = string.Join("/", stack);
            return result;
        }

        /// <summary>
        /// Gets the directory portion of a path.
        /// </summary>
        private static string GetDirectoryName(string path)
        {
            path = path.Replace('\\', '/');
            var lastSlash = path.LastIndexOf('/');
            if (lastSlash >= 0)
            {
                return path.Substring(0, lastSlash);
            }
            return ".";
        }

        /// <summary>
        /// Combines a directory path and a relative file path.
        /// </summary>
        private static string CombinePath(string directory, string relativePath)
        {
            if (string.IsNullOrEmpty(directory) || directory == ".")
            {
                return relativePath;
            }
            return directory + "/" + relativePath;
        }
    }
}
