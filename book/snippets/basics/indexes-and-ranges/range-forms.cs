using Standard;

int[] numbers = new int[] { 10, 20, 30, 40, 50 };
int[] both = numbers[1..4];
int[] startOnly = numbers[2..];
int[] endOnly = numbers[..2];
int[] full = numbers[..];
System.Console.WriteLine($"both={both.Length}");
System.Console.WriteLine($"startOnly={startOnly.Length}");
System.Console.WriteLine($"endOnly={endOnly.Length}");
System.Console.WriteLine($"full={full.Length}");
