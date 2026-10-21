using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MemoProfile;

internal static class Program
{
    private static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var entered = DateTime.Now;
        var repoRoot = FindRepoRoot();

        if(args is ["cold-one", var coldVariant])
        {
            Console.WriteLine(ColdOne(coldVariant, repoRoot, entered));
            return 0;
        }

        if(args is not ["all", "--out", var outDir])
        {
            Console.Error.WriteLine("usage: MemoProfile all --out <results-dir>");
            return 1;
        }

        _ = Directory.CreateDirectory(outDir);
        var log = new StringBuilder();
        void note(string line)
        {
            Console.WriteLine(line);
            _ = log.AppendLine(line);
        }

        File.WriteAllText(Path.Combine(outDir, "environment.txt"), RunEnvironment.Describe(repoRoot));
        var design = Protocol.Rounds().Select(r => r.Order).ToList();
        var positionCounts = design.SelectMany(row => row.Select((v, p) => (v, p))).CountBy(x => x).Select(x => x.Value).Distinct().Count();
        var successorCounts = design.SelectMany(row => row.Zip(row.Skip(1))).CountBy(x => x).Select(x => x.Value).ToList();
        note($"design: {design.Count} rounds over {Protocol.Variants.Length} variants; every variant-position count equal = {positionCounts == 1}; ordered successor pairs {successorCounts.Count}/{Protocol.Variants.Length * (Protocol.Variants.Length - 1)}, each {string.Join("/", successorCounts.Distinct())} times");
        note("load at start: " + RunEnvironment.Load());

        var grammar = VariantSourceBuilder.ReadGrammar(repoRoot);
        var sources = Protocol.Variants.Where(v => v != ParserVariant.MistCore)
            .ToDictionary(v => v, v => VariantSourceBuilder.Build(v, grammar));
        File.WriteAllText(Path.Combine(outDir, "plans.txt"), DescribePlans(grammar, sources));
        var sourceChecks = CheckSources(repoRoot, grammar, sources);
        File.WriteAllText(Path.Combine(outDir, "sources.txt"), sourceChecks.Report);
        note("source checks: " + (sourceChecks.Passed ? "passed" : "FAILED"));

        var variants = new List<ParserVariant> { ParserVariant.FromMistCore(sources[VariantSourceBuilder.Production].Plan) };
        variants.AddRange(sources.Values.Select(ParserVariant.Compile));
        variants = Protocol.Variants.Select(name => variants.Single(v => v.Name == name)).ToList();

        var workload = Workload.Load(repoRoot);
        File.WriteAllLines(Path.Combine(outDir, "workload.csv"), workload.Select(w => $"{w.Id},{w.Kind},{w.Sha256},{w.Bytes},{w.Tokens.Kind.Length},{w.LexDiagnostics}").Prepend("id,kind,sha256,bytes,tokens,lex_diagnostics"));
        note($"workload: {workload.Count} inputs ({workload.Count(w => w.Kind == "valid")} valid files, {workload.Count(w => w.Kind != "valid")} derived invalid), {workload.Sum(w => w.Bytes)} bytes, {workload.Sum(w => w.Tokens.Kind.Length)} tokens");

        var streams = variants.ToDictionary(v => v.Name, v => (IReadOnlyList<object>)workload.Select(w => v.MakeTokens(w.Tokens)).ToList());
        var equivalent = CheckEquivalence(outDir, variants, workload, streams, note);
        if(!equivalent || !sourceChecks.Passed)
        {
            note("equivalence or source checks failed; timing skipped");
            File.WriteAllText(Path.Combine(outDir, "run-log.txt"), log.ToString());
            return 2;
        }

        RunWarm(outDir, variants, workload, streams, note);
        RunCold(outDir, note);
        note("load at end: " + RunEnvironment.Load());
        File.WriteAllText(Path.Combine(outDir, "run-log.txt"), log.ToString());
        return 0;
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while(dir != null && !File.Exists(Path.Combine(dir, "Mist.slnx")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? throw new InvalidOperationException("Mist.slnx not found above " + AppContext.BaseDirectory);
    }

    private static string DescribePlans(IReadOnlyList<GrammarFile> grammar, IReadOnlyDictionary<string, VariantSources> sources)
    {
        var rules = VariantSourceBuilder.LoweredRules(grammar);
        var production = sources[VariantSourceBuilder.Production].Plan.MemoizedRuleNames;
        var auto = sources[VariantSourceBuilder.AutoOnly].Plan.MemoizedRuleNames;
        var text = new StringBuilder();
        _ = text.AppendLine($"grammar files: {string.Join(", ", grammar.Select(g => Path.GetFileName(g.Path)))}");
        _ = text.AppendLine($"lowered rules: {rules.Count}; @memo-annotated: {rules.Count(r => r.IsMemo)} ({string.Join(", ", rules.Where(r => r.IsMemo).Select(r => r.Name))})");
        foreach(var (name, variant) in sources)
        {
            _ = text.AppendLine($"{name}: {variant.Plan.Count} memoized rules; derivation: {variant.Derivation}");
            _ = text.AppendLine("  " + string.Join(", ", variant.Plan.MemoizedRuleNames));
        }

        _ = text.AppendLine($"{ParserVariant.MistCore}: the Mist.Core assembly's compiled parser, built by MSBuild from the same grammar; plan = production");
        _ = text.AppendLine($"in production only: [{string.Join(", ", production.Except(auto))}]");
        _ = text.AppendLine($"in auto-only only: [{string.Join(", ", auto.Except(production))}]");
        foreach(var (name, isMemo) in rules.Where(r => r.IsMemo))
        {
            _ = text.AppendLine($"@memo {name}: automatic analysis selects it without the annotation = {auto.Contains(name)}");
        }

        return text.ToString();
    }

    private static (bool Passed, string Report) CheckSources(string repoRoot, IReadOnlyList<GrammarFile> grammar, IReadOnlyDictionary<string, VariantSources> sources)
    {
        var text = new StringBuilder();
        var passed = true;
        void check(string what, bool ok)
        {
            passed &= ok;
            _ = text.AppendLine($"{(ok ? "PASS" : "FAIL")} {what}");
        }

        string generated(string variant, string suffix)
        {
            return sources[variant].Files.Single(f => f.HintName.EndsWith(suffix, StringComparison.Ordinal)).Text;
        }

        var productionParser = generated(VariantSourceBuilder.Production, ".Parser.g.cs");

        foreach(var suffix in new[] { "Storm.Runtime.g.cs", ".Ast.g.cs", ".Lexer.g.cs" })
        {
            check($"{suffix} identical across every generated variant",
                sources.Values.Select(v => generated(v.Name, suffix)).Distinct().Count() == 1);
        }

        check("ParserEmitter.Emit over the analysed production plan reproduces the generator's production parser byte for byte",
            VariantSourceBuilder.EmitParser(grammar, sources[VariantSourceBuilder.Production].Plan) == productionParser);

        foreach(var variant in sources.Values)
        {
            var ids = Regex.Matches(generated(variant.Name, ".Parser.g.cs"), @"var _key = MemoKey\((\d+), _pos\);").Select(m => m.Groups[1].Value).Distinct().Count();
            check($"{variant.Name}: parser text memoizes {ids} rules, plan says {variant.Plan.Count}", ids == variant.Plan.Count);
        }

        var built = Directory.Exists(Path.Combine(repoRoot, "Source", "Mist", "obj"))
            ? Directory.EnumerateFiles(Path.Combine(repoRoot, "Source", "Mist", "obj"), "Mist.Parser.g.cs", SearchOption.AllDirectories).ToList()
            : [];
        foreach(var path in built)
        {
            check($"MSBuild output {Path.GetRelativePath(repoRoot, path)} equals the harness production parser", File.ReadAllText(path) == productionParser);
        }

        if(built.Count == 0)
        {
            _ = text.AppendLine("SKIP no MSBuild-generated Mist.Parser.g.cs under Source/Mist/obj");
        }

        var productionLines = productionParser.Split('\n');
        var noMemoLines = generated(VariantSourceBuilder.NoMemo, ".Parser.g.cs").Split('\n');
        _ = text.AppendLine($"no-memo parser: {noMemoLines.Length} lines vs production {productionLines.Length}; lines only in production: {productionLines.Except(noMemoLines).Count()} distinct, only in no-memo: {noMemoLines.Except(productionLines).Count()} distinct");
        _ = text.AppendLine("  distinct production-only lines: " + string.Join(" | ", productionLines.Except(noMemoLines).Select(l => l.Trim()).Distinct().Take(12)));
        _ = text.AppendLine("  distinct no-memo-only lines: " + string.Join(" | ", noMemoLines.Except(productionLines).Select(l => l.Trim()).Distinct().Take(12)));
        foreach(var variant in sources.Values)
        {
            _ = text.AppendLine($"{variant.Name} parser sha256 {Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(generated(variant.Name, ".Parser.g.cs"))))}");
        }

        return (passed, text.ToString());
    }

    private static bool CheckEquivalence(string outDir, IReadOnlyList<ParserVariant> variants, IReadOnlyList<WorkloadInput> workload, IReadOnlyDictionary<string, IReadOnlyList<object>> streams, Action<string> note)
    {
        var rows = new List<string> { "id,variant,root,nodes,tree_sha256,last_position,default_channel_tokens,diagnostics,diagnostics_sha256,matches_mist_core" };
        var memo = variants.ToDictionary(v => v.Name, _ => new Dictionary<string, long>(StringComparer.Ordinal));
        var mismatches = 0;
        var stoppedEarly = new List<string>();
        for(var i = 0; i < workload.Count; i++)
        {
            var defaultTokens = workload[i].Tokens.Hidden.Count(hidden => !hidden);
            ParseOutcome? reference = null;
            foreach(var variant in variants)
            {
                var outcome = variant.ParseForCheck(streams[variant.Name][i]);
                reference ??= outcome;
                var diagnosticsSha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', outcome.Diagnostics))))[..16];
                var matches = outcome.Tree == reference.Tree && outcome.LastPosition == reference.LastPosition && outcome.Diagnostics.SequenceEqual(reference.Diagnostics);
                mismatches += matches ? 0 : 1;
                rows.Add($"{workload[i].Id},{variant.Name},{(outcome.Tree is null ? "null" : "tree")},{outcome.Tree?.NodeCount ?? 0},{outcome.Tree?.Sha256[..16] ?? "-"},{outcome.LastPosition},{defaultTokens},{outcome.Diagnostics.Length},{diagnosticsSha},{matches}");
                if(variant == variants[0] && outcome.LastPosition < defaultTokens - 1)
                {
                    stoppedEarly.Add(workload[i].Kind);
                }

                foreach(var (rule, count) in outcome.MemoEntriesByRule)
                {
                    memo[variant.Name][rule] = memo[variant.Name].GetValueOrDefault(rule) + count;
                }
            }
        }

        File.WriteAllLines(Path.Combine(outDir, "equivalence.csv"), rows);
        File.WriteAllLines(Path.Combine(outDir, "memo-entries.csv"), memo
            .SelectMany(kv => kv.Value.OrderByDescending(r => r.Value).Select(r => $"{kv.Key},{r.Key},{r.Value}"))
            .Prepend("variant,rule,memo_entries_summed_over_workload"));
        var nullRoots = rows.Skip(1).Count(r => r.Split(',')[2] == "null") / variants.Count;
        note($"equivalence: {workload.Count} inputs x {variants.Count} variants, {mismatches} mismatches against {ParserVariant.MistCore}; {nullRoots} inputs yield no tree; parse stops before end of input on {stoppedEarly.Count} inputs ({string.Join(", ", stoppedEarly.CountBy(k => k).Select(k => $"{k.Value} {k.Key}"))}), {workload.Count - stoppedEarly.Count} reach it");
        foreach(var (name, rules) in memo)
        {
            note($"memo entries {name}: {rules.Values.Sum()} total over the workload");
        }

        return mismatches == 0;
    }

    private static void RunWarm(string outDir, IReadOnlyList<ParserVariant> variants, IReadOnlyList<WorkloadInput> workload, IReadOnlyDictionary<string, IReadOnlyList<object>> streams, Action<string> note)
    {
        var ticks = new long[workload.Count];
        for(var pass = 0; pass < Protocol.WarmupPassesPerVariant; pass++)
        {
            foreach(var variant in variants)
            {
                _ = Protocol.MeasurePass(variant, streams[variant.Name], ticks);
            }
        }

        var rows = new List<string> { "round,position,variant,pass,wall_ms,process_cpu_ms,allocated_bytes,gen0,gen1,gen2,gc_pause_ms,null_roots" };
        var perInput = variants.ToDictionary(v => v.Name, _ => workload.Select(_ => new List<double>()).ToList());
        var samples = variants.ToDictionary(v => v.Name, _ => new List<PassSample>());
        var roundRatios = variants.ToDictionary(v => v.Name, _ => new List<double>());
        var budget = Stopwatch.StartNew();
        var complete = true;
        foreach(var (round, order) in Protocol.Rounds())
        {
            if(budget.Elapsed > Protocol.WarmBudget)
            {
                complete = false;
                break;
            }

            var roundMedian = new Dictionary<string, double>();
            for(var position = 0; position < order.Length; position++)
            {
                var variant = variants[order[position]];
                var walls = new List<double>();
                for(var pass = 0; pass < Protocol.PassesPerSlot; pass++)
                {
                    var sample = Protocol.MeasurePass(variant, streams[variant.Name], ticks);
                    samples[variant.Name].Add(sample);
                    walls.Add(sample.WallMs);
                    rows.Add($"{round},{position},{variant.Name},{pass},{sample.WallMs:F3},{sample.ProcessCpuMs:F1},{sample.AllocatedBytes},{sample.Gen0},{sample.Gen1},{sample.Gen2},{sample.GcPauseMs:F3},{sample.NullRoots}");
                    for(var i = 0; i < ticks.Length; i++)
                    {
                        perInput[variant.Name][i].Add(ticks[i] * 1_000_000.0 / Stopwatch.Frequency);
                    }
                }

                roundMedian[variant.Name] = Stats.Median(walls);
            }

            foreach(var variant in variants)
            {
                roundRatios[variant.Name].Add(roundMedian[variant.Name] / roundMedian[VariantSourceBuilder.Production]);
            }
        }

        File.WriteAllLines(Path.Combine(outDir, "warm-samples.csv"), rows);
        File.WriteAllLines(Path.Combine(outDir, "warm-inputs.csv"), workload.Select((w, i) => $"{w.Id},{w.Tokens.Kind.Length},{string.Join(',', variants.Select(v => Stats.Median(perInput[v.Name][i]).ToString("F1")))}")
            .Prepend("id,tokens," + string.Join(',', variants.Select(v => v.Name + "_median_us"))));

        note($"warm: {(complete ? "complete" : "BUDGET EXCEEDED, incomplete")} in {budget.Elapsed.TotalSeconds:F0}s; {Protocol.WarmupPassesPerVariant} discarded warm-up passes per variant, then {rows.Count - 1} recorded passes");
        foreach(var variant in variants)
        {
            var s = samples[variant.Name];
            note($"warm {variant.Name}: n={s.Count} wall_ms median {Stats.Median(s.Select(x => x.WallMs)):F1} [{s.Min(x => x.WallMs):F1}..{s.Max(x => x.WallMs):F1}] iqr {Stats.Iqr(s.Select(x => x.WallMs))}; cpu_ms median {Stats.Median(s.Select(x => x.ProcessCpuMs)):F1}; alloc MB median {Stats.Median(s.Select(x => x.AllocatedBytes / 1e6)):F2} [{s.Min(x => x.AllocatedBytes) / 1e6:F2}..{s.Max(x => x.AllocatedBytes) / 1e6:F2}]; gen0/1/2 per pass median {Stats.Median(s.Select(x => (double)x.Gen0))}/{Stats.Median(s.Select(x => (double)x.Gen1))}/{Stats.Median(s.Select(x => (double)x.Gen2))}; gc pause ms median {Stats.Median(s.Select(x => x.GcPauseMs)):F2}; per-round wall ratio to production median {Stats.Median(roundRatios[variant.Name]):F3} [{roundRatios[variant.Name].Min():F3}..{roundRatios[variant.Name].Max():F3}]");
        }

        var heaviest = workload.Select((w, i) => (w.Id, Us: Stats.Median(perInput[VariantSourceBuilder.Production][i]))).OrderByDescending(x => x.Us).Take(5);
        note("warm production heaviest inputs (median us): " + string.Join("; ", heaviest.Select(h => $"{h.Id} {h.Us:F0}")));
    }

    private static void RunCold(string outDir, Action<string> note)
    {
        var rows = new List<string> { "rep,position,variant,runtime_startup_ms,generation_ms,compile_load_ms,workload_lex_ms,first_pass_ms,first_pass_alloc_bytes,second_pass_ms,process_total_ms" };
        var dll = typeof(Protocol).Assembly.Location;
        var budget = Stopwatch.StartNew();
        var complete = true;
        for(var rep = 0; rep < Protocol.ColdProcessesPerVariant && complete; rep++)
        {
            var order = Protocol.Rounds().ElementAt(rep % Protocol.WilliamsDesign(Protocol.Variants.Length).Count).Order;
            for(var position = 0; position < order.Length; position++)
            {
                if(budget.Elapsed > Protocol.ColdBudget)
                {
                    complete = false;
                    break;
                }

                var variant = Protocol.Variants[order[position]];
                var wall = Stopwatch.StartNew();
                using var child = Process.Start(new ProcessStartInfo("dotnet", $"\"{dll}\" cold-one {variant}") { RedirectStandardOutput = true, UseShellExecute = false })!;
                var output = child.StandardOutput.ReadToEnd().Trim();
                child.WaitForExit();
                rows.Add($"{rep},{position},{variant},{output},{wall.Elapsed.TotalMilliseconds:F0}");
            }
        }

        File.WriteAllLines(Path.Combine(outDir, "cold-samples.csv"), rows);
        note($"cold: {(complete ? "complete" : "BUDGET EXCEEDED, incomplete")} in {budget.Elapsed.TotalSeconds:F0}s, {rows.Count - 1} fresh processes");
        foreach(var variant in Protocol.Variants)
        {
            var cells = rows.Skip(1).Select(r => r.Split(',')).Where(c => c[2] == variant).ToList();
            string column(int index)
            {
                return $"{Stats.Median(cells.Select(c => double.Parse(c[index]))):F0} [{cells.Min(c => double.Parse(c[index])):F0}..{cells.Max(c => double.Parse(c[index])):F0}]";
            }

            note($"cold {variant}: n={cells.Count} generation_ms {column(4)}; compile_load_ms {column(5)}; first_pass_ms {column(7)}; second_pass_ms {column(9)}; process_total_ms {column(10)}");
        }
    }

    private static string ColdOne(string variantName, string repoRoot, DateTime entered)
    {
        var startup = (entered - Process.GetCurrentProcess().StartTime).TotalMilliseconds;
        var stopwatch = Stopwatch.StartNew();
        ParserVariant variant;
        double generation = 0;
        if(variantName == ParserVariant.MistCore)
        {
            variant = ParserVariant.FromMistCore(Storm.SourceGenerator.Analyze.MemoizationPlan.Empty);
        }
        else
        {
            var sources = VariantSourceBuilder.Build(variantName, VariantSourceBuilder.ReadGrammar(repoRoot));
            generation = sources.GenerationMs;
            variant = ParserVariant.Compile(sources);
        }

        var compile = variant.CompileMs;
        stopwatch.Restart();
        var workload = Workload.Load(repoRoot);
        var streams = workload.Select(w => variant.MakeTokens(w.Tokens)).ToList();
        var lex = stopwatch.Elapsed.TotalMilliseconds;
        var ticks = new long[streams.Count];
        var first = Protocol.MeasurePass(variant, streams, ticks);
        var second = Protocol.MeasurePass(variant, streams, ticks);
        return $"{startup:F0},{generation:F0},{compile:F0},{lex:F0},{first.WallMs:F1},{first.AllocatedBytes},{second.WallMs:F1}";
    }
}

internal static class Stats
{
    public static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        return sorted.Length == 0 ? double.NaN : sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
    }

    public static string Iqr(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        return $"{sorted[sorted.Length / 4]:F1}..{sorted[3 * sorted.Length / 4]:F1}";
    }
}