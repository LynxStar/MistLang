// Struct: copy semantics
var a = new Color(255, 0, 0);
var b = a;          // b is an independent copy
b.r = 0;            // modifying b does not affect a
Console.WriteLine($"a.r={a.r} b.r={b.r}");
// Output: a.r=255 b.r=0

// Class: reference semantics
var p1 = new Point(3, 4);
var p2 = p1;        // p2 points to the same object
// Changes through p2 would be visible through p1
