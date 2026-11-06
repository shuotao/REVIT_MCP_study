using System;
using Autodesk.Revit.DB;
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
        #region set_section_box（直接設定 3D 視圖 Section Box）

        /// <summary>
        /// set_section_box：直接設定非樣板 3D 視圖的 Section Box（不需目標元素）。
        /// 模式二擇一：
        ///   絕對：minMm + maxMm（世界座標、內部原點、軸向對齊）。
        ///   相對：offsetsMm { minX,maxX,minY,maxY,minZ,maxZ }，在目前 Section Box 各面上位移，
        ///         正＝往外擴、負＝往內縮；目前框有旋轉時於框自身座標系內位移並保留其 Transform。
        /// 與 set_3d_section_box 相同：正交視圖關閉 CropBox 避免雙重裁切（透視視圖必須保留裁剪，不動）。
        /// set_3d_section_box 本身不修改。
        /// </summary>
        private object SetSectionBox(JObject parameters)
        {
            var uiDoc = _uiApp.ActiveUIDocument;
            Document doc = uiDoc.Document;

            // 2026-09-26 稽核：parameters 為 null 或 "viewId": null 時舊版丟 NullReference／轉型錯誤。
            IdType viewIdParam = parameters?["viewId"]?.Type == JTokenType.Null ? 0 : (parameters?["viewId"]?.Value<IdType?>() ?? 0);
            if (parameters == null) parameters = new JObject();
            View3D view3D = (viewIdParam == 0 ? doc.ActiveView : doc.GetElement(viewIdParam.ToElementId())) as View3D;
            if (view3D == null || view3D.IsTemplate)
                throw new Exception(viewIdParam == 0
                    ? "目前作用中的視圖不是非樣板 3D 視圖；請指定 viewId"
                    : $"視圖 ID={viewIdParam} 不是非樣板 3D 視圖");

            bool enable = parameters["enable"]?.Value<bool>() ?? true;
            JObject minObj = parameters["minMm"] as JObject;
            JObject maxObj = parameters["maxMm"] as JObject;
            JObject offObj = parameters["offsetsMm"] as JObject;

            bool absolute = minObj != null || maxObj != null;
            bool relative = offObj != null;
            if (absolute && relative)
                throw new Exception("minMm/maxMm（絕對）與 offsetsMm（相對）只能擇一");
            if (!absolute && !relative)
                throw new Exception("必須提供 minMm＋maxMm（絕對範圍），或 offsetsMm（相對目前框位移）");
            if (absolute && (minObj == null || maxObj == null))
                throw new Exception("絕對模式需同時提供 minMm 與 maxMm");

            bool wasActive = view3D.IsSectionBoxActive;
            BoundingBoxXYZ current = view3D.GetSectionBox();
            WorldBox before = current != null ? ToWorldBox(current) : null;

            BoundingBoxXYZ newBox;
            string mode;
            if (absolute)
            {
                mode = "Absolute";
                double x0 = ReadMm(minObj, "x", "minMm"), y0 = ReadMm(minObj, "y", "minMm"), z0 = ReadMm(minObj, "z", "minMm");
                double x1 = ReadMm(maxObj, "x", "maxMm"), y1 = ReadMm(maxObj, "y", "maxMm"), z1 = ReadMm(maxObj, "z", "maxMm");
                ValidateMinMax(x0, x1, y0, y1, z0, z1);
                newBox = new BoundingBoxXYZ
                {
                    Min = new XYZ(x0 / 304.8, y0 / 304.8, z0 / 304.8),
                    Max = new XYZ(x1 / 304.8, y1 / 304.8, z1 / 304.8),
                    Transform = Transform.Identity,
                    Enabled = true
                };
            }
            else
            {
                mode = "Offsets";
                if (current == null)
                    throw new Exception("此 3D 視圖讀不到目前的 Section Box，無法使用 offsetsMm；請改用 minMm＋maxMm");
                double oMinX = offObj["minX"]?.Value<double>() ?? 0, oMaxX = offObj["maxX"]?.Value<double>() ?? 0;
                double oMinY = offObj["minY"]?.Value<double>() ?? 0, oMaxY = offObj["maxY"]?.Value<double>() ?? 0;
                double oMinZ = offObj["minZ"]?.Value<double>() ?? 0, oMaxZ = offObj["maxZ"]?.Value<double>() ?? 0;
                // 局部座標（mm）：Min 面往外＝減、Max 面往外＝加
                double x0 = current.Min.X * 304.8 - oMinX, x1 = current.Max.X * 304.8 + oMaxX;
                double y0 = current.Min.Y * 304.8 - oMinY, y1 = current.Max.Y * 304.8 + oMaxY;
                double z0 = current.Min.Z * 304.8 - oMinZ, z1 = current.Max.Z * 304.8 + oMaxZ;
                ValidateMinMax(x0, x1, y0, y1, z0, z1);
                newBox = new BoundingBoxXYZ
                {
                    Min = new XYZ(x0 / 304.8, y0 / 304.8, z0 / 304.8),
                    Max = new XYZ(x1 / 304.8, y1 / 304.8, z1 / 304.8),
                    Transform = current.Transform ?? Transform.Identity,
                    Enabled = true
                };
            }

            bool cropTurnedOff = false;
            using (Transaction trans = TransactionHelper.Begin(doc, "設定剖面框"))
            {
                trans.Start();
                view3D.SetSectionBox(newBox);
                view3D.IsSectionBoxActive = enable;
                if (!view3D.IsPerspective && (view3D.CropBoxActive || view3D.CropBoxVisible))
                {
                    view3D.CropBoxActive = false;
                    view3D.CropBoxVisible = false;
                    cropTurnedOff = true;
                }
                trans.Commit();
            }

            WorldBox after = ToWorldBox(view3D.GetSectionBox());

            return new
            {
                Success = true,
                Mode = mode,
                ViewId = view3D.Id.GetIdValue(),
                ViewName = view3D.Name,
                IsPerspective = view3D.IsPerspective,
                SectionBoxWasActive = wasActive,
                SectionBoxActive = view3D.IsSectionBoxActive,
                CropBoxActive = view3D.CropBoxActive,
                CropBoxTurnedOff = cropTurnedOff,
                CropNote = view3D.IsPerspective ? "透視視圖必須保留裁剪區域，CropBox 未變更" : null,
                Before_mm = before?.ToJson(),
                After_mm = after?.ToJson()
            };
        }

        private static double ReadMm(JObject obj, string key, string owner)
        {
            JToken t = obj[key];
            if (t == null || (t.Type != JTokenType.Float && t.Type != JTokenType.Integer))
                throw new Exception($"{owner}.{key} 必須是數值（mm）");
            return t.Value<double>();
        }

        private static void ValidateMinMax(double x0, double x1, double y0, double y1, double z0, double z1)
        {
            if (!(x0 < x1)) throw new Exception($"X 軸 min ({x0:F0}) 必須小於 max ({x1:F0}) mm");
            if (!(y0 < y1)) throw new Exception($"Y 軸 min ({y0:F0}) 必須小於 max ({y1:F0}) mm");
            if (!(z0 < z1)) throw new Exception($"Z 軸 min ({z0:F0}) 必須小於 max ({z1:F0}) mm");
        }

        private sealed class WorldBox
        {
            public double MinX, MinY, MinZ, MaxX, MaxY, MaxZ; // mm
            public object ToJson() => new
            {
                Min = new { x = Math.Round(MinX, 1), y = Math.Round(MinY, 1), z = Math.Round(MinZ, 1) },
                Max = new { x = Math.Round(MaxX, 1), y = Math.Round(MaxY, 1), z = Math.Round(MaxZ, 1) },
                Size = new { x = Math.Round(MaxX - MinX, 1), y = Math.Round(MaxY - MinY, 1), z = Math.Round(MaxZ - MinZ, 1) }
            };
        }

        /// <summary>BoundingBoxXYZ（含 Transform）→ 世界座標軸向外接框（mm）。</summary>
        private static WorldBox ToWorldBox(BoundingBoxXYZ box)
        {
            if (box == null) return null;
            Transform t = box.Transform ?? Transform.Identity;
            var w = new WorldBox
            {
                MinX = double.PositiveInfinity, MinY = double.PositiveInfinity, MinZ = double.PositiveInfinity,
                MaxX = double.NegativeInfinity, MaxY = double.NegativeInfinity, MaxZ = double.NegativeInfinity
            };
            for (int ix = 0; ix <= 1; ix++)
            for (int iy = 0; iy <= 1; iy++)
            for (int iz = 0; iz <= 1; iz++)
            {
                XYZ p = t.OfPoint(new XYZ(ix == 0 ? box.Min.X : box.Max.X, iy == 0 ? box.Min.Y : box.Max.Y, iz == 0 ? box.Min.Z : box.Max.Z));
                w.MinX = Math.Min(w.MinX, p.X * 304.8); w.MaxX = Math.Max(w.MaxX, p.X * 304.8);
                w.MinY = Math.Min(w.MinY, p.Y * 304.8); w.MaxY = Math.Max(w.MaxY, p.Y * 304.8);
                w.MinZ = Math.Min(w.MinZ, p.Z * 304.8); w.MaxZ = Math.Max(w.MaxZ, p.Z * 304.8);
            }
            return w;
        }

        #endregion
    }
}
