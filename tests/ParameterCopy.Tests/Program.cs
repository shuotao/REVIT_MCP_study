using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitMCP.Core;

static Parameter P(object? value, ForgeTypeId group, StorageType storage = StorageType.String, string? name = null, string dataType = "text") =>
    new() { Definition = new(name ?? (group.Equals(GroupTypeId.Constraints) ? "頂部約束" : "牆頂至"), group, dataType), Value = value, StorageType = storage };
static (Document Doc, Parameter Source, Parameter Target) Fixture(object? source = null, object? target = null,
    StorageType storage = StorageType.String)
{
    var doc = new Document();
    var e = new Element { Id = new ElementId(5_000_000_001) };
    var s = P(source ?? "NEW", GroupTypeId.Constraints, storage);
    var t = P(target ?? "OLD", GroupTypeId.Text, storage);
    e.Parameters.AddRange([s, t]); doc.Elements.Add(e);
    return (doc, s, t);
}
static JObject Run(Document doc, JObject? args = null) {
    args ??= new(); if (args["valueTransform"] == null) args["valueTransform"] = "raw";
    return JObject.FromObject(new CommandExecutor(doc).Run(args));
}
static JObject Apply() => new() { ["apply"] = true, ["dryRun"] = false };
static void Eq(object? expected, object? actual) { if (!Equals(expected, actual)) throw new Exception($"Expected {expected ?? "null"}, got {actual ?? "null"}."); }
static void Counts(JObject result, int scanned, int planned, int updated, int unchanged, int skipped, int failed)
{
    foreach (var pair in new[] { ("Scanned", scanned), ("Planned", planned), ("Updated", updated), ("Unchanged", unchanged), ("Skipped", skipped), ("Failed", failed) })
        Eq(pair.Item2, result[pair.Item1]!.Value<int>());
}
var cases = new List<(string Name, Action Test)>
{
    ("door and window sill lengths copy in one transaction, including zero", () => {
        var doc = new Document();
        var targets = new List<Parameter>();
        foreach (var pair in new[] { (BuiltInCategory.OST_Doors, 0.0), (BuiltInCategory.OST_Windows, 3.25) }) {
            var element = new Element { Id = new(targets.Count + 1), Category = pair.Item1 };
            var target = P(null, GroupTypeId.Text, StorageType.Double, "RC台度", "length");
            element.Parameters.AddRange([P(pair.Item2, GroupTypeId.Constraints, StorageType.Double, "窗台高度", "length"), target]);
            targets.Add(target); doc.Elements.Add(element);
        }
        var args = Apply(); args["categories"] = new JArray("Doors", "Windows");
        args["sourceParameterName"] = "窗台高度"; args["targetParameterName"] = "RC台度";
        Counts(Run(doc, args), 2, 2, 2, 0, 0, 0);
        Eq(0.0, targets[0].Value); Eq(3.25, targets[1].Value); Eq(1, doc.TransactionsStarted);
        Counts(Run(doc, args), 2, 0, 0, 2, 0, 0); Eq(1, doc.TransactionsStarted);
    }),
    ("default afterColon uses ElementId display text and writes only text", () => {
        var f = Fixture(new ElementId(42), "OLD", StorageType.ElementId);
        f.Doc.Elements[0].Parameters.Remove(f.Target);
        var target = P("OLD", GroupTypeId.Text); f.Doc.Elements[0].Parameters.Add(target);
        f.Source.DisplayValue = "樓層: RF:上層 ";
        var r = JObject.FromObject(new CommandExecutor(f.Doc).Run(new()));
        Counts(r, 1, 1, 0, 0, 0, 0); Eq("RF:上層", r["Results"]![0]!["NewValue"]!.Value<string>());
        Eq("OLD", target.Value);
        var args = Apply(); args["valueTransform"] = "afterColon";
        Counts(Run(f.Doc, args), 1, 1, 1, 0, 0, 0); Eq("RF:上層", target.Value);
        Counts(Run(f.Doc, args), 1, 0, 0, 1, 0, 0);
    }),
    ("afterColon handles full-width delimiter and skips missing or empty suffix", () => {
        foreach (var pair in new[] { ("樓層： B1F ", ""), ("RF", "MissingDelimiter"), ("樓層:  ", "EmptyTransformedSource"), (" ", "EmptySource") }) {
            var f = Fixture(pair.Item1, "OLD");
            var args = Apply(); args["valueTransform"] = "afterColon";
            var r = Run(f.Doc, args);
            if (pair.Item2 == "") { Counts(r, 1, 1, 1, 0, 0, 0); Eq("B1F", f.Target.Value); }
            else { Counts(r, 1, 0, 0, 0, 1, 0); Eq(pair.Item2, r["Results"]![0]!["Reason"]!.Value<string>()); Eq("OLD", f.Target.Value); }
        }
        var numeric = Fixture(1.0, 2.0, StorageType.Double);
        Counts(Run(numeric.Doc, new() { ["valueTransform"] = "afterColon" }), 1, 0, 0, 0, 1, 0);
    }),
    ("afterColon transaction rolls back all converted values on failure", () => {
        var f = Fixture("樓層:RF", "OLD"); var e = new Element { Id = new(2) };
        var target = P("SECOND", GroupTypeId.Text); target.ThrowOnSet = true;
        e.Parameters.AddRange([P("樓層:B1F", GroupTypeId.Constraints), target]); f.Doc.Elements.Add(e);
        var args = Apply(); args["valueTransform"] = "afterColon";
        Counts(Run(f.Doc, args), 2, 2, 0, 0, 0, 2); Eq("OLD", f.Target.Value); Eq(1, f.Doc.Rollbacks);
    }),
    ("defaults preview and exact group matching despite parameter order", () => {
        var f = Fixture();
        f.Doc.Elements[0].Parameters.Reverse();
        var r = Run(f.Doc);
        Counts(r, 1, 1, 0, 0, 0, 0); Eq(0, f.Doc.TransactionsStarted); Eq("OLD", f.Target.Value);
        Eq("OLD", r["Results"]![0]!["OldValue"]!.Value<string>());
        Eq("NEW", r["Results"]![0]!["NewValue"]!.Value<string>());
        Eq(5_000_000_001L, r["Results"]![0]!["ElementId"]!.Value<long>());
    }),
    ("same-name parameters are selected by exact group", () => {
        var doc = new Document(); var e = new Element { Id = new(1) };
        var source = P("NEW", GroupTypeId.Constraints, name: "編號");
        var target = P("OLD", GroupTypeId.Text, name: "編號");
        e.Parameters.AddRange([target, source]); doc.Elements.Add(e);
        var args = Apply(); args["sourceParameterName"] = "編號"; args["targetParameterName"] = "編號";
        Counts(Run(doc, args), 1, 1, 1, 0, 0, 0); Eq("NEW", target.Value); Eq("NEW", source.Value);
    }),
    ("wall offset defaults copy zero length and exclude floors without string conversion", () => {
        var doc = new Document(); var wall = new Element { Id = new(1) };
        var source = P(0.0, GroupTypeId.Constraints, StorageType.Double, dataType: "length");
        var target = P(-2.5, GroupTypeId.Text, StorageType.Double, dataType: "length");
        wall.Parameters.AddRange([source, target]); doc.Elements.Add(wall);
        var floor = new Element { Id = new(2), Category = BuiltInCategory.OST_Floors };
        floor.Parameters.AddRange([P(1.0, GroupTypeId.Constraints, StorageType.Double, dataType: "length"),
            P(2.0, GroupTypeId.Text, StorageType.Double, dataType: "length")]); doc.Elements.Add(floor);
        var r = Run(doc); Counts(r, 1, 1, 0, 0, 0, 0);
        Eq("頂部約束", r["SourceParameterName"]!.Value<string>());
        Eq("牆頂至", r["TargetParameterName"]!.Value<string>());
        Counts(Run(doc, Apply()), 1, 1, 1, 0, 0, 0); Eq(0.0, target.Value);
        wall.Parameters.Remove(target); var textTarget = P("keep me", GroupTypeId.Text); wall.Parameters.Add(textTarget);
        r = Run(doc, Apply()); Counts(r, 1, 0, 0, 0, 1, 0);
        Eq("IncompatibleStorageType", r["Results"]![0]!["Reason"]!.Value<string>()); Eq("keep me", textTarget.Value);
    }),
    ("all flag combinations require explicit apply true and dryRun false", () => {
        foreach (var args in new JObject[] { new(), new() { ["apply"] = true }, new() { ["dryRun"] = false },
            new() { ["apply"] = false, ["dryRun"] = false }, new() { ["apply"] = true, ["dryRun"] = true }, Apply() })
        {
            var f = Fixture(); var r = Run(f.Doc, args);
            bool write = args["apply"]?.Value<bool>() == true && args["dryRun"]?.Value<bool>() == false;
            Eq(write ? "NEW" : "OLD", f.Target.Value); Eq(write ? 1 : 0, f.Doc.TransactionsStarted);
            Counts(r, 1, 1, write ? 1 : 0, 0, 0, 0);
        }
    }),
    ("empty and whitespace sources skipped; zero remains valid", () => {
        foreach (var empty in new object?[] { null, "", "  " }) {
            var f = Fixture(); f.Source.Value = empty; var r = Run(f.Doc, Apply());
            Counts(r, 1, 0, 0, 0, 1, 0); Eq("EmptySource", r["Results"]![0]!["Reason"]!.Value<string>());
        }
        var zero = Fixture(0, 1, StorageType.Integer); Run(zero.Doc, Apply()); Eq(0, zero.Target.Value);
    }),
    ("missing source, missing target and ambiguous parameters are skipped", () => {
        foreach (bool source in new[] { true, false }) {
            var f = Fixture(); f.Doc.Elements[0].Parameters.Remove(source ? f.Source : f.Target);
            var r = Run(f.Doc, Apply()); Counts(r, 1, 0, 0, 0, 1, 0);
            Eq(source ? "MissingSourceParameter" : "MissingTargetParameter", r["Results"]![0]!["Reason"]!.Value<string>());
            var duplicate = Fixture(); duplicate.Doc.Elements[0].Parameters.Add(source ? duplicate.Source : duplicate.Target);
            r = Run(duplicate.Doc, Apply()); Counts(r, 1, 0, 0, 0, 1, 0);
            Eq(source ? "AmbiguousSourceParameter" : "AmbiguousTargetParameter", r["Results"]![0]!["Reason"]!.Value<string>());
        }
    }),
    ("read-only target is reported without opening a transaction", () => {
        var f = Fixture(); f.Target.IsReadOnly = true; var r = Run(f.Doc, Apply());
        Counts(r, 1, 0, 0, 0, 1, 0); Eq("ReadOnlyTarget", r["Results"]![0]!["Reason"]!.Value<string>()); Eq(0, f.Doc.TransactionsStarted);
    }),
    ("storage and data-type mismatches are skipped", () => {
        foreach (bool storageMismatch in new[] { true, false }) {
            var f = Fixture(); f.Doc.Elements[0].Parameters.Remove(f.Target);
            f.Doc.Elements[0].Parameters.Add(P(storageMismatch ? 3 : "x", GroupTypeId.Text,
                storageMismatch ? StorageType.Integer : StorageType.String, dataType: "different"));
            var r = Run(f.Doc, Apply()); Counts(r, 1, 0, 0, 0, 1, 0);
            Eq(storageMismatch ? "IncompatibleStorageType" : "IncompatibleDataType", r["Results"]![0]!["Reason"]!.Value<string>());
        }
    }),
    ("same values are unchanged; overwrite false preserves populated target", () => {
        var f = Fixture("SAME", "SAME"); var r = Run(f.Doc, Apply()); Counts(r, 1, 0, 0, 1, 0, 0);
        f = Fixture(); var args = Apply(); args["overwrite"] = false;
        r = Run(f.Doc, args); Counts(r, 1, 0, 0, 0, 1, 0); Eq("OLD", f.Target.Value);
        f.Target.Value = null; r = Run(f.Doc, args); Counts(r, 1, 1, 1, 0, 0, 0); Eq("NEW", f.Target.Value);
    }),
    ("scope excludes types and unrequested categories", () => {
        var f = Fixture();
        f.Doc.Elements.Add(new Element { Id = new(2), IsType = true });
        f.Doc.Elements.Add(new Element { Id = new(3), Category = BuiltInCategory.OST_Doors });
        Counts(Run(f.Doc), 1, 1, 0, 0, 0, 0);
        Counts(Run(f.Doc, new() { ["categories"] = new JArray("Floors") }), 0, 0, 0, 0, 0, 0);
    }),
    ("double and ElementId copies preserve raw values", () => {
        var f = Fixture(123.456, 7.0, StorageType.Double); Counts(Run(f.Doc, Apply()), 1, 1, 1, 0, 0, 0); Eq(123.456, f.Target.Value);
        f = Fixture(new ElementId(9_000_000_001), new ElementId(1), StorageType.ElementId);
        var r = Run(f.Doc, Apply()); Eq(new ElementId(9_000_000_001), f.Target.Value);
        Eq(9_000_000_001L, r["ValueChanges"]![0]!["NewValue"]!.Value<long>());
        f.Source.Value = ElementId.InvalidElementId; Counts(Run(f.Doc, Apply()), 1, 0, 0, 0, 1, 0);
    }),
    ("category totals and value transitions reconcile after commit", () => {
        var f = Fixture(); var e = new Element { Id = new(2), Category = BuiltInCategory.OST_Floors };
        e.Parameters.AddRange([P("NEW", GroupTypeId.Constraints), P("OLD", GroupTypeId.Text)]);
        f.Doc.Elements.Add(e); var args = Apply(); args["categories"] = new JArray("Walls", "Floors");
        var r = Run(f.Doc, args); Counts(r, 2, 2, 2, 0, 0, 0);
        Eq(1, f.Doc.TransactionsStarted); Eq(true, r["Committed"]!.Value<bool>());
        Eq(2, r["ByCategory"]!.Sum(c => c["Counts"]!["Updated"]!.Value<int>()));
        Eq(2, r["ValueChanges"]!.Sum(c => c["Updated"]!.Value<int>()));
    }),
    ("later Set exception rolls back earlier writes and clears Updated", () => {
        var f = Fixture(); var e = new Element { Id = new(9_000_000_001) };
        var target = P("SECOND_OLD", GroupTypeId.Text); target.ThrowOnSet = true;
        e.Parameters.AddRange([P("SECOND_NEW", GroupTypeId.Constraints), target]); f.Doc.Elements.Add(e);
        var r = Run(f.Doc, Apply()); Counts(r, 2, 2, 0, 0, 0, 2);
        Eq("OLD", f.Target.Value); Eq("SECOND_OLD", target.Value); Eq(1, f.Doc.TransactionsStarted);
        Eq(1, f.Doc.Rollbacks); Eq(true, r["RolledBack"]!.Value<bool>()); Eq(false, r["Success"]!.Value<bool>());
    }),
    ("Set false and commit rejection are atomic failures", () => {
        foreach (bool rejectCommit in new[] { true, false }) {
            var f = Fixture(); f.Doc.RejectCommit = rejectCommit; f.Target.ReturnFalseOnSet = !rejectCommit;
            var r = Run(f.Doc, Apply()); Counts(r, 1, 1, 0, 0, 0, 1); Eq("OLD", f.Target.Value); Eq(1, f.Doc.Rollbacks);
        }
    }),
    ("regeneration exceptions and changed values roll back", () => {
        foreach (bool throwException in new[] { true, false }) {
            var f = Fixture();
            f.Doc.OnRegenerate = () => { if (throwException) throw new Exception("Regeneration failed."); f.Target.Value = "CONSTRAINT_OVERRIDE"; };
            var r = Run(f.Doc, Apply()); Counts(r, 1, 1, 0, 0, 0, 1); Eq("OLD", f.Target.Value);
        }
    }),
    ("group aliases and full IDs resolve to the same parameter", () => {
        foreach (string group in new[] { "約束", "Constraints", "PG_CONSTRAINTS", GroupTypeId.Constraints.TypeId }) {
            var f = Fixture(); Counts(Run(f.Doc, new() { ["sourceGroup"] = group }), 1, 1, 0, 0, 0, 0);
        }
    }),
    ("malformed arguments fail before any transaction", () => {
        foreach (var json in new[] { "{'valueTransform':'invalid'}", "{'apply':'true'}", "{'dryRun':null}", "{'scope':'view'}", "{'sourceGroup':'wrong'}",
            "{'sourceParameterName':' '}", "{'categories':[]}", "{'categories':['UnknownCategory']}", "{'categories':'Walls'}" }) {
            var f = Fixture(); bool threw = false;
            try { Run(f.Doc, JObject.Parse(json)); } catch (ArgumentException) { threw = true; }
            Eq(true, threw); Eq(0, f.Doc.TransactionsStarted);
        }
    })
};
int failures = 0;
foreach (var (name, test) in cases)
{
    try { test(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
}
Console.WriteLine($"{cases.Count - failures}/{cases.Count} command tests passed.");
return failures == 0 ? 0 : 1;
