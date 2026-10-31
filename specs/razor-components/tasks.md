# Implementation Plan: Razor Components

## Overview

Give the C# backend ownership of `.razor` files through a generic backend-claimed-input seam, translate each
component into a Mist class, and map every diagnostic back to the `.razor` line. Each task leaves the
solution building with zero warnings and `dotnet format --verify-no-changes` clean. Tiers follow the root
`AGENTS.md` "Tests" table; a test's tier is declared with `[Trait("Tier", "N")]`.

Carbonek project MIST is the work record; this plan is the delivery order, not a queue. Tasks 1 and 2 need
no Razor and can land first; tasks 3 to 5 are the backend; task 7 cannot bank until the prerequisite lands.

## Prerequisite

- [ ] 0. `clr-slot-filling` delivered (Carbonek MIST-1126): a Mist member filling a CLR virtual or abstract method or property
  slot is emitted with `override`, and an unfilled abstract slot is a Mist diagnostic
  - Measured 2026-10-30 at `main`: `BuildRenderTree` and `OnInitialized` on a `ComponentBase` subclass emit without `override`; `Mode` on a `RenderModeAttribute` subclass is refused as a redeclaration
  - _Requirements: 9.1, 9.3_

## Tasks

- [ ] 1. The backend seam
  - [ ] 1.1 Add `IBackend`, `ISourceTranslator`, `TranslationContext`, `ClaimedInput`, `TranslatedUnit` and `ProjectFacts` under `Source/Mist/Backends/Contracts/`
    - _Requirements: 1.1, 2.2_
  - [ ] 1.2 Add `BackendRegistry` under `Source/Mist/Driver/` holding `CSharpBackend`; replace the `BackendId.CSharp` comparison in `CompilerDriver.Compile`
    - _Requirements: 1.1, 1.6_
  - [ ] 1.3 `SourcePathSelection.EnumerateCandidates(folder, claimedExtensions)`; `ProjectLayout` and `ProjectReferenceLoader` pass the backend's extensions; the CLI's "no source files" check counts claimed inputs
    - _Requirements: 1.2, 1.3, 1.4_
  - [ ] 1.4 Tier 1 `SourcePathSelectionTests` additions: a claimed extension enumerated at the root and in a referenced project, excluded under `out/` and `bin/`, and ignored when no extension is claimed
    - _Requirements: 1.2, 1.5_
  - [ ] 1.5 Create the `IAmbientTypeSource` after Stage 1.5 and carry it on `PipelineInput`; Stage 4 uses it when present
    - _Requirements: 2.3_
  - [ ] 1.6 `TranslationStage`: run each translator, write `out/translated/<relative>.mist`, lex and parse the results, append them under the owning project, report the stage; stop at the parse gate on translator errors
    - _Requirements: 2.1, 2.4, 2.5, 2.6, 2.7_
  - [ ] 1.7 Tier 4 `CompilerDriverClaimedInputTests`: a stub backend claiming `.probe` whose translator emits one class; discovery, stage order, grouping and binding from a `.mist` unit
    - _Requirements: 13.5_

- [ ] 2. Origin mapping
  - [ ] 2.1 Add `OriginMap` and `OriginSegment` under `Source/Mist/Common/Source/`; `SourceText.Origin`
    - _Requirements: 3.1, 3.7_
  - [ ] 2.2 The origin resolver returns `GeneratedLocation` with `Mapped` for a node whose text has an `Origin`
    - _Requirements: 3.2_
  - [ ] 2.3 `DiagnosticFormatter` prints the mapped position; `DiagnosticJsonWriter` carries both; verbose output appends the translated position
    - _Requirements: 3.3, 3.5, 3.6_
  - [ ] 2.4 Tier 1 `OriginMapTests`: verbatim shift, synthesized span, segment boundary, offset past the end; Tier 1 formatter row for a mapped `GeneratedLocation`
    - _Requirements: 13.2_
  - [ ] 2.5 Tier 3 `TranslatedUnitDiagnosticsTests`: a hand-written translated unit with a Mist error reports the mapped span through the real parser and model, ambient source stubbed
    - _Requirements: 3.2, 3.4, 12.2_

- [ ] 3. The Razor markup parser (backend)
  - [ ] 3.1 Decide Storm grammar versus scanner by attempting `Razor/Grammar/RazorMarkup.storm` with lexer modes; record the decision in `design.md`
    - _Requirements: 4.9_
  - [ ] 3.2 Implement transitions, balancing over Mist literals and comments, code mode, markup mode, directive lines, directive attributes; produce `RazorSyntaxTree`
    - _Requirements: 4.9, 5.4, 5.5, 5.6, 5.7_
  - [ ] 3.3 Add the descriptors of Requirement 12.1 to the registry with freshly minted Numbers; regenerate the text freeze table with `tools/diagnostics/freeze_table.py`
    - _Requirements: 12.1_
  - [ ] 3.4 Tier 1 `RazorParserTests` over every shape of Requirement 13.1 and every malformed shape with its position
    - _Requirements: 13.1_

- [ ] 4. The component catalog
  - [ ] 4.1 `ComponentFacts`, `ParameterFacts`, `EventFacts`, `ParameterShape`; `ComponentCatalog.Resolve(tagName, scope)`
    - _Requirements: 7.1, 7.6_
  - [ ] 4.2 `CatalogBuilder`: Mist classes from parsed units and from Code_Blocks parsed as class bodies; base-list fixpoint; CLR lookup through `IAmbientTypeSource.FindClrType`; `EventHandlers` attributes
    - _Requirements: 7.2, 7.3, 7.4, 7.5_
  - [ ] 4.3 Tier 1 `ComponentCatalogTests` over hand-written facts: one match, none, two; each shape; event fallback. Tier 2 `CatalogBuilderTests` over Code_Blocks through the real parser
    - _Requirements: 13.3_

- [ ] 5. The translator
  - [ ] 5.1 `MistWriter` with verbatim and synthesized appends building the `OriginMap`
    - _Requirements: 3.1_
  - [ ] 5.2 `ImportsResolution` and `DirectiveLowering`: usings, attributes, base list, `@inject` as a readonly field plus the synthesized constructor, `@namespace`, `@rendermode` attribute class, refusals
    - _Requirements: 4.5, 8.1 to 8.11, 10.2, 10.4_
  - [ ] 5.3 Class skeleton, Code_Block members verbatim, nested-type refusal
    - _Requirements: 4.3, 4.4, 4.6, 4.7, 4.8_
  - [ ] 5.4 `RenderProtocolWriter`: markup runs, elements and attributes, text, expressions, control blocks, `@:`, `<text>`, whitespace rule, sequence numbering
    - _Requirements: 5.1 to 5.8_
  - [ ] 5.5 Events, `@key`, `@ref`, `@attributes`, element `@bind` with `:event`, `:format`, `:culture`
    - _Requirements: 5.9, 5.10, 5.11_
  - [ ] 5.6 Components: catalog-typed parameters, child content, named templates with `Context`, `@rendermode` on a tag, `@bind-Param`, unknown and ambiguous components, expression-parameter and generic refusals
    - _Requirements: 6.1 to 6.8_
  - [ ] 5.7 Tier 2 `RazorComponentTranslatorTests` over hand-built catalogs: each lowering row of design sections 6 and 7, and the fixture's `Counter.razor` and `Greeting.razor` against their expected Mist
    - _Requirements: 13.4_
  - [ ] 5.8 Read the whitespace Razor's compiler emits for the fixture and pin the rules as Tier 1 cases
    - _Requirements: 5.8_

- [ ] 6. Hosting and static assets
  - [ ] 6.1 `StaticAssetDeployment`: infer the `Internal.Assets` pack from the framework reference for an executable with Component_Files, a `[packages]` entry pinning it; package `_framework/` to `<bin>/wwwroot/_framework/`, project `wwwroot/` to `<bin>/wwwroot/`
    - _Requirements: 11.2, 11.3_
  - [ ] 6.2 `mist run` content root: read how the CLI launches today, choose working directory or `--contentRoot`, record in `design.md`
    - _Requirements: 11.4_
  - [ ] 6.3 Tier 1 tests of the pack-inference rule (executable, Component_Files, framework reference, pin wins) and of the selection rules over hand-built package listings
    - _Requirements: 11.2_

- [ ] 7. Sample and acceptance
  - [ ] 7.1 Copy `specs/razor-components/fixture/` to `samples/RazorComponents/`, run it, capture the HTML into `samples/__baselines__/AllFeaturesV2.out`, reference it from the orchestrator, add the sample designation sentence to `requirements.md` and the V2-only provenance row to `samples/AGENTS.md`
    - _Requirements: 13.6_
  - [ ] 7.2 Interactive Server acceptance by hand: a host project from design section 9, the spike's `verify_browser.py` against `/counter`, count 0→1→2→3→0→1 over the `_blazor` socket, one navigation through an `@inject NavigationManager`, recorded as a finding
    - _Requirements: 13.7_
  - [ ] 7.3 Book: a Razor components chapter under `book/AGENTS.md` rules, with snippets only for what compiles
    - _Requirements: 14_
