namespace Mist.CLI.Args;

public static class CliHelp
{
    public static void Write(TextWriter output)
    {
        output.WriteLine("The Mist programming language compiler");
        output.WriteLine();
        output.WriteLine("Usage: mist <command> [project-folder] [options]");
        output.WriteLine();
        output.WriteLine("Commands:");
        output.WriteLine("  build              Compile the project");
        output.WriteLine("  run                Compile and execute the project");
        output.WriteLine("  test               Compile and run tests");
        output.WriteLine("  steering           Install Mist steering files");
        output.WriteLine("  inspect <TypeName> Inspect a CLR type's projected and hidden members");
        output.WriteLine("  restore            Resolve packages, framework references and C# project references without building");
        output.WriteLine("  clean --generators Remove cached generators and runs");
        output.WriteLine("  version            Print the compiler version");
        output.WriteLine();
        output.WriteLine("Options:");
        output.WriteLine("  --backend <id>     Backend to use (default: csharp)");
        output.WriteLine("  --output <dir>     Output directory for emitted files");
        output.WriteLine("  --bin <dir>        Binary output directory");
        output.WriteLine("  --mode <mode>      Compiler mode: compile, lex");
        output.WriteLine("  --pipeline <name>  Compilation pipeline: standard (default: standard)");
        output.WriteLine("  --platform <os>    Platform to compile for: windows, linux, macos (default: this machine)");
        output.WriteLine("  --rid <rid>        Runtime identifier whose native assets are deployed, e.g. linux-x64 (default: this machine)");
        output.WriteLine("  --diagnostic-format text|json  Diagnostic format (default: text)");
        output.WriteLine("  --diagnostic-file <path>       Write JSON to a file; keep text on stderr");
        output.WriteLine("  --warnings-as-errors [Number,...]  Promote all Promotable diagnostics or listed numbers");
        output.WriteLine("  --disable <Number,...>         Disable Optional rules");
        output.WriteLine("  --enable <Number,...>          Enable Optional rules");
        output.WriteLine("  --min-severity info|warning|error  Filter displayed diagnostics; errors remain visible");
        output.WriteLine("  Diagnostic controls apply to build, run, test, inspect and restore.");
        output.WriteLine("  Lex mode accepts format, file and minimum severity; steering/version accept none.");
        output.WriteLine("  --verbose, -v      Show detailed output");
        output.WriteLine("  --quiet, -q        Suppress progress output");
    }

}