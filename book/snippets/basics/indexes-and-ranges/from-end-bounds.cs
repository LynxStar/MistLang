using Standard;

int[] numbers = new int[] { 10, 20, 30, 40, 50 };
int[] trimmed = numbers[1..^1];
int[] lastThree = numbers[^3..];
System.Console.WriteLine($"trimmed={trimmed.Length}");
System.Console.WriteLine($"trimmedFirst={trimmed[0]}");
System.Console.WriteLine($"lastThree={lastThree.Length}");
System.Console.WriteLine($"lastThreeFirst={lastThree[0]}");
