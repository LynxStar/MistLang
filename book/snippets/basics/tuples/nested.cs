using Standard;

((int, int), string) nested = ((1, 2), "label");
System.Console.WriteLine(System.FormattableString.Invariant($"inner=({nested.Item1.Item1}, {nested.Item1.Item2}) outer={nested.Item2}"));
