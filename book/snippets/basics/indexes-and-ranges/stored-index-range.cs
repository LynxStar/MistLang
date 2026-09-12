using Standard;

int[] numbers = new int[] { 10, 20, 30, 40, 50 };
System.Index fromEnd = ^2;
System.Console.WriteLine($"valueAtStored={numbers[fromEnd]}");
System.Range window = 0..2;
int[] slice = numbers[window];
System.Console.WriteLine($"slice0={slice[0]}");
System.Console.WriteLine($"slice1={slice[1]}");
