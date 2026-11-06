using System;
using RevitMCP.Core;

class Program
{
    static void Check(int expected, int actual, string name)
    { if (expected != actual) throw new Exception(name + ": " + actual); }
    static void Main()
    {
        Check(0, ElevationSteelMatching.Select(0, 6000, 9000, new[] { (3000d, 9200d), (3000d, 12200d) }, 1500), "same bay, closest floor");
        Check(1, ElevationSteelMatching.Select(0, 6000, 9000, new[] { (6500d, 9000d), (3000d, 9200d) }, 1500), "ignore adjacent bay even when vertically closer");
        Check(-2, ElevationSteelMatching.Select(0, 6000, 9000, new[] { (2000d, 9200d), (4000d, 9250d) }, 1500), "ambiguous marks");
        Check(-1, ElevationSteelMatching.Select(0, 6000, 9000, new[] { (3000d, 10600d) }, 1500), "distance cutoff");
        Check(0, ElevationSteelMatching.Select(6000, 0, -500, new[] { (3000d, -300d) }, 1500), "reversed grid and negative elevations");
        Check(-1, ElevationSteelMatching.Select(0, 6000, 0, Array.Empty<(double,double)>(), 1500), "no labels");
        Check(-2, ElevationSteelMatching.Select(0, 6000, 0, new[] { (2000d, 100d), (4000d, -100d) }, 1500), "equal distances above and below");
        Check(0, ElevationSteelMatching.Select(0, 6000, 0, new[] { (0d, 1500d) }, 1500), "boundary inclusivity");
        Console.WriteLine("8 elevation steel matching tests passed.");
    }
}
