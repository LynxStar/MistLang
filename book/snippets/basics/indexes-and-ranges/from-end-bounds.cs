using Standard;

int[] numbers = new int[] { 10, 20, 30, 40, 50 };
int[] trimmed = numbers[1..^1];
int[] lastThree = numbers[^3..];
System.Console.WriteLine(System.FormattableString.Invariant($"trimmed={trimmed.Length}"));
System.Console.WriteLine(System.FormattableString.Invariant($"trimmedFirst={trimmed[0]}"));
System.Console.WriteLine(System.FormattableString.Invariant($"lastThree={lastThree.Length}"));
System.Console.WriteLine(System.FormattableString.Invariant($"lastThreeFirst={lastThree[0]}"));
