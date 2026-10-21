using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace MemoProfile;

internal static class RunEnvironment
{
    public static string Describe(string repoRoot)
    {
        var text = new StringBuilder();
        void line(string key, string? value)
        {
            _ = text.Append(key).Append(": ").AppendLine(value?.Trim());
        }

        line("captured_utc", DateTime.UtcNow.ToString("O"));
        line("mist_commit", Run("git", $"-C \"{repoRoot}\" rev-parse HEAD"));
        line("mist_worktree_status", Run("git", $"-C \"{repoRoot}\" status --porcelain --untracked-files=no") is { Length: > 0 } dirty ? "dirty: " + dirty.Replace('\n', ' ') : "clean");
        line("storm_commit", Run("git", $"-C \"{Path.Combine(repoRoot, "Source", "Storm.SourceGenerator")}\" rev-parse HEAD"));
        line("runtime", RuntimeInformation.FrameworkDescription);
        line("sdk", Run("dotnet", "--version"));
        line("os", RuntimeInformation.OSDescription);
        line("process_architecture", RuntimeInformation.ProcessArchitecture.ToString());
        line("processor_count", Environment.ProcessorCount.ToString());
        line("cpu", Run("sysctl", "-n machdep.cpu.brand_string"));
        line("memory_bytes", Run("sysctl", "-n hw.memsize"));
        line("gc", Protocol.GcDescription());
        line("tiered_compilation", AppContext.TryGetSwitch("System.Runtime.TieredCompilation", out var tiered) ? tiered.ToString() : "default");
        line("tool_jit_optimizer_disabled", Assembly.GetExecutingAssembly().GetCustomAttribute<DebuggableAttribute>()?.IsJITOptimizerDisabled.ToString() ?? "False");
        line("mist_core_jit_optimizer_disabled", typeof(Mist.Generated.MistParser).Assembly.GetCustomAttribute<DebuggableAttribute>()?.IsJITOptimizerDisabled.ToString() ?? "False");
        line("dotnet_env", string.Join(' ', Environment.GetEnvironmentVariables().Keys.Cast<string>().Where(k => k.StartsWith("DOTNET_", StringComparison.Ordinal)).Order().Select(k => k + "=" + Environment.GetEnvironmentVariable(k))));
        return text.ToString();
    }

    public static string Load()
    {
        var processes = Run("ps", "-A -o pcpu=,comm=") ?? "";
        var rows = processes.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(r => r.Trim().Split(' ', 2))
            .Where(r => r.Length == 2 && double.TryParse(r[0], out _))
            .Select(r => (Cpu: double.Parse(r[0]), Command: r[1]))
            .ToList();
        var dotnet = rows.Count(r => r.Command.EndsWith("dotnet", StringComparison.Ordinal) || r.Command.Contains("VBCSCompiler", StringComparison.Ordinal));
        return $"loadavg={Run("sysctl", "-n vm.loadavg")?.Trim()} total_pcpu={rows.Sum(r => r.Cpu):F1} dotnet_processes={dotnet} top={string.Join("; ", rows.OrderByDescending(r => r.Cpu).Take(3).Select(r => $"{r.Cpu:F1} {Path.GetFileName(r.Command)}"))}";
    }

    private static string? Run(string file, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(file, arguments) { RedirectStandardOutput = true, RedirectStandardError = true })!;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return output.TrimEnd();
        }
        catch(System.ComponentModel.Win32Exception exception)
        {
            return "unavailable: " + exception.Message;
        }
    }
}