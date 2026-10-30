namespace Mist.Driver.Generators;

internal sealed record GeneratorMessage(string File, int Line, int Column, string Severity, string Code, string Text);