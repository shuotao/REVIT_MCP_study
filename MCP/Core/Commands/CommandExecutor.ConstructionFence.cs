using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

namespace RevitMCP.Core
{
    public partial class CommandExecutor
    {
        // Standalone family document only: never transact against or load into the host project.
        private object ExportConstructionFence(JObject parameters, JObject spec)
        {
            if(spec.Value<bool?>("greenFence")==true)return ExportGreenFence(parameters,spec);
            if(spec.Value<bool?>("curtainLouver")==true)return ExportCurtainLouver(parameters,spec);
            if(spec.Value<bool?>("siteGate")==true)return ExportNewSiteGate(parameters,spec);
            string template = spec.Value<string>("templatePath");
            string folder = parameters.Value<string>("outputFolder");
            if (string.IsNullOrWhiteSpace(template) || !Path.IsPathRooted(template) ||
                !File.Exists(template) || !string.Equals(Path.GetExtension(template), ".rft", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Provide an existing absolute unhosted metric Generic Model .rft path.");
            if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathRooted(folder))
                throw new ArgumentException("outputFolder must be absolute.");
            if (_uiApp.Application.VersionNumber != "2024")
                throw new InvalidOperationException("This reusable 2024 family must be generated in Revit 2024 (no down-saving).");
            double width = FenceDimension(spec, "widthMm", 2400, 1200, 3000);
            double height = FenceDimension(spec, "heightMm", 2400, 2400, 4500);
            double sheet = FenceDimension(spec, "sheetThicknessMm", 1.2, 1.2, 3);
            bool hasGate = spec["door"] is JObject;
            var door = spec["door"] as JObject ?? new JObject();
            double doorWidth = FenceDimension(door, "widthMm", 1000, 600, width - 200);
            double doorHeight = FenceDimension(door, "heightMm", 1200, 800, height - 200);
            string stem = "台中_可調式固定鐵皮圍籬" + (hasGate ? "_置中小門" : "") + "_" + width.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) +
                "x" + height.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) +
                "_t" + sheet.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            string path = Path.Combine(Path.GetFullPath(folder), stem + ".rfa");
            bool apply = parameters.Value<bool?>("apply") == true && parameters.Value<bool?>("dryRun") == false;
            var assumptions = new[] {
                "Type parameters drive native geometry: bay width, fence height, sheet thickness; optional centered door clear width/height. Door leaf is closed with 5 mm edge clearance.",
                "Brace attachment remains 1680 mm above ground when fence height changes; brace positions follow bay width.",
                "Post/rail L60x60x5, brace 50mm schematic strip with rear endpoint at 900mm plus 25mm profile extent, and plinth 200x200mm are modeling assumptions, not prescribed structural design.",
                "Positive Y is site interior. Each standalone bay includes both end posts; repeated bays need shared-post detailing.",
                "Anchors, foundations, wind design, gates, corner sightlines, lights and required greening are site-specific and not certified by this family."
            };
            if (!apply) return new { Success = true, DryRun = true, OutputPath = path, WidthMm = width,
                HeightMm = height, SheetThicknessMm = sheet, ExistingFile = File.Exists(path), Assumptions = assumptions,
                Source = "https://law.taichung.gov.tw/LawContent.aspx?id=GL001332 (Article 17)" };
            if (File.Exists(path)) throw new IOException("Output exists; choose a new outputFolder. Existing families are never overwritten.");
            Document family = null;
            string staging = null;
            string stagingFolder = null;
            int count = 0;
            FenceParametricBuilder builder = null;
            try
            {
                family = _uiApp.Application.NewFamilyDocument(template);
                if (family == null || !family.IsFamilyDocument || family.OwnerFamily.FamilyPlacementType != FamilyPlacementType.OneLevelBased)
                    throw new InvalidOperationException("Template must be an unhosted, one-level-based model family.");
                using (var tx = new Transaction(family, "Create Taichung fixed sheet-metal fence"))
                {
                    tx.Start();
                    tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(new FenceParametricBuilder.FenceFailures()).SetClearAfterRollback(true));
                    family.OwnerFamily.FamilyCategory = Category.GetCategory(family, BuiltInCategory.OST_GenericModel);
                    var fm = family.FamilyManager;
                    fm.NewType(stem);
                    var note = fm.AddParameter("設計說明", GroupTypeId.IdentityData, SpecTypeId.String.Text, false);
                    fm.Set(note, "台中施工圍籬；類型參數連動寬高板厚及小門淨寬淨高。斜撐接點固定1680mm高。骨架/支撐/防溢座為建模假設，非結構設計。門口無防溢座，排水措施另定。");
                    var url = fm.AddParameter("法規來源", GroupTypeId.IdentityData, SpecTypeId.String.Text, false);
                    fm.Set(url, "https://law.taichung.gov.tw/LawContent.aspx?id=GL001332");
                    ElementId metal = Material.Create(family, "圍籬_鍍鋅鋼板");
                    ((Material)family.GetElement(metal)).Color = new Color(185, 195, 200);
                    ElementId frame = Material.Create(family, "圍籬_鋼骨架");
                    ((Material)family.GetElement(frame)).Color = new Color(85, 95, 105);
                    ElementId concrete = Material.Create(family, "圍籬_混凝土防溢座");
                    ((Material)family.GetElement(concrete)).Color = new Color(150, 150, 145);
                    builder = new FenceParametricBuilder(family, width, height, sheet, hasGate, doorWidth, doorHeight);
                    builder.Build(metal, frame, concrete);
                    count = builder.Count;
                    family.Regenerate();
                    FenceVerify(family, count, width, height);
                    if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Family transaction did not commit.");
                }
                // Exercise real constraints and all component bounds before returning to requested values.
                builder.Flex(1800, 3000, 1.5, 900, 1300);
                builder.Flex(3000, 4500, 3, 1100, 1500);
                builder.Flex(width, height, sheet, doorWidth, doorHeight);
                Directory.CreateDirectory(folder);
                stagingFolder = Path.Combine(folder, ".fence-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(stagingFolder);
                staging = Path.Combine(stagingFolder, stem + ".rfa");
                family.SaveAs(staging, new SaveAsOptions { OverwriteExistingFile = false, MaximumBackups = 1 });
                family.Close(false); family = null;
                family = _uiApp.Application.OpenDocumentFile(staging);
                FenceVerify(family, count, width, height);
                using (var flex = new Transaction(family, "Verify saved family constraints"))
                {
                    flex.Start();
                    flex.SetFailureHandlingOptions(flex.GetFailureHandlingOptions().SetFailuresPreprocessor(new FenceParametricBuilder.FenceFailures()).SetClearAfterRollback(true));
                    var fm = family.FamilyManager;
                    fm.Set(fm.get_Parameter(FenceParametricBuilder.W), 2800 / 304.8);
                    fm.Set(fm.get_Parameter(FenceParametricBuilder.H), 3200 / 304.8);
                    fm.Set(fm.get_Parameter(FenceParametricBuilder.T), 2 / 304.8);
                    family.Regenerate();
                    FenceVerify(family, count, 2800, 3200);
                    flex.RollBack();
                }
                family.Close(false); family = null;
                File.Move(staging, path); staging = null;
                return new { Success = true, DryRun = false, OutputPath = path, RevitVersion = "2024",
                    NativeExtrusions = count, WidthMm = width, HeightMm = height, SheetThicknessMm = sheet,
                    ReopenedAndVerified = true, ParametricFlexCasesPassed = 4, HasCenteredDoor = hasGate,
                    DoorClearWidthMm = hasGate ? (double?)doorWidth : null, DoorClearHeightMm = hasGate ? (double?)doorHeight : null,
                    HostProjectModified = false, Assumptions = assumptions };
            }
            finally
            {
                if (family != null && family.IsValidObject) family.Close(false);
                if (staging != null && File.Exists(staging)) File.Delete(staging);
                if (stagingFolder != null && Directory.Exists(stagingFolder) && !Directory.EnumerateFileSystemEntries(stagingFolder).Any())
                    Directory.Delete(stagingFolder);
            }
        }

        private static double FenceDimension(JObject spec, string name, double fallback, double min, double max)
        {
            double value = spec.Value<double?>(name) ?? fallback;
            if (double.IsNaN(value) || double.IsInfinity(value) || value < min || value > max)
                throw new ArgumentOutOfRangeException(name, "Outside supported range.");
            return value;
        }
        private static void FenceRecord(FamilyManager fm, string name, double mm)
        {
            var p = fm.AddParameter(name, GroupTypeId.Geometry, SpecTypeId.Length, false);
            fm.SetFormula(p, mm.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture) + " mm");
        }
        private static void FenceBox(Document doc, double x0, double y0, double z0, double x1, double y1, double z1, ElementId material)
        {
            FenceExtrusion(doc, new XYZ(x0,y0,z0), XYZ.BasisX, XYZ.BasisY, XYZ.BasisZ,
                new[] {new UV(0,0),new UV(x1-x0,0),new UV(x1-x0,y1-y0),new UV(0,y1-y0)}, z1-z0, material);
        }
        private static void FenceAngle(Document doc, XYZ origin, XYZ u, XYZ v, XYZ axis, double length, ElementId material)
        {
            FenceExtrusion(doc, origin,u,v,axis,new[] {new UV(0,0),new UV(60,0),new UV(60,5),
                new UV(5,5),new UV(5,60),new UV(0,60)},length,material);
        }
        private static void FenceExtrusion(Document doc, XYZ originMm, XYZ u, XYZ v, XYZ axis, UV[] outline, double lengthMm, ElementId material)
        {
            XYZ origin = originMm / 304.8;
            var loop = new CurveArray();
            for (int i=0; i<outline.Length; i++)
            {
                UV a = outline[i], b = outline[(i+1)%outline.Length];
                loop.Append(Line.CreateBound(origin+(u*a.U+v*a.V)/304.8, origin+(u*b.U+v*b.V)/304.8));
            }
            var loops = new CurveArrArray(); loops.Append(loop);
            var plane = SketchPlane.Create(doc, Plane.CreateByNormalAndOrigin(axis,origin));
            var solid = doc.FamilyCreate.NewExtrusion(true, loops, plane, lengthMm/304.8);
            solid.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM).Set(material);
        }
        private static void FenceVerify(Document doc, int expected, double width, double height)
        {
            var extrusions = new FilteredElementCollector(doc).OfClass(typeof(Extrusion)).Cast<Extrusion>().ToList();
            if (!doc.IsFamilyDocument || extrusions.Count != expected) throw new InvalidOperationException("Unexpected family geometry count.");
            double minX = double.PositiveInfinity, maxX = double.NegativeInfinity, minZ = double.PositiveInfinity, maxZ = double.NegativeInfinity;
            foreach (var e in extrusions)
            {
                var solids = e.get_Geometry(new Options()).OfType<Solid>().Where(s=>s.Volume>0).ToList();
                if (solids.Count != 1) throw new InvalidOperationException("Invalid native solid.");
                var box = e.get_BoundingBox(null);
                minX = Math.Min(minX,box.Min.X); maxX = Math.Max(maxX,box.Max.X);
                minZ = Math.Min(minZ,box.Min.Z); maxZ = Math.Max(maxZ,box.Max.Z);
            }
            if (Math.Abs((maxX-minX)*304.8-width)>0.1 || Math.Abs((maxZ-minZ)*304.8-height)>0.1)
                throw new InvalidOperationException("Family envelope differs from requested dimensions.");
        }
    }
}
