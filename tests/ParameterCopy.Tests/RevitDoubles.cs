// Small API doubles for exercising the actual command outside Revit.
// These tests verify command decisions and transaction calls, not Autodesk runtime behavior.
using System.Collections;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

namespace RevitMCP.Core
{
    public partial class CommandExecutor
    {
        private readonly TestApplication _uiApp;
        public CommandExecutor(Document doc) => _uiApp = new TestApplication { ActiveUIDocument = new TestUIDocument { Document = doc } };
        public object Run(JObject args) => CopyParameterByGroup(args);
    }
    public sealed class TestApplication { public TestUIDocument? ActiveUIDocument { get; set; } }
    public sealed class TestUIDocument { public Document Document { get; set; } = null!; }
}

namespace Autodesk.Revit.DB
{
    public enum StorageType { None, Integer, Double, String, ElementId }
    public enum BuiltInCategory { OST_Walls, OST_StructuralColumns, OST_StructuralFraming, OST_Floors, OST_Doors, OST_Windows }
    public enum TransactionStatus { Uninitialized, Started, Committed, RolledBack }
    public enum FailureSeverity { Warning, Error }
    public enum FailureProcessingResult { Continue, ProceedWithRollBack }
    public sealed class ForgeTypeId(string typeId)
    {
        public string TypeId { get; } = typeId;
        public override bool Equals(object? other) => other is ForgeTypeId id && id.TypeId == TypeId;
        public override int GetHashCode() => TypeId.GetHashCode();
    }
    public static class GroupTypeId
    {
        public static ForgeTypeId Constraints => new("autodesk.parameter.group:constraints-1.0.0");
        public static ForgeTypeId Text => new("autodesk.parameter.group:text-1.0.0");
    }
    public sealed class ElementId(long value)
    {
        public long Value { get; } = value;
        public static ElementId InvalidElementId { get; } = new(-1);
        public override bool Equals(object? other) => other is ElementId id && id.Value == Value;
        public override int GetHashCode() => Value.GetHashCode();
        public static bool operator ==(ElementId? a, ElementId? b) => Equals(a, b);
        public static bool operator !=(ElementId? a, ElementId? b) => !Equals(a, b);
    }
    public sealed class Definition(string name, ForgeTypeId group, string dataType)
    {
        public string Name { get; } = name;
        public ForgeTypeId GetGroupTypeId() => group;
        public ForgeTypeId GetDataType() => new(dataType);
    }
    public sealed class Parameter
    {
        public Definition Definition { get; init; } = null!;
        public StorageType StorageType { get; init; }
        public object? Value { get; set; }
        public bool HasValue => Value != null;
        public bool IsReadOnly { get; set; }
        public bool ThrowOnSet { get; set; }
        public bool ReturnFalseOnSet { get; set; }
        public string? DisplayValue { get; set; }
        public string? AsValueString() => DisplayValue;
        public string? AsString() => (string?)Value;
        public int AsInteger() => (int)Value!;
        public double AsDouble() => (double)Value!;
        public ElementId AsElementId() => (ElementId)Value!;
        private bool Write(object value)
        {
            if (IsReadOnly || ThrowOnSet) throw new InvalidOperationException("Injected set failure.");
            if (ReturnFalseOnSet) return false;
            Value = value;
            return true;
        }
        public bool Set(string value) => Write(value);
        public bool Set(int value) => Write(value);
        public bool Set(double value) => Write(value);
        public bool Set(ElementId value) => Write(value);
    }
    public sealed class Element
    {
        public ElementId Id { get; init; } = null!;
        public BuiltInCategory Category { get; init; } = BuiltInCategory.OST_Walls;
        public bool IsType { get; init; }
        public List<Parameter> Parameters { get; } = [];
        public IList<Parameter> GetParameters(string name) => Parameters.Where(p => p.Definition.Name == name).ToList();
    }
    public sealed class Document
    {
        public bool IsFamilyDocument { get; set; }
        public List<Element> Elements { get; } = [];
        public int TransactionsStarted { get; set; }
        public int Rollbacks { get; set; }
        public bool RejectCommit { get; set; }
        public Action? OnRegenerate { get; set; }
        public void Regenerate() => OnRegenerate?.Invoke();
    }
    public sealed class FilteredElementCollector(Document doc) : IEnumerable<Element>
    {
        private IEnumerable<Element> query = doc.Elements;
        public FilteredElementCollector OfCategory(BuiltInCategory category) { query = query.Where(e => e.Category == category); return this; }
        public FilteredElementCollector WhereElementIsNotElementType() { query = query.Where(e => !e.IsType); return this; }
        public IEnumerator<Element> GetEnumerator() => query.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    public interface IFailuresPreprocessor { FailureProcessingResult PreprocessFailures(FailuresAccessor accessor); }
    public sealed class FailureMessageAccessor { public FailureSeverity GetSeverity() => FailureSeverity.Error; }
    public sealed class FailuresAccessor { public IList<FailureMessageAccessor> GetFailureMessages() => []; }
    public sealed class FailureHandlingOptions
    {
        public FailureHandlingOptions SetFailuresPreprocessor(IFailuresPreprocessor value) => this;
        public FailureHandlingOptions SetClearAfterRollback(bool value) => this;
        public FailureHandlingOptions SetForcedModalHandling(bool value) => this;
    }
    public sealed class Transaction(Document doc, string name) : IDisposable
    {
        private TransactionStatus status;
        private List<(Parameter Parameter, object? Value)> snapshot = [];
        public TransactionStatus Start()
        {
            doc.TransactionsStarted++;
            snapshot = doc.Elements.SelectMany(e => e.Parameters).Select(p => (p, p.Value)).ToList();
            return status = TransactionStatus.Started;
        }
        public TransactionStatus GetStatus() => status;
        public FailureHandlingOptions GetFailureHandlingOptions() => new();
        public void SetFailureHandlingOptions(FailureHandlingOptions options) { }
        public TransactionStatus Commit() => doc.RejectCommit ? RollBack() : status = TransactionStatus.Committed;
        public TransactionStatus RollBack()
        {
            foreach (var (parameter, value) in snapshot) parameter.Value = value;
            doc.Rollbacks++;
            return status = TransactionStatus.RolledBack;
        }
        public void Dispose() { if (status == TransactionStatus.Started) RollBack(); }
    }
}
