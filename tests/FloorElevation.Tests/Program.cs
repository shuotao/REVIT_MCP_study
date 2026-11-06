using RevitMCP.Core;
using T=RevitMCP.Core.FloorCadElevation.Text;
void Check(bool ok,string s){if(!ok)throw new Exception(s);Console.WriteLine("PASS "+s);}
List<FloorCadElevation.Match> Run(T[] n,T[] e,bool fold=true)=>FloorCadElevation.Resolve(n,e,2607,355,10,1800,100,new HashSet<string>{"ST1(上)","ES1(斜版)","CES"},fold);
var r=Run(new[]{new T{Value="S1",X=0},new T{Value="ES2",X=5000}},new[]{new T{Value="(-25)",X=600}});
Check(r[0].LevelId==355&&r[0].OffsetMm==-250&&r[1].LevelId==2607&&r[1].OffsetMm==0,"numeric cm versus no annotation");
r=Run(new[]{new T{Value="ES4(折板)"},new T{Value="ST1(上)",X=5000}},Array.Empty<T>());
Check(r[0].Code=="ES4"&&r[0].LevelId==2607&&r[0].Reason==null&&r[1].Reason=="ExcludedCadLabel","folded flat opt-in and exact exclusions");
Check(Run(new[]{new T{Value="ES4(折板)"}},Array.Empty<T>(),false)[0].Reason!=null,"folded requires consent");
r=Run(new[]{new T{Value="S1",X=0},new T{Value="S2",X=1000}},new[]{new T{Value="(+25)",X=500}});
Check(r.All(x=>x.Reason=="AmbiguousElevationPair"),"ambiguous pair never defaults");
r=Run(new[]{new T{Value="S1"}},new[]{new T{Value="(+25)",X=500},new T{Value="(-55)",X=600}});
Check(r[0].Reason=="ConflictingElevationAnnotations","conflicting heights skipped");
Check(Run(new[]{new T{Value="S1"}},new[]{new T{Value="(-55)",X=5000}})[0].Reason=="ElevationOutsidePairingRange","orphan height marks nearest as unresolved");
Check(Run(new[]{new T{Value="ES1（+20）"}},Array.Empty<T>())[0].OffsetMm==200,"full-width parentheses");
Check(Run(new[]{new T{Value="S1"}},new[]{new T{Value="(車道下)",X=300}})[0].Reason!=null,"nonnumeric unknown is not default");
Check(Run(new[]{new T{Value="S1",X=0,Y=3000},new T{Value="S2",X=0,Y=0}},new[]{new T{Value="(+25)",X=0,Y=3500}})[0].OffsetMm==250,"pairing uses both plan coordinates");
if(args.Length==2){
 T[] Read(string path){using var d=System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(path).TrimStart('\uFEFF'));return d.RootElement.GetProperty("data").EnumerateArray().Select(e=>new T{Value=e.GetProperty("text").GetString(),X=e.GetProperty("x").GetDouble()*10,Y=e.GetProperty("y").GetDouble()*10}).ToArray();}
 var actual=Run(Read(args[0]),Read(args[1]));
 Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(actual.GroupBy(m=>new{m.LevelId,m.OffsetMm,m.Reason}).Select(g=>new{g.Key,Count=g.Count()})));
}
