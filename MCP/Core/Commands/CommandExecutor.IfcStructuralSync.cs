using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Newtonsoft.Json.Linq;

#if REVIT2025_OR_GREATER
using IdType = System.Int64;
#else
using IdType = System.Int32;
#endif

namespace RevitMCP.Core
{
    public partial class CommandExecutor
    {
        private const double IfcSyncFeetToMm = 304.8;
        private const double IfcSyncMmToFeet = 1.0 / 304.8;

        private object SyncIfcStructuralToNative(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            IdType linkInstanceId = parameters["linkInstanceId"]?.Value<IdType>() ?? 0;
            bool apply = parameters["apply"]?.Value<bool>() ?? false;
            bool dryRun = parameters["dryRun"]?.Value<bool>() ?? !apply;
            bool replaceExisting = parameters["replaceExisting"]?.Value<bool>() ?? false;
            bool includeFraming = parameters["includeFraming"]?.Value<bool>() ?? true;
            bool includeColumns = parameters["includeColumns"]?.Value<bool>() ?? true;
            int maxFraming = Math.Max(0, parameters["maxFraming"]?.Value<int>() ?? 5000);
            int maxColumns = Math.Max(0, parameters["maxColumns"]?.Value<int>() ?? 5000);
            int batchSize = Math.Max(1, Math.Min(100, parameters["batchSize"]?.Value<int>() ?? 100));
            double minLengthMm = Math.Max(1, parameters["minLengthMm"]?.Value<double>() ?? 100.0);
            double sizeRoundMm = Math.Max(1, parameters["sizeRoundMm"]?.Value<double>() ?? 5.0);
            string framingCategory = parameters["framingCategory"]?.Value<string>() ?? "StructuralFraming";
            string columnCategory = parameters["columnCategory"]?.Value<string>() ?? "Columns";
            string baseFramingType = parameters["baseFramingType"]?.Value<string>();
            string baseColumnType = parameters["baseColumnType"]?.Value<string>();
            string baseSteelColumnType = parameters["baseSteelColumnType"]?.Value<string>() ?? baseColumnType;
            string baseShsColumnType = parameters["baseShsColumnType"]?.Value<string>() ?? "SHS";
            string baseRcColumnType = parameters["baseRcColumnType"]?.Value<string>() ?? "AE-RC方柱";
            bool autoColumnBaseType = parameters["autoColumnBaseType"]?.Value<bool>() ?? true;
            double shsColumnMinSizeMm = Math.Max(1, parameters["shsColumnMinSizeMm"]?.Value<double>() ?? 350.0);
            double shsSquareToleranceMm = Math.Max(0, parameters["shsSquareToleranceMm"]?.Value<double>() ?? 25.0);
            bool alignColumnTopsToFloorBottom = parameters["alignColumnTopsToFloorBottom"]?.Value<bool>() ?? true;
            double maxColumnTopSearchDistanceMm = Math.Max(1, parameters["maxColumnTopSearchDistanceMm"]?.Value<double>() ?? 6000.0);
            string sourceTagPrefix = parameters["sourceTagPrefix"]?.Value<string>() ?? "IFC_STRUCT_SYNC";

            RevitLinkInstance linkInstance = doc.GetElement(linkInstanceId.ToElementId()) as RevitLinkInstance;
            if (linkInstance == null)
                throw new Exception($"找不到連結模型實體 ID: {linkInstanceId}");

            Document linkDoc = linkInstance.GetLinkDocument();
            if (linkDoc == null)
                throw new Exception("連結模型未載入或無法讀取");

            Transform linkTransform = linkInstance.GetTotalTransform();
            List<Level> levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();

            if (levels.Count == 0)
                throw new Exception("主模型沒有可用 Level，無法建立原生結構構件");

            SyncTypeCache typeCache = new SyncTypeCache();
            Dictionary<string, List<ElementId>> existingElementsByKey = CollectExistingIfcSyncElements(doc, sourceTagPrefix, linkInstanceId);
            HashSet<string> existingKeys = replaceExisting
                ? new HashSet<string>()
                : new HashSet<string>(existingElementsByKey.Keys);
            List<IfcNativeFramingPlan> framingPlans = includeFraming
                ? BuildIfcFramingPlans(linkDoc, linkTransform, framingCategory, sourceTagPrefix, linkInstanceId, maxFraming, minLengthMm, sizeRoundMm, existingKeys)
                : new List<IfcNativeFramingPlan>();
            List<IfcNativeColumnPlan> columnPlans = includeColumns
                ? BuildIfcColumnPlans(linkDoc, doc, linkTransform, columnCategory, sourceTagPrefix, linkInstanceId, maxColumns, sizeRoundMm, existingKeys, levels, autoColumnBaseType, shsColumnMinSizeMm, shsSquareToleranceMm, alignColumnTopsToFloorBottom, maxColumnTopSearchDistanceMm)
                : new List<IfcNativeColumnPlan>();

            List<object> createdFraming = new List<object>();
            List<object> createdColumns = new List<object>();
            List<object> failed = new List<object>();
            int deletedExisting = 0;
            List<ElementId> replacementElementIds = replaceExisting
                ? framingPlans.Where(p => p.CanCreate && existingElementsByKey.ContainsKey(p.SourceKey))
                    .SelectMany(p => existingElementsByKey[p.SourceKey])
                    .Concat(columnPlans.Where(p => p.CanCreate && existingElementsByKey.ContainsKey(p.SourceKey))
                        .SelectMany(p => existingElementsByKey[p.SourceKey]))
                    .Distinct(new ElementIdComparer())
                    .ToList()
                : new List<ElementId>();

            if (!dryRun && (framingPlans.Any(p => p.CanCreate) || columnPlans.Any(p => p.CanCreate)))
            {
                FamilySymbol baseFramingSymbol = includeFraming
                    ? FindNativeFamilySymbol(doc, BuiltInCategory.OST_StructuralFraming, baseFramingType, new[] { "UB-通用樑", "AE-RC矩形梁" })
                    : null;
                FamilySymbol baseColumnSymbol = includeColumns
                    ? FindNativeFamilySymbol(doc, BuiltInCategory.OST_StructuralColumns, baseSteelColumnType, new[] { "AE-鋼柱", "鋼柱" })
                    : null;
                FamilySymbol baseShsColumnSymbol = includeColumns && autoColumnBaseType
                    ? FindNativeFamilySymbol(doc, BuiltInCategory.OST_StructuralColumns, baseShsColumnType, new[] { "SHS-正方形空心剖面-柱", "SHS" })
                    : null;
                FamilySymbol baseRcColumnSymbol = includeColumns && autoColumnBaseType
                    ? FindNativeFamilySymbol(doc, BuiltInCategory.OST_StructuralColumns, baseRcColumnType, new[] { "AE-RC方柱", "RC方柱" })
                    : null;

                if (includeFraming && baseFramingSymbol == null && framingPlans.Any(p => p.CanCreate))
                    throw new Exception("找不到可用的 StructuralFraming 基準族型，請提供 baseFramingType");
                if (includeColumns && baseColumnSymbol == null && columnPlans.Any(p => p.CanCreate))
                    throw new Exception("找不到可用的 StructuralColumns 基準族型，請提供 baseColumnType");
                if (includeColumns && autoColumnBaseType && baseShsColumnSymbol == null && columnPlans.Any(p => p.CanCreate && p.ColumnBaseKind == "SHS"))
                    throw new Exception("找不到可用的 SHS StructuralColumns 基準族型，請提供 baseShsColumnType");
                if (includeColumns && autoColumnBaseType && baseRcColumnSymbol == null && columnPlans.Any(p => p.CanCreate && p.ColumnBaseKind == "RC"))
                    throw new Exception("找不到可用的 RC StructuralColumns 基準族型，請提供 baseRcColumnType");

                List<IfcNativeFramingPlan> pendingFraming = framingPlans.Where(p => p.CanCreate).ToList();
                List<IfcNativeColumnPlan> pendingColumns = columnPlans.Where(p => p.CanCreate).ToList();
                int framingCursor = 0;
                int columnCursor = 0;
                int batchIndex = 0;

                if (replacementElementIds.Count > 0)
                {
                    using (Transaction trans = new Transaction(doc, "Replace existing IFC structural sync elements"))
                    {
                        trans.Start();
                        foreach (ElementId elementId in replacementElementIds)
                        {
                            try
                            {
                                if (doc.GetElement(elementId) == null) continue;
                                doc.Delete(elementId);
                                deletedExisting++;
                            }
                            catch (Exception ex)
                            {
                                failed.Add(new { Kind = "REPLACE", ElementId = elementId.GetIdValue(), Error = ex.Message });
                            }
                        }
                        trans.Commit();
                    }
                }

                while (framingCursor < pendingFraming.Count || columnCursor < pendingColumns.Count)
                {
                    batchIndex++;
                    int slots = batchSize;

                    using (Transaction trans = new Transaction(doc, $"Sync IFC structural to native batch {batchIndex}"))
                    {
                        trans.Start();
                        FailureHandlingOptions failureOptions = trans.GetFailureHandlingOptions();
                        failureOptions.SetFailuresPreprocessor(new DismissWarningsPreprocessor());
                        trans.SetFailureHandlingOptions(failureOptions);

                        if (baseFramingSymbol != null && !baseFramingSymbol.IsActive)
                        {
                            baseFramingSymbol.Activate();
                            doc.Regenerate();
                        }

                        if (baseColumnSymbol != null && !baseColumnSymbol.IsActive)
                        {
                            baseColumnSymbol.Activate();
                            doc.Regenerate();
                        }
                        if (baseShsColumnSymbol != null && !baseShsColumnSymbol.IsActive)
                        {
                            baseShsColumnSymbol.Activate();
                            doc.Regenerate();
                        }
                        if (baseRcColumnSymbol != null && !baseRcColumnSymbol.IsActive)
                        {
                            baseRcColumnSymbol.Activate();
                            doc.Regenerate();
                        }

                        while (slots > 0 && framingCursor < pendingFraming.Count)
                        {
                            IfcNativeFramingPlan plan = pendingFraming[framingCursor++];
                            slots--;

                            try
                            {
                                FamilySymbol symbol = EnsureSizedSymbol(
                                    doc,
                                    baseFramingSymbol,
                                    plan.TypeName,
                                    plan.WidthMm,
                                    plan.DepthMm,
                                    plan.SMm,
                                    plan.RMm,
                                    typeCache.FramingSymbols,
                                    isColumn: false);

                                Level level = FindNearestLevelAtOrBelow(levels, ((plan.Start.Z + plan.End.Z) * 0.5));
                                Line line = Line.CreateBound(plan.Start, plan.End);
                                FamilyInstance instance = doc.Create.NewFamilyInstance(line, symbol, level, StructuralType.Beam);
                                TrySetBeamEndpointOffsets(instance, plan, level);
                                TrySetStringParameter(instance, plan.Mark, "標記", "Mark");
                                TrySetStringParameter(instance, plan.SourceComment, "備註", "Comments");

                                createdFraming.Add(plan.ToCreatedResult(instance.Id.GetIdValue(), symbol));
                            }
                            catch (Exception ex)
                            {
                                failed.Add(new { Kind = "BEAM", plan.SourceElementId, Error = ex.Message });
                            }
                        }

                        while (slots > 0 && columnCursor < pendingColumns.Count)
                        {
                            IfcNativeColumnPlan plan = pendingColumns[columnCursor++];
                            slots--;

                            try
                            {
                                FamilySymbol selectedColumnBaseSymbol = SelectColumnBaseSymbol(plan, baseColumnSymbol, baseShsColumnSymbol, baseRcColumnSymbol);
                                FamilySymbol symbol = EnsureSizedSymbol(
                                    doc,
                                    selectedColumnBaseSymbol,
                                    plan.TypeName,
                                    plan.WidthMm,
                                    plan.DepthMm,
                                    plan.SMm,
                                    plan.RMm,
                                    typeCache.ColumnSymbols,
                                    isColumn: true);

                                XYZ location = new XYZ(plan.CenterXFeet, plan.CenterYFeet, 0);
                                FamilyInstance instance = doc.Create.NewFamilyInstance(location, symbol, plan.BaseLevel, StructuralType.Column);
                                TrySetColumnLevelAndOffsets(instance, plan);
                                if (alignColumnTopsToFloorBottom && plan.ColumnTopAlignedToFloorBottom)
                                    TrySetColumnTopAttachment(instance, 0);
                                TrySetStringParameter(instance, plan.Mark, "標記", "Mark");
                                TrySetStringParameter(instance, plan.SourceComment, "備註", "Comments");

                                createdColumns.Add(plan.ToCreatedResult(instance.Id.GetIdValue(), symbol));
                            }
                            catch (Exception ex)
                            {
                                failed.Add(new { Kind = "COLUMN", plan.SourceElementId, Error = ex.Message });
                            }
                        }

                        trans.Commit();
                    }
                }
            }

            return new
            {
                Success = true,
                DryRun = dryRun,
                ApplyRequested = apply,
                ReplaceExisting = replaceExisting,
                BatchSize = batchSize,
                AlignColumnTopsToFloorBottom = alignColumnTopsToFloorBottom,
                MaxColumnTopSearchDistanceMm = maxColumnTopSearchDistanceMm,
                LinkInstanceId = linkInstanceId,
                LinkFileName = linkDoc.Title,
                LinkTransformIsIdentity = linkTransform.IsIdentity,
                SourceCounts = new
                {
                    FramingPlans = framingPlans.Count,
                    ColumnPlans = columnPlans.Count,
                    ExistingSkipped = framingPlans.Count(p => p.SkipReason == "existing-native-source") +
                                      columnPlans.Count(p => p.SkipReason == "existing-native-source")
                },
                PlannedCounts = new
                {
                    Framing = framingPlans.Count(p => p.CanCreate),
                    Columns = columnPlans.Count(p => p.CanCreate),
                    SkippedFraming = framingPlans.Count(p => !p.CanCreate),
                    SkippedColumns = columnPlans.Count(p => !p.CanCreate)
                },
                CreatedCounts = new
                {
                    Framing = createdFraming.Count,
                    Columns = createdColumns.Count,
                    DeletedExisting = deletedExisting,
                    ReplaceCandidates = replacementElementIds.Count,
                    Failed = failed.Count
                },
                TypePlan = new
                {
                    FramingTypes = framingPlans.Where(p => p.CanCreate).Select(p => p.TypeName).Distinct().OrderBy(x => x).ToList(),
                    ColumnTypes = columnPlans.Where(p => p.CanCreate).Select(p => p.TypeName).Distinct().OrderBy(x => x).ToList(),
                    ColumnTypesByBaseKind = columnPlans
                        .Where(p => p.CanCreate)
                        .GroupBy(p => p.ColumnBaseKind ?? "STEEL")
                        .OrderBy(g => g.Key)
                        .Select(g => new
                        {
                            ColumnBaseKind = g.Key,
                            Count = g.Count(),
                            Types = g.Select(p => p.TypeName).Distinct().OrderBy(x => x).ToList()
                        })
                        .ToList()
                },
                ColumnTopAlignment = new
                {
                    Enabled = alignColumnTopsToFloorBottom,
                    AlignedToFloorBottom = columnPlans.Count(p => p.CanCreate && p.ColumnTopAlignedToFloorBottom),
                    NotAligned = columnPlans.Count(p => p.CanCreate && !p.ColumnTopAlignedToFloorBottom),
                    TargetFloors = columnPlans
                        .Where(p => p.CanCreate && p.ColumnTopAlignedToFloorBottom)
                        .GroupBy(p => new { p.TopFloorId, p.TopFloorName })
                        .OrderBy(g => g.Key.TopFloorName)
                        .Select(g => new
                        {
                            FloorId = g.Key.TopFloorId,
                            FloorName = g.Key.TopFloorName,
                            Count = g.Count()
                        })
                        .ToList()
                },
                Samples = new
                {
                    Framing = framingPlans.Take(10).Select(p => p.ToPreviewResult()).ToList(),
                    Columns = columnPlans.Take(10).Select(p => p.ToPreviewResult()).ToList()
                },
                Created = new
                {
                    Framing = createdFraming.Take(50).ToList(),
                    Columns = createdColumns.Take(50).ToList()
                },
                Failures = failed.Take(50).ToList()
            };
        }

        private List<IfcNativeFramingPlan> BuildIfcFramingPlans(
            Document linkDoc,
            Transform linkTransform,
            string categoryName,
            string sourceTagPrefix,
            IdType linkInstanceId,
            int maxCount,
            double minLengthMm,
            double sizeRoundMm,
            HashSet<string> existingKeys)
        {
            BuiltInCategory bic = LinkedModelHelper.ResolveBuiltInCategory(categoryName);
            List<Element> sourceElements = new FilteredElementCollector(linkDoc)
                .OfCategory(bic)
                .WhereElementIsNotElementType()
                .Take(maxCount)
                .ToList();

            List<IfcNativeFramingPlan> plans = new List<IfcNativeFramingPlan>();
            foreach (Element element in sourceElements)
            {
                IdType sourceId = element.Id.GetIdValue();
                string sourceKey = MakeSourceKey(sourceTagPrefix, linkInstanceId, "BEAM", sourceId);
                string ifcGuid = ReadAnyParameterString(element, linkDoc, "IfcGUID", "Ifc GUID", "GUID");

                IfcNativeFramingPlan plan = new IfcNativeFramingPlan
                {
                    SourceElementId = sourceId,
                    SourceName = element.Name ?? "",
                    IfcGuid = ifcGuid,
                    SourceKey = sourceKey,
                    SourceComment = MakeSourceComment(sourceKey, ifcGuid),
                    Mark = $"IFC-BEAM-{sourceId}",
                    CanCreate = false
                };

                if (existingKeys.Contains(sourceKey))
                {
                    plan.SkipReason = "existing-native-source";
                    plans.Add(plan);
                    continue;
                }

                List<XYZ> vertices;
                List<IfcGeometryEdge> edges;
                double solidVolumeFeet3;
                CollectIfcElementGeometry(element, linkTransform, out vertices, out edges, out solidVolumeFeet3);
                if (vertices.Count == 0)
                {
                    plan.SkipReason = "no-geometry-vertices";
                    plans.Add(plan);
                    continue;
                }

                XYZ min;
                XYZ max;
                GetBounds(vertices, out min, out max);
                XYZ axis = EstimateMainAxis(edges, min, max);
                XYZ side;
                XYZ localUp;
                BuildLocalFrame(axis, out side, out localUp);

                double minAxis;
                double maxAxis;
                double minSide;
                double maxSide;
                double minUp;
                double maxUp;
                ProjectRanges(vertices, axis, side, localUp, out minAxis, out maxAxis, out minSide, out maxSide, out minUp, out maxUp);

                double lengthMm = (maxAxis - minAxis) * IfcSyncFeetToMm;
                if (lengthMm < minLengthMm)
                {
                    plan.SkipReason = "length-too-short";
                    plans.Add(plan);
                    continue;
                }

                XYZ center = Midpoint(min, max);
                double centerAxis = center.DotProduct(axis);
                XYZ centerlineStart = center + axis.Multiply(minAxis - centerAxis);
                XYZ centerlineEnd = center + axis.Multiply(maxAxis - centerAxis);

                double depthFeet = Math.Max(1 * IfcSyncMmToFeet, maxUp - minUp);
                double widthFeet = Math.Max(1 * IfcSyncMmToFeet, maxSide - minSide);
                XYZ insertionShift = localUp.Multiply(depthFeet * 0.5);
                XYZ start = centerlineStart + insertionShift;
                XYZ end = centerlineEnd + insertionShift;

                double widthMm = ReadAnyParameterDoubleMm(element, linkDoc, "b", "B", "w", "W", "寬", "寬度", "Width")
                    ?? RoundTo((widthFeet * IfcSyncFeetToMm), sizeRoundMm);
                double depthMm = ReadAnyParameterDoubleMm(element, linkDoc, "h", "H", "d", "D", "深", "深度", "Height")
                    ?? RoundTo((depthFeet * IfcSyncFeetToMm), sizeRoundMm);
                double? sMm = ReadAnyParameterDoubleMm(element, linkDoc, "s", "S", "tw", "Tw", "腹板厚", "腹板厚度", "Web Thickness");
                double? rMm = ReadAnyParameterDoubleMm(element, linkDoc, "r", "R", "tf", "Tf", "翼板厚", "翼板厚度", "Flange Thickness");

                plan.Start = start;
                plan.End = end;
                plan.LengthMm = Math.Round(lengthMm, 2);
                plan.WidthMm = widthMm;
                plan.DepthMm = depthMm;
                plan.SMm = sMm;
                plan.RMm = rMm;
                plan.TypeName = BuildIfcTypeName("IFC-BEAM", depthMm, widthMm, sMm, rMm);
                plan.CanCreate = true;
                plans.Add(plan);
            }

            return plans;
        }

        private List<IfcNativeColumnPlan> BuildIfcColumnPlans(
            Document linkDoc,
            Document hostDoc,
            Transform linkTransform,
            string categoryName,
            string sourceTagPrefix,
            IdType linkInstanceId,
            int maxCount,
            double sizeRoundMm,
            HashSet<string> existingKeys,
            List<Level> levels,
            bool autoColumnBaseType,
            double shsColumnMinSizeMm,
            double shsSquareToleranceMm,
            bool alignColumnTopsToFloorBottom,
            double maxColumnTopSearchDistanceMm)
        {
            BuiltInCategory bic = LinkedModelHelper.ResolveBuiltInCategory(categoryName);
            List<Element> sourceElements = new FilteredElementCollector(linkDoc)
                .OfCategory(bic)
                .WhereElementIsNotElementType()
                .Take(maxCount)
                .ToList();

            List<IfcNativeColumnPlan> plans = new List<IfcNativeColumnPlan>();
            foreach (Element element in sourceElements)
            {
                IdType sourceId = element.Id.GetIdValue();
                string sourceKey = MakeSourceKey(sourceTagPrefix, linkInstanceId, "COLUMN", sourceId);
                string ifcGuid = ReadAnyParameterString(element, linkDoc, "IfcGUID", "Ifc GUID", "GUID");

                IfcNativeColumnPlan plan = new IfcNativeColumnPlan
                {
                    SourceElementId = sourceId,
                    SourceName = element.Name ?? "",
                    IfcGuid = ifcGuid,
                    SourceKey = sourceKey,
                    SourceComment = MakeSourceComment(sourceKey, ifcGuid),
                    Mark = $"IFC-COL-{sourceId}",
                    CanCreate = false
                };

                if (existingKeys.Contains(sourceKey))
                {
                    plan.SkipReason = "existing-native-source";
                    plans.Add(plan);
                    continue;
                }

                List<XYZ> vertices;
                List<IfcGeometryEdge> edges;
                double solidVolumeFeet3;
                CollectIfcElementGeometry(element, linkTransform, out vertices, out edges, out solidVolumeFeet3);
                if (vertices.Count == 0)
                {
                    plan.SkipReason = "no-geometry-vertices";
                    plans.Add(plan);
                    continue;
                }

                XYZ min;
                XYZ max;
                GetBounds(vertices, out min, out max);

                double widthMm = ReadAnyParameterDoubleMm(element, linkDoc, "b", "B", "w", "W", "寬", "寬度", "Width")
                    ?? RoundTo((max.X - min.X) * IfcSyncFeetToMm, sizeRoundMm);
                double depthMm = ReadAnyParameterDoubleMm(element, linkDoc, "h", "H", "d", "D", "深", "深度", "Height")
                    ?? RoundTo((max.Y - min.Y) * IfcSyncFeetToMm, sizeRoundMm);
                NormalizeColumnSectionDimensions(ref widthMm, ref depthMm);
                double? sMm = ReadAnyParameterDoubleMm(element, linkDoc, "s", "S", "tw", "Tw", "腹板厚", "腹板厚度", "Web Thickness");
                double? rMm = ReadAnyParameterDoubleMm(element, linkDoc, "r", "R", "tf", "Tf", "翼板厚", "翼板厚度", "Flange Thickness");
                double heightMm = Math.Round((max.Z - min.Z) * IfcSyncFeetToMm, 2);
                double bboxVolumeFeet3 = Math.Max(0, (max.X - min.X) * (max.Y - min.Y) * (max.Z - min.Z));
                double? solidVolumeRatio = bboxVolumeFeet3 > 1e-9
                    ? Math.Max(0, Math.Min(1, solidVolumeFeet3 / bboxVolumeFeet3))
                    : (double?)null;

                plan.CenterXFeet = (min.X + max.X) * 0.5;
                plan.CenterYFeet = (min.Y + max.Y) * 0.5;
                plan.BaseZFeet = min.Z;
                plan.TopZFeet = max.Z;
                plan.WidthMm = Math.Max(1, widthMm);
                plan.DepthMm = Math.Max(1, depthMm);
                plan.SMm = sMm;
                plan.RMm = rMm;
                plan.HeightMm = heightMm;
                plan.SolidVolumeRatio = solidVolumeRatio;
                plan.ColumnBaseKind = ClassifyIfcColumnBaseKind(element, linkDoc, plan.WidthMm, plan.DepthMm, sMm, rMm, solidVolumeRatio, autoColumnBaseType, shsColumnMinSizeMm, shsSquareToleranceMm, out string classificationReason);
                plan.ColumnBaseReason = classificationReason;
                plan.TypeName = BuildIfcTypeName("IFC-COL", plan.DepthMm, plan.WidthMm, sMm, rMm);
                plan.BaseLevel = FindNearestLevelAtOrBelow(levels, min.Z);
                plan.TopLevel = FindNearestLevelAtOrBelow(levels, max.Z);
                plan.BaseOffsetFeet = min.Z - plan.BaseLevel.Elevation;
                plan.TopOffsetFeet = max.Z - plan.TopLevel.Elevation;
                ResolveColumnTopToFloorBottom(hostDoc, levels, plan, max.Z, alignColumnTopsToFloorBottom, maxColumnTopSearchDistanceMm);
                plan.CanCreate = heightMm >= 50;
                if (!plan.CanCreate)
                    plan.SkipReason = "height-too-short";
                plans.Add(plan);
            }

            return plans;
        }

        private static void CollectIfcElementGeometry(
            Element element,
            Transform transform,
            out List<XYZ> vertices,
            out List<IfcGeometryEdge> edges,
            out double solidVolumeFeet3)
        {
            vertices = new List<XYZ>();
            edges = new List<IfcGeometryEdge>();
            solidVolumeFeet3 = 0;
            Options options = new Options
            {
                DetailLevel = ViewDetailLevel.Fine,
                IncludeNonVisibleObjects = false
            };

            GeometryElement geometry = element.get_Geometry(options);
            CollectIfcGeometry(geometry, transform, vertices, edges, ref solidVolumeFeet3);

            if (vertices.Count == 0)
            {
                BoundingBoxXYZ bbox = element.get_BoundingBox(null);
                if (bbox != null)
                {
                    AddBboxVertices(transform.OfPoint(bbox.Min), transform.OfPoint(bbox.Max), vertices);
                }
            }
        }

        private static void CollectIfcGeometry(
            GeometryElement geometry,
            Transform transform,
            List<XYZ> vertices,
            List<IfcGeometryEdge> edges,
            ref double solidVolumeFeet3)
        {
            if (geometry == null) return;

            foreach (GeometryObject obj in geometry)
            {
                Solid solid = obj as Solid;
                if (solid != null && solid.Faces.Size > 0)
                {
                    if (solid.Volume > 1e-9)
                        solidVolumeFeet3 += Math.Abs(solid.Volume);

                    foreach (Face face in solid.Faces)
                    {
                        Mesh mesh = face.Triangulate();
                        for (int i = 0; i < mesh.NumTriangles; i++)
                        {
                            MeshTriangle triangle = mesh.get_Triangle(i);
                            vertices.Add(transform.OfPoint(triangle.get_Vertex(0)));
                            vertices.Add(transform.OfPoint(triangle.get_Vertex(1)));
                            vertices.Add(transform.OfPoint(triangle.get_Vertex(2)));
                        }

                        foreach (EdgeArray loop in face.EdgeLoops)
                        {
                            foreach (Edge edge in loop)
                            {
                                Curve curve = edge.AsCurve();
                                if (curve == null) continue;
                                XYZ p0 = transform.OfPoint(curve.GetEndPoint(0));
                                XYZ p1 = transform.OfPoint(curve.GetEndPoint(1));
                                double length = p0.DistanceTo(p1);
                                if (length > 1e-6)
                                    edges.Add(new IfcGeometryEdge { Start = p0, End = p1, Length = length });
                            }
                        }
                    }
                }
                else
                {
                    GeometryInstance instance = obj as GeometryInstance;
                    if (instance != null)
                    {
                        Transform nextTransform = transform.Multiply(instance.Transform);
                        CollectIfcGeometry(instance.GetInstanceGeometry(), nextTransform, vertices, edges, ref solidVolumeFeet3);
                    }
                }
            }
        }

        private static void AddBboxVertices(XYZ min, XYZ max, List<XYZ> vertices)
        {
            vertices.Add(new XYZ(min.X, min.Y, min.Z));
            vertices.Add(new XYZ(max.X, min.Y, min.Z));
            vertices.Add(new XYZ(min.X, max.Y, min.Z));
            vertices.Add(new XYZ(max.X, max.Y, min.Z));
            vertices.Add(new XYZ(min.X, min.Y, max.Z));
            vertices.Add(new XYZ(max.X, min.Y, max.Z));
            vertices.Add(new XYZ(min.X, max.Y, max.Z));
            vertices.Add(new XYZ(max.X, max.Y, max.Z));
        }

        private static XYZ EstimateMainAxis(List<IfcGeometryEdge> edges, XYZ min, XYZ max)
        {
            List<IfcDirectionGroup> groups = new List<IfcDirectionGroup>();
            foreach (IfcGeometryEdge edge in edges)
            {
                if (edge.Length * IfcSyncFeetToMm < 50) continue;
                XYZ dir = (edge.End - edge.Start).Normalize();
                dir = CanonicalDirection(dir);
                IfcDirectionGroup group = groups.FirstOrDefault(g => Math.Abs(g.Direction.DotProduct(dir)) > 0.985);
                if (group == null)
                {
                    group = new IfcDirectionGroup { Direction = dir };
                    groups.Add(group);
                }

                group.TotalLength += edge.Length;
                group.MaxLength = Math.Max(group.MaxLength, edge.Length);
                group.Count++;
            }

            IfcDirectionGroup best = groups
                .OrderByDescending(g => g.MaxLength)
                .ThenByDescending(g => g.TotalLength)
                .FirstOrDefault();

            if (best != null)
                return best.Direction.Normalize();

            XYZ size = max - min;
            if (Math.Abs(size.X) >= Math.Abs(size.Y) && Math.Abs(size.X) >= Math.Abs(size.Z))
                return XYZ.BasisX;
            if (Math.Abs(size.Y) >= Math.Abs(size.X) && Math.Abs(size.Y) >= Math.Abs(size.Z))
                return XYZ.BasisY;
            return XYZ.BasisZ;
        }

        private static XYZ CanonicalDirection(XYZ dir)
        {
            if (dir.X < -1e-6 || (Math.Abs(dir.X) < 1e-6 && dir.Y < -1e-6) ||
                (Math.Abs(dir.X) < 1e-6 && Math.Abs(dir.Y) < 1e-6 && dir.Z < -1e-6))
            {
                return dir.Negate();
            }

            return dir;
        }

        private static void BuildLocalFrame(XYZ axis, out XYZ side, out XYZ localUp)
        {
            XYZ upSeed = Math.Abs(axis.DotProduct(XYZ.BasisZ)) > 0.95 ? XYZ.BasisX : XYZ.BasisZ;
            side = axis.CrossProduct(upSeed);
            if (side.GetLength() < 1e-9)
                side = XYZ.BasisY;
            side = side.Normalize();
            localUp = side.CrossProduct(axis).Normalize();
            if (localUp.Z < 0)
                localUp = localUp.Negate();
        }

        private static void ProjectRanges(
            List<XYZ> vertices,
            XYZ axis,
            XYZ side,
            XYZ localUp,
            out double minAxis,
            out double maxAxis,
            out double minSide,
            out double maxSide,
            out double minUp,
            out double maxUp)
        {
            minAxis = minSide = minUp = double.PositiveInfinity;
            maxAxis = maxSide = maxUp = double.NegativeInfinity;
            foreach (XYZ vertex in vertices)
            {
                double a = vertex.DotProduct(axis);
                double s = vertex.DotProduct(side);
                double u = vertex.DotProduct(localUp);
                minAxis = Math.Min(minAxis, a);
                maxAxis = Math.Max(maxAxis, a);
                minSide = Math.Min(minSide, s);
                maxSide = Math.Max(maxSide, s);
                minUp = Math.Min(minUp, u);
                maxUp = Math.Max(maxUp, u);
            }
        }

        private static void GetBounds(List<XYZ> vertices, out XYZ min, out XYZ max)
        {
            min = new XYZ(
                vertices.Min(p => p.X),
                vertices.Min(p => p.Y),
                vertices.Min(p => p.Z));
            max = new XYZ(
                vertices.Max(p => p.X),
                vertices.Max(p => p.Y),
                vertices.Max(p => p.Z));
        }

        private static XYZ Midpoint(XYZ min, XYZ max)
        {
            return new XYZ((min.X + max.X) * 0.5, (min.Y + max.Y) * 0.5, (min.Z + max.Z) * 0.5);
        }

        private static Dictionary<string, List<ElementId>> CollectExistingIfcSyncElements(Document doc, string sourceTagPrefix, IdType linkInstanceId)
        {
            Dictionary<string, List<ElementId>> elementsByKey = new Dictionary<string, List<ElementId>>();
            BuiltInCategory[] categories = { BuiltInCategory.OST_StructuralFraming, BuiltInCategory.OST_StructuralColumns };
            foreach (BuiltInCategory category in categories)
            {
                IEnumerable<Element> elements = new FilteredElementCollector(doc)
                    .OfCategory(category)
                    .WhereElementIsNotElementType()
                    .ToElements();

                foreach (Element element in elements)
                {
                    string comment = ReadAnyParameterString(element, doc, "備註", "Comments");
                    if (string.IsNullOrEmpty(comment)) continue;
                    if (!comment.Contains(sourceTagPrefix) || !comment.Contains($"Link:{linkInstanceId}")) continue;
                    string key = comment.Split('|').Take(4).Aggregate((a, b) => a + "|" + b);
                    if (!elementsByKey.TryGetValue(key, out List<ElementId> ids))
                    {
                        ids = new List<ElementId>();
                        elementsByKey[key] = ids;
                    }
                    ids.Add(element.Id);
                }
            }

            return elementsByKey;
        }

        private static string MakeSourceKey(string sourceTagPrefix, IdType linkInstanceId, string kind, IdType sourceId)
        {
            return $"{sourceTagPrefix}|Link:{linkInstanceId}|Kind:{kind}|Source:{sourceId}";
        }

        private static string MakeSourceComment(string sourceKey, string ifcGuid)
        {
            return string.IsNullOrWhiteSpace(ifcGuid)
                ? sourceKey
                : $"{sourceKey}|IfcGUID:{ifcGuid}";
        }

        private static FamilySymbol FindNativeFamilySymbol(Document doc, BuiltInCategory category, string requestedType, string[] preferredFamilyKeywords)
        {
            List<FamilySymbol> symbols = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(fs => fs.Category != null && fs.Category.Id.GetIdValue() == (IdType)category)
                .ToList();

            if (!string.IsNullOrWhiteSpace(requestedType))
            {
                FamilySymbol requested = symbols.FirstOrDefault(fs =>
                    fs.Name.Equals(requestedType, StringComparison.OrdinalIgnoreCase) ||
                    fs.FamilyName.IndexOf(requestedType, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    fs.Name.IndexOf(requestedType, StringComparison.OrdinalIgnoreCase) >= 0);
                if (requested != null) return requested;
            }

            foreach (string keyword in preferredFamilyKeywords)
            {
                FamilySymbol match = symbols.FirstOrDefault(fs =>
                    fs.FamilyName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    fs.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
                if (match != null) return match;
            }

            return symbols.FirstOrDefault();
        }

        private static FamilySymbol EnsureSizedSymbol(
            Document doc,
            FamilySymbol baseSymbol,
            string typeName,
            double widthMm,
            double depthMm,
            double? sMm,
            double? rMm,
            Dictionary<string, FamilySymbol> cache,
            bool isColumn)
        {
            string cacheKey = $"{baseSymbol.FamilyName}::{typeName}";
            if (cache.TryGetValue(cacheKey, out FamilySymbol cached))
                return cached;

            FamilySymbol existing = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(fs => fs.Category != null &&
                    fs.Category.Id.GetIdValue() == baseSymbol.Category.Id.GetIdValue() &&
                    fs.FamilyName.Equals(baseSymbol.FamilyName, StringComparison.OrdinalIgnoreCase) &&
                    fs.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));

            FamilySymbol symbol = existing ?? (FamilySymbol)baseSymbol.Duplicate(typeName);
            SetTypeSizeParameters(symbol, widthMm, depthMm, sMm, rMm, isColumn);
            if (!symbol.IsActive)
                symbol.Activate();
            cache[cacheKey] = symbol;
            return symbol;
        }

        private static FamilySymbol SelectColumnBaseSymbol(IfcNativeColumnPlan plan, FamilySymbol steelSymbol, FamilySymbol shsSymbol, FamilySymbol rcSymbol)
        {
            if (plan != null && plan.ColumnBaseKind == "RC" && rcSymbol != null)
                return rcSymbol;
            if (plan != null && plan.ColumnBaseKind == "SHS" && shsSymbol != null)
                return shsSymbol;
            return steelSymbol;
        }

        private static string ClassifyIfcColumnBaseKind(
            Element element,
            Document doc,
            double widthMm,
            double depthMm,
            double? sMm,
            double? rMm,
            double? solidVolumeRatio,
            bool autoColumnBaseType,
            double shsColumnMinSizeMm,
            double shsSquareToleranceMm,
            out string reason)
        {
            if (!autoColumnBaseType)
            {
                reason = "auto-disabled";
                return "STEEL";
            }

            string text = string.Join(" ",
                element.Name ?? "",
                ReadAnyParameterString(element, doc, "Type Name", "Type", "Name", "ObjectType", "IfcName", "IfcObjectType", "型號", "類型名稱"));

            if (ContainsIfcColumnKeyword(text, "RC", "CONCRETE", "REINFORCED CONCRETE", "混凝土", "鋼筋混凝土", "實心"))
            {
                reason = "name-indicates-solid-rc";
                return "RC";
            }

            if (ContainsIfcColumnKeyword(text, "SHS", "HSS", "BOX", "BOX COLUMN", "SQUARE HOLLOW", "正方形空心", "方形空心", "箱型"))
            {
                reason = "name-indicates-hollow-square";
                return "SHS";
            }

            bool isNearlySquare = Math.Abs(widthMm - depthMm) <= shsSquareToleranceMm;
            bool isLargeEnough = Math.Min(widthMm, depthMm) >= shsColumnMinSizeMm;
            bool hasPlateThickness = sMm.HasValue || rMm.HasValue;

            if (isNearlySquare && isLargeEnough && solidVolumeRatio.HasValue && solidVolumeRatio.Value >= 0.85)
            {
                reason = $"solid-volume-ratio={Math.Round(solidVolumeRatio.Value, 3)}";
                return "RC";
            }

            if (isNearlySquare && isLargeEnough && !hasPlateThickness)
            {
                reason = solidVolumeRatio.HasValue
                    ? $"hollow-or-non-solid-square-volume-ratio={Math.Round(solidVolumeRatio.Value, 3)}"
                    : $"square-size>={FormatSize(shsColumnMinSizeMm)}mm";
                return "SHS";
            }

            reason = hasPlateThickness
                ? "steel-section-thickness-parameters"
                : "rectangular-or-small-section";
            return "STEEL";
        }

        private void ResolveColumnTopToFloorBottom(
            Document hostDoc,
            List<Level> levels,
            IfcNativeColumnPlan plan,
            double originalTopZFeet,
            bool alignColumnTopsToFloorBottom,
            double maxColumnTopSearchDistanceMm)
        {
            plan.OriginalTopZFeet = originalTopZFeet;
            plan.TopAlignmentMode = alignColumnTopsToFloorBottom ? "floor-bottom" : "ifc-top";

            if (!alignColumnTopsToFloorBottom)
            {
                plan.TopAlignmentMessage = "disabled";
                return;
            }

            XYZ samplePoint = new XYZ(plan.CenterXFeet, plan.CenterYFeet, originalTopZFeet);
            List<FloorHitInfo> hits = CollectGeometryFloorBottomHitsAtPoint(
                hostDoc,
                samplePoint,
                originalTopZFeet,
                maxColumnTopSearchDistanceMm,
                null);

            FloorHitInfo target = hits
                .Where(h => h != null && h.HasHit)
                .OrderBy(h => Math.Abs(h.BottomZFeet - originalTopZFeet))
                .FirstOrDefault();

            if (target == null)
            {
                plan.TopAlignmentMessage = "no-floor-bottom-hit";
                return;
            }

            plan.TopZFeet = target.BottomZFeet;
            plan.TopLevel = FindNearestLevelAtOrBelow(levels, target.BottomZFeet);
            plan.TopOffsetFeet = target.BottomZFeet - plan.TopLevel.Elevation;
            plan.ColumnTopAlignedToFloorBottom = true;
            plan.TopFloorId = target.FloorId;
            plan.TopFloorName = target.FloorName;
            plan.TopAlignmentDeltaMm = Math.Round((target.BottomZFeet - originalTopZFeet) * IfcSyncFeetToMm, 2);
            plan.TopAlignmentMessage = target.Message;
        }

        private static bool ContainsIfcColumnKeyword(string value, params string[] needles)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            foreach (string needle in needles)
            {
                if (value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private static void SetTypeSizeParameters(FamilySymbol symbol, double widthMm, double depthMm, double? sMm, double? rMm, bool isColumn)
        {
            string[] widthNames = isColumn
                ? new[] { "寬", "寬度", "Width", "W", "w", "b", "B" }
                : new[] { "w", "b", "B", "寬", "寬度", "Width" };
            string[] depthNames = isColumn
                ? new[] { "深", "深度", "Depth", "D", "d", "H", "h" }
                : new[] { "h", "H", "d", "D", "深", "深度", "Height" };

            TrySetAllDoubleParametersMm(symbol, widthMm, widthNames);
            TrySetAllDoubleParametersMm(symbol, depthMm, depthNames);
            TrySetDoubleParameterMm(symbol, sMm, "s", "S", "tw", "Tw", "腹板厚", "腹板厚度", "Web Thickness");
            TrySetDoubleParameterMm(symbol, rMm, "r", "R", "tf", "Tf", "翼板厚", "翼板厚度", "Flange Thickness");
            TrySetStringParameter(symbol, symbol.Name, "剖面名稱關鍵字", "類型標記", "Type Mark");
        }

        private static void TrySetColumnLevelAndOffsets(FamilyInstance column, IfcNativeColumnPlan plan)
        {
            TrySetElementIdParameter(column, plan.BaseLevel.Id, BuiltInParameter.FAMILY_BASE_LEVEL_PARAM);
            TrySetDoubleBuiltInParameter(column, plan.BaseOffsetFeet, BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM);
            TrySetElementIdParameter(column, plan.TopLevel.Id, BuiltInParameter.FAMILY_TOP_LEVEL_PARAM);
            TrySetDoubleBuiltInParameter(column, plan.TopOffsetFeet, BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM);
        }

        private static void TrySetAllDoubleParametersMm(Element element, double? valueMm, params string[] names)
        {
            if (!valueMm.HasValue) return;

            foreach (string name in names)
            {
                Parameter parameter = element.LookupParameter(name);
                if (parameter == null || parameter.IsReadOnly || parameter.StorageType != StorageType.Double) continue;
                parameter.Set(valueMm.Value * IfcSyncMmToFeet);
            }
        }

        private static void TrySetBeamEndpointOffsets(FamilyInstance beam, IfcNativeFramingPlan plan, Level level)
        {
            double startOffset = plan.Start.Z - level.Elevation;
            double endOffset = plan.End.Z - level.Elevation;
            TrySetDoubleParameterFeet(beam, startOffset, "起始樓層偏移", "起點樓層偏移", "Start Level Offset");
            TrySetDoubleParameterFeet(beam, endOffset, "結束樓層偏移", "終點樓層偏移", "End Level Offset");
        }

        private static void TrySetDoubleParameterFeet(Element element, double valueFeet, params string[] names)
        {
            foreach (string name in names)
            {
                Parameter parameter = element.LookupParameter(name);
                if (parameter == null || parameter.IsReadOnly || parameter.StorageType != StorageType.Double) continue;
                parameter.Set(valueFeet);
                return;
            }
        }

        private static void TrySetElementIdParameter(Element element, ElementId value, BuiltInParameter builtInParameter)
        {
            Parameter parameter = element.get_Parameter(builtInParameter);
            if (parameter != null && !parameter.IsReadOnly)
                parameter.Set(value);
        }

        private static void TrySetDoubleBuiltInParameter(Element element, double valueFeet, BuiltInParameter builtInParameter)
        {
            Parameter parameter = element.get_Parameter(builtInParameter);
            if (parameter != null && !parameter.IsReadOnly && parameter.StorageType == StorageType.Double)
                parameter.Set(valueFeet);
        }

        private static Level FindNearestLevelAtOrBelow(List<Level> levels, double zFeet)
        {
            Level result = levels.First();
            foreach (Level level in levels)
            {
                if (level.Elevation <= zFeet + 1e-6)
                    result = level;
                else
                    break;
            }

            return result;
        }

        private static string ReadAnyParameterString(Element element, Document doc, params string[] names)
        {
            foreach (string name in names)
            {
                Parameter parameter = element.LookupParameter(name);
                if (parameter != null && parameter.HasValue)
                {
                    string value = parameter.AsString() ?? parameter.AsValueString();
                    if (!string.IsNullOrWhiteSpace(value)) return value;
                }
            }

            Element typeElement = doc.GetElement(element.GetTypeId());
            if (typeElement != null)
            {
                foreach (string name in names)
                {
                    Parameter parameter = typeElement.LookupParameter(name);
                    if (parameter != null && parameter.HasValue)
                    {
                        string value = parameter.AsString() ?? parameter.AsValueString();
                        if (!string.IsNullOrWhiteSpace(value)) return value;
                    }
                }
            }

            return "";
        }

        private static double? ReadAnyParameterDoubleMm(Element element, Document doc, params string[] names)
        {
            double? instanceValue = ReadParameterDoubleMm(element, names);
            if (instanceValue.HasValue)
                return instanceValue.Value;

            Element typeElement = doc.GetElement(element.GetTypeId());
            return typeElement == null ? null : ReadParameterDoubleMm(typeElement, names);
        }

        private static double? ReadParameterDoubleMm(Element element, params string[] names)
        {
            foreach (string name in names)
            {
                Parameter parameter = element.LookupParameter(name);
                if (parameter == null || !parameter.HasValue) continue;

                if (parameter.StorageType == StorageType.Double)
                    return parameter.AsDouble() * IfcSyncFeetToMm;

                string raw = parameter.AsString() ?? parameter.AsValueString();
                if (string.IsNullOrWhiteSpace(raw)) continue;

                string numeric = new string(raw.Where(c => char.IsDigit(c) || c == '.' || c == '-').ToArray());
                if (double.TryParse(numeric, out double value))
                    return value;
            }

            return null;
        }

        private static string BuildIfcTypeName(string prefix, double depthMm, double widthMm, double? sMm, double? rMm)
        {
            string name = $"{prefix}-H{FormatSize(depthMm)}xB{FormatSize(widthMm)}";
            if (sMm.HasValue && sMm.Value > 0)
                name += $"xS{FormatSize(sMm.Value)}";
            if (rMm.HasValue && rMm.Value > 0)
                name += $"xR{FormatSize(rMm.Value)}";
            return name;
        }

        private static void NormalizeColumnSectionDimensions(ref double widthMm, ref double depthMm)
        {
            if (widthMm <= 0 || depthMm <= 0)
                return;

            double shortSide = Math.Min(widthMm, depthMm);
            double longSide = Math.Max(widthMm, depthMm);
            widthMm = shortSide;
            depthMm = longSide;
        }

        private static double RoundTo(double value, double increment)
        {
            return Math.Round(value / increment) * increment;
        }

        private static string FormatSize(double value)
        {
            return Math.Abs(value - Math.Round(value)) < 0.001
                ? Math.Round(value).ToString("0")
                : value.ToString("0.##");
        }

        private class SyncTypeCache
        {
            public Dictionary<string, FamilySymbol> FramingSymbols { get; } = new Dictionary<string, FamilySymbol>();
            public Dictionary<string, FamilySymbol> ColumnSymbols { get; } = new Dictionary<string, FamilySymbol>();
        }

        private class ElementIdComparer : IEqualityComparer<ElementId>
        {
            public bool Equals(ElementId x, ElementId y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (x == null || y == null) return false;
                return x.GetIdValue() == y.GetIdValue();
            }

            public int GetHashCode(ElementId obj)
            {
                return obj == null ? 0 : obj.GetIdValue().GetHashCode();
            }
        }

        private class IfcGeometryEdge
        {
            public XYZ Start { get; set; }
            public XYZ End { get; set; }
            public double Length { get; set; }
        }

        private class IfcDirectionGroup
        {
            public XYZ Direction { get; set; }
            public double TotalLength { get; set; }
            public double MaxLength { get; set; }
            public int Count { get; set; }
        }

        private class IfcNativeFramingPlan
        {
            public IdType SourceElementId { get; set; }
            public string SourceName { get; set; }
            public string IfcGuid { get; set; }
            public string SourceKey { get; set; }
            public string SourceComment { get; set; }
            public string Mark { get; set; }
            public bool CanCreate { get; set; }
            public string SkipReason { get; set; }
            public string TypeName { get; set; }
            public XYZ Start { get; set; }
            public XYZ End { get; set; }
            public double LengthMm { get; set; }
            public double WidthMm { get; set; }
            public double DepthMm { get; set; }
            public double? SMm { get; set; }
            public double? RMm { get; set; }

            public object ToPreviewResult()
            {
                return new
                {
                    SourceElementId,
                    SourceName,
                    IfcGuid,
                    CanCreate,
                    SkipReason,
                    TypeName,
                    WidthMm,
                    DepthMm,
                    SMm,
                    RMm,
                    LengthMm,
                    Start = Start == null ? null : ToPoint(Start),
                    End = End == null ? null : ToPoint(End),
                    SourceComment
                };
            }

            public object ToCreatedResult(IdType elementId, FamilySymbol symbol)
            {
                return new
                {
                    ElementId = elementId,
                    SourceElementId,
                    TypeName = symbol.Name,
                    FamilyName = symbol.FamilyName,
                    WidthMm,
                    DepthMm,
                    SMm,
                    RMm,
                    LengthMm,
                    SourceComment
                };
            }
        }

        private class IfcNativeColumnPlan
        {
            public IdType SourceElementId { get; set; }
            public string SourceName { get; set; }
            public string IfcGuid { get; set; }
            public string SourceKey { get; set; }
            public string SourceComment { get; set; }
            public string Mark { get; set; }
            public bool CanCreate { get; set; }
            public string SkipReason { get; set; }
            public string TypeName { get; set; }
            public double CenterXFeet { get; set; }
            public double CenterYFeet { get; set; }
            public double BaseZFeet { get; set; }
            public double TopZFeet { get; set; }
            public double OriginalTopZFeet { get; set; }
            public double WidthMm { get; set; }
            public double DepthMm { get; set; }
            public double? SMm { get; set; }
            public double? RMm { get; set; }
            public double HeightMm { get; set; }
            public double? SolidVolumeRatio { get; set; }
            public string ColumnBaseKind { get; set; }
            public string ColumnBaseReason { get; set; }
            public bool ColumnTopAlignedToFloorBottom { get; set; }
            public IdType? TopFloorId { get; set; }
            public string TopFloorName { get; set; }
            public double? TopAlignmentDeltaMm { get; set; }
            public string TopAlignmentMode { get; set; }
            public string TopAlignmentMessage { get; set; }
            public Level BaseLevel { get; set; }
            public Level TopLevel { get; set; }
            public double BaseOffsetFeet { get; set; }
            public double TopOffsetFeet { get; set; }

            public object ToPreviewResult()
            {
                return new
                {
                    SourceElementId,
                    SourceName,
                    IfcGuid,
                    CanCreate,
                    SkipReason,
                    TypeName,
                    WidthMm,
                    DepthMm,
                    SMm,
                    RMm,
                    SolidVolumeRatio = SolidVolumeRatio.HasValue ? (object)Math.Round(SolidVolumeRatio.Value, 3) : null,
                    ColumnBaseKind,
                    ColumnBaseReason,
                    HeightMm,
                    ColumnTopAlignedToFloorBottom,
                    TopFloorId,
                    TopFloorName,
                    TopAlignmentDeltaMm,
                    TopAlignmentMode,
                    TopAlignmentMessage,
                    Center = new
                    {
                        X = Math.Round(CenterXFeet * IfcSyncFeetToMm, 2),
                        Y = Math.Round(CenterYFeet * IfcSyncFeetToMm, 2)
                    },
                    OriginalTopZMm = Math.Round(OriginalTopZFeet * IfcSyncFeetToMm, 2),
                    TopZMm = Math.Round(TopZFeet * IfcSyncFeetToMm, 2),
                    BaseLevel = BaseLevel?.Name,
                    TopLevel = TopLevel?.Name,
                    BaseOffsetMm = Math.Round(BaseOffsetFeet * IfcSyncFeetToMm, 2),
                    TopOffsetMm = Math.Round(TopOffsetFeet * IfcSyncFeetToMm, 2),
                    SourceComment
                };
            }

            public object ToCreatedResult(IdType elementId, FamilySymbol symbol)
            {
                return new
                {
                    ElementId = elementId,
                    SourceElementId,
                    TypeName = symbol.Name,
                    FamilyName = symbol.FamilyName,
                    WidthMm,
                    DepthMm,
                    SMm,
                    RMm,
                    SolidVolumeRatio = SolidVolumeRatio.HasValue ? (object)Math.Round(SolidVolumeRatio.Value, 3) : null,
                    ColumnBaseKind,
                    ColumnBaseReason,
                    HeightMm,
                    ColumnTopAlignedToFloorBottom,
                    TopFloorId,
                    TopFloorName,
                    TopAlignmentDeltaMm,
                    TopAlignmentMode,
                    TopAlignmentMessage,
                    OriginalTopZMm = Math.Round(OriginalTopZFeet * IfcSyncFeetToMm, 2),
                    TopZMm = Math.Round(TopZFeet * IfcSyncFeetToMm, 2),
                    BaseLevel = BaseLevel.Name,
                    TopLevel = TopLevel.Name,
                    SourceComment
                };
            }
        }

        private static object ToPoint(XYZ point)
        {
            return new
            {
                X = Math.Round(point.X * IfcSyncFeetToMm, 2),
                Y = Math.Round(point.Y * IfcSyncFeetToMm, 2),
                Z = Math.Round(point.Z * IfcSyncFeetToMm, 2)
            };
        }
    }
}
