using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Newtonsoft.Json.Linq;
using RevitMCP.Tests;

namespace RevitMCP.Tests.AreaFormulaNumbering
{
    /// <summary>
    /// Exercises the pure geometry and numbering logic behind create_floor_area_formula,
    /// renumber_parking_spaces and create_sequence_numbers.
    ///
    /// These methods are internal statics on CommandExecutor that take plain values and no
    /// Revit objects, so they are reachable without an open Document — which is exactly what
    /// tests/README.md says belongs here. Everything needing a live model (boundary
    /// extraction, TextNote creation, parameter writes) is NOT covered here and is not
    /// claimed to be.
    ///
    /// Reached by reflection because the methods are internal to the add-in assembly.
    /// Exit code 0 = every check passed.
    /// </summary>
    internal static class Program
    {
        private static int _failures;
        private static int _checks;
        private static Type _executor;

        private static int Main()
        {
            // The resolver must be installed before any method that references a type from
            // RevitMCP.dll is JIT-compiled, so the add-in is only touched inside Run().
            // Doing it in Main would resolve the type on entry and fail before the hook exists.
            if (!RevitAssemblies.TryInstallResolver()) return 2;
            try
            {
                return Run();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[harness] FATAL {ex.GetType().FullName}: {ex.Message}");
                if (ex.InnerException != null)
                    Console.WriteLine($"[harness]   inner {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}");
                return 2;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int Run()
        {
            _executor = typeof(RevitMCP.Core.CommandExecutor);
            Console.WriteLine($"[harness] target: {_executor.Assembly.Location}");
            Console.WriteLine();

            SimplifyPolygonChecks();
            PolygonAreaChecks();
            SelectOuterLoopChecks();
            ClassifyPolygonChecks();
            FormulaTermChecks();
            NumberSequenceChecks();
            LetterLabelChecks();
            ParkingCategoryChecks();
            ExclusionChecks();
            ArrayParameterChecks();
            TextNoteTextChecks();
            OrderAndRotateChecks();

            Console.WriteLine();
            Console.WriteLine($"[harness] {_checks - _failures}/{_checks} checks passed");
            if (_failures > 0)
                Console.WriteLine($"[harness] FAILED: {_failures} check(s)");
            return _failures == 0 ? 0 : 1;
        }

        // ---------- reflection helpers ----------

        private static MethodInfo M(string name)
        {
            var method = _executor.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
            if (method == null) throw new Exception($"method not found: {name}");
            return method;
        }

        private static object Invoke(string name, params object[] args) => M(name).Invoke(null, args);

        private static List<double[]> Pts(params double[] xy)
        {
            var list = new List<double[]>();
            for (int i = 0; i < xy.Length; i += 2) list.Add(new[] { xy[i], xy[i + 1] });
            return list;
        }

        private static void Check(string label, object expected, object actual)
        {
            _checks++;
            bool ok = Equals(expected, actual);
            if (!ok && expected is double e && actual is double a) ok = Math.Abs(e - a) < 1e-9;
            Console.WriteLine($"  {(ok ? "PASS" : "FAIL")}  {label}");
            if (!ok)
            {
                Console.WriteLine($"        expected: {Fmt(expected)}");
                Console.WriteLine($"        actual  : {Fmt(actual)}");
                _failures++;
            }
        }

        private static void CheckClose(string label, double expected, double actual, double tolerance)
        {
            _checks++;
            bool ok = Math.Abs(expected - actual) <= tolerance;
            Console.WriteLine($"  {(ok ? "PASS" : "FAIL")}  {label}");
            if (!ok)
            {
                Console.WriteLine($"        expected: {expected.ToString("F6", CultureInfo.InvariantCulture)} (+/- {tolerance})");
                Console.WriteLine($"        actual  : {actual.ToString("F6", CultureInfo.InvariantCulture)}");
                _failures++;
            }
        }

        private static string Fmt(object value)
        {
            if (value == null) return "null";
            if (value is string s) return "\"" + s + "\"";
            if (value is double d) return d.ToString("G17", CultureInfo.InvariantCulture);
            if (value is IEnumerable en && !(value is string))
                return "[" + string.Join(", ", en.Cast<object>().Select(Fmt)) + "]";
            return value.ToString();
        }

        // ---------- SimplifyPolygon ----------

        private static void SimplifyPolygonChecks()
        {
            Console.WriteLine("SimplifyPolygon (duplicate + collinear removal)");

            // A rectangle whose bottom wall was split in two produces a collinear midpoint.
            // Without collinear removal this reads as 5 vertices and is misjudged as undecomposable.
            var split = Pts(0, 0, 5000, 0, 10000, 0, 10000, 8000, 0, 8000);
            var simplified = (List<double[]>)Invoke("SimplifyPolygon", split, 1.0, 1.0);
            Check("split bottom wall collapses to 4 vertices", 4, simplified.Count);

            // Closing point repeated: the loop must not keep both.
            var closed = Pts(0, 0, 10000, 0, 10000, 8000, 0, 8000, 0, 0);
            var closedSimplified = (List<double[]>)Invoke("SimplifyPolygon", closed, 1.0, 1.0);
            Check("repeated closing vertex removed", 4, closedSimplified.Count);

            // A 2 mm jog is a real corner, not noise: it must survive a 1 mm tolerance.
            var jog = Pts(0, 0, 5000, 2, 10000, 0, 10000, 8000, 0, 8000);
            var jogSimplified = (List<double[]>)Invoke("SimplifyPolygon", jog, 1.0, 1.0);
            Check("2 mm jog is kept (not flattened)", 5, jogSimplified.Count);

            var lShape = Pts(0, 0, 10000, 0, 10000, 4000, 5000, 4000, 5000, 8000, 0, 8000);
            var lSimplified = (List<double[]>)Invoke("SimplifyPolygon", lShape, 1.0, 1.0);
            Check("L-shape keeps all 6 vertices", 6, lSimplified.Count);
            Console.WriteLine();
        }

        // ---------- PolygonAreaM2 ----------

        private static void PolygonAreaChecks()
        {
            Console.WriteLine("PolygonAreaM2 (shoelace, mm in / m2 out)");

            var rect = Pts(0, 0, 10000, 0, 10000, 8000, 0, 8000);
            CheckClose("10 m x 8 m rectangle = 80 m2", 80.0, (double)Invoke("PolygonAreaM2", rect), 1e-9);

            // Reversed winding must give the same magnitude, not a negative area.
            var reversed = Pts(0, 8000, 10000, 8000, 10000, 0, 0, 0);
            CheckClose("reversed winding still 80 m2", 80.0, (double)Invoke("PolygonAreaM2", reversed), 1e-9);

            var tri = Pts(0, 0, 6000, 0, 0, 3000);
            CheckClose("6 m x 3 m right triangle = 9 m2", 9.0, (double)Invoke("PolygonAreaM2", tri), 1e-9);

            var lShape = Pts(0, 0, 10000, 0, 10000, 4000, 5000, 4000, 5000, 8000, 0, 8000);
            CheckClose("L-shape = 60 m2", 60.0, (double)Invoke("PolygonAreaM2", lShape), 1e-9);
            Console.WriteLine();
        }

        // ---------- SelectOuterLoopIndex ----------

        private static void SelectOuterLoopChecks()
        {
            Console.WriteLine("SelectOuterLoopIndex (含內環的空間元素挑外環，不假設 loops[0] 就是外環)");

            // 2026-09-18 follow-up：Revit 的 GetBoundarySegments 回傳的第一個環不保證是外環——
            // 一個帶內環（樓梯間、管道間）的房間，內環有時排在 loops[0]。用面積最大者當外環，
            // 而不是永遠信第一個。
            var outer = Pts(0, 0, 10000, 0, 10000, 8000, 0, 8000); // 80 m2
            var inner = Pts(2000, 2000, 3000, 2000, 3000, 3000, 2000, 3000); // 1 m2

            var innerFirst = new List<List<double[]>> { inner, outer };
            Check("內環排第一個時仍選到面積較大的外環", 1, (int)Invoke("SelectOuterLoopIndex", innerFirst));

            var outerFirst = new List<List<double[]>> { outer, inner };
            Check("外環本來就排第一個", 0, (int)Invoke("SelectOuterLoopIndex", outerFirst));

            var single = new List<List<double[]>> { outer };
            Check("只有一個環（沒有內環）", 0, (int)Invoke("SelectOuterLoopIndex", single));
            Console.WriteLine();
        }

        // ---------- ClassifyPolygon ----------

        private static object Classify(List<double[]> points, double angleTolerance)
            => Invoke("ClassifyPolygon", points, angleTolerance);

        private static string ShapeOf(object result)
            => (string)result.GetType().GetField("Shape").GetValue(result);

        private static double[] DimsOf(object result)
            => (double[])result.GetType().GetField("DimensionsM").GetValue(result);

        private static int VerticesOf(object result)
            => (int)result.GetType().GetField("VertexCount").GetValue(result);

        private static void ClassifyPolygonChecks()
        {
            Console.WriteLine("ClassifyPolygon");

            var rect = Pts(0, 0, 10000, 0, 10000, 8000, 0, 8000);
            var rectResult = Classify(rect, 1.0);
            Check("rectangle detected", "rectangle", ShapeOf(rectResult));
            CheckClose("rectangle long side = 10 m", 10.0, DimsOf(rectResult)[0], 1e-9);
            CheckClose("rectangle short side = 8 m", 8.0, DimsOf(rectResult)[1], 1e-9);

            // Right triangle: the two legs must be used, not the hypotenuse. Taking the
            // longest side as the base would print 6.71x2.68/2 instead of 6.00x3.00/2.
            var rightTri = Pts(0, 0, 6000, 0, 0, 3000);
            var rightResult = Classify(rightTri, 1.0);
            Check("right triangle detected", "triangle", ShapeOf(rightResult));
            var rightDims = DimsOf(rightResult).OrderByDescending(d => d).ToArray();
            CheckClose("right triangle leg a = 6 m", 6.0, rightDims[0], 1e-9);
            CheckClose("right triangle leg b = 3 m", 3.0, rightDims[1], 1e-9);
            CheckClose("right triangle legs reproduce 9 m2", 9.0, rightDims[0] * rightDims[1] / 2.0, 1e-9);

            // Oblique triangle: no right angle, so base = longest side and height is derived.
            var oblique = Pts(0, 0, 10000, 0, 3000, 4000);
            var obliqueResult = Classify(oblique, 1.0);
            Check("oblique triangle detected", "triangle", ShapeOf(obliqueResult));
            var obliqueDims = DimsOf(obliqueResult);
            CheckClose("oblique base = longest side 10 m", 10.0, obliqueDims[0], 1e-9);
            CheckClose("oblique base x height / 2 reproduces area", 20.0, obliqueDims[0] * obliqueDims[1] / 2.0, 1e-9);

            // A parallelogram has four vertices but no right angles: it must be refused,
            // because "length x width" would silently overstate its area.
            var parallelogram = Pts(0, 0, 10000, 0, 12000, 8000, 2000, 8000);
            var paraResult = Classify(parallelogram, 1.0);
            Check("non-right quadrilateral refused", "undecomposed", ShapeOf(paraResult));
            Check("refused quad reports 4 vertices", 4, VerticesOf(paraResult));

            var lShape = Pts(0, 0, 10000, 0, 10000, 4000, 5000, 4000, 5000, 8000, 0, 8000);
            var lResult = Classify(lShape, 1.0);
            Check("L-shape refused", "undecomposed", ShapeOf(lResult));
            Check("L-shape reports 6 vertices", 6, VerticesOf(lResult));

            var degenerate = Pts(0, 0, 10000, 0);
            Check("two vertices refused", "undecomposed", ShapeOf(Classify(degenerate, 1.0)));
            Console.WriteLine();
        }

        // ---------- BuildFormulaTerm ----------

        private static void FormulaTermChecks()
        {
            Console.WriteLine("BuildFormulaTerm (prints rounded dimensions, area from rounded product)");

            var args = new object[] { "rectangle", new[] { 10.204, 8.396 }, 2, null, null };
            M("BuildFormulaTerm").Invoke(null, args);
            Check("rectangle expression", "10.20×8.40 = 85.68", args[3]);
            CheckClose("rectangle area from rounded sides", 85.68, (double)args[4], 1e-9);

            var triArgs = new object[] { "triangle", new[] { 3.0, 2.1 }, 2, null, null };
            M("BuildFormulaTerm").Invoke(null, triArgs);
            Check("triangle expression", "3.00×2.10÷2 = 3.15", triArgs[3]);
            CheckClose("triangle area", 3.15, (double)triArgs[4], 1e-9);

            // Half-up, not banker's rounding: 0.5 must go to 1, not to 0.
            var halfArgs = new object[] { "rectangle", new[] { 0.5, 1.0 }, 0, null, null };
            M("BuildFormulaTerm").Invoke(null, halfArgs);
            Check("0.5 rounds away from zero (not banker's)", "1×1 = 1", halfArgs[3]);

            // The printed area must equal the printed sides multiplied, even when that
            // differs from the true geometric area. A drawing whose own arithmetic does not
            // check out is worse than one that is 0.01 m2 off.
            var driftArgs = new object[] { "rectangle", new[] { 3.334, 3.334 }, 2, null, null };
            M("BuildFormulaTerm").Invoke(null, driftArgs);
            Check("printed product is self-consistent", "3.33×3.33 = 11.09", driftArgs[3]);
            Console.WriteLine();
        }

        // ---------- BuildNumberSequence ----------

        private static void NumberSequenceChecks()
        {
            Console.WriteLine("BuildNumberSequence (skipFour)");

            var plain = (List<long>)Invoke("BuildNumberSequence", 1L, 5, false);
            Check("no skip: 1..5", "[1, 2, 3, 4, 5]", Fmt(plain));

            var skipped = (List<long>)Invoke("BuildNumberSequence", 1L, 5, true);
            Check("skipFour drops 4", "[1, 2, 3, 5, 6]", Fmt(skipped));

            // Any digit 4 is skipped, not just the units digit.
            var teens = (List<long>)Invoke("BuildNumberSequence", 12L, 4, true);
            Check("skipFour drops 14", "[12, 13, 15, 16]", Fmt(teens));

            // The whole 40-49 block disappears.
            var forties = (List<long>)Invoke("BuildNumberSequence", 39L, 3, true);
            Check("skipFour jumps the entire 40s", "[39, 50, 51]", Fmt(forties));

            var fromStart = (List<long>)Invoke("BuildNumberSequence", 433L, 2, false);
            Check("honours a non-1 start", "[433, 434]", Fmt(fromStart));
            Console.WriteLine();
        }

        // ---------- ToLetterLabel ----------

        private static void LetterLabelChecks()
        {
            Console.WriteLine("ToLetterLabel");

            Check("1 -> a", "a", Invoke("ToLetterLabel", 1L, false));
            Check("26 -> z", "z", Invoke("ToLetterLabel", 26L, false));
            Check("27 -> aa (carry)", "aa", Invoke("ToLetterLabel", 27L, false));
            Check("28 -> ab", "ab", Invoke("ToLetterLabel", 28L, false));
            Check("52 -> az", "az", Invoke("ToLetterLabel", 52L, false));
            Check("53 -> ba", "ba", Invoke("ToLetterLabel", 53L, false));
            Check("uppercase flag", "AA", Invoke("ToLetterLabel", 27L, true));
            Console.WriteLine();
        }

        // ---------- ClassifyParkingCategory ----------

        private static void ParkingCategoryChecks()
        {
            Console.WriteLine("ClassifyParkingCategory");

            var motor = new List<string> { "機車", "motor", "scooter" };
            var bus = new List<string> { "大客車", "巴士", "bus", "coach" };

            Check("Chinese motorcycle keyword", "motorcycle",
                Invoke("ClassifyParkingCategory", "停車位 機車位", motor, bus));
            Check("English keyword is case-insensitive", "motorcycle",
                Invoke("ClassifyParkingCategory", "Parking MOTORcycle Standard", motor, bus));
            Check("bus keyword", "bus",
                Invoke("ClassifyParkingCategory", "Bus Bay Large", motor, bus));
            Check("unmatched falls back to car", "car",
                Invoke("ClassifyParkingCategory", "汽車位 標準", motor, bus));
            Check("empty name falls back to car", "car",
                Invoke("ClassifyParkingCategory", "", motor, bus));
            Console.WriteLine();
        }

        // ---------- IsExcluded ----------

        private static bool CallIsExcluded(string id, string currentValue, string[] ids, string[] values, out string reason)
        {
            var idSet = ids == null ? null : new HashSet<string>(ids, StringComparer.Ordinal);
            var valueSet = values == null ? null : new HashSet<string>(values, StringComparer.Ordinal);
            var args = new object[] { id, currentValue, idSet, valueSet, null };
            bool result = (bool)M("IsExcluded").Invoke(null, args);
            reason = (string)args[4];
            return result;
        }

        private static void ExclusionChecks()
        {
            Console.WriteLine("IsExcluded (excludeElementIds / excludeValues)");
            string reason;

            Check("id 在排除清單內", true,
                CallIsExcluded("893126", null, new[] { "893126" }, null, out reason));
            Check("  理由指向 excludeElementIds", true, reason != null && reason.Contains("excludeElementIds"));

            Check("現值命中 excludeValues", true,
                CallIsExcluded("111", "變更", null, new[] { "變更" }, out reason));
            Check("  理由帶出實際現值", true, reason != null && reason.Contains("變更"));

            Check("兩條規則都不命中", false,
                CallIsExcluded("111", "27", new[] { "999" }, new[] { "變更" }, out reason));
            Check("  不命中時 reason 為 null", true, reason == null);

            // 現值為空時，即使 excludeValues 含空字串也不能排除 —— 否則所有還沒編號的
            // 車位（OldValue=null，也就是絕大多數）會被整批排除，結果看起來仍是綠的。
            // 註：null 這條實際上是 HashSet<string>.Contains(null) 恆為 false 擋下的，
            // 不是 IsNullOrEmpty 守衛擋的 —— 拿掉守衛做突變時它不會轉紅。真正守住這條
            // 規則的是下面的空字串案例，兩條都留著是為了把這個差別寫在案發現場。
            Check("現值 null 不會被空字串規則掃到", false,
                CallIsExcluded("111", null, null, new[] { "" }, out reason));
            Check("現值空字串不會被掃到", false,
                CallIsExcluded("111", "", null, new[] { "" }, out reason));

            // Ordinal 比對：尾隨空白是不同的值，不做 trim
            Check("尾隨空白視為不同值", false,
                CallIsExcluded("111", "變更 ", null, new[] { "變更" }, out reason));

            Check("兩個清單都是 null 不會炸", false,
                CallIsExcluded("111", "變更", null, null, out reason));

            Check("id 規則優先於值規則", true,
                CallIsExcluded("893126", "27", new[] { "893126" }, new[] { "變更" }, out reason));
            Check("  優先時理由是 id 那條", true, reason != null && reason.Contains("excludeElementIds"));
            Console.WriteLine();
        }

        // ---------- ReadStringArray / ReadIdArray：陣列參數的嚴格檢查 ----------

        private static string InvokeExpectingThrow(string name, params object[] args)
        {
            try
            {
                M(name).Invoke(null, args);
                return null;
            }
            catch (TargetInvocationException ex)
            {
                return ex.InnerException?.Message ?? ex.Message;
            }
        }

        private static void ArrayParameterChecks()
        {
            Console.WriteLine("ReadStringArray / ReadIdArray (陣列參數不得靜默退回預設)");

            // 2026-09-17 審查 Critical：token 存在但不是 JArray（client 送成字串、或 Node server
            // 跑的是舊 schema）時，原本靜默退回 fallback。excludeValues 是防覆寫既有編號的唯一
            // 防線，退回空清單等於把防線拆掉還回綠燈。
            var arr = new JObject { ["excludeValues"] = new JArray("變更", "", "保留") };
            var got = (List<string>)Invoke("ReadStringArray", arr, "excludeValues", new string[0]);
            Check("JArray 正常讀取並剔除空白", "變更|保留", string.Join("|", got));

            var missing = new JObject();
            got = (List<string>)Invoke("ReadStringArray", missing, "excludeValues", new[] { "預設" });
            Check("缺 key 走 fallback", "預設", string.Join("|", got));

            var asString = new JObject { ["excludeValues"] = "變更" };
            string msg = InvokeExpectingThrow("ReadStringArray", asString, "excludeValues", new string[0]);
            Check("字串而非陣列 → 丟例外", true, msg != null);
            Check("  例外訊息點名參數與型別", true, msg != null && msg.Contains("excludeValues") && msg.Contains("陣列"));

            var ids = new JObject { ["sourceIds"] = new JArray(2063997, 2067719) };
            var idList = (IList)Invoke("ReadIdArray", ids, "sourceIds");
            Check("ReadIdArray 讀出兩個 id", 2, idList.Count);
            Check("  第一個 id 值正確", "2063997", Convert.ToString(idList[0], CultureInfo.InvariantCulture));

            Check("ReadIdArray 缺 key 回 null", true, Invoke("ReadIdArray", new JObject(), "sourceIds") == null);
            Check("ReadIdArray 空陣列回 null", true,
                Invoke("ReadIdArray", new JObject { ["sourceIds"] = new JArray() }, "sourceIds") == null);

            msg = InvokeExpectingThrow("ReadIdArray", new JObject { ["sourceIds"] = "2063997" }, "sourceIds");
            Check("ReadIdArray 字串而非陣列 → 丟例外", true, msg != null && msg.Contains("sourceIds"));

            msg = InvokeExpectingThrow("ReadIdArray", new JObject { ["sourceIds"] = new JArray("abc") }, "sourceIds");
            Check("ReadIdArray 非整數元素 → 丟例外", true, msg != null);
            Console.WriteLine();
        }

        // ---------- BuildTextNoteText：TextNote 內部換行 ----------

        private static void TextNoteTextChecks()
        {
            Console.WriteLine("BuildTextNoteText (TextNote 換行用 \\r)");

            // Revit TextNote.Text 的行分隔是單一 \r；AppendLine 給的 \r\n 在部分版本會多出空行。
            string text = (string)Invoke("BuildTextNoteText", "1F", new List<string> { "A = 1", "B = 2" }, "合計 = 3 ㎡");
            Check("標題+兩行+合計，共四行", 4, text.Split('\r').Length);
            Check("不含 \\n", false, text.Contains("\n"));
            Check("最後一行是合計", "合計 = 3 ㎡", text.Split('\r').Last());

            text = (string)Invoke("BuildTextNoteText", null, new List<string> { "A = 1" }, "合計 = 1 ㎡");
            Check("無標題時不留空首行", "A = 1", text.Split('\r').First());
            Console.WriteLine();
        }

        // ---------- OrderRoomsTopDownLeftRight + TryRotateToStart ----------

        private static object MakePoint(string id, double x, double y)
        {
            Type pointType = _executor.GetNestedType("RoomOrderPoint", BindingFlags.NonPublic);
            if (pointType == null) throw new Exception("RoomOrderPoint type not found");
            object point = Activator.CreateInstance(pointType);
            pointType.GetField("Id").SetValue(point, id);
            pointType.GetField("X").SetValue(point, x);
            pointType.GetField("Y").SetValue(point, y);
            return point;
        }

        private static IList MakeList(params object[] points)
        {
            Type pointType = _executor.GetNestedType("RoomOrderPoint", BindingFlags.NonPublic);
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(pointType));
            foreach (var p in points) list.Add(p);
            return list;
        }

        private static string IdsOf(IList list)
        {
            Type pointType = _executor.GetNestedType("RoomOrderPoint", BindingFlags.NonPublic);
            FieldInfo idField = pointType.GetField("Id");
            return "[" + string.Join(", ", list.Cast<object>().Select(p => (string)idField.GetValue(p))) + "]";
        }

        private static void OrderAndRotateChecks()
        {
            Console.WriteLine("OrderRoomsTopDownLeftRight + TryRotateToStart");

            // Two rows of two. Higher Y is the upper row and must come first; within a row,
            // smaller X first. Row 2 is 5000 mm below, well outside the 1500 mm tolerance.
            var input = MakeList(
                MakePoint("d", 6000, 0),
                MakePoint("b", 6000, 5000),
                MakePoint("c", 1000, 0),
                MakePoint("a", 1000, 5000));

            var ordered = (IList)M("OrderRoomsTopDownLeftRight").Invoke(null, new object[] { input, 1500.0 });
            Check("top row first, left to right", "[a, b, c, d]", IdsOf(ordered));

            // Anchor-based row grouping: three points each 1200 mm below the last are within
            // tolerance of their neighbour but 2400 mm from the row anchor, so they must NOT
            // chain into one row.
            var drift = MakeList(
                MakePoint("p0", 0, 0),
                MakePoint("p1", 0, -1200),
                MakePoint("p2", 0, -2400));
            var driftOrdered = (IList)M("OrderRoomsTopDownLeftRight").Invoke(null, new object[] { drift, 1500.0 });
            Type pointType = _executor.GetNestedType("RoomOrderPoint", BindingFlags.NonPublic);
            FieldInfo rowField = pointType.GetField("RowIndex");
            var rows = driftOrdered.Cast<object>().Select(p => (int)rowField.GetValue(p)).ToArray();
            Check("tolerance does not chain across rows", "[0, 0, 1]", Fmt(rows));

            // Rotation moves the chosen element to the front and wraps the rest around;
            // it must not drop anyone.
            var rotateArgs = new object[] { ordered, "c", null };
            bool rotated = (bool)M("TryRotateToStart").Invoke(null, rotateArgs);
            Check("rotate to a known id succeeds", true, rotated);
            Check("sequence wraps around the start element", "[c, d, a, b]", IdsOf((IList)rotateArgs[2]));
            Check("rotation keeps every element", 4, ((IList)rotateArgs[2]).Count);

            // An unknown start id must report failure rather than silently using the default
            // start: a whole batch numbered from the wrong place looks entirely normal.
            var missingArgs = new object[] { ordered, "zzz", null };
            Check("unknown start id reports failure", false, (bool)M("TryRotateToStart").Invoke(null, missingArgs));

            var noneArgs = new object[] { ordered, null, null };
            Check("null start id is a no-op success", true, (bool)M("TryRotateToStart").Invoke(null, noneArgs));
            Console.WriteLine();
        }
    }
}
