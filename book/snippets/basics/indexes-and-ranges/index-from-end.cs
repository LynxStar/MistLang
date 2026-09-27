using Standard;

int[] numbers = new int[] { 10, 20, 30, 40, 50 };
int last = numbers[^1];
int secondToLast = numbers[^2];
System.Console.WriteLine(System.FormattableString.Invariant($"last={last}"));
System.Console.WriteLine(System.FormattableString.Invariant($"secondToLast={secondToLast}"));
