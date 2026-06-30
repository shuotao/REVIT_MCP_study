using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using ItemFactoryBase = Autodesk.Revit.Creation.ItemFactoryBase;

namespace RevitMCP.Core
{
    public partial class CommandExecutor
    {
        private class DoorWindowLegendTypeInfo
        {
        	public ElementId TypeId { get; set; }

        	public string TypeMarkRaw { get; set; }

        	public string TypeMarkDisplay { get; set; }

        	public string TypeName { get; set; }

        	public double SillHeightCm { get; set; }

        	public double SillHeightFeet { get; set; }

        	public string SillHeightSource { get; set; }

        	public string SillHeightFailureReason { get; set; }

        	public ElementId RepresentativeInstanceId { get; set; } = ElementId.InvalidElementId;
        }

        private class DoorWindowLegendSillHeightInfo
        {
        	public double SillHeightCm { get; set; }

        	public double SillHeightFeet { get; set; }

        	public string Source { get; set; }

        	public string FailureReason { get; set; }
        }

        private class DoorWindowLegendExistingItem
        {
        	public ElementId ComponentId { get; set; } = ElementId.InvalidElementId;

        	public ElementId TypeId { get; set; } = ElementId.InvalidElementId;

        	public string TypeMarkDisplay { get; set; }

        	public string TypeName { get; set; }

        	public string Key { get; set; }

        	public BoundingBoxXYZ Bounds { get; set; }

        	public XYZ Anchor { get; set; }

        	public ElementId FflLineId { get; set; } = ElementId.InvalidElementId;

        	public bool HasDetectedFfl { get; set; }

        	public double SillHeightCm { get; set; }

        	public int GridIndex { get; set; }
        }

        private class DoorWindowLegendViewTargetCounts
        {
        	public int DoorCount { get; set; }

        	public int WindowCount { get; set; }
        }

        private class DoorWindowLegendDeleteResult
        {
        	public bool Success { get; set; }

        	public string FailureReason { get; set; }

        	public List<int> DeleteElementIds { get; set; } = new List<int>();

        	public List<int> DeletedElementIds { get; set; } = new List<int>();
        }

        private class DoorWindowLegendTypeMarkSyncResult
        {
        	public string CurrentTypeMark { get; set; }

        	public string ExistingText { get; set; }

        	public ElementId TextNoteId { get; set; } = ElementId.InvalidElementId;

        	public string Action { get; set; } = "skip";

        	public string SkipReason { get; set; }
        }

        private class DoorWindowLegendFailedType
        {
        	public ElementId TypeId { get; set; }

        	public string TypeMarkDisplay { get; set; }

        	public string TypeName { get; set; }

        	public string Reason { get; set; }
        }

        private class DoorWindowLegendDimensionResult
        {
        	public ElementId WidthDimensionId { get; private set; } = ElementId.InvalidElementId;

        	public ElementId HeightDimensionId { get; private set; } = ElementId.InvalidElementId;

        	public string WidthReferenceSource { get; private set; } = "failed";

        	public string HeightReferenceSource { get; private set; } = "failed";

        	public string FailureReason { get; private set; }

        	public List<ElementId> KeepElementIds { get; } = new List<ElementId>();

        	public List<int> ReferenceCurveIds { get; } = new List<int>();

        	public int CreatedCount { get; private set; }

        	public int FailedCount { get; private set; }

        	public string ReferenceSource
        	{
        		get
        		{
        			if (WidthReferenceSource == HeightReferenceSource)
        			{
        				return WidthReferenceSource;
        			}
        			if (WidthReferenceSource == "failed" && HeightReferenceSource == "failed")
        			{
        				return "failed";
        			}
        			return "mixed";
        		}
        	}

        	public void ApplyAxisResult(string axis, DoorWindowLegendDimensionAxisResult axisResult)
        	{
        		if (axisResult == null || !IsValidDimensionId(axisResult.DimensionId))
        		{
        			FailedCount++;
        			AppendFailure(axisResult?.FailureReason ?? (axis + " dimension 建立失敗。"));
        			if (axis == "width")
        			{
        				WidthReferenceSource = "failed";
        			}
        			else
        			{
        				HeightReferenceSource = "failed";
        			}
        			return;
        		}
        		CreatedCount++;
        		KeepElementIds.Add(axisResult.DimensionId);
        		KeepElementIds.AddRange(axisResult.ReferenceCurveIds.Where(IsValidDimensionId));
        		ReferenceCurveIds.AddRange(from id in axisResult.ReferenceCurveIds.Where(IsValidDimensionId)
        			select id.GetIdValue());
        		if (axis == "width")
        		{
        			WidthDimensionId = axisResult.DimensionId;
        			WidthReferenceSource = axisResult.ReferenceSource;
        		}
        		else
        		{
        			HeightDimensionId = axisResult.DimensionId;
        			HeightReferenceSource = axisResult.ReferenceSource;
        		}
        	}

        	public void AddFailure(string reason)
        	{
        		FailedCount += 2;
        		AppendFailure(reason);
        	}

        	private void AppendFailure(string reason)
        	{
        		if (!string.IsNullOrWhiteSpace(reason))
        		{
        			FailureReason = (string.IsNullOrWhiteSpace(FailureReason) ? reason : (FailureReason + "; " + reason));
        		}
        	}

        	private static bool IsValidDimensionId(ElementId id)
        	{
        		if (id != (ElementId)null)
        		{
        			return id != ElementId.InvalidElementId;
        		}
        		return false;
        	}
        }

        private class DoorWindowLegendFflResult
        {
        	public ElementId LineId { get; set; } = ElementId.InvalidElementId;

        	public ElementId TextId { get; set; } = ElementId.InvalidElementId;

        	public string FailureReason { get; set; }

        	public List<ElementId> KeepElementIds { get; } = new List<ElementId>();
        }

        private class DoorWindowLegendSillDimensionResult
        {
        	public ElementId DimensionId { get; set; } = ElementId.InvalidElementId;

        	public string ReferenceSource { get; set; } = "failed";

        	public string FailureReason { get; set; }

        	public List<ElementId> KeepElementIds { get; } = new List<ElementId>();

        	public List<int> ReferenceCurveIds { get; } = new List<int>();
        }

        private class DoorWindowLegendDimensionAxisResult
        {
        	public ElementId DimensionId { get; private set; } = ElementId.InvalidElementId;

        	public string ReferenceSource { get; private set; } = "failed";

        	public string FailureReason { get; private set; }

        	public List<ElementId> ReferenceCurveIds { get; private set; } = new List<ElementId>();

        	public static DoorWindowLegendDimensionAxisResult Created(ElementId dimensionId, string referenceSource, IEnumerable<ElementId> referenceCurveIds)
        	{
        		return new DoorWindowLegendDimensionAxisResult
        		{
        			DimensionId = dimensionId,
        			ReferenceSource = referenceSource,
        			ReferenceCurveIds = (referenceCurveIds ?? Enumerable.Empty<ElementId>()).ToList()
        		};
        	}

        	public static DoorWindowLegendDimensionAxisResult Failed(string reason)
        	{
        		return new DoorWindowLegendDimensionAxisResult
        		{
        			DimensionId = ElementId.InvalidElementId,
        			ReferenceSource = "failed",
        			FailureReason = reason,
        			ReferenceCurveIds = new List<ElementId>()
        		};
        	}
        }

        private class DoorWindowLegendGeometryReference
        {
        	public Reference Reference { get; set; }

        	public XYZ Start { get; set; }

        	public XYZ End { get; set; }

        	public bool IsVertical { get; set; }

        	public bool IsHorizontal { get; set; }

        	public double Length => Start.DistanceTo(End);

        	public double CenterX => (Start.X + End.X) / 2.0;

        	public double CenterY => (Start.Y + End.Y) / 2.0;

        	public double MinX => Math.Min(Start.X, End.X);

        	public double MaxX => Math.Max(Start.X, End.X);

        	public double MinY => Math.Min(Start.Y, End.Y);

        	public double MaxY => Math.Max(Start.Y, End.Y);
        }

        private class DoorWindowLegendCleanupResult
        {
        	public int DeletedCount { get; set; }

        	public int SkippedCount { get; set; }

        	public string Reason { get; set; }

        	public int SeedOriginalElementCount { get; set; }

        	public int ProtectedElementCount { get; set; }

        	public int FinalViewElementCountBeforeCleanup { get; set; }

        	public int FinalViewElementCountAfterCleanup { get; set; }

        	public List<object> SkippedOriginalIds { get; set; } = new List<object>();

        	public List<int> DeletedElementIds { get; set; } = new List<int>();

        	public static DoorWindowLegendCleanupResult Skipped()
        	{
        		return new DoorWindowLegendCleanupResult
        		{
        			DeletedCount = 0,
        			SkippedCount = 0,
        			Reason = "not_started",
        			SeedOriginalElementCount = 0,
        			ProtectedElementCount = 0,
        			FinalViewElementCountBeforeCleanup = 0,
        			FinalViewElementCountAfterCleanup = 0,
        			SkippedOriginalIds = new List<object>(),
        			DeletedElementIds = new List<int>()
        		};
        	}
        }

        private class ElementIdValueComparer : IEqualityComparer<ElementId>
        {
        	public bool Equals(ElementId x, ElementId y)
        	{
        		if (x == y)
        		{
        			return true;
        		}
        		if (x == (ElementId)null || y == (ElementId)null)
        		{
        			return false;
        		}
        		return x.GetIdValue() == y.GetIdValue();
        	}

        	public int GetHashCode(ElementId obj)
        	{
        		if (!(obj == (ElementId)null))
        		{
        			return obj.GetIdValue().GetHashCode();
        		}
        		return 0;
        	}
        }

        private class NaturalStringComparer : IComparer<string>
        {
        	private static readonly Regex TokenRegex = new Regex("\\d+|\\D+", RegexOptions.Compiled);

        	public int Compare(string x, string y)
        	{
        		x = x ?? string.Empty;
        		y = y ?? string.Empty;
        		MatchCollection matchCollection = TokenRegex.Matches(x);
        		MatchCollection matchCollection2 = TokenRegex.Matches(y);
        		int num = Math.Min(matchCollection.Count, matchCollection2.Count);
        		for (int i = 0; i < num; i++)
        		{
        			string value = matchCollection[i].Value;
        			string value2 = matchCollection2[i].Value;
        			long result;
        			bool num2 = long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result);
        			long result2;
        			bool flag = long.TryParse(value2, NumberStyles.None, CultureInfo.InvariantCulture, out result2);
        			int num3 = ((num2 && flag) ? result.CompareTo(result2) : string.Compare(value, value2, StringComparison.OrdinalIgnoreCase));
        			if (num3 != 0)
        			{
        				return num3;
        			}
        		}
        		return matchCollection.Count.CompareTo(matchCollection2.Count);
        	}
        }

        private const string AwaitingSeedSelectionState = "awaiting_seed_selection";

        private const string AwaitingUserChoiceState = "awaiting_user_choice";

        private const string AwaitingTargetTypeSelectionState = "awaiting_target_type_selection";

        private const string AwaitingLegendViewSelectionState = "awaiting_legend_view_selection";

        private const string AwaitingLayoutPreferencesState = "awaiting_layout_preferences";

        private const string AwaitingValidLayoutPreferencesState = "awaiting_valid_layout_preferences";

        private const string AwaitingDimensionTypeSelectionState = "awaiting_dimension_type_selection";

        private const string MissingCreateLayoutError = "create_mode_requires_layout_direction_and_max_per_line";

        private const string InvalidSeedTypeError = "invalid_seed_type";

        private const string LegendSeedViewNotFoundError = "legend_seed_view_not_found";

        private const string LegendSeedComponentNotFoundError = "legend_seed_component_not_found";

        private const string LegendSeedComponentTypeMismatchError = "legend_seed_component_type_mismatch";

        private const string LegendViewTargetTypeMismatchError = "legend_view_target_type_mismatch";

        private const string LegendComponentTypeSwapFailedError = "legend_component_type_swap_failed";

        private const string DimensionTypeNotFoundError = "dimension_type_not_found";

        private const double HorizontalSpacingCm = 500.0;

        private const double VerticalSpacingCm = 500.0;

        private const double LabelOffsetCm = 35.0;

        private const double DimensionOffsetCm = 50.0;

        private const double LabelAboveDimensionOffsetCm = 80.0;

        private const double DimensionReferenceToleranceCm = 3.0;

        private const double DimensionReferenceStubCm = 12.0;

        private const double DoorFflLineLengthFactor = 2.0;

        private const double DoorFflTextOffsetCm = 5.0;

        private const double WindowFflLineLengthFactor = 2.0;

        private const double WindowFflTextOffsetCm = 5.0;

        private const double DoorTypeMarkOffsetCm = 400.0;

        private const double WindowTypeMarkOffsetCm = 400.0;

        private const double SillHeightGroupingPrecisionCm = 0.1;

        private const double CmToFeet = 0.0328083989501312;

        private static readonly Guid DoorWindowLegendTextMetadataSchemaGuid = new Guid("5f4ad61f-91cb-4c6a-b5b2-733960ef7ad1");

        private object DoorWindowLegendTools(JObject parameters)
        {
        	Document document = _uiApp.ActiveUIDocument.Document;
        	string text = parameters?["targetType"]?.Value<string>()?.Trim().ToLowerInvariant();
        	string text2 = parameters?["mode"]?.Value<string>()?.Trim().ToLowerInvariant();
        	if (text2 != "list" && text2 != "create" && text2 != "update")
        	{
        		throw new Exception("mode must be list, create, or update");
        	}
        	if (text != "door" && text != "window")
        	{
        		if (text2 == "update")
        		{
        			return new
        			{
					WorkflowState = "awaiting_target_type_selection",
					NextAction = "ask_target_type",
					SelectionMode = "user_must_choose",
					SelectionField = "targetType",
					RequiresUserInput = true,
					MissingFields = new string[1] { "targetType" },
					Options = new string[2] { "door", "window" },
        				Message = "更新門窗圖例表前，需要先選擇要更新門圖例表或窗圖例表。"
        			};
        		}
        		throw new Exception("targetType must be door or window");
        	}
        	List<DoorWindowLegendTypeInfo> list = SortDoorWindowTypesByTypeMark(CollectUsedDoorWindowTypes(document, text));
        	if (text2 == "list")
        	{
        		return BuildDoorWindowLegendListResult(text, list);
        	}
        	string text3 = parameters?["layoutDirection"]?.Value<string>()?.Trim().ToLowerInvariant();
        	int? maxPerLine = parameters?["maxPerLine"]?.Value<int?>();
        	int? num = parameters?["seedLegendViewId"]?.Value<int?>();
        	int? legendViewId = parameters?["legendViewId"]?.Value<int?>();
        	int? dimensionTypeId = parameters?["dimensionTypeId"]?.Value<int?>();
        	if (text2 == "update")
        	{
        		return UpdateDoorWindowLegend(document, text, text3, maxPerLine, list, legendViewId, dimensionTypeId);
        	}
        	if (!num.HasValue)
        	{
        		return new
        		{
        			TargetType = text,
        			DisplayName = GetDoorWindowDisplayName(text),
        			WorkflowState = "awaiting_seed_selection",
        			NextAction = "call_list_seeds",
        			SeedTypeRequired = "legend",
        			RequiresUserInput = true,
        			DoNotAutoSelectSeed = true,
        			DoNotRetryWithOtherSeeds = true,
        			PromptToUser = "請先呼叫 list_seeds，並請使用者選擇一個 Legend View 作為 seed。",
        			Message = "建立門表或窗表前，需要先選擇 seed Legend 視圖。"
        		};
        	}
        	List<string> list2 = new List<string>();
        	if (string.IsNullOrWhiteSpace(text3))
        	{
        		list2.Add("layoutDirection");
        	}
        	if (!maxPerLine.HasValue)
        	{
        		list2.Add("maxPerLine");
        	}
        	if (list2.Count > 0)
        	{
        		return new
        		{
        			TargetType = text,
        			DisplayName = GetDoorWindowDisplayName(text),
        			WorkflowState = "awaiting_layout_preferences",
        			NextAction = "ask_layout_preferences",
        			RequiresUserInput = true,
        			DoNotAutoAssignLayout = true,
        			DoNotRetryCreateWithoutLayout = true,
        			MissingFields = list2,
        			PromptToUser = "請提供排列方向 horizontal 或 vertical，以及每列/欄數 maxPerLine。",
        			Message = "建立門表或窗表前，需要先提供排版方向與每列/欄數。"
        		};
        	}
        	List<string> list3 = ValidateLegendLayout(text3, maxPerLine);
        	if (list3.Count > 0)
        	{
        		return BuildInvalidLayoutResponse(text, list3);
        	}
        	if (!dimensionTypeId.HasValue)
        	{
        		return BuildAwaitingDimensionTypeResponse(text);
        	}
        	return CreateDoorWindowLegend(document, text, text3, maxPerLine.Value, list, num.Value, dimensionTypeId.Value);
        }

        private object ListSeeds(JObject parameters)
        {
        	Document document = _uiApp.ActiveUIDocument.Document;
        	if (parameters?["seedType"]?.Value<string>()?.Trim().ToLowerInvariant() != "legend")
        	{
        		throw new Exception("invalid_seed_type");
        	}
        	List<object> list = ListLegendSeedCandidates(document);
        	return new
        	{
        		SeedType = "legend",
        		Count = list.Count,
        		WorkflowState = "awaiting_user_choice",
        		SelectionMode = "user_must_choose",
        		SelectionField = "ViewName",
        		RequiresUserInput = true,
        		DoNotAutoSelect = true,
        		DoNotAutoRetryCreate = true,
        		PromptToUser = "請從以下 Legend 視圖中選一個 ViewName 作為 seed。",
        		Seeds = list
        	};
        }

        private object ListDimensionTypes(JObject parameters)
        {
        	Document document = _uiApp.ActiveUIDocument.Document;
        	List<object> list = ListDimensionTypeCandidates(document);
        	return new
        	{
        		Count = list.Count,
        		WorkflowState = "awaiting_user_choice",
        		SelectionMode = "user_must_choose",
        		SelectionField = "dimensionTypeName",
        		RequiresUserInput = true,
        		DoNotAutoSelect = true,
        		DoNotAutoRetryCreate = true,
        		PromptToUser = "請從以下標註類型中選一個 dimensionTypeName 作為門窗圖例尺寸標註類型。",
        		DimensionTypes = list
        	};
        }

        private object ListLegendViews(JObject parameters)
        {
        	Document document = _uiApp.ActiveUIDocument.Document;
        	List<object> list = ListLegendViewCandidates(document);
        	return new
        	{
        		Count = list.Count,
				WorkflowState = "awaiting_user_choice",
				SelectionMode = "user_must_choose",
				SelectionField = "viewName",
				RequiresUserInput = true,
				DoNotAutoSelect = true,
        		PromptToUser = "請從以下 Legend 視圖中選擇要更新的門窗圖例表。",
        		LegendViews = list
        	};
        }

        private List<string> ValidateLegendLayout(string layoutDirection, int? maxPerLine)
        {
        	List<string> list = new List<string>();
        	if (layoutDirection != "horizontal" && layoutDirection != "vertical")
        	{
        		list.Add("layoutDirection");
        	}
        	if (!maxPerLine.HasValue || maxPerLine.Value < 1)
        	{
        		list.Add("maxPerLine");
        	}
        	return list;
        }

        private object BuildInvalidLayoutResponse(string targetType, List<string> invalidFields)
        {
        	return new
        	{
        		TargetType = targetType,
        		DisplayName = GetDoorWindowDisplayName(targetType),
        		WorkflowState = "awaiting_valid_layout_preferences",
        		NextAction = "ask_layout_preferences",
        		RequiresUserInput = true,
        		DoNotAutoAssignLayout = true,
        		DoNotRetryCreateWithoutLayout = true,
        		InvalidFields = invalidFields,
        		PromptToUser = "請提供有效的排版方向 horizontal 或 vertical，以及大於等於 1 的 maxPerLine。",
        		Message = "排版參數無效，請重新提供 layoutDirection 與 maxPerLine。"
        	};
        }

        private object BuildAwaitingDimensionTypeResponse(string targetType)
        {
        	return new
        	{
        		TargetType = targetType,
        		DisplayName = GetDoorWindowDisplayName(targetType),
        		WorkflowState = "awaiting_dimension_type_selection",
        		NextAction = "call_list_dimension_types",
        		RequiresUserInput = true,
        		DoNotAutoSelectDimensionType = true,
        		DoNotRetryCreateWithoutDimensionType = true,
        		MissingFields = new string[1] { "dimensionTypeId" },
        		PromptToUser = "請先從 list_dimension_types 的結果中選擇一個 dimensionTypeName 作為標註類型。",
        		Message = "建立或更新門窗圖例表前，需要先選擇 Revit 標註類型。"
        	};
        }

        private object BuildDoorWindowLegendListResult(string targetType, List<DoorWindowLegendTypeInfo> usedTypes)
        {
        	return new
        	{
        		TargetType = targetType,
        		DisplayName = GetDoorWindowDisplayName(targetType),
        		Count = usedTypes.Count,
        		Types = usedTypes.Select((DoorWindowLegendTypeInfo t) => new
        		{
        			TypeId = t.TypeId.GetIdValue(),
        			TypeMarkRaw = t.TypeMarkRaw,
        			TypeMarkDisplay = t.TypeMarkDisplay,
        			TypeName = t.TypeName,
        			SillHeightCm = t.SillHeightCm,
        			SillHeightFeet = t.SillHeightFeet,
        			SillHeightSource = t.SillHeightSource,
        			RepresentativeInstanceId = SafeGetElementIdValue(t.RepresentativeInstanceId)
        		}).ToList(),
        		SuggestedAction = "create_legend"
        	};
        }

        private object UpdateDoorWindowLegend(Document doc, string targetType, string layoutDirection, int? maxPerLine, List<DoorWindowLegendTypeInfo> desiredTypes, int? legendViewId, int? dimensionTypeId)
        {
        	//IL_04e6: Unknown result type (might be due to invalid IL or missing references)
        	//IL_04ed: Expected O, but got Unknown
        	//IL_04ef: Unknown result type (might be due to invalid IL or missing references)
        	//IL_07d6: Unknown result type (might be due to invalid IL or missing references)
        	//IL_0733: Unknown result type (might be due to invalid IL or missing references)
        	if (!legendViewId.HasValue)
        	{
        		return new
        		{
        			TargetType = targetType,
        			DisplayName = GetDoorWindowDisplayName(targetType),
        			WorkflowState = "awaiting_legend_view_selection",
        			NextAction = "call_list_legend_views",
        			SelectionMode = "user_must_choose",
        			SelectionField = "viewName",
        			RequiresUserInput = true,
        			DoNotAutoSelectLegendView = true,
        			MissingFields = new string[1] { "legendViewId" },
        			Message = "更新門窗圖例表前，需要先選擇要更新的 Legend 視圖。"
        		};
        	}
        	List<string> list = new List<string>();
        	if (string.IsNullOrWhiteSpace(layoutDirection))
        	{
        		list.Add("layoutDirection");
        	}
        	if (!maxPerLine.HasValue)
        	{
        		list.Add("maxPerLine");
        	}
        	if (list.Count > 0)
        	{
        		return new
        		{
        			TargetType = targetType,
        			DisplayName = GetDoorWindowDisplayName(targetType),
        			WorkflowState = "awaiting_layout_preferences",
        			NextAction = "ask_layout_preferences",
        			RequiresUserInput = true,
        			MissingFields = list,
        			Message = "更新門窗圖例表時，需要使用者提供 layoutDirection 與 maxPerLine，不自動偵測。"
        		};
        	}
        	List<string> list2 = ValidateLegendLayout(layoutDirection, maxPerLine);
        	if (list2.Count > 0)
        	{
        		return BuildInvalidLayoutResponse(targetType, list2);
        	}
        	View legendViewById = GetLegendViewById(doc, legendViewId.Value);
        	if (legendViewById == null)
        	{
        		return new
        		{
        			TargetType = targetType,
        			ErrorCode = "legend_seed_view_not_found",
        			LegendViewId = legendViewId.Value,
        			Message = $"找不到 legendViewId={legendViewId.Value} 的 Legend 視圖。"
        		};
        	}
        	DoorWindowLegendViewTargetCounts doorWindowLegendViewTargetCounts = CountDoorWindowLegendComponentsByTargetType(doc, legendViewById);
        	int num = ((targetType == "door") ? doorWindowLegendViewTargetCounts.DoorCount : doorWindowLegendViewTargetCounts.WindowCount);
        	int num2 = ((targetType == "door") ? doorWindowLegendViewTargetCounts.WindowCount : doorWindowLegendViewTargetCounts.DoorCount);
        	if (num == 0)
        	{
        		string message = ((num2 > 0) ? ("你選到的 Legend 視圖看起來是" + GetDoorWindowDisplayName((targetType == "door") ? "window" : "door") + "圖例表，但目前指令是更新" + GetDoorWindowDisplayName(targetType) + "圖例表。請重新選擇要更新的" + GetDoorWindowDisplayName(targetType) + "圖例表。") : ("選定的 Legend 視圖內沒有可更新的" + GetDoorWindowDisplayName(targetType) + " Legend Component，請重新選擇既有" + GetDoorWindowDisplayName(targetType) + "圖例表。"));
        		return new
        		{
        			TargetType = targetType,
        			DisplayName = GetDoorWindowDisplayName(targetType),
        			WorkflowState = "awaiting_legend_view_selection",
        			NextAction = "call_list_legend_views",
        			SelectionMode = "user_must_choose",
        			SelectionField = "viewName",
        			RequiresUserInput = true,
        			DoNotAutoSelectLegendView = true,
        			ErrorCode = "legend_view_target_type_mismatch",
        			LegendViewId = legendViewId.Value,
        			LegendViewName = SafeGetViewName(legendViewById),
        			DoorLegendComponentCount = doorWindowLegendViewTargetCounts.DoorCount,
        			WindowLegendComponentCount = doorWindowLegendViewTargetCounts.WindowCount,
        			MissingFields = new string[1] { "legendViewId" },
        			Message = message
        		};
        	}
        	List<DoorWindowLegendExistingItem> list3 = CollectExistingDoorWindowLegendItems(doc, legendViewById, targetType, layoutDirection, maxPerLine.Value);
        	DimensionType val = (dimensionTypeId.HasValue ? GetDimensionTypeById(doc, dimensionTypeId.Value) : InferDimensionTypeFromView(doc, legendViewById));
        	string dimensionTypeSource = (dimensionTypeId.HasValue ? "user_selected" : "existing_view_dimension");
        	if (dimensionTypeId.HasValue && val == null)
        	{
        		return new
        		{
        			TargetType = targetType,
        			DisplayName = GetDoorWindowDisplayName(targetType),
        			ErrorCode = "dimension_type_not_found",
        			DimensionTypeId = dimensionTypeId.Value,
        			Message = $"dimensionTypeId={dimensionTypeId.Value} 不是有效的 Revit DimensionType。"
        		};
        	}
        	if (val == null)
        	{
        		return BuildAwaitingDimensionTypeResponse(targetType);
        	}
        	Dictionary<string, DoorWindowLegendTypeInfo> dictionary = (from t in desiredTypes
        		group t by BuildDoorWindowLegendItemKey(targetType, t.TypeId, t.SillHeightCm)).ToDictionary((IGrouping<string, DoorWindowLegendTypeInfo> g) => g.Key, (IGrouping<string, DoorWindowLegendTypeInfo> g) => g.First());
        	HashSet<int> hashSet = new HashSet<int>(desiredTypes.Select((DoorWindowLegendTypeInfo t) => t.TypeId.GetIdValue()));
        	HashSet<string> existingKeys = (from i in list3
        		where !string.IsNullOrWhiteSpace(i.Key)
        		select i.Key).ToHashSet();
        	List<DoorWindowLegendTypeInfo> list4 = desiredTypes.Where((DoorWindowLegendTypeInfo t) => !existingKeys.Contains(BuildDoorWindowLegendItemKey(targetType, t.TypeId, t.SillHeightCm))).ToList();
        	List<DoorWindowLegendExistingItem> list5 = new List<DoorWindowLegendExistingItem>();
        	List<object> list6 = new List<object>();
        	foreach (DoorWindowLegendExistingItem item in list3)
        	{
        		if (string.IsNullOrWhiteSpace(item.Key) || !dictionary.ContainsKey(item.Key))
        		{
        			if (targetType == "window" && !item.HasDetectedFfl && hashSet.Contains(item.TypeId.GetIdValue()))
        			{
        				list6.Add(new
        				{
        					ExistingKey = item.Key,
        					TypeId = item.TypeId.GetIdValue(),
        					DetectedGridIndex = item.GridIndex,
        					DetectedFflLineId = SafeGetElementIdValue(item.FflLineId),
        					DetectedSillHeightCm = ((targetType == "window") ? new double?(item.SillHeightCm) : ((double?)null)),
        					UpdateAction = "skip_delete",
        					SkipReason = "window_ffl_missing_same_type_still_used"
        				});
        			}
        			else
        			{
        				list5.Add(item);
        			}
        		}
        	}
        	int addedCount = 0;
        	int num3 = 0;
        	int num4 = list6.Count;
        	int dimensionCreatedCount = 0;
        	int dimensionFailedCount = 0;
        	int num5 = 0;
        	int num6 = 0;
        	List<object> list7 = new List<object>();
        	if (doorWindowLegendViewTargetCounts.DoorCount > 0 && doorWindowLegendViewTargetCounts.WindowCount > 0)
        	{
        		list7.Add(new
        		{
        			UpdateAction = "warning",
        			WarningCode = "mixed_target_types_in_legend_view",
        			MixedTargetTypesInLegendView = true,
        			DoorLegendComponentCount = doorWindowLegendViewTargetCounts.DoorCount,
        			WindowLegendComponentCount = doorWindowLegendViewTargetCounts.WindowCount,
        			Message = "選定的 Legend 視圖同時包含門與窗 Legend Component；update 只會操作本次指定的 targetType，另一類不會被修改。"
        		});
        	}
        	List<object> list8 = new List<object>();
        	List<DoorWindowLegendFailedType> failedTypes = new List<DoorWindowLegendFailedType>();
        	List<ElementId> keepElementIds = new List<ElementId>();
        	Transaction val2 = new Transaction(doc, "更新" + GetDoorWindowDisplayName(targetType) + "圖例表");
        	try
        	{
        		val2.Start();
        		foreach (DoorWindowLegendExistingItem item2 in list5)
        		{
        			DoorWindowLegendDeleteResult doorWindowLegendDeleteResult = DeleteDoorWindowLegendItemGroup(doc, legendViewById, item2, targetType);
        			if (doorWindowLegendDeleteResult.Success)
        			{
        				num3++;
        				list8.Add(new
        				{
        					ExistingKey = item2.Key,
        					TypeId = item2.TypeId.GetIdValue(),
        					DetectedGridIndex = item2.GridIndex,
        					DetectedFflLineId = SafeGetElementIdValue(item2.FflLineId),
        					DetectedSillHeightCm = ((targetType == "window") ? new double?(item2.SillHeightCm) : ((double?)null)),
        					UpdateAction = "delete",
        					DeleteElementIds = doorWindowLegendDeleteResult.DeletedElementIds
        				});
        			}
        			else
        			{
        				num4++;
        				list6.Add(new
        				{
        					ExistingKey = item2.Key,
        					TypeId = item2.TypeId.GetIdValue(),
        					DetectedGridIndex = item2.GridIndex,
        					DetectedFflLineId = SafeGetElementIdValue(item2.FflLineId),
        					DetectedSillHeightCm = ((targetType == "window") ? new double?(item2.SillHeightCm) : ((double?)null)),
        					UpdateAction = "skip_delete",
        					SkipReason = doorWindowLegendDeleteResult.FailureReason,
        					DeleteElementIds = doorWindowLegendDeleteResult.DeleteElementIds
        				});
        			}
        		}
        		foreach (DoorWindowLegendExistingItem item3 in list3)
        		{
        			if (!list5.Contains(item3) && !string.IsNullOrWhiteSpace(item3.Key) && dictionary.ContainsKey(item3.Key))
        			{
        				DoorWindowLegendTypeMarkSyncResult doorWindowLegendTypeMarkSyncResult = SyncDoorWindowLegendTypeMarkTextNote(doc, legendViewById, item3, targetType);
        				if (doorWindowLegendTypeMarkSyncResult.Action == "updated")
        				{
        					num5++;
        				}
        				else if (doorWindowLegendTypeMarkSyncResult.Action == "skip")
        				{
        					num6++;
        				}
        				list7.Add(new
        				{
        					ExistingKey = item3.Key,
        					TypeId = item3.TypeId.GetIdValue(),
        					DetectedGridIndex = item3.GridIndex,
        					DetectedFflLineId = SafeGetElementIdValue(item3.FflLineId),
        					DetectedSillHeightCm = ((targetType == "window") ? new double?(item3.SillHeightCm) : ((double?)null)),
        					UpdateAction = "keep",
        					CurrentTypeMark = doorWindowLegendTypeMarkSyncResult.CurrentTypeMark,
        					ExistingTypeMarkText = doorWindowLegendTypeMarkSyncResult.ExistingText,
        					TypeMarkTextNoteId = SafeGetElementIdValue(doorWindowLegendTypeMarkSyncResult.TextNoteId),
        					TypeMarkSyncAction = doorWindowLegendTypeMarkSyncResult.Action,
        					TypeMarkSyncSkipReason = doorWindowLegendTypeMarkSyncResult.SkipReason
        				});
        			}
        		}
        		if (list4.Count > 0)
        		{
        			DoorWindowLegendExistingItem doorWindowLegendExistingItem = list3.OrderByDescending((DoorWindowLegendExistingItem i) => i.GridIndex).FirstOrDefault();
        			if (doorWindowLegendExistingItem == null || !IsValidElementId(doorWindowLegendExistingItem.ComponentId))
        			{
        				val2.RollBack();
        				return new
        				{
        					TargetType = targetType,
        					LegendViewId = SafeGetElementIdValue((Element)(object)legendViewById),
        					LegendViewName = SafeGetViewName(legendViewById),
        					ErrorCode = "legend_seed_component_not_found",
        					Message = "選定的 Legend 視圖內沒有可作為 append source 的目標類型 Legend Component。"
        				};
        			}
        			XYZ doorWindowLegendGridOrigin = GetDoorWindowLegendGridOrigin(list3);
        			int startIndex = Math.Max(0, list3.Max((DoorWindowLegendExistingItem i) => i.GridIndex) + 1);
        			addedCount = PlaceLegendItemsFromOriginalSeedSource(doc, legendViewById, doorWindowLegendExistingItem.ComponentId, targetType, layoutDirection, maxPerLine.Value, list4, failedTypes, list7, val, out dimensionCreatedCount, out dimensionFailedCount, out keepElementIds, startIndex, doorWindowLegendGridOrigin);
        		}
        		doc.Regenerate();
        		val2.Commit();
        	}
        	finally
        	{
        		((IDisposable)val2)?.Dispose();
        	}
        	return new
        	{
        		WorkflowState = "updated",
        		TargetType = targetType,
        		DisplayName = GetDoorWindowDisplayName(targetType),
        		LegendViewId = SafeGetElementIdValue((Element)(object)legendViewById),
        		LegendViewName = SafeGetViewName(legendViewById),
        		DoorLegendComponentCount = doorWindowLegendViewTargetCounts.DoorCount,
        		WindowLegendComponentCount = doorWindowLegendViewTargetCounts.WindowCount,
        		MixedTargetTypesInLegendView = (doorWindowLegendViewTargetCounts.DoorCount > 0 && doorWindowLegendViewTargetCounts.WindowCount > 0),
        		DesiredCount = desiredTypes.Count,
        		ExistingCount = list3.Count,
        		AddedCount = addedCount,
        		DeletedCount = num3,
        		SkippedDeleteCount = num4,
        		MissingItems = list4.Select((DoorWindowLegendTypeInfo t) => new
        		{
        			TypeId = t.TypeId.GetIdValue(),
        			TypeMarkDisplay = t.TypeMarkDisplay,
        			TypeName = t.TypeName,
        			SillHeightCm = ((targetType == "window") ? new double?(t.SillHeightCm) : ((double?)null))
        		}).ToList(),
        		DeletedItems = list8,
        		SkippedItems = list6,
        		FailedTypes = failedTypes,
        		AttemptDebug = list7,
        		DimensionTypeId = SafeGetElementIdValue((Element)(object)val),
        		DimensionTypeName = SafeGetDimensionTypeName(val),
        		DimensionTypeSource = dimensionTypeSource,
        		DimensionCreatedCount = dimensionCreatedCount,
        		DimensionFailedCount = dimensionFailedCount,
        		TypeMarkUpdatedCount = num5,
        		TypeMarkSkippedCount = num6
        	};
        }

        private object CreateDoorWindowLegend(Document doc, string targetType, string layoutDirection, int maxPerLine, List<DoorWindowLegendTypeInfo> usedTypes, int seedLegendViewId, int dimensionTypeId)
        {
        	View legendViewById = GetLegendViewById(doc, seedLegendViewId);
        	if (legendViewById == null)
        	{
        		return new
        		{
        			TargetType = targetType,
        			DisplayName = GetDoorWindowDisplayName(targetType),
        			ErrorCode = "legend_seed_view_not_found",
        			Message = $"找不到 viewId={seedLegendViewId} 的 seed Legend，請重新選擇 seed。"
        		};
        	}
        	if (CollectLegendComponentIds(doc, legendViewById).Count == 0)
        	{
        		return new
        		{
        			TargetType = targetType,
        			DisplayName = GetDoorWindowDisplayName(targetType),
        			ErrorCode = "legend_seed_component_not_found",
        			SeedLegendViewId = SafeGetElementIdValue((Element)(object)legendViewById),
        			SeedLegendViewName = SafeGetViewName(legendViewById),
        			Message = "Seed Legend " + SafeGetViewName(legendViewById) + " 內沒有可用的 Legend Component。",
        			SeedViewDebug = BuildLegendViewDebug(doc, legendViewById)
        		};
        	}
        	DimensionType dimensionTypeById = GetDimensionTypeById(doc, dimensionTypeId);
        	if (dimensionTypeById == null)
        	{
        		return new
        		{
        			TargetType = targetType,
        			DisplayName = GetDoorWindowDisplayName(targetType),
        			ErrorCode = "dimension_type_not_found",
        			DimensionTypeId = dimensionTypeId,
        			Message = $"找不到 dimensionTypeId={dimensionTypeId} 的有效標註類型，請重新選擇標註類型。"
        		};
        	}
        	try
        	{
        		return CreateDoorWindowLegendFromSeed(doc, targetType, layoutDirection, maxPerLine, usedTypes, legendViewById, dimensionTypeById);
        	}
        	catch (Exception ex)
        	{
        		Logger.Error($"door-window-legend-tools create failed before entering seed flow. targetType={targetType}, seedLegendViewId={seedLegendViewId}", ex);
        		return new
        		{
        			TargetType = targetType,
        			DisplayName = GetDoorWindowDisplayName(targetType),
        			ErrorCode = "legend_seed_component_type_mismatch",
        			SeedLegendViewId = SafeGetElementIdValue((Element)(object)legendViewById),
        			SeedLegendViewName = SafeGetViewName(legendViewById),
        			Message = ex.Message,
        			SeedViewDebug = BuildLegendViewDebug(doc, legendViewById)
        		};
        	}
        }

        private object CreateDoorWindowLegendFromSeed(Document doc, string targetType, string layoutDirection, int maxPerLine, List<DoorWindowLegendTypeInfo> usedTypes, View seedView, DimensionType dimensionType)
        {
        	//IL_0415: Unknown result type (might be due to invalid IL or missing references)
        	//IL_041d: Unknown result type (might be due to invalid IL or missing references)
        	//IL_0065: Unknown result type (might be due to invalid IL or missing references)
        	//IL_006c: Expected O, but got Unknown
        	//IL_006e: Unknown result type (might be due to invalid IL or missing references)
        	//IL_00c5: Unknown result type (might be due to invalid IL or missing references)
        	//IL_00cc: Expected O, but got Unknown
        	//IL_00ce: Unknown result type (might be due to invalid IL or missing references)
        	//IL_0459: Unknown result type (might be due to invalid IL or missing references)
        	//IL_028a: Unknown result type (might be due to invalid IL or missing references)
        	//IL_0292: Unknown result type (might be due to invalid IL or missing references)
        	//IL_046f: Unknown result type (might be due to invalid IL or missing references)
        	string text = ((targetType == "door") ? "門表" : "窗表");
        	object obj = SafeGetElementIdValue((Element)(object)seedView);
        	string text2 = SafeGetViewName(seedView);
        	object dimensionTypeId = SafeGetElementIdValue((Element)(object)dimensionType);
        	string dimensionTypeName = SafeGetDimensionTypeName(dimensionType);
        	TransactionGroup val = new TransactionGroup(doc, "撱箇?" + text);
        	try
        	{
        		val.Start();
        		ElementId val2 = ElementId.InvalidElementId;
        		string text3 = string.Empty;
        		int num = 0;
        		List<DoorWindowLegendFailedType> list = new List<DoorWindowLegendFailedType>();
        		List<object> attemptDebug = new List<object>();
        		object duplicatedViewDebug = null;
        		DoorWindowLegendCleanupResult doorWindowLegendCleanupResult = DoorWindowLegendCleanupResult.Skipped();
        		int num2 = 0;
        		int generatedElementCount = 0;
        		int num3 = 0;
        		int num4 = 0;
        		int dimensionCreatedCount = 0;
        		int dimensionFailedCount = 0;
        		Transaction val3 = new Transaction(doc, "撱箇?" + text + " Legend");
        		try
        		{
        			val3.Start();
        			View val4 = DuplicateLegendView(doc, seedView, text);
        			val4.Scale = 50;
        			doc.Regenerate();
        			val2 = ((Element)val4).Id;
        			text3 = ((Element)val4).Name;
        			List<ElementId> list2 = CollectViewElementIds(doc, val4);
        			num2 = list2.Count;
        			List<ElementId> keepElementIds = new List<ElementId>();
        			if (usedTypes.Count == 0)
        			{
        				doorWindowLegendCleanupResult = DeleteSeedOriginalIntersection(doc, val4, list2);
        				num3 = doorWindowLegendCleanupResult.FinalViewElementCountBeforeCleanup;
        				num4 = doorWindowLegendCleanupResult.FinalViewElementCountAfterCleanup;
        				Logger.Info($"door-window-legend-tools cleanup completed. mode=delete_seed_original_ids_one_by_one, seedOriginalElementCount={num2}, protectedCount={doorWindowLegendCleanupResult.ProtectedElementCount}, finalBefore={num3}, finalAfter={num4}, deleted={doorWindowLegendCleanupResult.DeletedCount}, skipped={doorWindowLegendCleanupResult.SkippedCount}, reason={doorWindowLegendCleanupResult.Reason}");
        				duplicatedViewDebug = BuildLegendViewDebug(doc, val4);
        			}
        			else
        			{
        				HashSet<int> seedOriginalIdValues = (from id in list2.Where(IsValidElementId)
        					select id.GetIdValue()).ToHashSet();
        				ElementId val5 = (from id in CollectLegendComponentIds(doc, val4)
        					where seedOriginalIdValues.Contains(id.GetIdValue())
        					select id).ToList().FirstOrDefault((ElementId id) => IsValidElementId(id) && doc.GetElement(id) != null);
        				if (!IsValidElementId(val5))
        				{
        					duplicatedViewDebug = BuildLegendViewDebug(doc, val4);
        					Logger.Error($"door-window-legend-tools duplicated seed view has no usable source component. targetType={targetType}, seedView={text2}({obj})");
        					val3.RollBack();
        					val.RollBack();
        					return new
        					{
        						TargetType = targetType,
        						DisplayName = GetDoorWindowDisplayName(targetType),
        						ErrorCode = "legend_seed_component_type_mismatch",
        						SeedLegendViewId = obj,
        						SeedLegendViewName = text2,
        						Message = "duplicated Legend 視圖內沒有可用的 source Legend Component。",
        						SeedViewDebug = BuildLegendViewDebug(doc, seedView),
        						DuplicatedViewDebug = duplicatedViewDebug,
        						AttemptDebug = attemptDebug
        					};
        				}
        				try
        				{
        					Logger.Info($"door-window-legend-tools create start. targetType={targetType}, seedView={text2}({obj}), duplicatedView={text3}({val2.GetIdValue()}), usedTypeCount={usedTypes.Count}, sourceSeedComponentId={val5.GetIdValue()}");
        					num = PlaceLegendItemsFromOriginalSeedSource(doc, val4, val5, targetType, layoutDirection, maxPerLine, usedTypes, list, attemptDebug, dimensionType, out dimensionCreatedCount, out dimensionFailedCount, out keepElementIds);
        					generatedElementCount = keepElementIds.Count;
        					doorWindowLegendCleanupResult = DeleteSeedOriginalIntersection(doc, val4, list2);
        					num3 = doorWindowLegendCleanupResult.FinalViewElementCountBeforeCleanup;
        					num4 = doorWindowLegendCleanupResult.FinalViewElementCountAfterCleanup;
        					Logger.Info($"door-window-legend-tools cleanup completed. mode=delete_seed_original_ids_one_by_one, seedOriginalElementCount={num2}, protectedCount={doorWindowLegendCleanupResult.ProtectedElementCount}, finalBefore={num3}, finalAfter={num4}, deleted={doorWindowLegendCleanupResult.DeletedCount}, skipped={doorWindowLegendCleanupResult.SkippedCount}, reason={doorWindowLegendCleanupResult.Reason}");
        					duplicatedViewDebug = BuildLegendViewDebug(doc, val4);
        				}
        				catch (Exception ex)
        				{
        					duplicatedViewDebug = BuildLegendViewDebug(doc, val4);
        					Logger.Error($"door-window-legend-tools placement failed. targetType={targetType}, seedView={text2}({obj})", ex);
        					val3.RollBack();
        					val.RollBack();
        					return new
        					{
        						TargetType = targetType,
        						DisplayName = GetDoorWindowDisplayName(targetType),
        						ErrorCode = "legend_seed_component_type_mismatch",
        						SeedLegendViewId = obj,
        						SeedLegendViewName = text2,
        						Message = ex.Message,
        						SeedViewDebug = BuildLegendViewDebug(doc, seedView),
        						DuplicatedViewDebug = duplicatedViewDebug,
        						AttemptDebug = attemptDebug
        					};
        				}
        			}
        			val3.Commit();
        		}
        		finally
        		{
        			((IDisposable)val3)?.Dispose();
        		}
        		val.Assimilate();
        		Element element = doc.GetElement(val2);
        		View val6 = (View)(object)((element is View) ? element : null);
        		if (val6 != null)
        		{
        			_uiApp.ActiveUIDocument.ActiveView = val6;
        		}
        		bool isEmptyLegend = usedTypes.Count == 0 || num == 0;
        		string message = ((usedTypes.Count == 0) ? ("已建立" + text + "，seed 原始內容已清理。") : ((num == 0) ? ("已建立" + text + "，但沒有可建立的 type；seed 原始內容已清理。") : ((list.Count <= 0) ? ("已建立" + text + "，seed 原始內容已清理。") : ("已建立" + text + "，部分 type 建立失敗；seed 原始內容已清理。"))));
        		return new
        		{
        			TargetType = targetType,
        			DisplayName = GetDoorWindowDisplayName(targetType),
        			ErrorCode = ((usedTypes.Count > 0 && num == 0) ? "legend_component_type_swap_failed" : null),
        			LegendViewId = val2.GetIdValue(),
        			LegendViewName = text3,
        			SeedLegendViewId = obj,
        			SeedLegendViewName = text2,
        			DimensionTypeId = dimensionTypeId,
        			DimensionTypeName = dimensionTypeName,
        			UsedTypeCount = usedTypes.Count,
        			PlacedCount = num,
        			FailedTypes = list.Select((DoorWindowLegendFailedType f) => new
        			{
        				TypeId = f.TypeId.GetIdValue(),
        				TypeMarkDisplay = f.TypeMarkDisplay,
        				TypeName = f.TypeName,
        				Reason = f.Reason
        			}).ToList(),
        			IsEmptyLegend = isEmptyLegend,
        			CleanupSkipped = false,
        			CleanupMode = "delete_seed_original_ids_one_by_one",
        			CleanupDeletedCount = doorWindowLegendCleanupResult.DeletedCount,
        			CleanupSkippedCount = doorWindowLegendCleanupResult.SkippedCount,
        			CleanupSkippedOriginalIds = doorWindowLegendCleanupResult.SkippedOriginalIds,
        			CleanupProtectedElementCount = doorWindowLegendCleanupResult.ProtectedElementCount,
        			CleanupDeletedElementIds = doorWindowLegendCleanupResult.DeletedElementIds,
        			CleanupReason = doorWindowLegendCleanupResult.Reason,
        			SeedOriginalElementCount = num2,
        			GeneratedElementCount = generatedElementCount,
        			DimensionCreatedCount = dimensionCreatedCount,
        			DimensionFailedCount = dimensionFailedCount,
        			FinalViewElementCountBeforeCleanup = num3,
        			FinalViewElementCountAfterCleanup = num4,
        			Message = message,
        			AttemptDebug = attemptDebug,
        			DuplicatedViewDebug = duplicatedViewDebug
        		};
        	}
        	finally
        	{
        		((IDisposable)val)?.Dispose();
        	}
        }

        private List<object> ListLegendSeedCandidates(Document doc)
        {
        	//IL_001a: Unknown result type (might be due to invalid IL or missing references)
        	return ((IEnumerable<View>)(from View v in (IEnumerable)new FilteredElementCollector(doc).OfClass(typeof(View))
        		where (int)v.ViewType == 11 && !v.IsTemplate
        		select v).OrderBy((View v) => ((Element)v).Name, StringComparer.OrdinalIgnoreCase)).Select((Func<View, object>)delegate(View v)
        	{
        		List<ElementId> list = CollectLegendComponentIds(doc, v);
        		return new
        		{
        			viewId = ((Element)v).Id.GetIdValue(),
        			viewName = ((Element)v).Name,
        			legendComponentCount = list.Count,
        			ViewId = ((Element)v).Id.GetIdValue(),
        			ViewName = ((Element)v).Name,
        			LegendComponentCount = list.Count,
        			IsUsableSeed = (list.Count > 0)
        		};
        	}).ToList();
        }

        private List<object> ListDimensionTypeCandidates(Document doc)
        {
        	//IL_0037: Unknown result type (might be due to invalid IL or missing references)
        	ElementId defaultDimensionTypeId = ElementId.InvalidElementId;
        	try
        	{
        		defaultDimensionTypeId = doc.GetDefaultElementTypeId((ElementTypeGroup)10);
        	}
        	catch
        	{
        		defaultDimensionTypeId = ElementId.InvalidElementId;
        	}
        	return ((IEnumerable<DimensionType>)((IEnumerable)new FilteredElementCollector(doc).OfClass(typeof(DimensionType))).Cast<DimensionType>().OrderBy<DimensionType, string>((DimensionType t) => SafeGetDimensionTypeName(t), StringComparer.OrdinalIgnoreCase)).Select((Func<DimensionType, object>)((DimensionType t) => new
        	{
        		DimensionTypeId = ((Element)t).Id.GetIdValue(),
        		DimensionTypeName = SafeGetDimensionTypeName(t),
        		FamilyName = SafeGetDimensionFamilyName(t),
        		IsDefault = (IsValidElementId(defaultDimensionTypeId) && ((Element)t).Id.GetIdValue() == defaultDimensionTypeId.GetIdValue())
        	})).ToList();
        }

        private List<object> ListLegendViewCandidates(Document doc)
        {
        	//IL_001a: Unknown result type (might be due to invalid IL or missing references)
        	return ((IEnumerable<View>)(from View v in (IEnumerable)new FilteredElementCollector(doc).OfClass(typeof(View))
        		where (int)v.ViewType == 11 && !v.IsTemplate
        		select v).OrderBy((View v) => ((Element)v).Name, StringComparer.OrdinalIgnoreCase)).Select((Func<View, object>)delegate(View v)
        	{
        		//IL_0077: Unknown result type (might be due to invalid IL or missing references)
        		//IL_0081: Expected O, but got Unknown
        		//IL_00a0: Unknown result type (might be due to invalid IL or missing references)
        		//IL_00aa: Expected O, but got Unknown
        		List<ElementId> list = CollectLegendComponentIds(doc, v);
        		int num = 0;
        		int num2 = 0;
        		foreach (ElementId item in list)
        		{
        			Element element = doc.GetElement(item);
        			FamilySymbol legendComponentFamilySymbol = GetLegendComponentFamilySymbol(doc, element);
        			if (((legendComponentFamilySymbol != null) ? ((Element)legendComponentFamilySymbol).Category : null) != null)
        			{
        				if (((Element)legendComponentFamilySymbol).Category.Id.GetIdValue() == RevitCompatibility.GetIdValue(new ElementId((BuiltInCategory)(-2000023))))
        				{
        					num++;
        				}
        				else if (((Element)legendComponentFamilySymbol).Category.Id.GetIdValue() == RevitCompatibility.GetIdValue(new ElementId((BuiltInCategory)(-2000014))))
        				{
        					num2++;
        				}
        			}
        		}
        		return new
        		{
        			viewId = ((Element)v).Id.GetIdValue(),
        			viewName = ((Element)v).Name,
        			legendComponentCount = list.Count,
        			doorLegendComponentCount = num,
        			windowLegendComponentCount = num2,
        			ViewId = ((Element)v).Id.GetIdValue(),
        			ViewName = ((Element)v).Name,
        			LegendComponentCount = list.Count,
        			DoorLegendComponentCount = num,
        			WindowLegendComponentCount = num2
        		};
        	}).ToList();
        }

        private List<DoorWindowLegendTypeInfo> CollectUsedDoorWindowTypes(Document doc, string targetType)
        {
        	//IL_001b: Unknown result type (might be due to invalid IL or missing references)
        	//IL_0028: Unknown result type (might be due to invalid IL or missing references)
        	//IL_002d: Unknown result type (might be due to invalid IL or missing references)
        	BuiltInCategory val = (BuiltInCategory)((targetType == "door") ? (-2000023) : (-2000014));
        	HashSet<ElementId> hashSet = new HashSet<ElementId>(new ElementIdValueComparer());
        	IList<Element> list = new FilteredElementCollector(doc).OfCategory(val).WhereElementIsNotElementType().WhereElementIsViewIndependent()
        		.ToElements();
        	if (targetType == "window")
        	{
        		return CollectUsedWindowTypesBySillHeight(doc, list);
        	}
        	foreach (Element item in list)
        	{
        		ElementId typeId = item.GetTypeId();
        		if (typeId != (ElementId)null && typeId != ElementId.InvalidElementId)
        		{
        			hashSet.Add(typeId);
        		}
        	}
        	List<DoorWindowLegendTypeInfo> list2 = new List<DoorWindowLegendTypeInfo>();
        	foreach (ElementId item2 in hashSet)
        	{
        		Element element = doc.GetElement(item2);
        		FamilySymbol val2 = (FamilySymbol)(object)((element is FamilySymbol) ? element : null);
        		if (val2 != null)
        		{
        			string typeMark = GetTypeMark(val2);
        			list2.Add(new DoorWindowLegendTypeInfo
        			{
        				TypeId = item2,
        				TypeMarkRaw = typeMark,
        				TypeMarkDisplay = (string.IsNullOrWhiteSpace(typeMark) ? "(?芸‵)" : typeMark.Trim()),
        				TypeName = (((Element)val2).Name ?? string.Empty)
        			});
        		}
        	}
        	return list2;
        }

        private List<DoorWindowLegendTypeInfo> CollectUsedWindowTypesBySillHeight(Document doc, IList<Element> instances)
        {
        	Dictionary<string, DoorWindowLegendTypeInfo> dictionary = new Dictionary<string, DoorWindowLegendTypeInfo>();
        	foreach (Element instance in instances)
        	{
        		ElementId typeId = instance.GetTypeId();
        		if (typeId == (ElementId)null || typeId == ElementId.InvalidElementId)
        		{
        			continue;
        		}
        		Element element = doc.GetElement(typeId);
        		FamilySymbol val = (FamilySymbol)(object)((element is FamilySymbol) ? element : null);
        		if (val != null)
        		{
        			DoorWindowLegendSillHeightInfo windowSillHeightInfo = GetWindowSillHeightInfo(instance, val);
        			double num = Math.Round(windowSillHeightInfo.SillHeightCm / 0.1) * 0.1;
        			double sillHeightFeet = num * 0.0328083989501312;
        			string key = $"{typeId.GetIdValue()}|{num:F1}";
        			if (!dictionary.ContainsKey(key))
        			{
        				string typeMark = GetTypeMark(val);
        				dictionary[key] = new DoorWindowLegendTypeInfo
        				{
        					TypeId = typeId,
        					TypeMarkRaw = typeMark,
        					TypeMarkDisplay = (string.IsNullOrWhiteSpace(typeMark) ? "(??詹\u0080?" : typeMark.Trim()),
        					TypeName = (((Element)val).Name ?? string.Empty),
        					SillHeightCm = num,
        					SillHeightFeet = sillHeightFeet,
        					SillHeightSource = windowSillHeightInfo.Source,
        					SillHeightFailureReason = windowSillHeightInfo.FailureReason,
        					RepresentativeInstanceId = instance.Id
        				};
        			}
        		}
        	}
        	return dictionary.Values.ToList();
        }

        private List<DoorWindowLegendTypeInfo> SortDoorWindowTypesByTypeMark(List<DoorWindowLegendTypeInfo> usedTypes)
        {
        	return usedTypes.OrderBy((DoorWindowLegendTypeInfo t) => string.IsNullOrWhiteSpace(t.TypeMarkRaw) ? 1 : 0).ThenBy<DoorWindowLegendTypeInfo, string>((DoorWindowLegendTypeInfo t) => NormalizeTypeMarkForSort(t.TypeMarkRaw), new NaturalStringComparer()).ThenBy((DoorWindowLegendTypeInfo t) => t.SillHeightCm)
        		.ThenBy<DoorWindowLegendTypeInfo, string>((DoorWindowLegendTypeInfo t) => t.TypeName, StringComparer.OrdinalIgnoreCase)
        		.ToList();
        }

        private View GetLegendViewById(Document doc, int viewId)
        {
        	//IL_0002: Unknown result type (might be due to invalid IL or missing references)
        	//IL_000c: Expected O, but got Unknown
        	//IL_0016: Unknown result type (might be due to invalid IL or missing references)
        	//IL_001d: Invalid comparison between Unknown and I4
        	Element element = doc.GetElement(new ElementId(viewId));
        	View val = (View)(object)((element is View) ? element : null);
        	if (val == null || (int)val.ViewType != 11 || val.IsTemplate)
        	{
        		return null;
        	}
        	return val;
        }

        private View DuplicateLegendView(Document doc, View sourceLegend, string defaultName)
        {
        	if (sourceLegend == null)
        	{
        		throw new Exception("legend_seed_view_not_found");
        	}
        	ElementId val = sourceLegend.Duplicate((ViewDuplicateOption)2);
        	Element element = doc.GetElement(val);
        	Element obj = ((element is View) ? element : null) ?? throw new Exception("legend_duplicate_failed");
        	obj.Name = BuildUniqueLegendName(doc, defaultName);
        	return (View)(object)obj;
        }

        private string BuildUniqueLegendName(Document doc, string baseName)
        {
        	//IL_0001: Unknown result type (might be due to invalid IL or missing references)
        	if (!(from View v in (IEnumerable)new FilteredElementCollector(doc).OfClass(typeof(View))
        		select ((Element)v).Name into n
        		where !string.IsNullOrWhiteSpace(n)
        		select n).ToHashSet(StringComparer.OrdinalIgnoreCase).Contains(baseName))
        	{
        		return baseName;
        	}
        	return $"{baseName}_{DateTime.Now:yyyyMMdd_HHmmss}";
        }

        private void ClearLegendViewContents(Document doc, View legendView)
        {
        	//IL_0007: Unknown result type (might be due to invalid IL or missing references)
        	ICollection<ElementId> collection = new FilteredElementCollector(doc, ((Element)legendView).Id).WhereElementIsNotElementType().ToElementIds();
        	if (collection.Count > 0)
        	{
        		doc.Delete(collection);
        	}
        }

        private void ClearLegendViewContentsExcept(Document doc, View legendView, IEnumerable<ElementId> keepElementIds)
        {
        	//IL_0057: Unknown result type (might be due to invalid IL or missing references)
        	HashSet<int> keepIds = new HashSet<int>(from id in (keepElementIds ?? Enumerable.Empty<ElementId>()).Where(IsValidElementId)
        		select id.GetIdValue());
        	ICollection<ElementId> collection = (from id in new FilteredElementCollector(doc, ((Element)legendView).Id).WhereElementIsNotElementType().ToElementIds().Where(IsValidElementId)
        		where !keepIds.Contains(id.GetIdValue())
        		select id).ToList();
        	if (collection.Count > 0)
        	{
        		doc.Delete(collection);
        	}
        }

        private List<ElementId> CollectViewElementIds(Document doc, View view)
        {
        	//IL_0014: Unknown result type (might be due to invalid IL or missing references)
        	if (doc == null || view == null)
        	{
        		return new List<ElementId>();
        	}
        	try
        	{
        		return new FilteredElementCollector(doc, ((Element)view).Id).WhereElementIsNotElementType().ToElementIds().Where(IsValidElementId)
        			.Distinct<ElementId>(new ElementIdValueComparer())
        			.ToList();
        	}
        	catch
        	{
        		return new List<ElementId>();
        	}
        }

        private DoorWindowLegendCleanupResult DeleteSeedOriginalIntersection(Document doc, View legendView, List<ElementId> seedOriginalElementIds)
        {
        	//IL_02c8: Unknown result type (might be due to invalid IL or missing references)
        	//IL_019d: Unknown result type (might be due to invalid IL or missing references)
        	//IL_01a4: Expected O, but got Unknown
        	//IL_01a6: Unknown result type (might be due to invalid IL or missing references)
        	//IL_0294: Unknown result type (might be due to invalid IL or missing references)
        	//IL_0230: Unknown result type (might be due to invalid IL or missing references)
        	DoorWindowLegendCleanupResult doorWindowLegendCleanupResult = new DoorWindowLegendCleanupResult
        	{
        		DeletedCount = 0,
        		Reason = "completed",
        		SeedOriginalElementCount = (seedOriginalElementIds?.Count ?? 0),
        		FinalViewElementCountBeforeCleanup = 0,
        		FinalViewElementCountAfterCleanup = 0
        	};
        	if (doc == null || legendView == null)
        	{
        		doorWindowLegendCleanupResult.Reason = "invalid_document_or_view";
        		return doorWindowLegendCleanupResult;
        	}
        	List<ElementId> list = CollectViewElementIds(doc, legendView);
        	doorWindowLegendCleanupResult.FinalViewElementCountBeforeCleanup = list.Count;
        	HashSet<int> seedOriginalIdValues = new HashSet<int>(from id in (seedOriginalElementIds ?? new List<ElementId>()).Where(IsValidElementId)
        		select id.GetIdValue());
        	HashSet<int> protectedIdValues = new HashSet<int>(from id in list.Where(IsValidElementId)
        		select id.GetIdValue() into id
        		where !seedOriginalIdValues.Contains(id)
        		select id);
        	doorWindowLegendCleanupResult.ProtectedElementCount = protectedIdValues.Count;
        	List<ElementId> list2 = (from id in (seedOriginalElementIds ?? new List<ElementId>()).Where(IsValidElementId)
        		where doc.GetElement(id) != null
        		select id).Distinct<ElementId>(new ElementIdValueComparer()).ToList();
        	if (list2.Count == 0)
        	{
        		doorWindowLegendCleanupResult.Reason = "nothing_to_delete";
        		doorWindowLegendCleanupResult.FinalViewElementCountAfterCleanup = doorWindowLegendCleanupResult.FinalViewElementCountBeforeCleanup;
        		return doorWindowLegendCleanupResult;
        	}
        	foreach (ElementId item in list2)
        	{
        		SubTransaction val = new SubTransaction(doc);
        		try
        		{
        			val.Start();
        			List<int> list3 = (from id in doc.Delete(item).Where(IsValidElementId)
        				select id.GetIdValue()).ToList();
        			List<int> list4 = list3.Where((int id) => protectedIdValues.Contains(id)).ToList();
        			if (list4.Count > 0)
        			{
        				val.RollBack();
        				doorWindowLegendCleanupResult.SkippedCount++;
        				doorWindowLegendCleanupResult.SkippedOriginalIds.Add(new
        				{
        					OriginalElementId = item.GetIdValue(),
        					WouldDeleteProtectedIds = list4,
        					Reason = "delete_would_remove_generated_elements"
        				});
        				Logger.Info(string.Format("door-window-legend-tools cleanup skipped original element. originalElementId={0}, wouldDeleteProtectedIds={1}", item.GetIdValue(), string.Join(",", list4)));
        			}
        			else
        			{
        				val.Commit();
        				doorWindowLegendCleanupResult.DeletedCount++;
        				doorWindowLegendCleanupResult.DeletedElementIds.AddRange(list3);
        			}
        		}
        		catch (Exception ex)
        		{
        			try
        			{
        				if (val.HasStarted())
        				{
        					val.RollBack();
        				}
        			}
        			catch
        			{
        			}
        			doorWindowLegendCleanupResult.SkippedCount++;
        			doorWindowLegendCleanupResult.SkippedOriginalIds.Add(new
        			{
        				OriginalElementId = item.GetIdValue(),
        				WouldDeleteProtectedIds = new List<int>(),
        				Reason = ex.Message
        			});
        			Logger.Error($"door-window-legend-tools cleanup skipped original element due to exception. originalElementId={item.GetIdValue()}", ex);
        		}
        	}
        	doc.Regenerate();
        	doorWindowLegendCleanupResult.FinalViewElementCountAfterCleanup = CollectViewElementIds(doc, legendView).Count;
        	doorWindowLegendCleanupResult.Reason = ((doorWindowLegendCleanupResult.SkippedCount > 0) ? "completed_with_skips" : "completed");
        	return doorWindowLegendCleanupResult;
        }

        private List<ElementId> CollectLegendComponentIds(Document doc, View legendView)
        {
        	//IL_0082: Unknown result type (might be due to invalid IL or missing references)
        	//IL_0032: Unknown result type (might be due to invalid IL or missing references)
        	if (doc == null || legendView == null)
        	{
        		return new List<ElementId>();
        	}
        	try
        	{
        		List<ElementId> list = new FilteredElementCollector(doc, ((Element)legendView).Id).OfCategory((BuiltInCategory)(-2000575)).WhereElementIsNotElementType().ToElementIds()
        			.Where(IsValidElementId)
        			.ToList();
        		if (list.Count > 0)
        		{
        			return list;
        		}
        	}
        	catch
        	{
        	}
        	try
        	{
        		return (from id in new FilteredElementCollector(doc, ((Element)legendView).Id).WhereElementIsNotElementType().ToElementIds().Where(IsValidElementId)
        			where IsLegendComponentElement(doc.GetElement(id))
        			select id).ToList();
        	}
        	catch
        	{
        		return new List<ElementId>();
        	}
        }

        private bool IsLegendComponentElement(Element element)
        {
        	//IL_0017: Unknown result type (might be due to invalid IL or missing references)
        	//IL_001d: Invalid comparison between Unknown and I4
        	if (element == null)
        	{
        		return false;
        	}
        	try
        	{
        		Parameter val = element.get_Parameter((BuiltInParameter)(-1133750));
        		return val != null && (int)val.StorageType == 4;
        	}
        	catch
        	{
        		return false;
        	}
        }

        private object BuildLegendViewDebug(Document doc, View legendView)
        {
        	//IL_003d: Unknown result type (might be due to invalid IL or missing references)
        	if (doc == null || legendView == null)
        	{
        		return null;
        	}
        	object viewId = SafeGetElementIdValue((Element)(object)legendView);
        	string viewName = SafeGetViewName(legendView);
        	try
        	{
        		List<ElementId> list = new FilteredElementCollector(doc, ((Element)legendView).Id).WhereElementIsNotElementType().ToElementIds().Where(IsValidElementId)
        			.ToList();
        		List<object> elements = (from id in list
        			select BuildElementDebugInfo(doc, id) into info
        			where info != null
        			select info).ToList();
        		return new
        		{
        			ViewId = viewId,
        			ViewName = viewName,
        			TotalElementCount = list.Count,
        			LegendComponentCount = CollectLegendComponentIds(doc, legendView).Count,
        			Elements = elements
        		};
        	}
        	catch (Exception ex)
        	{
        		return new
        		{
        			ViewId = viewId,
        			ViewName = viewName,
        			DebugError = ex.Message,
        			Elements = new object[0]
        		};
        	}
        }

        private object BuildElementDebugInfo(Document doc, ElementId elementId)
        {
        	if (!IsValidElementId(elementId))
        	{
        		return null;
        	}
        	Element val = null;
        	try
        	{
        		val = doc.GetElement(elementId);
        		if (val == null)
        		{
        			return new
        			{
        				ElementId = SafeGetElementIdValue(elementId),
        				Exists = false
        			};
        		}
        		Parameter val2 = val.get_Parameter((BuiltInParameter)(-1133750));
        		ElementId val3 = ((val2 != null) ? val2.AsElementId() : null);
        		object elementId2 = SafeGetElementIdValue(elementId);
        		Category category = val.Category;
        		return new
        		{
        			ElementId = elementId2,
        			Exists = true,
        			Category = (((category != null) ? category.Name : null) ?? string.Empty),
        			ClassName = ((object)val).GetType().Name,
        			Name = (val.Name ?? string.Empty),
        			HasLegendComponentParameter = (val2 != null),
        			LegendComponentTypeId = ((val3 != (ElementId)null && val3 != ElementId.InvalidElementId) ? ((object)val3.GetIdValue()) : null)
        		};
        	}
        	catch (Exception ex)
        	{
        		return new
        		{
        			ElementId = SafeGetElementIdValue(elementId),
        			DebugError = ex.Message
        		};
        	}
        }

        private object SafeGetElementIdValue(ElementId elementId)
        {
        	try
        	{
        		return (elementId != (ElementId)null && elementId != ElementId.InvalidElementId) ? ((object)elementId.GetIdValue()) : null;
        	}
        	catch
        	{
        		return null;
        	}
        }

        private bool IsValidElementId(ElementId elementId)
        {
        	if (elementId != (ElementId)null)
        	{
        		return elementId != ElementId.InvalidElementId;
        	}
        	return false;
        }

        private void SafeDeleteElement(Document doc, ElementId elementId)
        {
        	if (!IsValidElementId(elementId))
        	{
        		return;
        	}
        	try
        	{
        		if (doc.GetElement(elementId) != null)
        		{
        			doc.Delete(elementId);
        		}
        	}
        	catch
        	{
        	}
        }

        private object SafeGetElementIdValue(Element element)
        {
        	try
        	{
        		return (element != null) ? SafeGetElementIdValue(element.Id) : null;
        	}
        	catch
        	{
        		return null;
        	}
        }

        private string SafeGetViewName(View view)
        {
        	try
        	{
        		return ((view != null) ? ((Element)view).Name : null) ?? string.Empty;
        	}
        	catch
        	{
        		return string.Empty;
        	}
        }

        private DimensionType GetDimensionTypeById(Document doc, int dimensionTypeId)
        {
        	//IL_0008: Unknown result type (might be due to invalid IL or missing references)
        	//IL_0012: Expected O, but got Unknown
        	Element obj = ((doc != null) ? doc.GetElement(new ElementId(dimensionTypeId)) : null);
        	return (DimensionType)(object)((obj is DimensionType) ? obj : null);
        }

        private string SafeGetDimensionTypeName(DimensionType dimensionType)
        {
        	try
        	{
        		if (dimensionType == null)
        		{
        			return string.Empty;
        		}
        		Parameter obj = ((Element)dimensionType).get_Parameter((BuiltInParameter)(-1002001));
        		string text = ((obj != null) ? obj.AsString() : null);
        		return (!string.IsNullOrWhiteSpace(text)) ? text : (((Element)dimensionType).Name ?? string.Empty);
        	}
        	catch
        	{
        		return string.Empty;
        	}
        }

        private string SafeGetDimensionFamilyName(DimensionType dimensionType)
        {
        	try
        	{
        		return ((dimensionType != null) ? ((ElementType)dimensionType).FamilyName : null) ?? string.Empty;
        	}
        	catch
        	{
        		return string.Empty;
        	}
        }

        private int PlaceLegendItemsFromOriginalSeedSource(Document doc, View legendView, ElementId sourceSeedComponentId, string targetType, string layoutDirection, int maxPerLine, List<DoorWindowLegendTypeInfo> usedTypes, List<DoorWindowLegendFailedType> failedTypes, List<object> attemptDebug, DimensionType dimensionType, out int dimensionCreatedCount, out int dimensionFailedCount, out List<ElementId> keepElementIds, int startIndex = 0, XYZ gridOriginOverride = null)
        {
        	keepElementIds = new List<ElementId>();
        	dimensionCreatedCount = 0;
        	dimensionFailedCount = 0;
        	Element element = doc.GetElement(sourceSeedComponentId);
        	if (element == null)
        	{
        		throw new InvalidOperationException("source Legend Component 不存在，無法建立門窗圖例。");
        	}
        	BoundingBoxXYZ bounds = element.get_BoundingBox(legendView);
        	XYZ seedOrigin = gridOriginOverride ?? GetLegendPlacementAnchor(bounds, targetType);
        	ElementId defaultElementTypeId = doc.GetDefaultElementTypeId((ElementTypeGroup)12);
        	int num = 0;
        	for (int i = 0; i < usedTypes.Count; i++)
        	{
        		DoorWindowLegendTypeInfo doorWindowLegendTypeInfo = usedTypes[i];
        		int index = startIndex + i;
        		XYZ targetAnchor = GetTargetAnchor(seedOrigin, layoutDirection, maxPerLine, index);
        		XYZ legendPlacementAnchor = GetLegendPlacementAnchor(element.get_BoundingBox(legendView), targetType);
        		XYZ translation = targetAnchor - legendPlacementAnchor;
        		ElementId val = ElementId.InvalidElementId;
        		try
        		{
        			Logger.Info($"door-window-legend-tools type attempt start. view={SafeGetViewName(legendView)}({SafeGetElementIdValue((Element)(object)legendView)}), sourceSeedComponentId={sourceSeedComponentId.GetIdValue()}, targetTypeId={doorWindowLegendTypeInfo.TypeId.GetIdValue()}, typeMark={doorWindowLegendTypeInfo.TypeMarkDisplay}, typeName={doorWindowLegendTypeInfo.TypeName}");
        			val = CopyLegendComponentInView(doc, legendView, sourceSeedComponentId, translation);
        			if (val == ElementId.InvalidElementId)
        			{
        				string text = "無法從 seed Legend Component 複製出新的 Legend Component。";
        				failedTypes.Add(new DoorWindowLegendFailedType
        				{
        					TypeId = doorWindowLegendTypeInfo.TypeId,
        					TypeMarkDisplay = doorWindowLegendTypeInfo.TypeMarkDisplay,
        					TypeName = doorWindowLegendTypeInfo.TypeName,
        					Reason = text
        				});
        				attemptDebug.Add(new
        				{
        					Step = "copy",
        					Success = false,
        					TargetTypeId = doorWindowLegendTypeInfo.TypeId.GetIdValue(),
        					TypeMarkDisplay = doorWindowLegendTypeInfo.TypeMarkDisplay,
        					TypeName = doorWindowLegendTypeInfo.TypeName,
        					SourceSeedComponentId = sourceSeedComponentId.GetIdValue(),
        					Message = text
        				});
        				Logger.Error($"door-window-legend-tools copy failed. targetTypeId={doorWindowLegendTypeInfo.TypeId.GetIdValue()}, typeMark={doorWindowLegendTypeInfo.TypeMarkDisplay}, typeName={doorWindowLegendTypeInfo.TypeName}");
        				continue;
        			}
        			if (!TryApplyLegendComponentTypeInView(doc, ((Element)legendView).Id, val, doorWindowLegendTypeInfo.TypeId, sourceSeedComponentId, out ElementId appliedComponentId, out string reason))
        			{
        				doc.Delete(val);
        				failedTypes.Add(new DoorWindowLegendFailedType
        				{
        					TypeId = doorWindowLegendTypeInfo.TypeId,
        					TypeMarkDisplay = doorWindowLegendTypeInfo.TypeMarkDisplay,
        					TypeName = doorWindowLegendTypeInfo.TypeName,
        					Reason = reason
        				});
        				attemptDebug.Add(new
        				{
        					Step = "set_legend_component",
        					Success = false,
        					TargetTypeId = doorWindowLegendTypeInfo.TypeId.GetIdValue(),
        					TypeMarkDisplay = doorWindowLegendTypeInfo.TypeMarkDisplay,
        					TypeName = doorWindowLegendTypeInfo.TypeName,
        					SourceSeedComponentId = sourceSeedComponentId.GetIdValue(),
        					CopiedComponentId = SafeGetElementIdValue(val),
        					Message = reason,
        					ViewDebugAfter = BuildLegendViewDebug(doc, legendView)
        				});
        				Logger.Error($"door-window-legend-tools set LEGEND_COMPONENT failed. copiedComponentId={SafeGetElementIdValue(val)}, targetTypeId={doorWindowLegendTypeInfo.TypeId.GetIdValue()}, reason={reason}");
        				continue;
        			}
        			Element legendComponent = doc.GetElement(appliedComponentId) ?? FindLegendComponentByTargetType(doc, ((Element)legendView).Id, doorWindowLegendTypeInfo.TypeId);
        			XYZ val2 = AlignLegendComponentToTargetAnchor(doc, legendView, legendComponent, targetType, targetAnchor);
        			XYZ val3 = MoveWindowComponentBySillHeight(doc, legendView, legendComponent, targetType, doorWindowLegendTypeInfo.SillHeightFeet);
        			legendComponent = doc.GetElement(appliedComponentId) ?? FindLegendComponentByTargetType(doc, ((Element)legendView).Id, doorWindowLegendTypeInfo.TypeId);
        			ElementId val4 = ((targetType == "window") ? CreateWindowFflAlignedTypeMarkTextNote(doc, legendView, defaultElementTypeId, doorWindowLegendTypeInfo.TypeMarkDisplay, targetAnchor) : ((targetType == "door") ? CreateFflAlignedTypeMarkTextNote(doc, legendView, legendComponent, defaultElementTypeId, doorWindowLegendTypeInfo.TypeMarkDisplay) : CreateLegendTextNote(doc, legendView, legendComponent, defaultElementTypeId, doorWindowLegendTypeInfo.TypeMarkDisplay)));
        			DoorWindowLegendDimensionResult doorWindowLegendDimensionResult = CreateDoorWindowLegendDimensions(doc, legendView, legendComponent, dimensionType);
        			DoorWindowLegendFflResult doorWindowLegendFflResult = ((targetType == "window") ? CreateWindowLegendFflElements(doc, legendView, legendComponent, defaultElementTypeId, targetAnchor) : CreateDoorLegendFflElements(doc, legendView, legendComponent, defaultElementTypeId, targetType));
        			DoorWindowLegendSillDimensionResult doorWindowLegendSillDimensionResult = CreateWindowSillHeightDimension(doc, legendView, legendComponent, dimensionType, doorWindowLegendFflResult.LineId, targetAnchor, targetType);
        			string itemKey = BuildDoorWindowLegendItemKey(targetType, doorWindowLegendTypeInfo.TypeId, doorWindowLegendTypeInfo.SillHeightCm);
        			SetDoorWindowLegendTextMetadata(doc, val4, "type_mark", targetType, appliedComponentId, doorWindowLegendTypeInfo.TypeId, itemKey);
        			SetDoorWindowLegendTextMetadata(doc, doorWindowLegendFflResult.TextId, "ffl_label", targetType, appliedComponentId, doorWindowLegendTypeInfo.TypeId, itemKey);
        			keepElementIds.Add(appliedComponentId);
        			if (IsValidElementId(val4))
        			{
        				keepElementIds.Add(val4);
        			}
        			keepElementIds.AddRange(doorWindowLegendFflResult.KeepElementIds);
        			keepElementIds.AddRange(doorWindowLegendSillDimensionResult.KeepElementIds);
        			keepElementIds.AddRange(doorWindowLegendDimensionResult.KeepElementIds);
        			dimensionCreatedCount += doorWindowLegendDimensionResult.CreatedCount;
        			dimensionFailedCount += doorWindowLegendDimensionResult.FailedCount;
        			attemptDebug.Add(new
        			{
        				Step = "created",
        				Success = true,
        				TargetTypeId = doorWindowLegendTypeInfo.TypeId.GetIdValue(),
        				TypeMarkDisplay = doorWindowLegendTypeInfo.TypeMarkDisplay,
        				TypeName = doorWindowLegendTypeInfo.TypeName,
        				SourceSeedComponentId = sourceSeedComponentId.GetIdValue(),
        				CopiedComponentId = SafeGetElementIdValue(val),
        				AppliedComponentId = SafeGetElementIdValue(appliedComponentId),
        				PlacementAnchor = ((targetType == "window") ? "ffl_bottom_center" : ((targetType == "door") ? "bottom_center" : "left_center")),
        				PlacementAdjustmentX = val2.X,
        				PlacementAdjustmentY = val2.Y,
        				WindowSillMoveY = ((targetType == "window") ? new double?(val3.Y) : ((double?)null)),
        				LabelId = SafeGetElementIdValue(val4),
        				DimensionTypeId = SafeGetElementIdValue((Element)(object)dimensionType),
        				DimensionTypeName = SafeGetDimensionTypeName(dimensionType),
        				WidthDimensionId = SafeGetElementIdValue(doorWindowLegendDimensionResult.WidthDimensionId),
        				HeightDimensionId = SafeGetElementIdValue(doorWindowLegendDimensionResult.HeightDimensionId),
        				DimensionReferenceSource = doorWindowLegendDimensionResult.ReferenceSource,
        				WidthDimensionReferenceSource = doorWindowLegendDimensionResult.WidthReferenceSource,
        				HeightDimensionReferenceSource = doorWindowLegendDimensionResult.HeightReferenceSource,
        				DimensionReferenceCurveIds = doorWindowLegendDimensionResult.ReferenceCurveIds,
        				DimensionFailureReason = doorWindowLegendDimensionResult.FailureReason,
        				DoorFflLineId = SafeGetElementIdValue(doorWindowLegendFflResult.LineId),
        				DoorFflTextId = SafeGetElementIdValue(doorWindowLegendFflResult.TextId),
        				DoorFflLineLengthFactor = ((targetType == "door") ? new double?(DoorFflLineLengthFactor) : ((double?)null)),
        				DoorFflTextOffsetCm = ((targetType == "door") ? new double?(DoorFflTextOffsetCm) : ((double?)null)),
        				DoorFflFailureReason = doorWindowLegendFflResult.FailureReason,
        				WindowSillHeightCm = ((targetType == "window") ? new double?(doorWindowLegendTypeInfo.SillHeightCm) : ((double?)null)),
        				WindowSillHeightFeet = ((targetType == "window") ? new double?(doorWindowLegendTypeInfo.SillHeightFeet) : ((double?)null)),
        				WindowSillHeightSource = ((targetType == "window") ? doorWindowLegendTypeInfo.SillHeightSource : null),
        				WindowSillHeightFailureReason = ((targetType == "window") ? doorWindowLegendTypeInfo.SillHeightFailureReason : null),
        				RepresentativeInstanceId = ((targetType == "window") ? SafeGetElementIdValue(doorWindowLegendTypeInfo.RepresentativeInstanceId) : null),
        				WindowFflLineId = ((targetType == "window") ? SafeGetElementIdValue(doorWindowLegendFflResult.LineId) : null),
        				WindowFflTextId = ((targetType == "window") ? SafeGetElementIdValue(doorWindowLegendFflResult.TextId) : null),
        				WindowFflLineLengthFactor = ((targetType == "window") ? new double?(WindowFflLineLengthFactor) : ((double?)null)),
        				WindowFflTextOffsetCm = ((targetType == "window") ? new double?(WindowFflTextOffsetCm) : ((double?)null)),
        				WindowFflFailureReason = ((targetType == "window") ? doorWindowLegendFflResult.FailureReason : null),
        				WindowSillDimensionId = SafeGetElementIdValue(doorWindowLegendSillDimensionResult.DimensionId),
        				WindowSillDimensionReferenceSource = doorWindowLegendSillDimensionResult.ReferenceSource,
        				WindowSillDimensionReferenceCurveIds = doorWindowLegendSillDimensionResult.ReferenceCurveIds,
        				WindowSillDimensionFailureReason = doorWindowLegendSillDimensionResult.FailureReason,
        				DoorTypeMarkOffsetCm = ((targetType == "door") ? new double?(DoorTypeMarkOffsetCm) : ((double?)null)),
        				WindowTypeMarkOffsetCm = ((targetType == "window") ? new double?(WindowTypeMarkOffsetCm) : ((double?)null))
        			});
        			Logger.Info($"door-window-legend-tools type attempt success. appliedComponentId={SafeGetElementIdValue(appliedComponentId)}, targetTypeId={doorWindowLegendTypeInfo.TypeId.GetIdValue()}, typeMark={doorWindowLegendTypeInfo.TypeMarkDisplay}");
        			num++;
        		}
        		catch (Exception ex)
        		{
        			SafeDeleteElement(doc, val);
        			failedTypes.Add(new DoorWindowLegendFailedType
        			{
        				TypeId = doorWindowLegendTypeInfo.TypeId,
        				TypeMarkDisplay = doorWindowLegendTypeInfo.TypeMarkDisplay,
        				TypeName = doorWindowLegendTypeInfo.TypeName,
        				Reason = ex.Message
        			});
        			attemptDebug.Add(new
        			{
        				Step = "exception",
        				Success = false,
        				TargetTypeId = doorWindowLegendTypeInfo.TypeId.GetIdValue(),
        				TypeMarkDisplay = doorWindowLegendTypeInfo.TypeMarkDisplay,
        				TypeName = doorWindowLegendTypeInfo.TypeName,
        				SourceSeedComponentId = sourceSeedComponentId.GetIdValue(),
        				CopiedComponentId = SafeGetElementIdValue(val),
        				Message = ex.Message,
        				ViewDebugAfter = BuildLegendViewDebug(doc, legendView)
        			});
        			Logger.Error($"door-window-legend-tools type attempt exception. copiedComponentId={SafeGetElementIdValue(val)}, targetTypeId={doorWindowLegendTypeInfo.TypeId.GetIdValue()}, typeMark={doorWindowLegendTypeInfo.TypeMarkDisplay}", ex);
        		}
        	}
        	return num;
        }

        private ElementId CopyLegendComponentInView(Document doc, View legendView, ElementId sourceComponentId, XYZ translation)
        {
        	if (legendView == null || sourceComponentId == (ElementId)null || sourceComponentId == ElementId.InvalidElementId)
        	{
        		return ElementId.InvalidElementId;
        	}
        	HashSet<int> existingLegendComponentIds = (from id in CollectLegendComponentIds(doc, legendView)
        		select id.GetIdValue()).ToHashSet();
        	ICollection<ElementId> collection = ElementTransformUtils.CopyElement(doc, sourceComponentId, translation);
        	doc.Regenerate();
        	ElementId val = CollectLegendComponentIds(doc, legendView).FirstOrDefault((ElementId id) => !existingLegendComponentIds.Contains(id.GetIdValue()));
        	if (val != (ElementId)null && val != ElementId.InvalidElementId)
        	{
        		return val;
        	}
        	foreach (ElementId item in collection)
        	{
        		if (!(item == (ElementId)null) && !(item == ElementId.InvalidElementId))
        		{
        			Element element = doc.GetElement(item);
        			if (IsLegendComponentElement(element))
        			{
        				return item;
        			}
        		}
        	}
        	return ElementId.InvalidElementId;
        }

        private bool TryApplyLegendComponentTypeInView(Document doc, ElementId legendViewId, ElementId legendComponentId, ElementId targetTypeId, ElementId sourceSeedComponentId, out ElementId appliedComponentId, out string reason)
        {
        	appliedComponentId = ElementId.InvalidElementId;
        	reason = null;
        	Element element = doc.GetElement(legendComponentId);
        	Parameter val = ((element != null) ? element.get_Parameter((BuiltInParameter)(-1133750)) : null);
        	if (val == null)
        	{
        		reason = "找不到 LEGEND_COMPONENT 參數。";
        		return false;
        	}
        	if (((APIObject)val).IsReadOnly)
        	{
        		reason = "LEGEND_COMPONENT 參數為唯讀。";
        		return false;
        	}
        	string text = "set_parameter";
        	try
        	{
        		if (!val.Set(targetTypeId))
        		{
        			reason = "Revit 拒絕設定 LEGEND_COMPONENT。";
        			return false;
        		}
        		text = "regenerate";
        		doc.Regenerate();
        		text = "find_target_component";
        		if (LegendComponentMatchesTargetType(doc.GetElement(legendComponentId), targetTypeId))
        		{
        			appliedComponentId = legendComponentId;
        			return true;
        		}
        		ElementId val2 = FindLegendComponentIdByTargetType(doc, legendViewId, targetTypeId, (IEnumerable<ElementId>)(object)new ElementId[1] { sourceSeedComponentId });
        		if (!IsValidElementId(val2))
        		{
        			reason = "find_target_component: 找不到套用目標 type 後的 Legend Component。";
        			return false;
        		}
        		appliedComponentId = val2;
        		return true;
        	}
        	catch (Exception ex)
        	{
        		reason = text + ": " + ex.Message;
        		return false;
        	}
        }

        private Element FindLegendComponentByTargetType(Document doc, ElementId legendViewId, ElementId targetTypeId)
        {
        	ElementId val = FindLegendComponentIdByTargetType(doc, legendViewId, targetTypeId, Enumerable.Empty<ElementId>());
        	if (!IsValidElementId(val))
        	{
        		return null;
        	}
        	return doc.GetElement(val);
        }

        private ElementId FindLegendComponentIdByTargetType(Document doc, ElementId legendViewId, ElementId targetTypeId, IEnumerable<ElementId> excludedElementIds)
        {
        	//IL_00f2: Unknown result type (might be due to invalid IL or missing references)
        	//IL_008a: Unknown result type (might be due to invalid IL or missing references)
        	if (!IsValidElementId(legendViewId) || !IsValidElementId(targetTypeId))
        	{
        		return ElementId.InvalidElementId;
        	}
        	HashSet<int> excludedIds = new HashSet<int>(from id in (excludedElementIds ?? Enumerable.Empty<ElementId>()).Where(IsValidElementId)
        		select id.GetIdValue());
        	try
        	{
        		ElementId val = (from id in new FilteredElementCollector(doc, legendViewId).OfCategory((BuiltInCategory)(-2000575)).WhereElementIsNotElementType().ToElementIds()
        				.Where(IsValidElementId)
        			where !excludedIds.Contains(id.GetIdValue())
        			select id).FirstOrDefault((ElementId id) => LegendComponentMatchesTargetType(doc.GetElement(id), targetTypeId));
        		if (IsValidElementId(val))
        		{
        			return val;
        		}
        	}
        	catch
        	{
        	}
        	try
        	{
        		ElementId val2 = (from id in new FilteredElementCollector(doc, legendViewId).WhereElementIsNotElementType().ToElementIds().Where(IsValidElementId)
        			where !excludedIds.Contains(id.GetIdValue())
        			select id).FirstOrDefault((ElementId id) => LegendComponentMatchesTargetType(doc.GetElement(id), targetTypeId));
        		return IsValidElementId(val2) ? val2 : ElementId.InvalidElementId;
        	}
        	catch
        	{
        		return ElementId.InvalidElementId;
        	}
        }

        private bool LegendComponentMatchesTargetType(Element element, ElementId targetTypeId)
        {
        	if (element == null || !IsValidElementId(targetTypeId))
        	{
        		return false;
        	}
        	try
        	{
        		Parameter obj = element.get_Parameter((BuiltInParameter)(-1133750));
        		ElementId val = ((obj != null) ? obj.AsElementId() : null);
        		return val != (ElementId)null && val != ElementId.InvalidElementId && val.GetIdValue() == targetTypeId.GetIdValue();
        	}
        	catch
        	{
        		return false;
        	}
        }

        private ElementId CreateLegendTextNote(Document doc, View legendView, Element legendComponent, ElementId textTypeId, string text)
        {
        	//IL_007e: Unknown result type (might be due to invalid IL or missing references)
        	//IL_0084: Expected O, but got Unknown
        	//IL_0086: Unknown result type (might be due to invalid IL or missing references)
        	//IL_008b: Unknown result type (might be due to invalid IL or missing references)
        	//IL_0094: Expected O, but got Unknown
        	if (legendComponent == null || string.IsNullOrWhiteSpace(text))
        	{
        		return ElementId.InvalidElementId;
        	}
        	BoundingBoxXYZ val = legendComponent.get_BoundingBox(legendView);
        	if (val == null)
        	{
        		return ElementId.InvalidElementId;
        	}
        	double num = (val.Min.X + val.Max.X) / 2.0;
        	double y = val.Max.Y;
        	double num2 = Math.Max(35.0, 130.0);
        	XYZ val2 = new XYZ(num, y + num2 * 0.0328083989501312, 0.0);
        	TextNoteOptions val3 = new TextNoteOptions(textTypeId)
        	{
        		HorizontalAlignment = (HorizontalTextAlignment)2
        	};
        	TextNote obj = TextNote.Create(doc, ((Element)legendView).Id, val2, text, val3);
        	return ((obj != null) ? ((Element)obj).Id : null) ?? ElementId.InvalidElementId;
        }

        private ElementId CreateFflAlignedTypeMarkTextNote(Document doc, View legendView, Element legendComponent, ElementId textTypeId, string text)
        {
        	//IL_0043: Unknown result type (might be due to invalid IL or missing references)
        	//IL_0049: Expected O, but got Unknown
        	//IL_004a: Unknown result type (might be due to invalid IL or missing references)
        	//IL_004f: Unknown result type (might be due to invalid IL or missing references)
        	//IL_0057: Expected O, but got Unknown
        	if (doc == null || legendView == null || legendComponent == null || !IsValidElementId(textTypeId) || string.IsNullOrWhiteSpace(text))
        	{
        		return ElementId.InvalidElementId;
        	}
        	BoundingBoxXYZ val = legendComponent.get_BoundingBox(legendView);
        	if (val == null)
        	{
        		return ElementId.InvalidElementId;
        	}
        	double num = (val.Min.X + val.Max.X) / 2.0;
        	double num2 = val.Min.Y + DoorTypeMarkOffsetCm * CmToFeet;
        	XYZ val2 = new XYZ(num, num2, 0.0);
        	TextNoteOptions val3 = new TextNoteOptions(textTypeId)
        	{
        		HorizontalAlignment = (HorizontalTextAlignment)2
        	};
        	TextNote obj = TextNote.Create(doc, ((Element)legendView).Id, val2, text, val3);
        	return ((obj != null) ? ((Element)obj).Id : null) ?? ElementId.InvalidElementId;
        }

        private ElementId CreateWindowFflAlignedTypeMarkTextNote(Document doc, View legendView, ElementId textTypeId, string text, XYZ fflAnchor)
        {
        	//IL_0028: Unknown result type (might be due to invalid IL or missing references)
        	//IL_002e: Expected O, but got Unknown
        	//IL_002f: Unknown result type (might be due to invalid IL or missing references)
        	//IL_0034: Unknown result type (might be due to invalid IL or missing references)
        	//IL_003c: Expected O, but got Unknown
        	if (doc == null || legendView == null || !IsValidElementId(textTypeId) || string.IsNullOrWhiteSpace(text) || fflAnchor == null)
        	{
        		return ElementId.InvalidElementId;
        	}
        	XYZ val = new XYZ(fflAnchor.X, fflAnchor.Y + WindowTypeMarkOffsetCm * CmToFeet, 0.0);
        	TextNoteOptions val2 = new TextNoteOptions(textTypeId)
        	{
        		HorizontalAlignment = (HorizontalTextAlignment)2
        	};
        	TextNote obj = TextNote.Create(doc, ((Element)legendView).Id, val, text, val2);
        	return ((obj != null) ? ((Element)obj).Id : null) ?? ElementId.InvalidElementId;
        }

        private XYZ AlignLegendComponentToTargetAnchor(Document doc, View legendView, Element legendComponent, string targetType, XYZ targetAnchor)
        {
        	if ((targetType != "door" && targetType != "window") || doc == null || legendView == null || legendComponent == null || targetAnchor == null)
        	{
        		return XYZ.Zero;
        	}
        	BoundingBoxXYZ val = legendComponent.get_BoundingBox(legendView);
        	if (val == null)
        	{
        		return XYZ.Zero;
        	}
        	XYZ legendPlacementAnchor = GetLegendPlacementAnchor(val, targetType);
        	XYZ val2 = targetAnchor - legendPlacementAnchor;
        	if (val2.GetLength() <= 0.0001)
        	{
        		return XYZ.Zero;
        	}
        	ElementTransformUtils.MoveElement(doc, legendComponent.Id, val2);
        	doc.Regenerate();
        	return val2;
        }

        private XYZ MoveWindowComponentBySillHeight(Document doc, View legendView, Element legendComponent, string targetType, double sillHeightFeet)
        {
        	//IL_0043: Unknown result type (might be due to invalid IL or missing references)
        	//IL_0049: Expected O, but got Unknown
        	if (targetType != "window" || doc == null || legendView == null || legendComponent == null || Math.Abs(sillHeightFeet) <= 0.0001)
        	{
        		return XYZ.Zero;
        	}
        	XYZ val = new XYZ(0.0, sillHeightFeet, 0.0);
        	ElementTransformUtils.MoveElement(doc, legendComponent.Id, val);
        	doc.Regenerate();
        	return val;
        }

        private DoorWindowLegendFflResult CreateDoorLegendFflElements(Document doc, View legendView, Element legendComponent, ElementId textTypeId, string targetType)
        {
        	//IL_00dc: Unknown result type (might be due to invalid IL or missing references)
        	//IL_00ee: Unknown result type (might be due to invalid IL or missing references)
        	//IL_00f8: Expected O, but got Unknown
        	//IL_00f8: Expected O, but got Unknown
        	//IL_0155: Unknown result type (might be due to invalid IL or missing references)
        	//IL_015a: Unknown result type (might be due to invalid IL or missing references)
        	//IL_0161: Unknown result type (might be due to invalid IL or missing references)
        	//IL_016a: Expected O, but got Unknown
        	//IL_0181: Unknown result type (might be due to invalid IL or missing references)
        	//IL_0188: Expected O, but got Unknown
        	DoorWindowLegendFflResult doorWindowLegendFflResult = new DoorWindowLegendFflResult();
        	if (targetType != "door")
        	{
        		return doorWindowLegendFflResult;
        	}
        	if (doc == null || legendView == null || legendComponent == null)
        	{
        		doorWindowLegendFflResult.FailureReason = "door FFL 建立失敗：legend component 或 view 不存在。";
        		return doorWindowLegendFflResult;
        	}
        	try
        	{
        		BoundingBoxXYZ val = legendComponent.get_BoundingBox(legendView);
        		if (val == null)
        		{
        			doorWindowLegendFflResult.FailureReason = "door FFL 建立失敗：legend component 沒有可用 BoundingBox。";
        			return doorWindowLegendFflResult;
        		}
        		double num = val.Max.X - val.Min.X;
        		if (num <= 0.0)
        		{
        			doorWindowLegendFflResult.FailureReason = "door FFL 建立失敗：legend component bbox 寬度無效。";
        			return doorWindowLegendFflResult;
        		}
        		double num2 = (val.Min.X + val.Max.X) / 2.0;
        		double num3 = num * DoorFflLineLengthFactor / 2.0;
        		double y = val.Min.Y;
        		double num4 = num2 - num3;
        		double num5 = num2 + num3;
        		Line val2 = Line.CreateBound(new XYZ(num4, y, 0.0), new XYZ(num5, y, 0.0));
        		DetailCurve val3 = ((ItemFactoryBase)doc.Create).NewDetailCurve(legendView, (Curve)(object)val2);
        		doorWindowLegendFflResult.LineId = ((val3 != null) ? ((Element)val3).Id : null) ?? ElementId.InvalidElementId;
        		if (IsValidElementId(doorWindowLegendFflResult.LineId))
        		{
        			doorWindowLegendFflResult.KeepElementIds.Add(doorWindowLegendFflResult.LineId);
        		}
        		if (IsValidElementId(textTypeId))
        		{
        			TextNoteOptions val4 = new TextNoteOptions(textTypeId)
        			{
        				HorizontalAlignment = HorizontalTextAlignment.Left,
        				VerticalAlignment = VerticalTextAlignment.Bottom
        			};
        			XYZ val5 = new XYZ(num4, y + DoorFflTextOffsetCm * CmToFeet, 0.0);
        			TextNote val6 = TextNote.Create(doc, ((Element)legendView).Id, val5, "FFL", val4);
        			doorWindowLegendFflResult.TextId = ((val6 != null) ? ((Element)val6).Id : null) ?? ElementId.InvalidElementId;
        			if (IsValidElementId(doorWindowLegendFflResult.TextId))
        			{
        				doorWindowLegendFflResult.KeepElementIds.Add(doorWindowLegendFflResult.TextId);
        			}
        		}
        		else
        		{
        			doorWindowLegendFflResult.FailureReason = "door FFL 文字未建立：找不到預設 TextNoteType。";
        		}
        		return doorWindowLegendFflResult;
        	}
        	catch (Exception ex)
        	{
        		doorWindowLegendFflResult.FailureReason = ex.Message;
        		SafeDeleteElement(doc, doorWindowLegendFflResult.LineId);
        		SafeDeleteElement(doc, doorWindowLegendFflResult.TextId);
        		doorWindowLegendFflResult.KeepElementIds.Clear();
        		doorWindowLegendFflResult.LineId = ElementId.InvalidElementId;
        		doorWindowLegendFflResult.TextId = ElementId.InvalidElementId;
        		return doorWindowLegendFflResult;
        	}
        }

        private DoorWindowLegendFflResult CreateWindowLegendFflElements(Document doc, View legendView, Element legendComponent, ElementId textTypeId, XYZ fflAnchor)
        {
        	//IL_00b8: Unknown result type (might be due to invalid IL or missing references)
        	//IL_00ca: Unknown result type (might be due to invalid IL or missing references)
        	//IL_00d4: Expected O, but got Unknown
        	//IL_00d4: Expected O, but got Unknown
        	//IL_0131: Unknown result type (might be due to invalid IL or missing references)
        	//IL_0136: Unknown result type (might be due to invalid IL or missing references)
        	//IL_013d: Unknown result type (might be due to invalid IL or missing references)
        	//IL_0146: Expected O, but got Unknown
        	//IL_015d: Unknown result type (might be due to invalid IL or missing references)
        	//IL_0164: Expected O, but got Unknown
        	DoorWindowLegendFflResult doorWindowLegendFflResult = new DoorWindowLegendFflResult();
        	if (doc == null || legendView == null || legendComponent == null || fflAnchor == null)
        	{
        		doorWindowLegendFflResult.FailureReason = "window FFL 建立失敗：legend component、view 或 FFL anchor 不存在。";
        		return doorWindowLegendFflResult;
        	}
        	try
        	{
        		BoundingBoxXYZ val = legendComponent.get_BoundingBox(legendView);
        		if (val == null)
        		{
        			doorWindowLegendFflResult.FailureReason = "window FFL 建立失敗：legend component 沒有可用 BoundingBox。";
        			return doorWindowLegendFflResult;
        		}
        		double num = val.Max.X - val.Min.X;
        		if (num <= 0.0)
        		{
        			doorWindowLegendFflResult.FailureReason = "window FFL 建立失敗：legend component bbox 寬度無效。";
        			return doorWindowLegendFflResult;
        		}
        		double num2 = num * WindowFflLineLengthFactor / 2.0;
        		double num3 = fflAnchor.X - num2;
        		double num4 = fflAnchor.X + num2;
        		double y = fflAnchor.Y;
        		Line val2 = Line.CreateBound(new XYZ(num3, y, 0.0), new XYZ(num4, y, 0.0));
        		DetailCurve val3 = ((ItemFactoryBase)doc.Create).NewDetailCurve(legendView, (Curve)(object)val2);
        		doorWindowLegendFflResult.LineId = ((val3 != null) ? ((Element)val3).Id : null) ?? ElementId.InvalidElementId;
        		if (IsValidElementId(doorWindowLegendFflResult.LineId))
        		{
        			doorWindowLegendFflResult.KeepElementIds.Add(doorWindowLegendFflResult.LineId);
        		}
        		if (IsValidElementId(textTypeId))
        		{
        			TextNoteOptions val4 = new TextNoteOptions(textTypeId)
        			{
        				HorizontalAlignment = HorizontalTextAlignment.Left,
        				VerticalAlignment = VerticalTextAlignment.Bottom
        			};
        			XYZ val5 = new XYZ(num3, y + WindowFflTextOffsetCm * CmToFeet, 0.0);
        			TextNote val6 = TextNote.Create(doc, ((Element)legendView).Id, val5, "FFL", val4);
        			doorWindowLegendFflResult.TextId = ((val6 != null) ? ((Element)val6).Id : null) ?? ElementId.InvalidElementId;
        			if (IsValidElementId(doorWindowLegendFflResult.TextId))
        			{
        				doorWindowLegendFflResult.KeepElementIds.Add(doorWindowLegendFflResult.TextId);
        			}
        		}
        		else
        		{
        			doorWindowLegendFflResult.FailureReason = "window FFL 文字未建立：找不到預設 TextNoteType。";
        		}
        		return doorWindowLegendFflResult;
        	}
        	catch (Exception ex)
        	{
        		doorWindowLegendFflResult.FailureReason = ex.Message;
        		SafeDeleteElement(doc, doorWindowLegendFflResult.LineId);
        		SafeDeleteElement(doc, doorWindowLegendFflResult.TextId);
        		doorWindowLegendFflResult.KeepElementIds.Clear();
        		doorWindowLegendFflResult.LineId = ElementId.InvalidElementId;
        		doorWindowLegendFflResult.TextId = ElementId.InvalidElementId;
        		return doorWindowLegendFflResult;
        	}
        }

        private DoorWindowLegendSillDimensionResult CreateWindowSillHeightDimension(Document doc, View legendView, Element legendComponent, DimensionType dimensionType, ElementId fflLineId, XYZ fflAnchor, string targetType)
        {
        	//IL_01c6: Unknown result type (might be due to invalid IL or missing references)
        	//IL_01f4: Unknown result type (might be due to invalid IL or missing references)
        	//IL_01fe: Expected O, but got Unknown
        	//IL_01fe: Expected O, but got Unknown
        	//IL_0296: Unknown result type (might be due to invalid IL or missing references)
        	//IL_029d: Expected O, but got Unknown
        	//IL_02dc: Unknown result type (might be due to invalid IL or missing references)
        	//IL_02fc: Unknown result type (might be due to invalid IL or missing references)
        	//IL_0306: Expected O, but got Unknown
        	//IL_0306: Expected O, but got Unknown
        	DoorWindowLegendSillDimensionResult doorWindowLegendSillDimensionResult = new DoorWindowLegendSillDimensionResult();
        	if (targetType != "window")
        	{
        		return doorWindowLegendSillDimensionResult;
        	}
        	if (doc == null || legendView == null || legendComponent == null || dimensionType == null || !IsValidElementId(fflLineId))
        	{
        		doorWindowLegendSillDimensionResult.FailureReason = "window sill dimension 建立失敗：必要 reference 不存在。";
        		return doorWindowLegendSillDimensionResult;
        	}
        	DetailCurve val = null;
        	try
        	{
        		BoundingBoxXYZ bounds = legendComponent.get_BoundingBox(legendView);
        		if (bounds == null)
        		{
        			doorWindowLegendSillDimensionResult.FailureReason = "window sill dimension 建立失敗：legend component 沒有可用 BoundingBox。";
        			return doorWindowLegendSillDimensionResult;
        		}
        		Element element = doc.GetElement(fflLineId);
        		Element obj = ((element is DetailCurve) ? element : null);
        		object obj2;
        		if (obj == null)
        		{
        			obj2 = null;
        		}
        		else
        		{
        			Curve geometryCurve = ((CurveElement)obj).GeometryCurve;
        			obj2 = ((geometryCurve != null) ? geometryCurve.Reference : null);
        		}
        		Reference val2 = (Reference)obj2;
        		if (val2 == null)
        		{
        			doorWindowLegendSillDimensionResult.FailureReason = "window sill dimension 建立失敗：FFL line 沒有可用 reference。";
        			return doorWindowLegendSillDimensionResult;
        		}
        		if (Math.Abs(bounds.Min.Y - fflAnchor.Y) <= 0.0001)
        		{
        			doorWindowLegendSillDimensionResult.FailureReason = "window sill dimension 未建立：窗台高為 0，FFL 與窗底 reference 重合。";
        			return doorWindowLegendSillDimensionResult;
        		}
        		List<DoorWindowLegendGeometryReference> source = CollectLegendGeometryReferences(legendComponent, legendView, bounds);
        		double tolerance = DimensionReferenceToleranceCm * CmToFeet;
        		Reference val3 = (from r in source
        			where r.IsHorizontal
        			where Math.Abs(r.CenterY - bounds.Min.Y) <= tolerance
        			orderby Math.Abs(r.CenterY - bounds.Min.Y), r.Length descending
        			select r).FirstOrDefault()?.Reference;
        		if (val3 != null)
        		{
        			doorWindowLegendSillDimensionResult.ReferenceSource = "legend_geometry";
        		}
        		else
        		{
        			double num = 0.3937007874015744;
        			Line val4 = Line.CreateBound(new XYZ(bounds.Max.X - num, bounds.Min.Y, 0.0), new XYZ(bounds.Max.X, bounds.Min.Y, 0.0));
        			val = ((ItemFactoryBase)doc.Create).NewDetailCurve(legendView, (Curve)(object)val4);
        			ApplyInvisibleLineStyle(doc, val);
        			object obj3;
        			if (val == null)
        			{
        				obj3 = null;
        			}
        			else
        			{
        				Curve geometryCurve2 = ((CurveElement)val).GeometryCurve;
        				obj3 = ((geometryCurve2 != null) ? geometryCurve2.Reference : null);
        			}
        			val3 = (Reference)obj3;
        			if (val3 == null)
        			{
        				doorWindowLegendSillDimensionResult.FailureReason = "window sill dimension 建立失敗：無法建立 window bottom fallback reference。";
        				SafeDeleteElement(doc, ((val != null) ? ((Element)val).Id : null) ?? ElementId.InvalidElementId);
        				return doorWindowLegendSillDimensionResult;
        			}
        			doorWindowLegendSillDimensionResult.ReferenceSource = "detail_curve_fallback";
        			doorWindowLegendSillDimensionResult.ReferenceCurveIds.Add(((Element)val).Id.GetIdValue());
        			doorWindowLegendSillDimensionResult.KeepElementIds.Add(((Element)val).Id);
        		}
        		ReferenceArray val5 = new ReferenceArray();
        		val5.Append(val2);
        		val5.Append(val3);
        		double num2 = bounds.Max.X + 1.64041994750656;
        		Line val6 = Line.CreateBound(new XYZ(num2, fflAnchor.Y, 0.0), new XYZ(num2, bounds.Min.Y, 0.0));
        		Dimension val7 = ((ItemFactoryBase)doc.Create).NewDimension(legendView, val6, val5);
        		if (val7 == null)
        		{
        			doorWindowLegendSillDimensionResult.FailureReason = "window sill dimension 建立失敗：Revit 無法建立 Dimension。";
        			SafeDeleteElement(doc, ((val != null) ? ((Element)val).Id : null) ?? ElementId.InvalidElementId);
        			doorWindowLegendSillDimensionResult.KeepElementIds.Clear();
        			doorWindowLegendSillDimensionResult.ReferenceCurveIds.Clear();
        			return doorWindowLegendSillDimensionResult;
        		}
        		ApplyDimensionType(val7, dimensionType);
        		doorWindowLegendSillDimensionResult.DimensionId = ((Element)val7).Id;
        		doorWindowLegendSillDimensionResult.KeepElementIds.Add(((Element)val7).Id);
        		return doorWindowLegendSillDimensionResult;
        	}
        	catch (Exception ex)
        	{
        		doorWindowLegendSillDimensionResult.FailureReason = ex.Message;
        		SafeDeleteElement(doc, ((val != null) ? ((Element)val).Id : null) ?? ElementId.InvalidElementId);
        		doorWindowLegendSillDimensionResult.KeepElementIds.Clear();
        		doorWindowLegendSillDimensionResult.ReferenceCurveIds.Clear();
        		doorWindowLegendSillDimensionResult.DimensionId = ElementId.InvalidElementId;
        		return doorWindowLegendSillDimensionResult;
        	}
        }

        private DoorWindowLegendDimensionResult CreateDoorWindowLegendDimensions(Document doc, View legendView, Element legendComponent, DimensionType dimensionType)
        {
        	DoorWindowLegendDimensionResult doorWindowLegendDimensionResult = new DoorWindowLegendDimensionResult();
        	if (doc == null || legendView == null || legendComponent == null || dimensionType == null)
        	{
        		doorWindowLegendDimensionResult.AddFailure("legend component、view 或 dimension type 不存在。");
        		return doorWindowLegendDimensionResult;
        	}
        	BoundingBoxXYZ val = legendComponent.get_BoundingBox(legendView);
        	if (val == null)
        	{
        		doorWindowLegendDimensionResult.AddFailure("legend component 沒有可用 BoundingBox。");
        		return doorWindowLegendDimensionResult;
        	}
        	List<DoorWindowLegendGeometryReference> references = CollectLegendGeometryReferences(legendComponent, legendView, val);
        	if (!TryCreateLegendGeometryDimension(doc, legendView, val, references, dimensionType, "width", out DoorWindowLegendDimensionAxisResult result, out string reason) && !TryCreateFallbackDetailCurveDimension(doc, legendView, val, dimensionType, "width", out result, out string reason2))
        	{
        		result = DoorWindowLegendDimensionAxisResult.Failed("width: " + reason + "; fallback: " + reason2);
        	}
        	if (!TryCreateLegendGeometryDimension(doc, legendView, val, references, dimensionType, "height", out DoorWindowLegendDimensionAxisResult result2, out string reason3) && !TryCreateFallbackDetailCurveDimension(doc, legendView, val, dimensionType, "height", out result2, out string reason4))
        	{
        		result2 = DoorWindowLegendDimensionAxisResult.Failed("height: " + reason3 + "; fallback: " + reason4);
        	}
        	doorWindowLegendDimensionResult.ApplyAxisResult("width", result);
        	doorWindowLegendDimensionResult.ApplyAxisResult("height", result2);
        	return doorWindowLegendDimensionResult;
        }

        private bool TryCreateLegendGeometryDimension(Document doc, View legendView, BoundingBoxXYZ bounds, List<DoorWindowLegendGeometryReference> references, DimensionType dimensionType, string axis, out DoorWindowLegendDimensionAxisResult result, out string reason)
        {
        	//IL_022f: Unknown result type (might be due to invalid IL or missing references)
        	//IL_0236: Expected O, but got Unknown
        	result = DoorWindowLegendDimensionAxisResult.Failed(null);
        	reason = null;
        	try
        	{
        		double tolerance = DimensionReferenceToleranceCm * CmToFeet;
        		DoorWindowLegendGeometryReference doorWindowLegendGeometryReference;
        		DoorWindowLegendGeometryReference doorWindowLegendGeometryReference2;
        		if (axis == "width")
        		{
        			List<DoorWindowLegendGeometryReference> source = references.Where((DoorWindowLegendGeometryReference r) => r.IsVertical).ToList();
        			doorWindowLegendGeometryReference = (from r in source
        				where Math.Abs(r.CenterX - bounds.Min.X) <= tolerance
        				orderby Math.Abs(r.CenterX - bounds.Min.X), r.Length descending
        				select r).FirstOrDefault();
        			doorWindowLegendGeometryReference2 = (from r in source
        				where Math.Abs(r.CenterX - bounds.Max.X) <= tolerance
        				orderby Math.Abs(r.CenterX - bounds.Max.X), r.Length descending
        				select r).FirstOrDefault();
        		}
        		else
        		{
        			List<DoorWindowLegendGeometryReference> source2 = references.Where((DoorWindowLegendGeometryReference r) => r.IsHorizontal).ToList();
        			doorWindowLegendGeometryReference = (from r in source2
        				where Math.Abs(r.CenterY - bounds.Min.Y) <= tolerance
        				orderby Math.Abs(r.CenterY - bounds.Min.Y), r.Length descending
        				select r).FirstOrDefault();
        			doorWindowLegendGeometryReference2 = (from r in source2
        				where Math.Abs(r.CenterY - bounds.Max.Y) <= tolerance
        				orderby Math.Abs(r.CenterY - bounds.Max.Y), r.Length descending
        				select r).FirstOrDefault();
        		}
        		if (doorWindowLegendGeometryReference == null || doorWindowLegendGeometryReference2 == null || doorWindowLegendGeometryReference.Reference == null || doorWindowLegendGeometryReference2.Reference == null)
        		{
        			reason = "找不到 " + axis + " 可用的 Legend geometry reference。";
        			return false;
        		}
        		Line val = CreateLegendDimensionLine(bounds, axis);
        		ReferenceArray val2 = new ReferenceArray();
        		val2.Append(doorWindowLegendGeometryReference.Reference);
        		val2.Append(doorWindowLegendGeometryReference2.Reference);
        		Dimension val3 = ((ItemFactoryBase)doc.Create).NewDimension(legendView, val, val2);
        		if (val3 == null)
        		{
        			reason = "Revit 無法建立 " + axis + " geometry dimension。";
        			return false;
        		}
        		ApplyDimensionType(val3, dimensionType);
        		result = DoorWindowLegendDimensionAxisResult.Created(((Element)val3).Id, "legend_geometry", Enumerable.Empty<ElementId>());
        		return true;
        	}
        	catch (Exception ex)
        	{
        		reason = ex.Message;
        		return false;
        	}
        }

        private List<DoorWindowLegendGeometryReference> CollectLegendGeometryReferences(Element legendComponent, View legendView, BoundingBoxXYZ bounds)
        {
        	//IL_0013: Unknown result type (might be due to invalid IL or missing references)
        	//IL_0018: Unknown result type (might be due to invalid IL or missing references)
        	//IL_001f: Unknown result type (might be due to invalid IL or missing references)
        	//IL_0026: Unknown result type (might be due to invalid IL or missing references)
        	//IL_002d: Unknown result type (might be due to invalid IL or missing references)
        	//IL_0035: Expected O, but got Unknown
        	List<DoorWindowLegendGeometryReference> list = new List<DoorWindowLegendGeometryReference>();
        	try
        	{
        		Options val = new Options
        		{
        			View = legendView,
        			ComputeReferences = true,
        			IncludeNonVisibleObjects = false,
        			DetailLevel = (ViewDetailLevel)3
        		};
        		GeometryElement geometry = legendComponent.get_Geometry(val);
        		CollectLegendGeometryReferences(geometry, list, Transform.Identity);
        	}
        	catch
        	{
        		return list;
        	}
        	double tolerance = DimensionReferenceToleranceCm * CmToFeet;
        	return (from r in list
        		where r.Reference != null
        		where r.Length > tolerance
        		where r.MaxX >= bounds.Min.X - tolerance && r.MinX <= bounds.Max.X + tolerance
        		where r.MaxY >= bounds.Min.Y - tolerance && r.MinY <= bounds.Max.Y + tolerance
        		select r).ToList();
        }

        private void CollectLegendGeometryReferences(GeometryElement geometry, List<DoorWindowLegendGeometryReference> references, Transform transform)
        {
        	//IL_00a5: Unknown result type (might be due to invalid IL or missing references)
        	//IL_00ac: Expected O, but got Unknown
        	if ((GeometryObject)(object)geometry == (GeometryObject)null)
        	{
        		return;
        	}
        	foreach (GeometryObject item in geometry)
        	{
        		if (item == (GeometryObject)null)
        		{
        			continue;
        		}
        		GeometryInstance val = (GeometryInstance)(object)((item is GeometryInstance) ? item : null);
        		if ((GeometryObject)(object)val != (GeometryObject)null)
        		{
        			Transform transform2 = transform.Multiply(val.Transform);
        			CollectLegendGeometryReferences(val.GetSymbolGeometry(), references, transform2);
        			continue;
        		}
        		Curve val2 = (Curve)(object)((item is Curve) ? item : null);
        		if ((GeometryObject)(object)val2 != (GeometryObject)null)
        		{
        			AddLegendGeometryReference(val2.Reference, val2, references, transform);
        			continue;
        		}
        		Solid val3 = (Solid)(object)((item is Solid) ? item : null);
        		if (!((GeometryObject)(object)val3 != (GeometryObject)null))
        		{
        			continue;
        		}
        		foreach (Edge edge in val3.Edges)
        		{
        			Edge val4 = edge;
        			Curve curve = val4.AsCurve();
        			AddLegendGeometryReference(val4.Reference, curve, references, transform);
        		}
        	}
        }

        private void AddLegendGeometryReference(Reference reference, Curve curve, List<DoorWindowLegendGeometryReference> references, Transform transform)
        {
        	if (reference != null && !((GeometryObject)(object)curve == (GeometryObject)null) && curve.IsBound)
        	{
        		XYZ val = transform.OfPoint(curve.GetEndPoint(0));
        		XYZ val2 = transform.OfPoint(curve.GetEndPoint(1));
        		double num = DimensionReferenceToleranceCm * CmToFeet;
        		double num2 = Math.Abs(val2.X - val.X);
        		double num3 = Math.Abs(val2.Y - val.Y);
        		if (num2 <= num || num3 <= num)
        		{
        			references.Add(new DoorWindowLegendGeometryReference
        			{
        				Reference = reference,
        				Start = val,
        				End = val2,
        				IsVertical = (num2 <= num && num3 > num),
        				IsHorizontal = (num3 <= num && num2 > num)
        			});
        		}
        	}
        }

        private bool TryCreateFallbackDetailCurveDimension(Document doc, View legendView, BoundingBoxXYZ bounds, DimensionType dimensionType, string axis, out DoorWindowLegendDimensionAxisResult result, out string reason)
        {
        	//IL_00fa: Unknown result type (might be due to invalid IL or missing references)
        	//IL_011e: Unknown result type (might be due to invalid IL or missing references)
        	//IL_0128: Expected O, but got Unknown
        	//IL_0128: Expected O, but got Unknown
        	//IL_014b: Unknown result type (might be due to invalid IL or missing references)
        	//IL_016f: Unknown result type (might be due to invalid IL or missing references)
        	//IL_0179: Expected O, but got Unknown
        	//IL_0179: Expected O, but got Unknown
        	//IL_0053: Unknown result type (might be due to invalid IL or missing references)
        	//IL_0077: Unknown result type (might be due to invalid IL or missing references)
        	//IL_0081: Expected O, but got Unknown
        	//IL_0081: Expected O, but got Unknown
        	//IL_00a4: Unknown result type (might be due to invalid IL or missing references)
        	//IL_00c8: Unknown result type (might be due to invalid IL or missing references)
        	//IL_00d2: Expected O, but got Unknown
        	//IL_00d2: Expected O, but got Unknown
        	//IL_0281: Unknown result type (might be due to invalid IL or missing references)
        	//IL_0288: Expected O, but got Unknown
        	result = DoorWindowLegendDimensionAxisResult.Failed(null);
        	reason = null;
        	List<ElementId> list = new List<ElementId>();
        	DetailCurve val = null;
        	DetailCurve val2 = null;
        	try
        	{
        		double num = DimensionReferenceStubCm * CmToFeet;
        		double topY = bounds.Max.Y;
        		double rightX = bounds.Max.X;
        		Line val3;
        		Line val4;
        		if (axis == "width")
        		{
        			double stubMinY = topY - num;
        			val3 = Line.CreateBound(new XYZ(bounds.Min.X, stubMinY, 0.0), new XYZ(bounds.Min.X, topY, 0.0));
        			val4 = Line.CreateBound(new XYZ(bounds.Max.X, stubMinY, 0.0), new XYZ(bounds.Max.X, topY, 0.0));
        		}
        		else
        		{
        			double stubMinX = rightX - num;
        			val3 = Line.CreateBound(new XYZ(stubMinX, bounds.Min.Y, 0.0), new XYZ(rightX, bounds.Min.Y, 0.0));
        			val4 = Line.CreateBound(new XYZ(stubMinX, bounds.Max.Y, 0.0), new XYZ(rightX, bounds.Max.Y, 0.0));
        		}
        		val = ((ItemFactoryBase)doc.Create).NewDetailCurve(legendView, (Curve)(object)val3);
        		val2 = ((ItemFactoryBase)doc.Create).NewDetailCurve(legendView, (Curve)(object)val4);
        		ApplyInvisibleLineStyle(doc, val);
        		ApplyInvisibleLineStyle(doc, val2);
        		if (val == null || val2 == null)
        		{
        			reason = "無法建立 " + axis + " fallback reference detail curves。";
        			SafeDeleteElement(doc, ((val != null) ? ((Element)val).Id : null) ?? ElementId.InvalidElementId);
        			SafeDeleteElement(doc, ((val2 != null) ? ((Element)val2).Id : null) ?? ElementId.InvalidElementId);
        			return false;
        		}
        		list.Add(((Element)val).Id);
        		list.Add(((Element)val2).Id);
        		Curve geometryCurve = ((CurveElement)val).GeometryCurve;
        		Reference val5 = ((geometryCurve != null) ? geometryCurve.Reference : null);
        		Curve geometryCurve2 = ((CurveElement)val2).GeometryCurve;
        		Reference val6 = ((geometryCurve2 != null) ? geometryCurve2.Reference : null);
        		if (val5 == null || val6 == null)
        		{
        			reason = "無法取得 " + axis + " fallback detail curve reference。";
        			SafeDeleteElement(doc, ((Element)val).Id);
        			SafeDeleteElement(doc, ((Element)val2).Id);
        			return false;
        		}
        		ReferenceArray val7 = new ReferenceArray();
        		val7.Append(val5);
        		val7.Append(val6);
        		Dimension val8 = ((ItemFactoryBase)doc.Create).NewDimension(legendView, CreateLegendDimensionLine(bounds, axis), val7);
        		if (val8 == null)
        		{
        			reason = "Revit 無法建立 " + axis + " fallback dimension。";
        			SafeDeleteElement(doc, ((Element)val).Id);
        			SafeDeleteElement(doc, ((Element)val2).Id);
        			return false;
        		}
        		ApplyDimensionType(val8, dimensionType);
        		result = DoorWindowLegendDimensionAxisResult.Created(((Element)val8).Id, "detail_curve_fallback", list);
        		return true;
        	}
        	catch (Exception ex)
        	{
        		reason = ex.Message;
        		SafeDeleteElement(doc, ((val != null) ? ((Element)val).Id : null) ?? ElementId.InvalidElementId);
        		SafeDeleteElement(doc, ((val2 != null) ? ((Element)val2).Id : null) ?? ElementId.InvalidElementId);
        		return false;
        	}
        }

        private Line CreateLegendDimensionLine(BoundingBoxXYZ bounds, string axis)
        {
        	//IL_0088: Unknown result type (might be due to invalid IL or missing references)
        	//IL_00a2: Unknown result type (might be due to invalid IL or missing references)
        	//IL_00ac: Expected O, but got Unknown
        	//IL_00ac: Expected O, but got Unknown
        	//IL_0038: Unknown result type (might be due to invalid IL or missing references)
        	//IL_0052: Unknown result type (might be due to invalid IL or missing references)
        	//IL_005c: Expected O, but got Unknown
        	//IL_005c: Expected O, but got Unknown
        	if (axis == "width")
        	{
        		double num = bounds.Max.Y + DimensionOffsetCm * CmToFeet;
        		return Line.CreateBound(new XYZ(bounds.Min.X, num, 0.0), new XYZ(bounds.Max.X, num, 0.0));
        	}
        	double num2 = bounds.Max.X + DimensionOffsetCm * CmToFeet;
        	return Line.CreateBound(new XYZ(num2, bounds.Min.Y, 0.0), new XYZ(num2, bounds.Max.Y, 0.0));
        }

        private void ApplyDimensionType(Dimension dimension, DimensionType dimensionType)
        {
        	if (dimension != null && dimensionType != null && (dimension.DimensionType == null || ((Element)dimension.DimensionType).Id.GetIdValue() != ((Element)dimensionType).Id.GetIdValue()))
        	{
        		((Element)dimension).ChangeTypeId(((Element)dimensionType).Id);
        	}
        }

        private void ApplyInvisibleLineStyle(Document doc, DetailCurve detailCurve)
        {
        	if (doc == null || detailCurve == null)
        	{
        		return;
        	}
        	try
        	{
        		GraphicsStyle val = TryFindInvisibleLineStyle(doc);
        		if (val != null)
        		{
        			((CurveElement)detailCurve).LineStyle = (Element)(object)val;
        		}
        	}
        	catch
        	{
        	}
        }

        private GraphicsStyle TryFindInvisibleLineStyle(Document doc)
        {
        	//IL_0029: Unknown result type (might be due to invalid IL or missing references)
        	//IL_002f: Expected O, but got Unknown
        	try
        	{
        		foreach (Category subCategory in doc.Settings.Categories.get_Item((BuiltInCategory)(-2000051)).SubCategories)
        		{
        			Category val = subCategory;
        			string text = val.Name ?? string.Empty;
        			if (text.IndexOf("Invisible", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("?梯?", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("Invisible", StringComparison.OrdinalIgnoreCase) >= 0)
        			{
        				return val.GetGraphicsStyle((GraphicsStyleType)1);
        			}
        		}
        	}
        	catch
        	{
        		return null;
        	}
        	return null;
        }

        private List<DoorWindowLegendExistingItem> CollectExistingDoorWindowLegendItems(Document doc, View legendView, string targetType, string layoutDirection, int maxPerLine)
        {
        	//IL_0119: Unknown result type (might be due to invalid IL or missing references)
        	List<DoorWindowLegendExistingItem> list = new List<DoorWindowLegendExistingItem>();
        	foreach (ElementId item in CollectLegendComponentIds(doc, legendView))
        	{
        		Element element = doc.GetElement(item);
        		FamilySymbol legendComponentFamilySymbol = GetLegendComponentFamilySymbol(doc, element);
        		if (FamilySymbolMatchesTargetType(legendComponentFamilySymbol, targetType))
        		{
        			BoundingBoxXYZ val = element.get_BoundingBox(legendView);
        			if (val != null)
        			{
        				DetailCurve val2 = FindNearestDoorWindowFflLine(doc, legendView, val, targetType);
        				bool flag = val2 != null;
        				double num = (flag ? GetDetailCurveLineY(val2) : val.Min.Y);
        				double sillHeightCm = ((targetType == "window") ? (Math.Round((val.Min.Y - num) / 0.0328083989501312 / 0.1) * 0.1) : 0.0);
        				XYZ anchor = (XYZ)((!(targetType == "window" && flag)) ? ((object)GetLegendPlacementAnchor(val, targetType)) : ((object)new XYZ((val.Min.X + val.Max.X) / 2.0, num, 0.0)));
        				list.Add(new DoorWindowLegendExistingItem
        				{
        					ComponentId = item,
        					TypeId = ((Element)legendComponentFamilySymbol).Id,
        					TypeMarkDisplay = GetTypeMark(legendComponentFamilySymbol),
        					TypeName = (((Element)legendComponentFamilySymbol).Name ?? string.Empty),
        					Bounds = val,
        					Anchor = anchor,
        					FflLineId = (flag ? ((Element)val2).Id : ElementId.InvalidElementId),
        					HasDetectedFfl = flag,
        					SillHeightCm = sillHeightCm,
        					Key = ((targetType == "window" && !flag) ? $"window:{((Element)legendComponentFamilySymbol).Id.GetIdValue()}|unknown" : BuildDoorWindowLegendItemKey(targetType, ((Element)legendComponentFamilySymbol).Id, sillHeightCm))
        				});
        			}
        		}
        	}
        	XYZ doorWindowLegendGridOrigin = GetDoorWindowLegendGridOrigin(list);
        	foreach (DoorWindowLegendExistingItem item2 in list)
        	{
        		item2.GridIndex = CalculateDoorWindowLegendGridIndex(item2.Anchor, doorWindowLegendGridOrigin, layoutDirection, maxPerLine);
        	}
        	return list.OrderBy((DoorWindowLegendExistingItem i) => i.GridIndex).ToList();
        }

        private DoorWindowLegendViewTargetCounts CountDoorWindowLegendComponentsByTargetType(Document doc, View legendView)
        {
        	DoorWindowLegendViewTargetCounts doorWindowLegendViewTargetCounts = new DoorWindowLegendViewTargetCounts();
        	if (doc == null || legendView == null)
        	{
        		return doorWindowLegendViewTargetCounts;
        	}
        	foreach (ElementId item in CollectLegendComponentIds(doc, legendView))
        	{
        		FamilySymbol legendComponentFamilySymbol = GetLegendComponentFamilySymbol(doc, doc.GetElement(item));
        		if (FamilySymbolMatchesTargetType(legendComponentFamilySymbol, "door"))
        		{
        			doorWindowLegendViewTargetCounts.DoorCount++;
        		}
        		else if (FamilySymbolMatchesTargetType(legendComponentFamilySymbol, "window"))
        		{
        			doorWindowLegendViewTargetCounts.WindowCount++;
        		}
        	}
        	return doorWindowLegendViewTargetCounts;
        }

        private FamilySymbol GetLegendComponentFamilySymbol(Document doc, Element component)
        {
        	try
        	{
        		Parameter obj = ((component != null) ? component.get_Parameter((BuiltInParameter)(-1133750)) : null);
        		ElementId val = ((obj != null) ? obj.AsElementId() : null);
        		object result;
        		if (!IsValidElementId(val))
        		{
        			result = null;
        		}
        		else
        		{
        			Element element = doc.GetElement(val);
        			result = ((element is FamilySymbol) ? element : null);
        		}
        		return (FamilySymbol)result;
        	}
        	catch
        	{
        		return null;
        	}
        }

        private bool FamilySymbolMatchesTargetType(FamilySymbol symbol, string targetType)
        {
        	//IL_0030: Unknown result type (might be due to invalid IL or missing references)
        	//IL_0023: Unknown result type (might be due to invalid IL or missing references)
        	//IL_0036: Expected O, but got Unknown
        	if (((symbol != null) ? ((Element)symbol).Category : null) == null)
        	{
        		return false;
        	}
        	ElementId id = ((targetType == "door") ? new ElementId((BuiltInCategory)(-2000023)) : new ElementId((BuiltInCategory)(-2000014)));
        	return ((Element)symbol).Category.Id.GetIdValue() == id.GetIdValue();
        }

        private string BuildDoorWindowLegendItemKey(string targetType, ElementId typeId, double sillHeightCm)
        {
        	if (!IsValidElementId(typeId))
        	{
        		return string.Empty;
        	}
        	if (targetType == "window")
        	{
        		double num = Math.Round(sillHeightCm / 0.1) * 0.1;
        		return $"{typeId.GetIdValue()}|{num:F1}";
        	}
        	return typeId.GetIdValue().ToString(CultureInfo.InvariantCulture);
        }

        private XYZ GetDoorWindowLegendGridOrigin(List<DoorWindowLegendExistingItem> items)
        {
        	//IL_0066: Unknown result type (might be due to invalid IL or missing references)
        	//IL_006c: Expected O, but got Unknown
        	if (items == null || items.Count == 0)
        	{
        		return XYZ.Zero;
        	}
        	double num = items.Min((DoorWindowLegendExistingItem i) => i.Anchor.X);
        	double num2 = items.Max((DoorWindowLegendExistingItem i) => i.Anchor.Y);
        	return new XYZ(num, num2, 0.0);
        }

        private int CalculateDoorWindowLegendGridIndex(XYZ anchor, XYZ origin, string layoutDirection, int maxPerLine)
        {
        	double num = 16.4041994750656;
        	double num2 = 16.4041994750656;
        	int num3 = Math.Max(0, (int)Math.Round((anchor.X - origin.X) / num));
        	int num4 = Math.Max(0, (int)Math.Round((origin.Y - anchor.Y) / num2));
        	if (layoutDirection == "horizontal")
        	{
        		return num4 * maxPerLine + num3;
        	}
        	return num3 * maxPerLine + num4;
        }

        private DetailCurve FindNearestDoorWindowFflLine(Document doc, View legendView, BoundingBoxXYZ bounds, string targetType)
        {
        	//IL_00a3: Unknown result type (might be due to invalid IL or missing references)
        	double width = bounds.Max.X - bounds.Min.X;
        	double centerX = (bounds.Min.X + bounds.Max.X) / 2.0;
        	double tolerance = 0.164041994750656;
        	double maxDistance = ((targetType == "window") ? 14.76377952755904 : 0.9842519685039359);
        	return (from x in (from x in ((IEnumerable)new FilteredElementCollector(doc, ((Element)legendView).Id).OfClass(typeof(CurveElement)).WhereElementIsNotElementType()).Cast<CurveElement>().OfType<DetailCurve>().Select(delegate(DetailCurve c)
        			{
        				Curve geometryCurve = ((CurveElement)c).GeometryCurve;
        				return new
        				{
        					Curve = c,
        					Line = (Line)(object)((geometryCurve is Line) ? geometryCurve : null)
        				};
        			})
        			where (GeometryObject)(object)x.Line != (GeometryObject)null
        			where Math.Abs(((Curve)x.Line).GetEndPoint(0).Y - ((Curve)x.Line).GetEndPoint(1).Y) <= 0.0001
        			select x).Where(x =>
        		{
        			double num = Math.Min(((Curve)x.Line).GetEndPoint(0).X, ((Curve)x.Line).GetEndPoint(1).X);
        			double num2 = Math.Max(((Curve)x.Line).GetEndPoint(0).X, ((Curve)x.Line).GetEndPoint(1).X);
        			double num3 = num2 - num;
        			double y = ((Curve)x.Line).GetEndPoint(0).Y;
        			return num <= centerX + tolerance && num2 >= centerX - tolerance && num3 >= width * 1.2 && y <= bounds.Min.Y + tolerance && Math.Abs(bounds.Min.Y - y) <= maxDistance;
        		})
        		orderby Math.Abs(bounds.Min.Y - ((Curve)x.Line).GetEndPoint(0).Y)
        		select x.Curve).FirstOrDefault();
        }

        private double GetDetailCurveLineY(DetailCurve curve)
        {
        	Curve obj = ((curve != null) ? ((CurveElement)curve).GeometryCurve : null);
        	Curve obj2 = ((obj is Line) ? obj : null);
        	if (obj2 == null)
        	{
        		return 0.0;
        	}
        	return obj2.GetEndPoint(0).Y;
        }

        private DimensionType InferDimensionTypeFromView(Document doc, View legendView)
        {
        	//IL_0007: Unknown result type (might be due to invalid IL or missing references)
        	try
        	{
        		Dimension obj = ((IEnumerable)new FilteredElementCollector(doc, ((Element)legendView).Id).OfClass(typeof(Dimension)).WhereElementIsNotElementType()).Cast<Dimension>().FirstOrDefault((Dimension d) => d.DimensionType != null);
        		return (obj != null) ? obj.DimensionType : null;
        	}
        	catch
        	{
        		return null;
        	}
        }

        private DoorWindowLegendDeleteResult DeleteDoorWindowLegendItemGroup(Document doc, View legendView, DoorWindowLegendExistingItem item, string targetType)
        {
        	//IL_00ae: Unknown result type (might be due to invalid IL or missing references)
        	//IL_00b4: Expected O, but got Unknown
        	//IL_01bc: Unknown result type (might be due to invalid IL or missing references)
        	//IL_00b5: Unknown result type (might be due to invalid IL or missing references)
        	//IL_0197: Unknown result type (might be due to invalid IL or missing references)
        	//IL_0168: Unknown result type (might be due to invalid IL or missing references)
        	DoorWindowLegendDeleteResult doorWindowLegendDeleteResult = new DoorWindowLegendDeleteResult();
        	List<ElementId> list = CollectDoorWindowLegendItemRelatedElementIds(doc, legendView, item, targetType);
        	HashSet<int> legendComponentIdValues = (from id in CollectLegendComponentIds(doc, legendView).Where(IsValidElementId)
        		select id.GetIdValue()).ToHashSet();
        	doorWindowLegendDeleteResult.DeleteElementIds = (from id in list.Where(IsValidElementId)
        		select id.GetIdValue()).ToList();
        	SubTransaction val = new SubTransaction(doc);
        	try
        	{
        		val.Start();
        		ICollection<ElementId> source = doc.Delete((ICollection<ElementId>)list);
        		List<int> deletedElementIds = (from id in source.Where(IsValidElementId)
        			select id.GetIdValue()).ToList();
        		List<int> list2 = (from id in source.Where(IsValidElementId)
        			where id.GetIdValue() != item.ComponentId.GetIdValue()
        			where legendComponentIdValues.Contains(id.GetIdValue())
        			select id.GetIdValue()).ToList();
        		if (list2.Count > 0)
        		{
        			val.RollBack();
        			doorWindowLegendDeleteResult.Success = false;
        			doorWindowLegendDeleteResult.FailureReason = "delete_would_remove_other_legend_components:" + string.Join(",", list2);
        			return doorWindowLegendDeleteResult;
        		}
        		val.Commit();
        		doorWindowLegendDeleteResult.Success = true;
        		doorWindowLegendDeleteResult.DeletedElementIds = deletedElementIds;
        		return doorWindowLegendDeleteResult;
        	}
        	catch (Exception ex)
        	{
        		try
        		{
        			if (val.HasStarted())
        			{
        				val.RollBack();
        			}
        		}
        		catch
        		{
        		}
        		doorWindowLegendDeleteResult.Success = false;
        		doorWindowLegendDeleteResult.FailureReason = ex.Message;
        		return doorWindowLegendDeleteResult;
        	}
        }

        private List<ElementId> CollectDoorWindowLegendItemRelatedElementIds(Document doc, View legendView, DoorWindowLegendExistingItem item, string targetType)
        {
        	//IL_00ff: Unknown result type (might be due to invalid IL or missing references)
        	HashSet<ElementId> hashSet = new HashSet<ElementId>(new ElementIdValueComparer());
        	if (IsValidElementId(item.ComponentId))
        	{
        		hashSet.Add(item.ComponentId);
        	}
        	BoundingBoxXYZ bounds = item.Bounds;
        	if (bounds == null)
        	{
        		return hashSet.ToList();
        	}
        	double val = bounds.Max.X - bounds.Min.X;
        	double num = (item.HasDetectedFfl ? item.Anchor.Y : bounds.Min.Y);
        	double num2 = bounds.Min.X - Math.Max(val, 3.28083989501312);
        	double num3 = bounds.Max.X + Math.Max(val, 3.9370078740157437);
        	double num4 = Math.Min(num, bounds.Min.Y) - 1.312335958005248;
        	double num5 = Math.Max(bounds.Max.Y + 1.64041994750656, num + 13.12335958005248) + 1.9685039370078719;
        	foreach (Element item2 in new FilteredElementCollector(doc, ((Element)legendView).Id).WhereElementIsNotElementType().ToElements())
        	{
        		if (item2 == null || !IsValidElementId(item2.Id) || hashSet.Contains(item2.Id) || IsLegendComponentElement(item2) || (!(item2 is TextNote) && !(item2 is Dimension) && !(item2 is DetailCurve)))
        		{
        			continue;
        		}
        		BoundingBoxXYZ val2 = item2.get_BoundingBox(legendView);
        		if (val2 != null)
        		{
        			XYZ val3 = (val2.Min + val2.Max) / 2.0;
        			if (val3.X >= num2 && val3.X <= num3 && val3.Y >= num4 && val3.Y <= num5)
        			{
        				hashSet.Add(item2.Id);
        			}
        		}
        	}
        	return hashSet.ToList();
        }

        private DoorWindowLegendTypeMarkSyncResult SyncDoorWindowLegendTypeMarkTextNote(Document doc, View legendView, DoorWindowLegendExistingItem item, string targetType)
        {
        	DoorWindowLegendTypeMarkSyncResult doorWindowLegendTypeMarkSyncResult = new DoorWindowLegendTypeMarkSyncResult
        	{
        		CurrentTypeMark = (item?.TypeMarkDisplay ?? string.Empty),
        		Action = "skip"
        	};
        	if (doc == null || legendView == null || item == null)
        	{
        		doorWindowLegendTypeMarkSyncResult.SkipReason = "invalid_sync_context";
        		return doorWindowLegendTypeMarkSyncResult;
        	}
        	string failureReason;
        	TextNote val = FindDoorWindowLegendTypeMarkTextNote(doc, legendView, item, targetType, out failureReason);
        	if (val == null)
        	{
        		doorWindowLegendTypeMarkSyncResult.SkipReason = failureReason;
        		return doorWindowLegendTypeMarkSyncResult;
        	}
        	doorWindowLegendTypeMarkSyncResult.TextNoteId = ((Element)val).Id;
        	doorWindowLegendTypeMarkSyncResult.ExistingText = ((TextElement)val).Text ?? string.Empty;
        	string text = doorWindowLegendTypeMarkSyncResult.CurrentTypeMark ?? string.Empty;
        	if (doorWindowLegendTypeMarkSyncResult.ExistingText == text)
        	{
        		doorWindowLegendTypeMarkSyncResult.Action = "unchanged";
        		return doorWindowLegendTypeMarkSyncResult;
        	}
        	((TextElement)val).Text = text;
        	doorWindowLegendTypeMarkSyncResult.Action = "updated";
        	return doorWindowLegendTypeMarkSyncResult;
        }

        private TextNote FindDoorWindowLegendTypeMarkTextNote(Document doc, View legendView, DoorWindowLegendExistingItem item, string targetType, out string failureReason)
        {
        	//IL_0184: Unknown result type (might be due to invalid IL or missing references)
        	//IL_0060: Unknown result type (might be due to invalid IL or missing references)
        	failureReason = null;
        	if (doc == null || legendView == null || item == null)
        	{
        		failureReason = "invalid_find_context";
        		return null;
        	}
        	string b = item.ComponentId.GetIdValue().ToString(CultureInfo.InvariantCulture);
        	string b2 = item.TypeId.GetIdValue().ToString(CultureInfo.InvariantCulture);
        	Schema doorWindowLegendTextMetadataSchema = GetDoorWindowLegendTextMetadataSchema(createIfMissing: false);
        	if (doorWindowLegendTextMetadataSchema != null)
        	{
        		foreach (TextNote item2 in ((IEnumerable)new FilteredElementCollector(doc, ((Element)legendView).Id).OfClass(typeof(TextNote))).Cast<TextNote>())
        		{
        			Dictionary<string, string> dictionary = ReadDoorWindowLegendTextMetadata((Element)(object)item2, doorWindowLegendTextMetadataSchema);
        			if (dictionary != null)
        			{
        				dictionary.TryGetValue("Role", out var value);
        				dictionary.TryGetValue("TargetType", out var value2);
        				dictionary.TryGetValue("ComponentId", out var value3);
        				dictionary.TryGetValue("TypeId", out var value4);
        				dictionary.TryGetValue("ItemKey", out var value5);
        				bool num = string.Equals(value, "type_mark", StringComparison.OrdinalIgnoreCase);
        				bool flag = string.Equals(value2, targetType, StringComparison.OrdinalIgnoreCase);
        				bool flag2 = string.Equals(value3, b, StringComparison.OrdinalIgnoreCase);
        				bool flag3 = string.Equals(value4, b2, StringComparison.OrdinalIgnoreCase) && string.Equals(value5, item.Key, StringComparison.OrdinalIgnoreCase);
        				if (num && flag && (flag2 || flag3))
        				{
        					return item2;
        				}
        			}
        		}
        	}
        	List<XYZ> source = BuildDoorWindowLegendTypeMarkExpectedPoints(item);
        	double num2 = 3.9370078740157437;
        	TextNote val = null;
        	double num3 = double.MaxValue;
        	foreach (TextNote item3 in ((IEnumerable)new FilteredElementCollector(doc, ((Element)legendView).Id).OfClass(typeof(TextNote))).Cast<TextNote>())
        	{
        		if (string.Equals((((TextElement)item3).Text ?? string.Empty).Trim(), "FFL", StringComparison.OrdinalIgnoreCase))
        		{
        			continue;
        		}
        		XYZ notePoint = GetElementCenter((Element)(object)item3, legendView);
        		if (notePoint != null)
        		{
        			double num4 = source.Select((XYZ point) => point.DistanceTo(notePoint)).DefaultIfEmpty(double.MaxValue).Min();
        			if (num4 < num3)
        			{
        				num3 = num4;
        				val = item3;
        			}
        		}
        	}
        	if (val != null && num3 <= num2)
        	{
        		return val;
        	}
        	failureReason = ((val == null) ? "type_mark_text_note_not_found" : $"type_mark_text_note_too_far:{num3 / 0.0328083989501312:F1}cm");
        	return null;
        }

        private List<XYZ> BuildDoorWindowLegendTypeMarkExpectedPoints(DoorWindowLegendExistingItem item)
        {
        	//IL_003d: Unknown result type (might be due to invalid IL or missing references)
        	//IL_0047: Expected O, but got Unknown
        	//IL_00ba: Unknown result type (might be due to invalid IL or missing references)
        	//IL_00c4: Expected O, but got Unknown
        	List<XYZ> list = new List<XYZ>();
        	if (item == null)
        	{
        		return list;
        	}
        	if (item.Anchor != null)
        	{
        		list.Add(new XYZ(item.Anchor.X, item.Anchor.Y + 13.12335958005248, 0.0));
        	}
        	if (item.Bounds != null)
        	{
        		double num = (item.Bounds.Min.X + item.Bounds.Max.X) / 2.0;
        		double num2 = Math.Max(35.0, 130.0);
        		list.Add(new XYZ(num, item.Bounds.Max.Y + num2 * 0.0328083989501312, 0.0));
        	}
        	return list;
        }

        private XYZ GetElementCenter(Element element, View view)
        {
        	//IL_0076: Unknown result type (might be due to invalid IL or missing references)
        	//IL_007c: Expected O, but got Unknown
        	BoundingBoxXYZ val = ((element != null) ? element.get_BoundingBox(view) : null);
        	if (val == null)
        	{
        		return null;
        	}
        	return new XYZ((val.Min.X + val.Max.X) / 2.0, (val.Min.Y + val.Max.Y) / 2.0, (val.Min.Z + val.Max.Z) / 2.0);
        }

        private void SetDoorWindowLegendTextMetadata(Document doc, ElementId textNoteId, string role, string targetType, ElementId componentId, ElementId typeId, string itemKey)
        {
        	//IL_002b: Unknown result type (might be due to invalid IL or missing references)
        	//IL_0031: Expected O, but got Unknown
        	if (doc == null || !IsValidElementId(textNoteId))
        	{
        		return;
        	}
        	Element element = doc.GetElement(textNoteId);
        	if (element == null)
        	{
        		return;
        	}
        	try
        	{
        		Schema doorWindowLegendTextMetadataSchema = GetDoorWindowLegendTextMetadataSchema(createIfMissing: true);
        		if (doorWindowLegendTextMetadataSchema != null)
        		{
        			Entity entity = new Entity(doorWindowLegendTextMetadataSchema);
        			SetSchemaString(entity, doorWindowLegendTextMetadataSchema, "Tool", "door-window-legend");
        			SetSchemaString(entity, doorWindowLegendTextMetadataSchema, "Role", role ?? string.Empty);
        			SetSchemaString(entity, doorWindowLegendTextMetadataSchema, "TargetType", targetType ?? string.Empty);
        			SetSchemaString(entity, doorWindowLegendTextMetadataSchema, "ComponentId", IsValidElementId(componentId) ? componentId.GetIdValue().ToString(CultureInfo.InvariantCulture) : string.Empty);
        			SetSchemaString(entity, doorWindowLegendTextMetadataSchema, "TypeId", IsValidElementId(typeId) ? typeId.GetIdValue().ToString(CultureInfo.InvariantCulture) : string.Empty);
        			SetSchemaString(entity, doorWindowLegendTextMetadataSchema, "ItemKey", itemKey ?? string.Empty);
        			element.SetEntity(entity);
        		}
        	}
        	catch (Exception ex)
        	{
        		Logger.Info($"door-window-legend metadata write skipped. elementId={SafeGetElementIdValue(textNoteId)}, reason={ex.Message}");
        	}
        }

        private Schema GetDoorWindowLegendTextMetadataSchema(bool createIfMissing)
        {
        	//IL_0018: Unknown result type (might be due to invalid IL or missing references)
        	//IL_001d: Unknown result type (might be due to invalid IL or missing references)
        	//IL_0029: Unknown result type (might be due to invalid IL or missing references)
        	//IL_0031: Unknown result type (might be due to invalid IL or missing references)
        	//IL_0039: Unknown result type (might be due to invalid IL or missing references)
        	//IL_0045: Unknown result type (might be due to invalid IL or missing references)
        	//IL_005b: Unknown result type (might be due to invalid IL or missing references)
        	//IL_0071: Unknown result type (might be due to invalid IL or missing references)
        	//IL_0087: Unknown result type (might be due to invalid IL or missing references)
        	//IL_009d: Unknown result type (might be due to invalid IL or missing references)
        	//IL_00b3: Unknown result type (might be due to invalid IL or missing references)
        	Schema val = Schema.Lookup(DoorWindowLegendTextMetadataSchemaGuid);
        	if (val != null || !createIfMissing)
        	{
        		return val;
        	}
        	SchemaBuilder val2 = new SchemaBuilder(DoorWindowLegendTextMetadataSchemaGuid);
        	val2.SetSchemaName("DoorWindowLegendTextMetadata");
        	val2.SetReadAccessLevel((AccessLevel)1);
        	val2.SetWriteAccessLevel((AccessLevel)1);
        	val2.SetVendorId("RMCP");
        	val2.AddSimpleField("Tool", typeof(string));
        	val2.AddSimpleField("Role", typeof(string));
        	val2.AddSimpleField("TargetType", typeof(string));
        	val2.AddSimpleField("ComponentId", typeof(string));
        	val2.AddSimpleField("TypeId", typeof(string));
        	val2.AddSimpleField("ItemKey", typeof(string));
        	return val2.Finish();
        }

        private Dictionary<string, string> ReadDoorWindowLegendTextMetadata(Element element, Schema schema)
        {
        	try
        	{
        		Entity entity = element.GetEntity(schema);
        		if (!entity.IsValid())
        		{
        			return null;
        		}
        		string schemaString = GetSchemaString(entity, schema, "Tool");
        		if (!string.Equals(schemaString, "door-window-legend", StringComparison.OrdinalIgnoreCase))
        		{
        			return null;
        		}
        		return new Dictionary<string, string>
        		{
        			{ "Tool", schemaString },
        			{
        				"Role",
        				GetSchemaString(entity, schema, "Role")
        			},
        			{
        				"TargetType",
        				GetSchemaString(entity, schema, "TargetType")
        			},
        			{
        				"ComponentId",
        				GetSchemaString(entity, schema, "ComponentId")
        			},
        			{
        				"TypeId",
        				GetSchemaString(entity, schema, "TypeId")
        			},
        			{
        				"ItemKey",
        				GetSchemaString(entity, schema, "ItemKey")
        			}
        		};
        	}
        	catch
        	{
        		return null;
        	}
        }

        private void SetSchemaString(Entity entity, Schema schema, string fieldName, string value)
        {
        	Field field = schema.GetField(fieldName);
        	if (field != null)
        	{
        		entity.Set<string>(field, value ?? string.Empty);
        	}
        }

        private string GetSchemaString(Entity entity, Schema schema, string fieldName)
        {
        	Field field = schema.GetField(fieldName);
        	string text;
        	if (field != null)
        	{
        		text = entity.Get<string>(field);
        		if (text == null)
        		{
        			return string.Empty;
        		}
        	}
        	else
        	{
        		text = string.Empty;
        	}
        	return text;
        }

        private XYZ GetLegendPlacementAnchor(BoundingBoxXYZ bounds, string targetType)
        {
        	//IL_0058: Unknown result type (might be due to invalid IL or missing references)
        	//IL_005e: Expected O, but got Unknown
        	//IL_0095: Unknown result type (might be due to invalid IL or missing references)
        	//IL_009b: Expected O, but got Unknown
        	if (bounds == null)
        	{
        		return XYZ.Zero;
        	}
        	if (!(targetType == "door") && !(targetType == "window"))
        	{
        		double num = (bounds.Min.Y + bounds.Max.Y) / 2.0;
        		return new XYZ(bounds.Min.X, num, 0.0);
        	}
        	return new XYZ((bounds.Min.X + bounds.Max.X) / 2.0, bounds.Min.Y, 0.0);
        }

        private XYZ GetTargetAnchor(XYZ seedOrigin, string layoutDirection, int maxPerLine, int index)
        {
        	//IL_006f: Unknown result type (might be due to invalid IL or missing references)
        	//IL_0075: Expected O, but got Unknown
        	//IL_004a: Unknown result type (might be due to invalid IL or missing references)
        	//IL_0050: Expected O, but got Unknown
        	int num = index % maxPerLine;
        	int num2 = index / maxPerLine;
        	double num3 = 16.4041994750656;
        	double num4 = 16.4041994750656;
        	if (!(layoutDirection == "horizontal"))
        	{
        		return new XYZ(seedOrigin.X + (double)num2 * num3, seedOrigin.Y - (double)num * num4, 0.0);
        	}
        	return new XYZ(seedOrigin.X + (double)num * num3, seedOrigin.Y - (double)num2 * num4, 0.0);
        }

        private string GetDoorWindowDisplayName(string targetType)
        {
        	if (!(targetType == "door"))
        	{
        		return "窗";
        	}
        	return "門";
        }

        private string GetDoorWindowDisplayNameFromType(Document doc, DoorWindowLegendTypeInfo type)
        {
        	//IL_0042: Unknown result type (might be due to invalid IL or missing references)
        	//IL_004c: Expected O, but got Unknown
        	//IL_006a: Unknown result type (might be due to invalid IL or missing references)
        	//IL_0074: Expected O, but got Unknown
        	Element element = doc.GetElement(type?.TypeId);
        	FamilySymbol val = (FamilySymbol)(object)((element is FamilySymbol) ? element : null);
        	if (((val != null) ? ((Element)val).Category : null) == null)
        	{
        		return "門窗";
        	}
        	if (((Element)val).Category.Id.GetIdValue() == RevitCompatibility.GetIdValue(new ElementId((BuiltInCategory)(-2000023))))
        	{
        		return "?\u0080";
        	}
        	if (((Element)val).Category.Id.GetIdValue() == RevitCompatibility.GetIdValue(new ElementId((BuiltInCategory)(-2000014))))
        	{
        		return "窗";
        	}
        	return "門窗";
        }

        private string GetTypeMark(FamilySymbol symbol)
        {
        	Parameter obj = ((Element)symbol).get_Parameter((BuiltInParameter)(-1001405));
        	return ((obj != null) ? obj.AsString() : null) ?? string.Empty;
        }

        private DoorWindowLegendSillHeightInfo GetWindowSillHeightInfo(Element instance, FamilySymbol symbol)
        {
        	if (TryGetWindowSillHeightFromElement(instance, out var sillHeightFeet))
        	{
        		return new DoorWindowLegendSillHeightInfo
        		{
        			SillHeightFeet = sillHeightFeet,
        			SillHeightCm = sillHeightFeet / 0.0328083989501312,
        			Source = "instance"
        		};
        	}
        	if (TryGetWindowSillHeightFromElement((Element)(object)symbol, out sillHeightFeet))
        	{
        		return new DoorWindowLegendSillHeightInfo
        		{
        			SillHeightFeet = sillHeightFeet,
        			SillHeightCm = sillHeightFeet / 0.0328083989501312,
        			Source = "type"
        		};
        	}
        	return new DoorWindowLegendSillHeightInfo
        	{
        		SillHeightFeet = 0.0,
        		SillHeightCm = 0.0,
        		Source = "missing_default_zero",
        		FailureReason = "找不到窗台高參數，使用 0cm。"
        	};
        }

        private bool TryGetWindowSillHeightFromElement(Element element, out double sillHeightFeet)
        {
        	sillHeightFeet = 0.0;
        	if (element == null)
        	{
        		return false;
        	}
        	try
        	{
        		Parameter parameter = element.get_Parameter((BuiltInParameter)(-1001361));
        		if (TryReadDoubleParameter(parameter, out sillHeightFeet))
        		{
        			return true;
        		}
        	}
        	catch
        	{
        	}
        	string[] array = new string[3] { "Sill Height", "窗台高", "窗台高度" };
        	foreach (string text in array)
        	{
        		try
        		{
        			Parameter parameter2 = element.LookupParameter(text);
        			if (TryReadDoubleParameter(parameter2, out sillHeightFeet))
        			{
        				return true;
        			}
        		}
        		catch
        		{
        		}
        	}
        	return false;
        }

        private bool TryReadDoubleParameter(Parameter parameter, out double value)
        {
        	//IL_000f: Unknown result type (might be due to invalid IL or missing references)
        	//IL_0015: Invalid comparison between Unknown and I4
        	value = 0.0;
        	if (parameter == null || (int)parameter.StorageType != 2 || !parameter.HasValue)
        	{
        		return false;
        	}
        	value = parameter.AsDouble();
        	return true;
        }

        private string NormalizeTypeMarkForSort(string typeMark)
        {
        	if (!string.IsNullOrWhiteSpace(typeMark))
        	{
        		return typeMark.Trim();
        	}
        	return string.Empty;
        }

    }
}
