using System;
using System.Collections.Generic;
using System.Linq;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Polygonize;

namespace RevitMCP.Core
{
    // Coordinates in feet, independent of Revit so candidate selection is testable offline.
    internal static class TerrainRectangleBoundary
    {
        internal static Polygon Select(IList<List<Coordinate>> paths)
        {
            var gf = new GeometryFactory(); var candidates = new List<Polygon>();
            foreach(var path in paths) { var rectangle=Rectangle(path,gf); if(rectangle!=null)candidates.Add(rectangle); }
            var polygonizer=new Polygonizer(); var edges=new HashSet<string>();
            foreach(var path in paths) for(int i=1;i<path.Count;i++)
            {
                var a=Snap(path[i-1]);var b=Snap(path[i]);if(a.Equals2D(b))continue;
                string ka=Key(a),kb=Key(b),key=string.CompareOrdinal(ka,kb)<0?ka+"/"+kb:kb+"/"+ka;
                if(edges.Add(key))polygonizer.Add(gf.CreateLineString(new[]{a,b}));
            }
            foreach(var p in polygonizer.GetPolygons().Cast<Polygon>())
            {
                if(p.NumInteriorRings!=0)continue;
                var rectangle=Rectangle(p.ExteriorRing.Coordinates.ToList(),gf);if(rectangle!=null)candidates.Add(rectangle);
            }
            var unique=new List<Polygon>();
            foreach(var p in candidates.OrderByDescending(p=>p.Area))
                if(!unique.Any(q=>Math.Abs(q.Area-p.Area)<1e-6 && q.Centroid.Distance(p.Centroid)<0.001))unique.Add(p);
            if(unique.Count==0)throw new InvalidOperationException("No rectangular CAD boundary found; no geometry was inferred from a bounding box.");
            if(unique.Count>1 && Math.Abs(unique[0].Area-unique[1].Area)<0.001)
                throw new InvalidOperationException("Largest rectangle is ambiguous (equal areas).");
            return unique[0];
        }
        private static Coordinate Snap(Coordinate p)=>new Coordinate(Math.Round(p.X*3048)/3048,Math.Round(p.Y*3048)/3048);
        private static string Key(Coordinate p)=>Math.Round(p.X*3048).ToString(System.Globalization.CultureInfo.InvariantCulture)+","+Math.Round(p.Y*3048).ToString(System.Globalization.CultureInfo.InvariantCulture);
        private static Polygon Rectangle(IList<Coordinate> raw,GeometryFactory gf)
        {
            var p=new List<Coordinate>();
            foreach(var v in raw)if(p.Count==0 || p.Last().Distance(v)>0.0001)p.Add(v.Copy());
            if(p.Count>1 && p[0].Distance(p.Last())<0.001)p.RemoveAt(p.Count-1);
            bool changed=true;
            while(changed && p.Count>4)
            {
                changed=false;
                for(int i=0;i<p.Count;i++)
                {
                    var a=p[(i+p.Count-1)%p.Count];var b=p[i];var c=p[(i+1)%p.Count];
                    double dx=c.X-a.X,dy=c.Y-a.Y,len=Math.Sqrt(dx*dx+dy*dy);
                    if(len>0 && Math.Abs(dx*(b.Y-a.Y)-dy*(b.X-a.X))/len<0.1/304.8 && (b.X-a.X)*(b.X-c.X)+(b.Y-a.Y)*(b.Y-c.Y)<=0)
                    {p.RemoveAt(i);changed=true;break;}
                }
            }
            if(p.Count!=4)return null;
            for(int i=0;i<4;i++)
            {
                var a=p[i];var b=p[(i+1)%4];var c=p[(i+2)%4];
                double ux=b.X-a.X,uy=b.Y-a.Y,vx=c.X-b.X,vy=c.Y-b.Y;
                double lengths=Math.Sqrt((ux*ux+uy*uy)*(vx*vx+vy*vy));
                if(lengths<1e-6 || Math.Abs(ux*vx+uy*vy)/lengths>0.0001)return null;
            }
            p.Add(p[0].Copy());var polygon=gf.CreatePolygon(p.ToArray());
            return polygon.IsValid && polygon.Area>1e-6?polygon:null;
        }
    }
}
