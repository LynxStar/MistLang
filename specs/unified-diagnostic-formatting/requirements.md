# Requirements Document

> **Superseded in part by `specs/diagnostics/requirements.md`.** That document supersedes Requirements 4.3, 4.4, 7.3 and 7.4 here by its Requirement 8; Requirement 5.6 and the design's Property 1 by its Requirement 11; and the design's rule of a minimum of 100 iterations for every property by its Requirement 17. Requirements 1 to 3 and 6 remain in force for the Storm boundary. The rest of this document, including the `MIST_...` spellings it cites, is left as history and is not rewritten; `specs/diagnostics/crosswalk.md` translates them.

## Introduction

The Mist compiler and Storm parser generator currently have inconsistent diagnostic reporting. Some pipeline stages include source location (file, line, column) in their diagnostics while others do not. Additionally, Storm and Mist use separate diagnostic types with no shared formatting system. This feature introduces a unified diagnostic formatting system that ensures every diagnostic across all pipeline stages in both Storm and Mist includes exact source location information and is rendered through a single, consistent formatter.

## Glossary

- **Diagnostic**: A structured error or warning message produced by a compiler pipeline stage, containing an identifier, severity, originating stage, human-readable message, source location, and file path.
- **Diagnostic_Formatter**: The single component responsible for converting a Diagnostic into a human-readable string for CLI output, following MSBuild-compatible format.
- **Source_Location**: A file path combined with a 1-based line and column position identifying where in source code a diagnostic originated.
- **LinePositionSpan**: A pair of LinePosition values (start and end) representing a range within a source file.
- **Pipeline_Stage**: One of the sequential phases of compilation: Lexing, Parsing, Semantic Model Construction, Analysis, or Backend.
- **Storm_Runtime_DiagnosticBag**: The generated diagnostic collection used by Storm-generated lexers and parsers, currently storing only plain string messages without structured location data.
- **Mist_DiagnosticBag**: The Mist compiler's append-only diagnostic collection that stores fully structured Diagnostic records.
- **Storm_Source_Generator**: The Roslyn incremental source generator that processes .storm grammar files and emits C# lexer/parser code.
- **Storm_Frontend**: The lexer and parser within the Storm source generator that process .storm grammar files themselves.
- **Mist_CLI**: The command-line interface for the Mist compiler that displays diagnostics to the developer.

## Requirements

### Requirement 1: Storm Runtime Structured Diagnostics

**User Story:** As a developer using Mist, I want lexer and parser errors from Storm-generated code to include file, line, and column information, so that I can locate the exact position of syntax errors in my source files.

#### Acceptance Criteria

1. WHEN the Storm source generator emits the runtime DiagnosticBag type, THE Storm_Source_Generator SHALL emit a diagnostic entry type that includes a message string, a file path string, a 1-based line integer, and a 1-based column integer.
2. WHEN the Storm source generator emits lexer error-handling code, THE Storm_Source_Generator SHALL populate the diagnostic entry with the file path from the SourceText and the line and column of the offending character.
3. WHEN the Storm source generator emits parser error-handling code, THE Storm_Source_Generator SHALL populate the diagnostic entry with the file path from the SourceText and the line and column of the current token.
4. WHEN a Storm-generated lexer produces a diagnostic, THE Storm_Runtime_DiagnosticBag SHALL store the structured diagnostic entry rather than a plain string message.
5. FOR ALL diagnostics produced by a Storm-generated lexer or parser, parsing the stored file path, line, and column SHALL yield the same values as computing the position from the source text and the token or character offset (round-trip property).

### Requirement 2: Mist Lexer Location Propagation

**User Story:** As a developer using Mist, I want lexer diagnostics to include the exact file, line, and column of the problematic token, so that I can navigate directly to the error in my editor.

#### Acceptance Criteria

1. WHEN the MistLexer maps a Storm runtime diagnostic to a Mist Diagnostic, THE MistLexer SHALL extract the file path, line, and column from the Storm runtime diagnostic entry and set the Location field to a LinePositionSpan constructed from those values.
2. WHEN the MistLexer maps a Storm runtime diagnostic to a Mist Diagnostic, THE MistLexer SHALL set the CompilationUnitPath field to the file path from the Storm runtime diagnostic entry.
3. FOR ALL Mist Diagnostics produced by the MistLexer with a non-empty source file, the Location field SHALL be non-null.

### Requirement 3: Mist Parser Location Propagation

**User Story:** As a developer using Mist, I want parser diagnostics to include the exact file, line, and column of the problematic token, so that I can navigate directly to the error in my editor.

#### Acceptance Criteria

1. WHEN the MistParser maps a Storm runtime diagnostic to a Mist Diagnostic, THE MistParser SHALL extract the file path, line, and column from the Storm runtime diagnostic entry and set the Location field to a LinePositionSpan constructed from those values.
2. WHEN the MistParser maps a Storm runtime diagnostic to a Mist Diagnostic, THE MistParser SHALL set the CompilationUnitPath field to the file path from the Storm runtime diagnostic entry.
3. FOR ALL Mist Diagnostics produced by the MistParser with a non-empty source file, the Location field SHALL be non-null.

### Requirement 4: Semantic Model Builder Location Propagation

**User Story:** As a developer using Mist, I want semantic analysis errors (undefined types, circular inheritance, duplicate declarations) to include the exact file, line, and column of the offending syntax node, so that I can navigate directly to the error in my editor.

#### Acceptance Criteria

1. WHEN the SemanticModelBuilder creates a Diagnostic for a syntax node that has a valid source span, THE SemanticModelBuilder SHALL resolve the corresponding SourceText and convert the span offset to a LinePositionSpan for the Location field.
2. WHEN the SemanticModelBuilder creates a Diagnostic for a syntax node that has a valid source span, THE SemanticModelBuilder SHALL set the CompilationUnitPath field to the file path of the resolved SourceText.
3. FOR ALL Diagnostics produced by the SemanticModelBuilder that reference a specific syntax node with a non-zero span length, the Location field SHALL be non-null.
4. IF the SemanticModelBuilder cannot resolve a source span to a source file (e.g., a driver-level error with no associated syntax node), THEN THE SemanticModelBuilder SHALL set the Location field to null and include the available context in the message.

### Requirement 5: Unified Diagnostic Formatter

**User Story:** As a developer using both Mist and Storm, I want all diagnostic output to follow a single consistent format, so that I can rely on a predictable pattern when reading errors and warnings regardless of which pipeline stage produced them.

#### Acceptance Criteria

1. THE Diagnostic_Formatter SHALL format diagnostics with a source location as: `<file>(<line>,<column>): <severity> <id>: <message>`.
2. THE Diagnostic_Formatter SHALL format diagnostics with a file path but no source location as: `<file>: <severity> <id>: <message>`.
3. THE Diagnostic_Formatter SHALL format diagnostics with no file path and no source location as: `<severity> <id>: <message>`.
4. THE Diagnostic_Formatter SHALL render the severity field as the lowercase string `error` for Error severity and `warning` for Warning severity.
5. THE Diagnostic_Formatter SHALL be a shared component accessible to both the Mist CLI and any future Storm CLI, located outside of CLI-specific projects.
6. FOR ALL Diagnostic values, formatting then parsing the formatted string SHALL recover the original severity, diagnostic id, and message (round-trip property for the formatter).

### Requirement 6: Storm Frontend Diagnostic Location Consistency

**User Story:** As a developer writing Storm grammar files, I want errors in my .storm files to include the exact file, line, and column, so that I can locate grammar issues quickly.

#### Acceptance Criteria

1. WHEN the Storm_Frontend lexer encounters a syntax error in a .storm file, THE Storm_Frontend SHALL create a StormDiagnostic with the FilePath, Line, and Column fields populated from the source position of the error.
2. WHEN the Storm_Frontend parser encounters a syntax error in a .storm file, THE Storm_Frontend SHALL create a StormDiagnostic with the FilePath, Line, and Column fields populated from the source position of the current token.
3. FOR ALL StormDiagnostics produced by the Storm_Frontend for a file with a non-null file path, the FilePath field SHALL be non-null and the Line and Column fields SHALL be greater than zero.

### Requirement 7: Diagnostic Location Completeness Across Pipeline

**User Story:** As a developer, I want confidence that no pipeline stage silently drops location information, so that every actionable diagnostic points me to the right place in my code.

#### Acceptance Criteria

1. WHEN a Diagnostic is created for an issue that corresponds to a specific position in a source file, THE originating Pipeline_Stage SHALL populate both the Location and CompilationUnitPath fields.
2. WHILE the compiler pipeline is executing, THE CompilerDriver SHALL propagate all Diagnostic records from each stage without modifying their Location or CompilationUnitPath fields.
3. WHEN the Mist_CLI receives a Diagnostic with a non-null Location, THE Mist_CLI SHALL pass the Diagnostic to the Diagnostic_Formatter and output the formatted string to stderr.
4. WHEN the Mist_CLI receives a Diagnostic with a null Location, THE Mist_CLI SHALL pass the Diagnostic to the Diagnostic_Formatter and output the formatted string (without position) to stderr.
5. FOR ALL Diagnostics produced during a compilation of a valid source file path, the CompilationUnitPath field SHALL be non-null when the diagnostic relates to content within that file.
