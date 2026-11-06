using System;
using RevitMCP.Core;
using S=RevitMCP.Core.WallTopExtremes.Segment;
class Program
{
    static void Near(double a,double b){if(Math.Abs(a-b)>1e-6)throw new Exception($"Expected {b}, got {a}");}
    static void Main()
    {
        var f=WallTopExtremes.Solve(new[]{new S{U0=100,U1=300,Z0=250,Z1=250,Source=1},new S{U0=5100,U1=5300,Z0=0,Z1=0,Source=2}});
        Near(f.High.U,200);Near(f.Low.U,5200);Near(f.At(0),260);Near(f.At(5750),-27.5);
        var reverse=WallTopExtremes.Solve(new[]{new S{U0=100,U1=300,Z0=0,Z1=0},new S{U0=5100,U1=5300,Z0=250,Z1=250}});
        Near(reverse.At(0),-10);Near(reverse.At(5750),277.5);
        var envelope=WallTopExtremes.Solve(new[]{new S{U0=0,U1=1000,Z0=100,Z1=0},new S{U0=0,U1=1000,Z0=0,Z1=100}});
        Near(envelope.Low.Z,50);Near(envelope.Low.U,500);
        var flat=WallTopExtremes.Solve(new[]{new S{U0=0,U1=1000,Z0=-100,Z1=-100}});Near(flat.At(3000),-100);
        var occluded=WallTopExtremes.Solve(new[]{new S{U0=0,U1=1000,Z0=0,Z1=0},new S{U0=0,U1=1000,Z0=100,Z1=200}});Near(occluded.Low.Z,100);Near(occluded.High.Z,200);
        var gap=WallTopExtremes.Solve(new[]{new S{U0=0,U1=100,Z0=100,Z1=100},new S{U0=900,U1=1000,Z0=100,Z1=100},new S{U0=1500,U1=1600,Z0=0,Z1=0}});
        if(gap.High.U>100&&gap.High.U<900)throw new Exception("Point chosen in empty gap");
        bool rejected=false;try{WallTopExtremes.Solve(Array.Empty<S>());}catch(ArgumentException){rejected=true;}if(!rejected)throw new Exception("Empty accepted");
        var lower=WallTopExtremes.Solve(new[]{new S{U0=0,U1=1000,Z0=-200,Z1=-100},new S{U0=0,U1=1000,Z0=0,Z1=100}},true);
        Near(lower.Low.Z,-200);Near(lower.High.Z,-100);
        var crossing=WallTopExtremes.Solve(new[]{new S{U0=0,U1=1000,Z0=-100,Z1=0},new S{U0=0,U1=1000,Z0=0,Z1=-100}},true);
        Near(crossing.High.Z,-50);Near(crossing.High.U,500);Near(crossing.Low.Z,-100);
        Console.WriteLine("9 wall-top extrema scenarios passed (including lower envelope).");
    }
}
