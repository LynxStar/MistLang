using System;
using System.Text.RegularExpressions;

var numbers = new[] { 10, 20, 30, 40, 50 };
var last = numbers[^(1)];
var secondToLast = numbers[^(2)];
Console.WriteLine($"last={last}");
Console.WriteLine($"secondToLast={secondToLast}");
