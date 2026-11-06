using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace RevitMCP.Core
{
    /// <summary>Native family constraints; no updater or add-in is needed after saving the RFA.</summary>
    internal sealed class FenceParametricBuilder
    {
        private readonly Document doc;
        private readonly FamilyManager fm;
        private readonly View plan;
        private readonly Dictionary<string, FamilyParameter> formulas = new Dictionary<string, FamilyParameter>();
        private readonly Dictionary<string, ReferencePlane> planes = new Dictionary<string, ReferencePlane>();
        private readonly ReferencePlane xBase, yBase;
        private readonly List<Tuple<Extrusion, string[]>> boxes = new List<Tuple<Extrusion, string[]>>();
        internal int Count { get; private set; }
        internal const string W = "單元寬度", H = "圍籬高度", T = "鐵皮厚度", DW = "小門淨寬", DH = "小門淨高";
        private readonly bool gate;
        internal FenceParametricBuilder(Document document, double width, double height, double thickness, bool hasGate, double doorWidth, double doorHeight)
        {
            doc = document; fm = doc.FamilyManager; gate = hasGate;
            plan = new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
                .First(v => !v.IsTemplate && Math.Abs(v.ViewDirection.DotProduct(XYZ.BasisZ)) > .99);
            Input(W, width); Input(H, height); Input(T, thickness);
            if (gate) { Input(DW, doorWidth); Input(DH, doorHeight); }
            xBase = PlaneAt(true, -1000, "X基準"); xBase.Pinned = true;
            yBase = PlaneAt(false, -1000, "Y基準"); yBase.Pinned = true;
        }
        internal void Input(string name, double mm)
        {
            var p = fm.AddParameter(name, GroupTypeId.Geometry, SpecTypeId.Length, false);
            fm.Set(p, mm / 304.8);
        }
        internal FamilyParameter Formula(string expression)
        {
            if (!formulas.TryGetValue(expression, out var p))
            {
                p = fm.AddParameter("連動_" + formulas.Count.ToString("D3"), GroupTypeId.Constraints, SpecTypeId.Length, false);
                formulas.Add(expression, p);
                try { fm.SetFormula(p, expression); }
                catch (Exception ex) { throw new InvalidOperationException("Invalid family formula: " + expression, ex); }
                doc.Regenerate();
            }
            return p;
        }
        internal double Value(string expression) => fm.CurrentType.AsDouble(Formula(expression)).Value;
        private ReferencePlane PlaneAt(bool x, double mm, string name)
        {
            double c = mm / 304.8;
            var rp = doc.FamilyCreate.NewReferencePlane(x ? new XYZ(c,-10,0) : new XYZ(-10,c,0),
                x ? new XYZ(c,10,0) : new XYZ(10,c,0), XYZ.BasisZ, plan);
            rp.Name = name;
            return rp;
        }
        private ReferencePlane Coordinate(bool x, string expression)
        {
            string key = (x ? "X:" : "Y:") + expression;
            if (planes.TryGetValue(key, out var rp)) return rp;
            double val = Value(expression);
            rp = PlaneAt(x, val * 304.8, "連動平面_" + planes.Count);
            planes.Add(key, rp);
            doc.Regenerate();
            var refs = new ReferenceArray(); refs.Append((x ? xBase : yBase).GetReference()); refs.Append(rp.GetReference());
            double offset = -12 - planes.Count * .1;
            var line = Line.CreateBound(x ? new XYZ(-1000/304.8,offset,0) : new XYZ(offset,-1000/304.8,0),
                x ? new XYZ(val,offset,0) : new XYZ(offset,val,0));
            var dim = doc.FamilyCreate.NewLinearDimension(plan, line, refs);
            dim.FamilyLabel = Formula("(" + expression + ") + 1000 mm");
            return rp;
        }
        internal void Box(string x0, string x1, string y0, string y1, string z0, string z1, ElementId material, string name)
        {
            var px0 = Coordinate(true,x0); var px1 = Coordinate(true,x1);
            var py0 = Coordinate(false,y0); var py1 = Coordinate(false,y1);
            double a=Value(x0), b=Value(x1), c=Value(y0), d=Value(y1);
            var pts = new[] {new XYZ(a,c,0),new XYZ(b,c,0),new XYZ(b,d,0),new XYZ(a,d,0)};
            var profile = new CurveArray();
            for(int i=0;i<4;i++) profile.Append(Line.CreateBound(pts[i],pts[(i+1)%4]));
            var profiles=new CurveArrArray(); profiles.Append(profile);
            var sketchPlane=SketchPlane.Create(doc,Plane.CreateByNormalAndOrigin(XYZ.BasisZ,XYZ.Zero));
            var e=doc.FamilyCreate.NewExtrusion(true,profiles,sketchPlane,Value(z1));
            e.get_Parameter(BuiltInParameter.EXTRUSION_START_PARAM).Set(Value(z0));
            e.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM).Set(material);
            e.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set(name);
            fm.AssociateElementParameterToFamilyParameter(e.get_Parameter(BuiltInParameter.EXTRUSION_START_PARAM),Formula(z0));
            fm.AssociateElementParameterToFamilyParameter(e.get_Parameter(BuiltInParameter.EXTRUSION_END_PARAM),Formula(z1));
            doc.Regenerate();
            var curves=e.Sketch.GetAllElements().Select(id=>doc.GetElement(id)).OfType<ModelCurve>().ToList();
            if(curves.Count!=4) throw new InvalidOperationException("Expected four native rectangle sketch edges.");
            foreach(var mc in curves)
            {
                var line=mc.GeometryCurve as Line;
                if(line==null) throw new InvalidOperationException("Unexpected sketch curve.");
                XYZ mid=line.Evaluate(.5,true);
                ReferencePlane target;
                if(Math.Abs(line.Direction.Y)>.99) target=Math.Abs(mid.X-a)<1e-7 ? px0 : px1;
                else target=Math.Abs(mid.Y-c)<1e-7 ? py0 : py1;
                doc.FamilyCreate.NewAlignment(plan,target.GetReference(),mc.GeometryCurve.Reference);
            }
            boxes.Add(Tuple.Create(e,new[]{x0,x1,y0,y1,z0,z1})); Count++;
        }
        internal void Build(ElementId sheet, ElementId frame, ElementId plinth)
        {
            string left="("+W+" - "+DW+") / 2", right="("+W+" + "+DW+") / 2";
            if(gate)
            {
                Box("0 mm",left,"-"+T,"0 mm","180 mm",H,sheet,"左鐵皮");
                Box(right,W,"-"+T,"0 mm","180 mm",H,sheet,"右鐵皮");
                Box(left,right,"-"+T,"0 mm",DH,H,sheet,"門上鐵皮");
                Box("0 mm",left,"-100 mm","100 mm","0 mm","200 mm",plinth,"左防溢座");
                Box(right,W,"-100 mm","100 mm","0 mm","200 mm",plinth,"右防溢座");
                Box(left+" + 5 mm",right+" - 5 mm","-"+T+" - 10 mm","-10 mm","5 mm",DH+" - 5 mm",sheet,"小門門扇_關閉");
                Box(left+" + 5 mm",left+" + 35 mm","-10 mm","20 mm","5 mm",DH+" - 5 mm",frame,"門扇左骨架");
                Box(right+" - 35 mm",right+" - 5 mm","-10 mm","20 mm","5 mm",DH+" - 5 mm",frame,"門扇右骨架");
                Box(left+" + 35 mm",right+" - 35 mm","-10 mm","20 mm","5 mm","35 mm",frame,"門扇下骨架");
                Box(left+" + 35 mm",right+" - 35 mm","-10 mm","20 mm",DH+" - 35 mm",DH+" - 5 mm",frame,"門扇上骨架");
                Box(right+" - 100 mm",right+" - 80 mm","-50 mm","-10 mm",DH+" / 2 - 40 mm",DH+" / 2 + 40 mm",frame,"門把手示意");
                Box(left+" - 50 mm",left,"0 mm","50 mm","0 mm",DH+" + 50 mm",frame,"左門框");
                Box(right,right+" + 50 mm","0 mm","50 mm","0 mm",DH+" + 50 mm",frame,"右門框");
                Box(left,right,"0 mm","50 mm",DH,DH+" + 50 mm",frame,"門框上橫材");
                Rail("60 mm",left,"240 mm",frame); Rail(right,W+" - 60 mm","240 mm",frame);
                Rail("60 mm",W+" - 60 mm","if("+H+" / 2 > "+DH+" + 50 mm, "+H+" / 2, "+DH+" + 50 mm)",frame);
            }
            else
            {
                Box("0 mm",W,"-"+T,"0 mm","180 mm",H,sheet,"鐵皮");
                Box("0 mm",W,"-100 mm","100 mm","0 mm","200 mm",plinth,"防溢座");
                Rail("60 mm",W+" - 60 mm","240 mm",frame);
                Rail("60 mm",W+" - 60 mm",H+" / 2",frame);
            }
            foreach(string x in new[]{"0 mm",W+" - 60 mm"})
            {
                Box(x,x+" + 60 mm","0 mm","5 mm","0 mm",H,frame,"立柱翼板");
                Box(x,x+" + 5 mm","5 mm","60 mm","0 mm",H,frame,"立柱腹板");
                Brace(x+" + 5 mm",x+" + 55 mm",frame);
            }
            Rail("60 mm",W+" - 60 mm",H+" - 60 mm",frame);
            doc.Regenerate(); Verify();
        }
        private void Rail(string a,string b,string z,ElementId mat)
        {
            Box(a,b,"0 mm","60 mm",z,"("+z+") + 5 mm",mat,"橫桿翼板");
            Box(a,b,"0 mm","5 mm","("+z+") + 5 mm","("+z+") + 60 mm",mat,"橫桿腹板");
        }
        private void Brace(string start,string end,ElementId mat)
        {
            // Fixed brace attachment height (1680 mm); bay-width movement follows extrusion offsets.
            // The 50 mm strip is schematic and is not a certified steel profile.
            var pts=new[]{new XYZ(0,45,1680),new XYZ(0,95,1680),new XYZ(0,925,100),new XYZ(0,875,100)};
            var profile=new CurveArray();
            for(int i=0;i<4;i++)profile.Append(Line.CreateBound(pts[i]/304.8,pts[(i+1)%4]/304.8));
            var loops=new CurveArrArray();loops.Append(profile);
            var e=doc.FamilyCreate.NewExtrusion(true,loops,SketchPlane.Create(doc,Plane.CreateByNormalAndOrigin(XYZ.BasisX,XYZ.Zero)),Value(end));
            e.get_Parameter(BuiltInParameter.EXTRUSION_START_PARAM).Set(Value(start));
            fm.AssociateElementParameterToFamilyParameter(e.get_Parameter(BuiltInParameter.EXTRUSION_START_PARAM),Formula(start));
            fm.AssociateElementParameterToFamilyParameter(e.get_Parameter(BuiltInParameter.EXTRUSION_END_PARAM),Formula(end));
            e.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM).Set(mat); Count++;
            foreach(string expression in new[]{"45 mm","925 mm","100 mm","1680 mm"}) Formula(expression);
            boxes.Add(Tuple.Create(e,new[]{start,end,"45 mm","925 mm","100 mm","1680 mm"}));
        }
        internal void Verify()
        {
            doc.Regenerate();
            foreach(var item in boxes)
            {
                var box=item.Item1.get_BoundingBox(null);
                var actual=new[]{box.Min.X,box.Max.X,box.Min.Y,box.Max.Y,box.Min.Z,box.Max.Z};
                for(int i=0;i<6;i++)if(Math.Abs(actual[i]-Value(item.Item2[i]))*304.8>.1)
                    throw new InvalidOperationException("Parametric fence failed geometric verification: "+item.Item2[i]);
            }
        }
        internal void Flex(double width,double height,double thickness,double doorWidth,double doorHeight)
        {
            using(var tx=new Transaction(doc,"Verify fence parameter flex"))
            {
                tx.Start();
                tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(new FenceFailures()).SetClearAfterRollback(true));
                fm.Set(fm.get_Parameter(W),width/304.8);fm.Set(fm.get_Parameter(H),height/304.8);fm.Set(fm.get_Parameter(T),thickness/304.8);
                if(gate){fm.Set(fm.get_Parameter(DW),doorWidth/304.8);fm.Set(fm.get_Parameter(DH),doorHeight/304.8);}
                Verify();
                if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Flex transaction failed.");
            }
        }
        internal sealed class FenceFailures : IFailuresPreprocessor
        {
            public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
            {
                return accessor.GetFailureMessages().Any(m=>m.GetSeverity() == FailureSeverity.Error)
                    ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
            }
        }
    }
}
