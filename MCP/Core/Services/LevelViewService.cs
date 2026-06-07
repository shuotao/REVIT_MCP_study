using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

#if REVIT2025_OR_GREATER
using IdType = System.Int64;
#else
using IdType = System.Int32;
#endif

namespace RevitMCP.Core.Services
{
    internal class LevelViewService
    {
        public IList<Level> ResolveLevels(Document doc, IEnumerable<string> levelNames, IEnumerable<long> levelIds)
        {
            var levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();

            var selected = new List<Level>();
            var idSet = new HashSet<long>(levelIds ?? Enumerable.Empty<long>());
            if (idSet.Count > 0)
            {
                selected.AddRange(levels.Where(level => idSet.Contains(Convert.ToInt64(level.Id.GetIdValue()))));
            }

            foreach (string? rawName in levelNames ?? Enumerable.Empty<string>())
            {
                string? name = rawName?.Trim();
                if (string.IsNullOrEmpty(name)) continue;

                Level? level = levels.FirstOrDefault(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    ?? levels.FirstOrDefault(l => l.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);

                if (level != null && selected.All(existing => existing.Id != level.Id))
                {
                    selected.Add(level);
                }
            }

            return selected.Count > 0 ? selected : levels;
        }

        public ViewPlan GetOrCreateCadFloorPlan(Document doc, Level level)
        {
            string viewName = BuildCadViewName(level);
            ViewPlan? existing = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .FirstOrDefault(v => !v.IsTemplate && v.ViewType == ViewType.FloorPlan && v.Name == viewName);

            if (existing != null)
            {
                return existing;
            }

            ViewFamilyType? floorPlanType = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.FloorPlan);

            if (floorPlanType == null)
            {
                throw new InvalidOperationException("找不到 Floor Plan 的 ViewFamilyType，無法建立 CAD 放樣視圖。");
            }

            ViewPlan view = ViewPlan.Create(doc, floorPlanType.Id, level.Id);
            view.Name = viewName;
            return view;
        }

        private static string BuildCadViewName(Level level)
        {
            return "CAD 放樣_" + level.Name;
        }
    }
}
