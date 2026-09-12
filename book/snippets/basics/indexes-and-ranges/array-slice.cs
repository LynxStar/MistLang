using Standard;

int[] numbers = new int[] { 10, 20, 30, 40, 50 };
int[] slice = numbers[1..4];
System.Console.WriteLine($"length={slice.Length}");
System.Console.WriteLine($"first={slice[0]}");
System.Console.WriteLine($"last={slice[^1]}");
slice[0] = 999;
System.Console.WriteLine($"sliceMutated={slice[0]}");
System.Console.WriteLine($"sourceUnchanged={numbers[1]}");
