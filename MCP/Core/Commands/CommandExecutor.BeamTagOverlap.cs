using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Newtonsoft.Json.Linq;

namespace RevitMCP.Core
{
    public partial class CommandExecutor
    {
        private sealed class TagRectangle
        {
            public double X0, Y0, X1, Y1;
            public TagRectangle Shift(double x, double y) => new TagRectangle { X0=X0+x, X1=X1+x, Y0=Y0+y, Y1=Y1+y };
            public bool Intersects(TagRectangle b, double gap) => X0 < b.X1+gap && X1+gap > b.X0 && Y0 < b.Y1+gap && Y1+gap > b.Y0;
        }

        private static TagRectangle ReadTagRectangle(Element tag, View view)
        {
            // Never call TagText: confirmed native crash in Revit 2025.
            var box = tag.get_BoundingBox(view);
            if (box == null) return null;
            var points = new List<XYZ>();
            for (int i=0;i<8;i++) points.Add(box.Transform.OfPoint(new XYZ(
                (i&1)==0?box.Min.X:box.Max.X, (i&2)==0?box.Min.Y:box.Max.Y, (i&4)==0?box.Min.Z:box.Max.Z)));
            var xs=points.Select(p=>p.DotProduct(view.RightDirection)).ToArray();
            var ys=points.Select(p=>p.DotProduct(view.UpDirection)).ToArray();
            if (xs.Any(x=>double.IsNaN(x)||double.IsInfinity(x)) || ys.Any(y=>double.IsNaN(y)||double.IsInfinity(y))) return null;
            return new TagRectangle { X0=xs.Min(), X1=xs.Max(), Y0=ys.Min(), Y1=ys.Max() };
        }

        private object AvoidBeamTagOverlap(JObject args)
        {
            var doc=_uiApp.ActiveUIDocument.Document;
            var view=doc.ActiveView;
            if (args["viewId"]?.Value<long>()!=view.Id.GetIdValue()) throw new ArgumentException("Active view changed.");
            var input=args["elementIds"] as JArray;
            if(input==null||input.Count==0||input.Count>10000)throw new ArgumentException("Explicit tag IDs required.");
            double gapMm=args["gapPaperMm"]?.Value<double>()??0.5, maxMm=args["maxMovePaperMm"]?.Value<double>()??8;
            if(double.IsNaN(gapMm)||double.IsInfinity(gapMm)||double.IsNaN(maxMm)||double.IsInfinity(maxMm)||gapMm<0||gapMm>3||maxMm<0.5||maxMm>20)throw new ArgumentException("Invalid movement limits.");
            double gap=gapMm*view.Scale/304.8, max=maxMm*view.Scale/304.8, step=0.5*view.Scale/304.8;
            var all=new FilteredElementCollector(doc,view.Id).OfClass(typeof(IndependentTag)).Cast<IndependentTag>()
                .Where(t=>t.OwnerViewId==view.Id).ToDictionary(t=>(long)t.Id.GetIdValue());
            var boxes=new Dictionary<long,TagRectangle>();
            foreach(var pair in all){var b=ReadTagRectangle(pair.Value,view);if(b!=null)boxes[pair.Key]=b;}
            var unknown=all.Keys.Where(id=>!boxes.ContainsKey(id)).ToArray();
            var rows=new JArray();
            var targets=new Dictionary<long,XYZ>();
            foreach(long id in input.Values<long>().Distinct().OrderBy(x=>x))
            {
                var row=new JObject{["ElementId"]=id,["Status"]="Skipped"};rows.Add(row);
                if(!all.TryGetValue(id,out var tag)||tag.Category.Id.GetIdValue()!=(long)BuiltInCategory.OST_StructuralFramingTags)
                {row["Reason"]="NotVisibleBeamTag";continue;}
                if(!boxes.TryGetValue(id,out var box)){row["Reason"]="MissingViewBoundingBox";continue;}
                if(!boxes.Any(p=>p.Key!=id&&box.Intersects(p.Value,gap))){row["Status"]="Unchanged";continue;}
                if(tag.Pinned||tag.IsOrphaned||tag.HasLeader||tag.GroupId!=ElementId.InvalidElementId)
                {row["Reason"]="PinnedOrOrphanedOrLeaderOrGrouped";continue;}
                var refs=tag.GetTaggedElementIds().ToList();
                var beam=refs.Count==1&&refs[0].HostElementId!=ElementId.InvalidElementId?doc.GetElement(refs[0].HostElementId) as FamilyInstance:null;
                if(beam==null||beam.StructuralType!=StructuralType.Beam||!(beam.Location is LocationCurve loc)||!(loc.Curve is Line line))
                {row["Reason"]="RequiresSingleLocalStraightBeam";continue;}
                var a=line.GetEndPoint(0);var b=line.GetEndPoint(1);
                var along=b-a;along-=view.ViewDirection.Multiply(along.DotProduct(view.ViewDirection));
                if(along.GetLength()<1e-6){row["Reason"]="BeamPerpendicularToView";continue;}
                double length=along.GetLength();along=along.Normalize();
                var across=view.ViewDirection.CrossProduct(along).Normalize();
                var old=tag.TagHeadPosition;
                var offsets=new List<XYZ>();
                for(double d=step;d<=max+1e-8;d+=step){offsets.Add(along*d);offsets.Add(along*(-d));}
                for(double d=step;d<=max+1e-8;d+=step){offsets.Add(across*d);offsets.Add(across*(-d));}
                bool found=false;
                foreach(var delta in offsets)
                {
                    var target=old+delta;
                    double station=(target-a).DotProduct(along);
                    if(station<0||station>length)continue;
                    var candidate=box.Shift(delta.DotProduct(view.RightDirection),delta.DotProduct(view.UpDirection));
                    if(boxes.Any(p=>p.Key!=id&&candidate.Intersects(p.Value,gap)))continue;
                    boxes[id]=candidate;targets[id]=target;found=true;
                    row["Status"]="Planned";row["BeamId"]=beam.Id.GetIdValue();row["DistanceMm"]=delta.GetLength()*304.8;
                    row["OldPositionMm"]=new JArray(old.X*304.8,old.Y*304.8,old.Z*304.8);
                    row["NewPositionMm"]=new JArray(target.X*304.8,target.Y*304.8,target.Z*304.8);break;
                }
                if(!found)row["Reason"]="NoFreePositionWithinLimit";
            }
            bool dry=args["dryRun"]?.Value<bool>()??true,apply=args["apply"]?.Value<bool>()??false,committed=false;
            string error=null;
            if(!dry&&apply&&targets.Count>0)using(var group=new TransactionGroup(doc,"Avoid beam tag overlaps"))
            {
                group.Start();
                try{
                    using(var tx=new Transaction(doc,"Adjust overlapping beam tags")){
                        tx.Start();tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(new FramingFloorFailures()).SetClearAfterRollback(true));
                        foreach(var p in targets)all[p.Key].TagHeadPosition=p.Value;
                        if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Commit failed.");
                    }
                    var actual=all.Select(p=>new{p.Key,Box=ReadTagRectangle(p.Value,view)}).Where(p=>p.Box!=null).ToDictionary(p=>p.Key,p=>p.Box);
                    foreach(var p in targets){
                        if(all[p.Key].TagHeadPosition.DistanceTo(p.Value)>0.1/304.8||!actual.ContainsKey(p.Key))throw new InvalidOperationException("Position verification failed.");
                        if(actual.Any(b=>b.Key!=p.Key&&actual[p.Key].Intersects(b.Value,Math.Max(0,gap-0.1/304.8))))throw new InvalidOperationException("Regenerated tag still overlaps; batch rolled back.");
                    }
                    if(group.Assimilate()!=TransactionStatus.Committed)throw new InvalidOperationException("Group commit failed.");
                    committed=true;
                    foreach(var r in rows.Where(r=>(string)r["Status"]=="Planned"))r["Status"]="Updated";
                }catch(Exception ex){if(group.GetStatus()==TransactionStatus.Started)group.RollBack();error=ex.Message;foreach(var r in rows.Where(r=>(string)r["Status"]=="Planned")){r["Status"]="Failed";r["Reason"]="BatchRolledBack";}}
            }
            return new{Success=error==null,DryRun=dry,Apply=apply,Committed=committed,Error=error,Scanned=rows.Count,Planned=targets.Count,
                Updated=rows.Count(r=>(string)r["Status"]=="Updated"),Unchanged=rows.Count(r=>(string)r["Status"]=="Unchanged"),
                Skipped=rows.Count(r=>(string)r["Status"]=="Skipped"),Failed=rows.Count(r=>(string)r["Status"]=="Failed"),
                UnmeasurableTagIds=unknown,GapPaperMm=gapMm,MaxMovePaperMm=maxMm,Results=rows};
        }
    }
}
