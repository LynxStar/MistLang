# Text capabilities

V2-only provenance: this sample is written for the approved MIST-1050 contract. Its baseline is captured from the standard pipeline and checked against the program by hand. It is composed into AllFeaturesV2; no additional system-test orchestrator is introduced.

The sample exercises every added String and Char declaration, nullable search results, UTF-16 positions, Unicode versus ASCII classification, invariant defaults under Turkish current culture, explicit ordinal comparisons, splitting and trimming, nullable helpers, ordered sequence composition, and runtime-template formatting with alignment, F2 and an explicit provider. Native slicing and ordinary invariant interpolation use existing language support. Non-ASCII inputs are constructed as characters at runtime and only ASCII results are printed.

The complete production overload inventory and concrete Mist mappings live in [text-capabilities.md](../../specs/standard-library/text-capabilities.md).
