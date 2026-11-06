using System;
using System.Globalization;

namespace RevitMCP.Core
{
    /// <summary>
    /// 外牆開口檢討的法規門檻值（可依轄區替換）。
    /// 預設值完全複製台灣建築技術規則 §45 / §110，行為與硬編碼版本一致。
    /// 其他轄區（例如越南 QCVN）建立自己的實例即可，不需修改 ExteriorWallOpeningChecker。
    /// </summary>
    public class OpeningCheckThresholds
    {
        /// <summary>轄區代碼，例：tw、vn</summary>
        public string JurisdictionCode { get; set; } = "tw";

        /// <summary>法源描述，寫入報告供追溯</summary>
        public string LegalBasis { get; set; } = "建築技術規則建築設計施工編 §45 / §110";

        // ── §45：開口與鄰地／鄰棟距離 ──────────────────────────────

        /// <summary>開口距基地境界線的最小距離（m）</summary>
        public double MinBoundaryDistanceM { get; set; } = 1.0;

        /// <summary>同一基地內建築物間，單面開口的最小水平淨距離（m）</summary>
        public double MinBuildingDistanceSingleSidedM { get; set; } = 1.0;

        /// <summary>同一基地內建築物間，相對開口的最小水平淨距離（m）</summary>
        public double MinBuildingDistanceDoubleSidedM { get; set; } = 2.0;

        // ── §110：防火間隔（對基地境界線）────────────────────────

        /// <summary>鄰接此距離以上之道路或永久空地即不適用防火間隔規定（m）</summary>
        public double BoundaryFireGapExemptM { get; set; } = 6.0;

        /// <summary>退縮距離小於此值 → 需全時效防火（m）</summary>
        public double BoundaryFullRatingBelowM { get; set; } = 1.5;

        /// <summary>退縮距離小於此值 → 需半時效防火（m）</summary>
        public double BoundaryHalfRatingBelowM { get; set; } = 3.0;

        // ── §110：防火間隔（同基地建築物之間）────────────────────

        /// <summary>建築物間距達此值以上即不需防火時效（m）</summary>
        public double BuildingFireGapExemptM { get; set; } = 6.0;

        /// <summary>建築物間距小於此值 → 需全時效防火（m）</summary>
        public double BuildingFullRatingBelowM { get; set; } = 3.0;

        /// <summary>建築物間距小於此值 → 需半時效防火（m）</summary>
        public double BuildingHalfRatingBelowM { get; set; } = 6.0;

        // ── 防火時效等級（小時）──────────────────────────────────

        /// <summary>全時效（小時）</summary>
        public double FullFireRatingHours { get; set; } = 1.0;

        /// <summary>半時效（小時）</summary>
        public double HalfFireRatingHours { get; set; } = 0.5;

        /// <summary>台灣預設門檻（與本類別預設值相同，明示用意）</summary>
        public static OpeningCheckThresholds Taiwan()
        {
            return new OpeningCheckThresholds();
        }

        /// <summary>以 InvariantCulture 格式化門檻數字，避免逗號小數點文化下報告文字變成「1,0m」。</summary>
        public static string Fmt(double value, string format)
        {
            return value.ToString(format, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 將時效小時數轉為報告用字。1.0 → 「1小時」、0.5 → 「半小時」，
        /// 其餘值以數字呈現，避免換轄區後文字與數值不符。
        /// </summary>
        public static string FormatRating(double hours)
        {
            if (Math.Abs(hours - 1.0) < 1e-9) return "1小時";
            if (Math.Abs(hours - 0.5) < 1e-9) return "半小時";
            return hours.ToString("0.##", CultureInfo.InvariantCulture) + "小時";
        }
    }
}
