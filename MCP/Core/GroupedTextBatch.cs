using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

namespace RevitMCP.Core
{
    public partial class CommandExecutor
    {
        private object SetGroupedTextBatch(JObject input)
        {
            if(input["floorNumberRules"]!=null || input.Value<bool?>("copyTypeNameBatch")==true || input.Value<bool?>("rcWallsStructuralBatch")==true)
                throw new ArgumentException("textUpdates cannot be combined with other batch modes.");
            var updates=input["textUpdates"] as JArray;
            if(updates==null || updates.Count<1 || updates.Count>2000) throw new ArgumentException("Requires 1..2000 textUpdates.");
            var doc=_uiApp.ActiveUIDocument.Document;
            var rows=new JArray();
            var plan=new List<Tuple<Parameter,string,JObject>>();
            var keys=new HashSet<string>();
            foreach(var item in updates)
            {
                if(!(item is JObject u) || u["elementId"]?.Type!=JTokenType.Integer || u["value"]?.Type!=JTokenType.String || string.IsNullOrWhiteSpace(u.Value<string>("name")))
                    throw new ArgumentException("Each update requires integer elementId and string name/value.");
                long id=u.Value<long>("elementId"); string name=u.Value<string>("name"),value=u.Value<string>("value");
                if(!keys.Add(id+":"+name)) throw new ArgumentException("Duplicate element/parameter update.");
                var row=new JObject{["ElementId"]=id,["Name"]=name,["Group"]="Text",["NewValue"]=value,["Status"]="Skipped"}; rows.Add(row);
                var e=doc.GetElement(new ElementId(id));
                if(e==null || e is ElementType){row["Reason"]="MissingInstance";continue;}
                if(e.GroupId!=ElementId.InvalidElementId){row["Reason"]="GroupedElement";continue;}
                var matches=e.Parameters.Cast<Parameter>().Where(p=>p.Definition.Name==name && p.Definition.GetGroupTypeId()==GroupTypeId.Text).ToList();
                if(matches.Count!=1){row["Reason"]=matches.Count==0?"MissingTextParameter":"AmbiguousTextParameter";continue;}
                var parameter=matches[0];
                if(parameter.IsReadOnly || parameter.StorageType!=StorageType.String){row["Reason"]=parameter.IsReadOnly?"ReadOnly":"IncompatibleStorage";continue;}
                row["OldValue"]=parameter.AsString();
                if(parameter.AsString()==value){row["Status"]="Unchanged";continue;}
                row["Status"]="Planned";plan.Add(Tuple.Create(parameter,value,row));
            }
            bool write=input.Value<bool?>("apply")==true && input.Value<bool?>("dryRun")==false;
            if(write && plan.Count>0)
            {
                using(var group=new TransactionGroup(doc,"Batch grouped text parameters"))
                using(var tx=new Transaction(doc,"Write text number and Floor"))
                {
                    group.Start();tx.Start();
                    tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(new ColumnBatchFailures()).SetClearAfterRollback(true));
                    foreach(var q in plan) if(!q.Item1.Set(q.Item2))throw new InvalidOperationException("Text parameter Set failed; batch rolled back.");
                    doc.Regenerate();
                    if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Revit rolled back text batch.");
                    foreach(var q in plan) if(q.Item1.AsString()!=q.Item2)throw new InvalidOperationException("Text readback failed; batch rolled back.");
                    group.Assimilate();
                    foreach(var q in plan)q.Item3["Status"]="Updated";
                }
            }
            return new {Scanned=rows.Count,Planned=plan.Count,Updated=write?plan.Count:0,
                Unchanged=rows.Count(r=>(string)r["Status"]=="Unchanged"),Skipped=rows.Count(r=>(string)r["Status"]=="Skipped"),Applied=write,Results=rows};
        }
    }
}
