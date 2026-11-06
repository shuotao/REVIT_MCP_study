using RevitMCP.Core;
var cases=new Dictionary<string,string>{
["FWB1"]="60x270",["FWB2"]="60x270",["FWB3"]="60x270",["FWB4"]="60x270",["FWB5"]="60x270",
["FB5"]="70x270",["FG9"]="70x270",["FB12345678901234567890"]="70x270",
["Fb7"]="60x270",["Fg12"]="60x270",["Fb0"]="50x270",[" FB5 "]="70x270",
["FWB6"]=null,["FWB10"]=null,["FB5A"]=null,["FB"]=null,["fb5"]=null,["FB*"]=null,["FB５"]=null,["F G9"]=null};
foreach(var c in cases)if(BeamNumberRules.TypeName(c.Key)!=c.Value)throw new Exception("Failed: "+c.Key);
if(BeamNumberRules.WidthMm("50x270")!=500 || BeamNumberRules.WidthMm("60x270")!=600 || BeamNumberRules.WidthMm("70x270")!=700)throw new Exception("Units failed");
Console.WriteLine("20 rule cases and 3 cm-to-mm width checks passed.");
var rules=new[]{new BeamScheduleRule{Patterns=new[]{"B*","G*"},TypeName="60x80",WidthMm=600,HeightMm=800,BeamRole="大樑"},new BeamScheduleRule{Patterns=new[]{"B1","G9"},TypeName="70x70",WidthMm=700,HeightMm=700,BeamRole="大樑"},new BeamScheduleRule{Patterns=new[]{"b201~b206","SSb1"},TypeName="35x60",WidthMm=350,HeightMm=600,BeamRole="次樑"}};
foreach(var rule in rules)rule.Validate();
foreach(var pair in new Dictionary<string,string>{{"B1","70x70"},{"G9","70x70"},{"B99","60x80"},{"b201","35x60"},{"b206","35x60"},{"SSb1","35x60"},{"b207",null},{"b1",null},{"B1a",null},{"SSB1",null}})if(BeamScheduleRule.Resolve(rules,pair.Key)?.TypeName!=pair.Value)throw new Exception("Custom rule failed: "+pair.Key);
bool invalid=false;try{BeamScheduleRule.Score("b206~b201","b203");}catch(ArgumentException){invalid=true;}if(!invalid)throw new Exception("Reversed range accepted");
invalid=false;try{BeamScheduleRule.Resolve(new[]{rules[0],rules[0]},"B9");}catch(ArgumentException){invalid=true;}if(!invalid)throw new Exception("Ambiguous rule accepted");
Console.WriteLine("12 custom rule assertions passed.");
foreach(var c in new[]{("B1", "B1", (double?)null),("B1(-15)","B1",(double?)-15),(" b201（ +12.5 ） ","b201",(double?)12.5),("SSg1(0)","SSg1",(double?)0),("G9(-.5)","G9",(double?)-.5)}){
 var parsed=BeamElevationLabel.Parse(c.Item1);if(parsed.Number!=c.Item2||parsed.Offset!=c.Item3)throw new Exception("Elevation parse failed: "+c.Item1);
}
foreach(string bad in new[]{"B1()","B1(-15)extra","B1(foo)","B1(1,5)","B1(1)(2)","B1（15)","B1(15）","B1(-15cm)","B1(NaN)","B1(1e4)","b*"}){
 bool rejected=false;try{BeamElevationLabel.Parse(bad);}catch(ArgumentException){rejected=true;}if(!rejected)throw new Exception("Invalid elevation accepted: "+bad);
}
Console.WriteLine("16 elevation label assertions passed.");
