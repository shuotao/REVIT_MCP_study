#if REVIT2024_OR_GREATER
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitMCP.Core.Grading;

namespace RevitMCP.Core
{
    public partial class CommandExecutor
    {
        /// <summary>
        /// 將樓板底面投影套用至 Toposolid 設計副本，並計算挖填方。
        /// </summary>
        private object GradeToposolidToFloors(JObject parameters)
        {
            var stopwatch = Stopwatch.StartNew();
            var timeline = new GradingTimeline();
            var request = new GradingRequest
            {
                ToposolidId = parameters["toposolidId"]?.Value<long>() ?? 0,
                FloorIds = parameters["floorIds"]?.Values<long>().ToArray() ?? new long[0],
                Mode = parameters["mode"]?.Value<string>() ?? "footprint_only",
                TargetFace = parameters["targetFace"]?.Value<string>() ?? "bottom",
                // 預設自動設定階段：一般使用者的地形建立於「新建」階段，
                // 整地需要它成為既有地貌；除非明確傳入 false，工具自動調整。
                AllowPhaseSetup = parameters["allowPhaseSetup"]?.Value<bool>() ?? true,
                UpdateExisting = parameters["updateExisting"]?.Value<bool>() ?? false
            };
            request.Validate();

            var doc = _uiApp.ActiveUIDocument.Document;
            IToposolidGradingAdapter adapter = new RevitToposolidGradingAdapter(timeline);
            Toposolid original;
            IReadOnlyList<Floor> floors;
            IReadOnlyList<FloorFootprint> footprints;
            using (timeline.Measure("元素驗證"))
            {
                original = adapter.ValidateToposolid(doc, request.ToposolidId);
                floors = adapter.ValidateFloors(doc, request.FloorIds);
            }

            using (timeline.Measure("樓板底面擷取"))
            {
                footprints = adapter.ExtractBottomFootprints(floors);
            }

            Toposolid design = null!;
            string associationId = null!;
            var modifiedPointCount = 0;
            var cutCubicMeters = 0.0;
            var fillCubicMeters = 0.0;

            using (var group = new TransactionGroup(doc, "樓板投影整地"))
            {
                try
                {
                    if (group.Start() != TransactionStatus.Started)
                    {
                        throw new InvalidOperationException("無法啟動樓板投影整地交易群組。");
                    }

                    using (var setupTransaction = new Transaction(doc, "建立整地設計副本"))
                    {
                        if (setupTransaction.Start() != TransactionStatus.Started)
                        {
                            throw new InvalidOperationException("無法啟動建立整地設計副本交易。");
                        }

                        using (timeline.Measure("設計副本與階段設定"))
                        {
                            design = adapter.CreateDesignCopy(doc, original, request.AllowPhaseSetup);
                            associationId = adapter.WriteAssociation(
                                doc,
                                design,
                                request.ToposolidId,
                                request.FloorIds);
                        }

                        using (timeline.Measure("交易一提交"))
                        {
                            if (setupTransaction.Commit() != TransactionStatus.Committed)
                            {
                                throw new InvalidOperationException("建立整地設計副本交易未能提交。");
                            }
                        }
                    }

                    using (var gradingTransaction = new Transaction(doc, "套用樓板投影並計算挖填方"))
                    {
                        if (gradingTransaction.Start() != TransactionStatus.Started)
                        {
                            throw new InvalidOperationException("無法啟動套用樓板投影交易。");
                        }

                        modifiedPointCount = adapter.ApplyFootprintOnly(doc, design, footprints);
                        using (timeline.Measure("整地後重生"))
                        {
                            doc.Regenerate();
                        }

                        using (timeline.Measure("CUT/FILL 讀取"))
                        {
                            (cutCubicMeters, fillCubicMeters) = adapter.ReadCutFill(design);
                        }

                        using (timeline.Measure("交易二提交"))
                        {
                            if (gradingTransaction.Commit() != TransactionStatus.Committed)
                            {
                                throw new InvalidOperationException("套用樓板投影交易未能提交。");
                            }
                        }
                    }

                    using (timeline.Measure("交易群組彙整"))
                    {
                        if (group.Assimilate() != TransactionStatus.Committed)
                        {
                            throw new InvalidOperationException("樓板投影整地交易群組未能完整提交。");
                        }
                    }
                }
                catch (Exception exception)
                {
                    if (group.GetStatus() == TransactionStatus.Started)
                    {
                        group.RollBack();
                    }

                    // 失敗也要留下效能與原因記錄，供逐次修正。
                    WriteGradingPerformanceRecord(new
                    {
                        Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        Document = doc.Title,
                        Success = false,
                        Error = exception.Message,
                        request.ToposolidId,
                        FloorIds = request.FloorIds,
                        TotalMilliseconds = stopwatch.ElapsedMilliseconds,
                        Stages = timeline.Stages
                            .Select(stage => new { stage.Name, stage.ElapsedMilliseconds })
                            .ToArray()
                    });

                    throw;
                }
            }

            var result = new GradingResult
            {
                OriginalToposolidId = request.ToposolidId,
                DesignToposolidId = design.Id.GetIdValue(),
                FloorIds = request.FloorIds,
                CutCubicMeters = cutCubicMeters,
                FillCubicMeters = fillCubicMeters,
                ModifiedPointCount = modifiedPointCount,
                AssociationId = associationId,
                Warnings = new string[0]
            };

            var timing = new
            {
                TotalMilliseconds = stopwatch.ElapsedMilliseconds,
                Stages = timeline.Stages
                    .Select(stage => new { stage.Name, stage.ElapsedMilliseconds })
                    .ToArray()
            };

            WriteGradingPerformanceRecord(new
            {
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Document = doc.Title,
                Success = true,
                Error = (string)null,
                request.ToposolidId,
                FloorIds = request.FloorIds,
                DesignToposolidId = result.DesignToposolidId,
                result.ModifiedPointCount,
                result.CutCubicMeters,
                result.FillCubicMeters,
                Timing = timing
            });

            return new
            {
                result.OriginalToposolidId,
                result.DesignToposolidId,
                result.FloorIds,
                result.CutCubicMeters,
                result.FillCubicMeters,
                result.NetCubicMeters,
                result.ModifiedPointCount,
                result.AssociationId,
                result.Warnings,
                Timing = timing,
                Message = "樓板投影整地完成。"
            };
        }

        /// <summary>
        /// 附加一筆整地效能記錄至 %APPDATA%\RevitMCP\grading-performance.jsonl。
        /// 記錄僅供優化參考，寫入失敗不得影響整地流程。
        /// </summary>
        private static void WriteGradingPerformanceRecord(object record)
        {
            try
            {
                var directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "RevitMCP");
                Directory.CreateDirectory(directory);
                File.AppendAllText(
                    Path.Combine(directory, "grading-performance.jsonl"),
                    JsonConvert.SerializeObject(record) + Environment.NewLine);
            }
            catch
            {
            }
        }
    }
}
#endif
