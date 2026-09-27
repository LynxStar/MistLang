using Standard;

(int x, int y) point = (x: 3, y: 4);
System.Console.WriteLine(System.FormattableString.Invariant($"point.x={point.x} point.y={point.y}"));
System.Console.WriteLine(System.FormattableString.Invariant($"positional: Item1={point.Item1} Item2={point.Item2}"));
