using Standard;

int[] numbers = new int[] { 10, 20, 30, 40, 50 };
int[] slice = numbers[1..4];
System.Console.WriteLine(System.FormattableString.Invariant($"length={slice.Length}"));
System.Console.WriteLine(System.FormattableString.Invariant($"first={slice[0]}"));
System.Console.WriteLine(System.FormattableString.Invariant($"last={slice[^1]}"));
slice[0] = 999;
System.Console.WriteLine(System.FormattableString.Invariant($"sliceMutated={slice[0]}"));
System.Console.WriteLine(System.FormattableString.Invariant($"sourceUnchanged={numbers[1]}"));
