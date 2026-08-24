using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

#if REVIT2025_OR_GREATER
using IdType = System.Int64;
#else
using IdType = System.Int32;
#endif

namespace RevitMCP.Core
{
    /// <summary>
    /// Maps the existing ClashDetector result into the conservative, read-only
    /// opening-candidate contract. It deliberately does not own a second clash engine.
    /// </summary>
    internal sealed class OpeningCandidateScanner
    {
        private const double MmPerFoot = 304.8;
        private const double FaceDistanceToleranceFeet = 1.0 / MmPerFoot;
        private const double OrthogonalityTolerance = 1e-6;

        private readonly UIApplication _uiApp;
        private readonly LinkedModelHelper _linkHelper;

        public OpeningCandidateScanner(UIApplication uiApp, LinkedModelHelper linkHelper)
        {
            _uiApp = uiApp ?? throw new ArgumentNullException(nameof(uiApp));
            _linkHelper = linkHelper ?? throw new ArgumentNullException(nameof(linkHelper));
        }

        public object Scan(JObject parameters)
        {
            if (!(parameters["mepSource"] is JObject mepSource))
                throw new Exception("必須提供 mepSource 參數");
            if (!(parameters["structureSource"] is JObject structureSource))
                throw new Exception("必須提供 structureSource 參數");
            if (parameters["clearanceMm"] == null || parameters["clearanceMm"].Type == JTokenType.Null)
                throw new Exception("必須提供明確的 clearanceMm；本工具不會套用預設預留量");

            double clearanceMm = parameters["clearanceMm"].Value<double>();
            if (clearanceMm < 0)
                throw new Exception("clearanceMm 不可小於 0");

            int maxCount = parameters["maxCount"]?.Value<int>() ?? 1000;
            if (maxCount < 1)
                throw new Exception("maxCount 必須大於或等於 1");

            JObject scopedMep = (JObject)mepSource.DeepClone();
            JObject scopedStructure = (JObject)structureSource.DeepClone();
            if (!(scopedMep["categories"] is JArray) && scopedMep["category"] == null)
                scopedMep["categories"] = new JArray("Pipes", "Ducts", "CableTrays", "Conduits");
            ApplyCategoryScope(parameters["categories"] as JArray, scopedMep, scopedStructure);

            var detectParameters = new JObject
            {
                ["mepSource"] = scopedMep,
                ["csaSource"] = scopedStructure,
                ["options"] = new JObject
                {
                    ["useCoarseFilter"] = true,
                    ["maxResults"] = maxCount
                }
            };

            object raw = new ClashDetector(_uiApp, _linkHelper).DetectClashes(detectParameters);
            JObject clashResult = JObject.FromObject(raw);
            JArray clashes = clashResult["Clashes"] as JArray ?? new JArray();

            Document projectDoc = _uiApp.ActiveUIDocument.Document;
            SourceContext mepContext = ResolveSource(projectDoc, scopedMep);
            SourceContext hostContext = ResolveSource(projectDoc, scopedStructure);
            var levelNames = new HashSet<string>(
                (parameters["levels"] as JArray)?.Values<string>() ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
            var projectLevels = new FilteredElementCollector(projectDoc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(level => level.Elevation)
                .ToList();

            var candidates = new JArray();
            var failures = new JArray();
            int sourceIndex = 0;

            foreach (JObject clash in clashes.OfType<JObject>())
            {
                sourceIndex++;
                try
                {
                    JObject candidate = MapCandidate(
                        clash,
                        sourceIndex,
                        clearanceMm,
                        projectLevels,
                        mepContext,
                        hostContext);

                    string resolvedLevel = candidate["openingBottom"]?["projectLevelName"]?.Value<string>();
                    if (levelNames.Count > 0 && (resolvedLevel == null || !levelNames.Contains(resolvedLevel)))
                        continue;

                    candidates.Add(candidate);
                }
                catch (Exception ex)
                {
                    failures.Add(new JObject
                    {
                        ["sourceClashIndex"] = sourceIndex,
                        ["reason"] = ex.Message,
                        ["suggestedAction"] = "確認來源 Link 已載入、元素仍存在且幾何可讀後重新掃描"
                    });
                }
            }

            return new JObject
            {
                ["totalCandidates"] = candidates.Count,
                ["reviewRequiredCount"] = candidates.Count(c => c["status"]?.Value<string>() == "review_required"),
                ["candidateCount"] = candidates.Count(c => c["status"]?.Value<string>() == "candidate"),
                ["failedCount"] = failures.Count,
                ["clearanceMmPerSide"] = clearanceMm,
                ["mepElementCount"] = clashResult["MepElementCount"] ?? 0,
                ["structureElementCount"] = clashResult["CsaElementCount"] ?? 0,
                ["candidates"] = candidates,
                ["failures"] = failures
            };
        }

        private JObject MapCandidate(
            JObject clash,
            int index,
            double clearanceMm,
            IList<Level> projectLevels,
            SourceContext mepContext,
            SourceContext hostContext)
        {
            IdType mepId = clash["MepElement"]?["Id"]?.Value<IdType>() ?? 0;
            IdType hostId = clash["CsaElement"]?["Id"]?.Value<IdType>() ?? 0;
            Element mep = mepContext.Document.GetElement(new ElementId(mepId));
            Element host = hostContext.Document.GetElement(new ElementId(hostId));
            if (mep == null) throw new Exception($"找不到穿管元素 {mepId}");
            if (host == null) throw new Exception($"找不到結構元素 {hostId}");

            XYZ entry = ReadPoint(clash["Intersection"]?["EntryPoint"]);
            XYZ exit = ReadPoint(clash["Intersection"]?["ExitPoint"]);
            XYZ center = (entry + exit) * 0.5;
            XYZ direction = (exit - entry).Normalize();
            double lengthMm = clash["Intersection"]?["PenetrationLength"]?.Value<double>() ?? 0;

            string mepCategory = GetCanonicalCategory(mep);
            string hostCategory = GetCanonicalCategory(host);
            JObject size = BuildSuggestedSize(mep, mepCategory, clearanceMm);
            XYZ hostNormal = TryResolveHostNormal(host, hostContext.Transform, entry, exit);
            double? deviationDegrees = hostNormal == null
                ? (double?)null
                : Math.Acos(Math.Min(1.0, Math.Abs(direction.DotProduct(hostNormal)))) * 180.0 / Math.PI;

            var warnings = new JArray();
            if (hostCategory == "StructuralFraming") warnings.Add("structural_framing_review");
            if (hostCategory == "StructuralColumns") warnings.Add("structural_column_review");
            if (hostNormal == null)
                warnings.Add("host_normal_unresolved");
            else if (Math.Abs(direction.DotProduct(hostNormal)) < 1.0 - OrthogonalityTolerance)
                warnings.Add("oblique_penetration");
            if (lengthMm < 10.0) warnings.Add("short_intersection");
            if (!HasResolvedSize(size)) warnings.Add("size_data_missing");

            JObject openingBottom = BuildOpeningBottom(center, size, projectLevels);
            if (openingBottom["projectElevationMm"].Type == JTokenType.Null ||
                openingBottom["projectLevelName"].Type == JTokenType.Null)
                warnings.Add("opening_bottom_unresolved");

            return new JObject
            {
                ["candidateId"] = $"OC-{index:D3}",
                ["revitLookup"] = new JObject
                {
                    ["penetratingElement"] = BuildLookup(mepId, mepContext),
                    ["hostElement"] = BuildLookup(hostId, hostContext)
                },
                ["entry"] = PointJson(entry),
                ["exit"] = PointJson(exit),
                ["center"] = PointJson(center),
                ["intersectionLengthMm"] = Math.Round(lengthMm, 2),
                ["orthogonalityDeviationDegrees"] = deviationDegrees.HasValue
                    ? new JValue(Math.Round(deviationDegrees.Value, 6))
                    : JValue.CreateNull(),
                ["mepCategory"] = mepCategory,
                ["hostCategory"] = hostCategory,
                ["suggestedOpeningSize"] = size,
                ["openingBottom"] = openingBottom,
                ["status"] = warnings.Count == 0 ? "candidate" : "review_required",
                ["warningCodes"] = warnings
            };
        }

        private static void ApplyCategoryScope(JArray categories, JObject mepSource, JObject structureSource)
        {
            if (categories == null || categories.Count == 0) return;

            var mep = new HashSet<string>(new[] { "Pipes", "Ducts", "CableTrays", "Conduits" }, StringComparer.OrdinalIgnoreCase);
            var structure = new HashSet<string>(new[] { "Walls", "Floors", "StructuralFraming", "StructuralColumns" }, StringComparer.OrdinalIgnoreCase);
            var mepSelection = new JArray(categories.Values<string>().Where(value => mep.Contains(value)));
            var structureSelection = new JArray(categories.Values<string>().Where(value => structure.Contains(value)));
            if (mepSelection.Count > 0) mepSource["categories"] = mepSelection;
            if (structureSelection.Count > 0) structureSource["categories"] = structureSelection;
        }

        private SourceContext ResolveSource(Document projectDoc, JObject source)
        {
            IdType linkInstanceId = source["linkInstanceId"]?.Value<IdType>() ?? 0;
            if (linkInstanceId == 0)
                return new SourceContext(projectDoc, Transform.Identity, 0);

            var data = _linkHelper.GetLinkData(linkInstanceId);
            if (data.Item2 == null)
                throw new Exception($"Link {linkInstanceId} 未載入");
            return new SourceContext(data.Item2, data.Item3, linkInstanceId);
        }

        private static JObject BuildLookup(IdType elementId, SourceContext context)
        {
            if (context.LinkInstanceId == 0)
            {
                return new JObject
                {
                    ["documentKind"] = "main",
                    ["elementId"] = elementId
                };
            }

            return new JObject
            {
                ["documentKind"] = "link",
                ["linkInstanceId"] = context.LinkInstanceId,
                ["linkedElementId"] = elementId
            };
        }

        private static JObject BuildSuggestedSize(Element mep, string category, double clearanceMm)
        {
            double? diameterMm = null;
            double? widthMm = null;
            double? heightMm = null;
            string shape;

            if (category == "Pipes" || category == "Conduits")
            {
                shape = "round";
                double? nominal = ReadLengthMm(mep, "Diameter", "直徑", "Nominal Diameter", "標稱直徑");
                if (nominal.HasValue) diameterMm = nominal.Value + 2.0 * clearanceMm;
            }
            else
            {
                shape = "rectangular";
                double? nominalWidth = ReadLengthMm(mep, "Width", "寬度");
                double? nominalHeight = ReadLengthMm(mep, "Height", "高度");
                if (nominalWidth.HasValue) widthMm = nominalWidth.Value + 2.0 * clearanceMm;
                if (nominalHeight.HasValue) heightMm = nominalHeight.Value + 2.0 * clearanceMm;
            }

            return new JObject
            {
                ["shape"] = shape,
                ["unit"] = "mm",
                ["diameterMm"] = diameterMm.HasValue ? new JValue(Math.Round(diameterMm.Value, 2)) : JValue.CreateNull(),
                ["widthMm"] = widthMm.HasValue ? new JValue(Math.Round(widthMm.Value, 2)) : JValue.CreateNull(),
                ["heightMm"] = heightMm.HasValue ? new JValue(Math.Round(heightMm.Value, 2)) : JValue.CreateNull(),
                ["clearanceMmPerSide"] = clearanceMm
            };
        }

        private static bool HasResolvedSize(JObject size)
        {
            return size["shape"]?.Value<string>() == "round"
                ? size["diameterMm"].Type != JTokenType.Null
                : size["widthMm"].Type != JTokenType.Null && size["heightMm"].Type != JTokenType.Null;
        }

        private static double? ReadLengthMm(Element element, params string[] names)
        {
            Element type = element.Document.GetElement(element.GetTypeId());
            foreach (string name in names)
            {
                Parameter parameter = element.LookupParameter(name) ?? type?.LookupParameter(name);
                if (parameter != null && parameter.StorageType == StorageType.Double && parameter.HasValue)
                {
                    double value = parameter.AsDouble();
                    if (value > 0) return value * MmPerFoot;
                }
            }
            return null;
        }

        private static JObject BuildOpeningBottom(XYZ center, JObject size, IList<Level> levels)
        {
            double? openingHeightMm = size["shape"]?.Value<string>() == "round"
                ? size["diameterMm"]?.Value<double?>()
                : size["heightMm"]?.Value<double?>();

            double? bottomMm = openingHeightMm.HasValue
                ? center.Z * MmPerFoot - openingHeightMm.Value / 2.0
                : (double?)null;
            Level level = bottomMm.HasValue
                ? levels.LastOrDefault(item => item.Elevation * MmPerFoot <= bottomMm.Value + 0.01)
                : null;

            return new JObject
            {
                ["basis"] = "opening_bottom_edge",
                ["projectLevelName"] = level != null ? new JValue(level.Name) : JValue.CreateNull(),
                ["projectElevationMm"] = bottomMm.HasValue ? new JValue(Math.Round(bottomMm.Value, 2)) : JValue.CreateNull(),
                ["offsetFromLevelMm"] = bottomMm.HasValue && level != null
                    ? new JValue(Math.Round(bottomMm.Value - level.Elevation * MmPerFoot, 2))
                    : JValue.CreateNull()
            };
        }

        private static XYZ TryResolveHostNormal(Element host, Transform transform, XYZ entry, XYZ exit)
        {
            PlanarFace bestFace = null;
            double bestDistance = double.MaxValue;
            foreach (Solid solid in GetTransformedSolids(host, transform))
            {
                foreach (Face face in solid.Faces)
                {
                    if (!(face is PlanarFace planar)) continue;
                    foreach (XYZ point in new[] { entry, exit })
                    {
                        IntersectionResult projection = face.Project(point);
                        if (projection != null && projection.Distance <= FaceDistanceToleranceFeet && projection.Distance < bestDistance)
                        {
                            bestFace = planar;
                            bestDistance = projection.Distance;
                        }
                    }
                }
            }
            return bestFace?.FaceNormal.Normalize();
        }

        private static IEnumerable<Solid> GetTransformedSolids(Element element, Transform transform)
        {
            var result = new List<Solid>();
            GeometryElement geometry = element.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine });
            if (geometry == null) return result;
            foreach (GeometryObject item in geometry)
            {
                if (item is Solid solid && solid.Volume > 0)
                    result.Add(SolidUtils.CreateTransformed(solid, transform));
                else if (item is GeometryInstance instance)
                {
                    foreach (GeometryObject nested in instance.GetInstanceGeometry(transform))
                        if (nested is Solid nestedSolid && nestedSolid.Volume > 0) result.Add(nestedSolid);
                }
            }
            return result;
        }

        private static string GetCanonicalCategory(Element element)
        {
            IdType id = element.Category?.Id.GetIdValue() ?? 0;
            if (id == (IdType)BuiltInCategory.OST_PipeCurves) return "Pipes";
            if (id == (IdType)BuiltInCategory.OST_DuctCurves) return "Ducts";
            if (id == (IdType)BuiltInCategory.OST_CableTray) return "CableTrays";
            if (id == (IdType)BuiltInCategory.OST_Conduit) return "Conduits";
            if (id == (IdType)BuiltInCategory.OST_Walls) return "Walls";
            if (id == (IdType)BuiltInCategory.OST_Floors) return "Floors";
            if (id == (IdType)BuiltInCategory.OST_StructuralFraming) return "StructuralFraming";
            if (id == (IdType)BuiltInCategory.OST_StructuralColumns) return "StructuralColumns";
            return element.Category?.Name ?? "Unknown";
        }

        private static XYZ ReadPoint(JToken point)
        {
            if (point == null) throw new Exception("碰撞結果缺少交點座標");
            return new XYZ(
                (point["X"]?.Value<double>() ?? 0) / MmPerFoot,
                (point["Y"]?.Value<double>() ?? 0) / MmPerFoot,
                (point["Z"]?.Value<double>() ?? 0) / MmPerFoot);
        }

        private static JObject PointJson(XYZ point)
        {
            return new JObject
            {
                ["x"] = Math.Round(point.X * MmPerFoot, 2),
                ["y"] = Math.Round(point.Y * MmPerFoot, 2),
                ["z"] = Math.Round(point.Z * MmPerFoot, 2),
                ["unit"] = "mm"
            };
        }

        private sealed class SourceContext
        {
            public SourceContext(Document document, Transform transform, IdType linkInstanceId)
            {
                Document = document;
                Transform = transform;
                LinkInstanceId = linkInstanceId;
            }

            public Document Document { get; }
            public Transform Transform { get; }
            public IdType LinkInstanceId { get; }
        }
    }
}
