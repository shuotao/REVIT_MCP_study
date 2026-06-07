using Autodesk.Revit.DB;

namespace RevitMCP.Models
{
    public enum DwgLoadMode
    {
        Link,
        Import
    }

    public enum DwgPlacementMode
    {
        OriginToOrigin,
        SharedCoordinates,
        OriginThenGridCheck,
        GridAlignment,
        ModelSpaceByLevelElevation
    }

    public class DwgImportSettings
    {
        public DwgLoadMode LoadMode { get; set; } = DwgLoadMode.Import;
        public DwgPlacementMode PlacementMode { get; set; } = DwgPlacementMode.OriginThenGridCheck;
        public bool ThisViewOnly { get; set; } = true;
        public bool PinAfterLoad { get; set; } = true;
        public bool VisibleLayersOnly { get; set; } = true;
        public ImportUnit Unit { get; set; } = ImportUnit.Millimeter;
        public ImportColorMode ColorMode { get; set; } = ImportColorMode.BlackAndWhite;
        public double ToleranceMm { get; set; } = 1.0;
    }

    public class DwgImportResult
    {
        public string DwgPath { get; set; } = string.Empty;
        public string LevelName { get; set; } = string.Empty;
        public long LevelId { get; set; }
        public string ViewName { get; set; } = string.Empty;
        public long ViewId { get; set; }
        public long ElementId { get; set; }
        public string LoadMode { get; set; } = string.Empty;
        public string PlacementMode { get; set; } = string.Empty;
        public bool Success { get; set; }
        public bool Pinned { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
