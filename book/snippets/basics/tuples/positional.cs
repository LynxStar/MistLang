using Standard;

(int, string) pair = (42, "forty-two");
System.Console.WriteLine(System.FormattableString.Invariant($"pair.Item1={pair.Item1} pair.Item2={pair.Item2}"));
