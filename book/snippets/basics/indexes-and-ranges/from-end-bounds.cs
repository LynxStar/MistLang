using System;

int[] numbers = new int[] { 10, 20, 30, 40, 50 };
int[] trimmed = numbers[1..^1];
int[] lastThree = numbers[^3..];
Console.WriteLine($"trimmed={trimmed.Length}");
Console.WriteLine($"trimmedFirst={trimmed[0]}");
Console.WriteLine($"lastThree={lastThree.Length}");
Console.WriteLine($"lastThreeFirst={lastThree[0]}");
