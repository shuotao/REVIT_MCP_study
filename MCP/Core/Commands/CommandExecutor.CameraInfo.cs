using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

// Revit 2025+ ElementId: int → long
#if REVIT2025_OR_GREATER
using IdType = System.Int64;
#else
using IdType = System.Int32;
#endif

namespace RevitMCP.Core
{
    /// <summary>
    /// 3D 視圖／相機的實際位置讀取。
    ///
    /// 存在理由：性質面板的 Eye Elevation / Target Elevation 只是一個數字，
    /// 看不出它從哪個高度量起（實案出現「在 1F 平面放相機，Eye Elevation 卻顯示負值」）。
    /// 這裡直接讀 ViewOrientation3D 的眼睛座標（內部原點座標系），
    /// 再跟 Eye Elevation 參數、各樓層、專案基準點、測量點逐一比對，
    /// 用數據算出該參數的 0 點，而不是推測。
    /// </summary>
    public partial class CommandExecutor
    {
        /// <summary>比對「參數 0 點」與候選基準高度時的容差 (mm)。</summary>
        private const double CameraDatumToleranceMm = 1.0;

        private object GetCameraInfo(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            View3D view = ResolveCameraView(doc, parameters);

            List<Level> levels = CameraMath.GetLevelsSorted(doc);
            CameraMath.CameraGeometry geometry = CameraMath.ComputeGeometry(view, levels);
            XYZ eye = geometry.Eye;
            XYZ forward = geometry.Forward;

            double eyeZMm = geometry.EyeZMm;
            double pitchDeg = geometry.PitchDeg;

            // 候選基準高度（全部是內部原點座標系的 Z），用來辨認 Eye Elevation 參數的 0 點
            var datums = new List<KeyValuePair<string, double>>
            {
                new KeyValuePair<string, double>("內部原點 (Internal Origin)", 0.0)
            };

            BasePoint projectBasePoint = BasePoint.GetProjectBasePoint(doc);
            BasePoint surveyPoint = BasePoint.GetSurveyPoint(doc);
            if (projectBasePoint != null)
                datums.Add(new KeyValuePair<string, double>("專案基準點 (Project Base Point)", CameraFeetToMm(projectBasePoint.Position.Z)));
            if (surveyPoint != null)
                datums.Add(new KeyValuePair<string, double>("測量點 (Survey Point)", CameraFeetToMm(surveyPoint.Position.Z)));

            foreach (Level level in levels)
                datums.Add(new KeyValuePair<string, double>("樓層 " + level.Name, CameraFeetToMm(level.ProjectElevation)));

            // 眼睛落在哪一層之上：取「樓層高程 ≤ 眼睛」的最高那層
            Level levelBelowEye = geometry.LevelBelow;

            var levelTable = levels.Select(l => (object)new
            {
                Name = l.Name,
                ProjectElevationMm = Math.Round(CameraFeetToMm(l.ProjectElevation), 1),
                EyeAboveLevelMm = Math.Round(eyeZMm - CameraFeetToMm(l.ProjectElevation), 1)
            }).ToList();

            // 讀視圖上所有 Eye / Target 相關的內建參數，並用實際眼睛 Z 反推 Eye 參數的 0 點
            var cameraParams = new List<object>();
            foreach (Parameter p in view.Parameters)
            {
                if (p == null || p.StorageType != StorageType.Double) continue;
                if (!(p.Definition is InternalDefinition def)) continue;

                string bipName = def.BuiltInParameter.ToString();
                bool isEye = bipName.IndexOf("EYE", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isTarget = bipName.IndexOf("TARGET", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!isEye && !isTarget) continue;

                double valueMm = CameraFeetToMm(p.AsDouble());
                object datumInfo = null;

                if (isEye)
                {
                    double impliedZeroMm = eyeZMm - valueMm;
                    var matches = datums
                        .Where(d => Math.Abs(d.Value - impliedZeroMm) <= CameraDatumToleranceMm)
                        .Select(d => d.Key)
                        .ToList();

                    datumInfo = new
                    {
                        ImpliedZeroElevationMm = Math.Round(impliedZeroMm, 1),
                        MatchesDatum = matches,
                        Note = matches.Count > 0
                            ? "此參數的 0 點與上列基準高度一致（容差 1 mm）。"
                            : "此參數的 0 點不等於任何樓層或基準點；ImpliedZeroElevationMm 是它在內部原點座標系的實際高度。"
                    };
                }

                cameraParams.Add(new
                {
                    Name = def.Name,
                    BuiltInParameter = bipName,
                    ValueMm = Math.Round(valueMm, 1),
                    DisplayValue = p.AsValueString(),
                    Datum = datumInfo
                });
            }

            return new
            {
                Success = true,
                ViewId = view.Id.GetIdValue(),
                ViewName = view.Name,
                IsPerspective = view.IsPerspective,
                Note = "所有 Mm 值皆為內部原點座標系下的毫米；DisplayValue 是性質面板顯示的原字串（專案顯示單位）。",
                Eye = new
                {
                    XMm = Math.Round(CameraFeetToMm(eye.X), 1),
                    YMm = Math.Round(CameraFeetToMm(eye.Y), 1),
                    ZMm = Math.Round(eyeZMm, 1),
                    LevelBelow = levelBelowEye != null ? levelBelowEye.Name : null,
                    AboveLevelBelowMm = levelBelowEye != null
                        ? (double?)Math.Round(eyeZMm - CameraFeetToMm(levelBelowEye.ProjectElevation), 1)
                        : null
                },
                ViewDirection = new
                {
                    X = Math.Round(forward.X, 4),
                    Y = Math.Round(forward.Y, 4),
                    Z = Math.Round(forward.Z, 4),
                    PitchDeg = pitchDeg,
                    IsLevel = Math.Abs(pitchDeg) < 0.05
                },
                BasePoints = new
                {
                    ProjectBasePointZMm = projectBasePoint != null ? (double?)Math.Round(CameraFeetToMm(projectBasePoint.Position.Z), 1) : null,
                    SurveyPointZMm = surveyPoint != null ? (double?)Math.Round(CameraFeetToMm(surveyPoint.Position.Z), 1) : null
                },
                Levels = levelTable,
                CameraParameters = cameraParams
            };
        }

        /// <summary>
        /// 解析目標 3D 視圖：viewId 可給 View3D 本身，也可給平面上選到的相機元件
        /// （Cameras 類別，與 View3D 同名）；省略時用目前作用中的視圖。
        /// </summary>
        private View3D ResolveCameraView(Document doc, JObject parameters)
        {
            IdType? requestedId = parameters?["viewId"]?.Value<IdType?>();

            if (requestedId == null)
            {
                if (_uiApp.ActiveUIDocument.ActiveView is View3D active)
                    return active;
                throw new Exception($"目前作用中的視圖 '{_uiApp.ActiveUIDocument.ActiveView?.Name}' 不是 3D 視圖，請指定 viewId。");
            }

            Element element = doc.GetElement(new ElementId(requestedId.Value));
            if (element == null)
                throw new Exception($"找不到元素 ID: {requestedId.Value}");

            if (element is View3D direct)
                return direct;

            // 平面上選到的相機圖元：用名稱找回同名、非樣板的 View3D
            View3D byName = new FilteredElementCollector(doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .FirstOrDefault(v => !v.IsTemplate && v.Name == element.Name);

            if (byName != null)
                return byName;

            throw new Exception($"元素 {requestedId.Value}（{element.Name}）不是 3D 視圖，也找不到同名的 3D 視圖。");
        }

        /// <summary>
        /// 保留原名稱／簽名以降低本次重構對這個檔案其他呼叫點的影響；實際運算已移到
        /// <see cref="CameraMath.FeetToMm"/>（相機管理視窗與這裡共用同一份換算）。
        /// </summary>
        private static double CameraFeetToMm(double feet)
        {
            return CameraMath.FeetToMm(feet);
        }
    }
}
