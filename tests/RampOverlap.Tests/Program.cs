using RevitMCP.Core;
var samples=Enumerable.Range(0,41).SelectMany(i=>Enumerable.Range(0,6).Select(j=>Tuple.Create(i/40.0,-4000+1000*i/40.0))).ToList();
var fit=RampOverlapFit.Fit(samples,1);if(Math.Abs(fit.Item1+4000)>1e-8||Math.Abs(fit.Item2+3000)>1e-8)throw new Exception("Slope mismatch");
void Reject(List<Tuple<double,double>> data){try{RampOverlapFit.Fit(data,1);}catch(ArgumentException){return;}throw new Exception("Expected rejection");}
Reject(samples.Select((s,i)=>Tuple.Create(s.Item1,s.Item2+(i%6)*10)).ToList());
Reject(samples.Select(s=>Tuple.Create(s.Item1,s.Item2+(s.Item1>.5?50:0))).ToList());
Reject(samples.Where(s=>s.Item1>.1).ToList());
Console.WriteLine("4 overlap plane tests passed");
