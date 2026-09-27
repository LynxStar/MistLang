using Standard;

int i = 42;
string interp = System.FormattableString.Invariant($"The answer is {i}");
string interpExpr = System.FormattableString.Invariant($"Doubled: {i * 2}");
System.Console.WriteLine(interp);
System.Console.WriteLine(interpExpr);
