# Source generation sample

Run `mist run samples/SourceGeneration`. The companion `../SourceGenerationGenerator` project is a
pure-Mist executable. It reads the declared `value.txt` input and ordered type-name argument from the
host request, emits a Mist class, and lists the output using the generator protocol. The consumer calls
the generated method and prints `Generated answer: 42`.

Both projects keep their Mist files at their sample-directory root so the sample parser corpus checks
them. No C# bridge or Storm component participates in this generation edge.

The AllFeaturesV2 line has V2-ONLY provenance: the generator protocol is a standard-pipeline capability.
Its value is checked against the input `42` and the emitted return expression.
