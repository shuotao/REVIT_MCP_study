using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using RevitMCP.Core;
using RevitMCP.Tests;

namespace RevitMCP.Tests.FramingAlign
{
    /// <summary>
    /// Regression harness for FramingAlignGeometry.EvaluateCandidate — the pure-geometry
    /// core of align_structural_framing.
    ///
    /// The three positive cases reproduce the values the user measured by hand in the real
    /// model on 2026-07-23 (recorded in the AlignFraming WIP notes): a T joint against an
    /// H700x200 beam giving -100mm, a T joint against a wide 大梁 whose bbox reads 433mm
    /// giving -216.5mm, and an L corner against an RH300x150 giving +75mm. Fixtures are
    /// constructed to match each described situation rather than dumped from the model,
    /// since the raw coordinates were never recorded -- what is being pinned is that the
    /// documented situation still produces the documented number.
    ///
    /// The negative cases matter just as much: without them a function that accepted
    /// everything would pass all three positives.
    /// </summary>
    internal static class Program
    {
        private const double MM = 304.8;

        private static double Mm(double mm) => mm / MM;

        private static int Main()
        {
            if (!RevitAssemblies.TryInstallResolver()) return 2;
            return Run();
        }

        /// <summary>Axis-aligned box in millimetres, relative to the beam endpoint at the origin.</summary>
        private static List<XYZ> Box(double x0, double x1, double y0, double y1, double z0, double z1)
        {
            var corners = new List<XYZ>(8);
            foreach (double x in new[] { Mm(x0), Mm(x1) })
                foreach (double y in new[] { Mm(y0), Mm(y1) })
                    foreach (double z in new[] { Mm(z0), Mm(z1) })
                        corners.Add(new XYZ(x, y, z));
            return corners;
        }

        private static int Run()
        {
            int failures = 0;

            // Our beam runs along +X; this is its End 1, so outward is +X.
            XYZ endPoint = XYZ.Zero;
            XYZ outward = new XYZ(1, 0, 0);
            double maxFace = Mm(700.0);   // the calibrated MAX_FACE_MM

            // --- Positive case 1: T joint onto an H700x200 beam -> -100mm ---
            // The connecting beam crosses our axis (material on both sides in plan), and our
            // endpoint sits on its centreline, so c = 0 and the flush face is one half-width
            // back: 200/2 = 100mm of retract.
            failures += Expect("case1 T joint, H700x200 crossing beam",
                Box(-100, 100, -1000, 1000, -350, 350), endPoint, outward,
                isColumn: false, name: "H700x200", maxFace: maxFace,
                expectAccepted: true, expectMm: -100.0, expectT: true);

            // --- Positive case 2: T joint onto a 大梁 whose name carries no H section ---
            // Name parsing fails, so the half-width falls back to the bbox, which the WIP
            // notes recorded as 433mm wide (join-inflated). 433/2 = 216.5mm of retract.
            failures += Expect("case2 T joint, 大梁 B11E (bbox fallback 433mm)",
                Box(-216.5, 216.5, -1000, 1000, -350, 350), endPoint, outward,
                isColumn: false, name: "B11E-大梁", maxFace: maxFace,
                expectAccepted: true, expectMm: -216.5, expectT: true);

            // --- Positive case 3: L corner onto an RH300x150 -> +75mm ---
            // The connecting beam stops at the joint instead of crossing it (material on one
            // side only), so this extends to the far face: +150/2 = +75mm.
            failures += Expect("case3 L corner, RH300x150 terminating beam",
                Box(-75, 75, 0, 2000, -150, 150), endPoint, outward,
                isColumn: false, name: "RH300x150", maxFace: maxFace,
                expectAccepted: true, expectMm: 75.0, expectT: false);

            // --- Negative: the 900mm column the axial window exists to exclude ---
            // This is the specific false positive the WIP notes call out: a column near the
            // axis but not actually connected. Its near face sits beyond the 700mm window.
            failures += ExpectRejected("negative: column 900mm along the axis (outside window)",
                Box(900, 1500, -300, 300, -2000, 2000), endPoint, outward,
                isColumn: true, name: "C1", maxFace: maxFace);

            // --- Negative: collinear end-to-end continuation beam ---
            // Runs along our own axis rather than crossing it, so its axial footprint blows
            // past 600mm. Without this filter case2's real model picked the wrong member.
            failures += ExpectRejected("negative: collinear continuation beam (footprint 3000mm)",
                Box(-100, 2900, -75, 75, -350, 350), endPoint, outward,
                isColumn: false, name: "H700x200", maxFace: maxFace);

            // --- Negative: laterally offset parallel beam ---
            // Endpoint falls outside its Y range, so it is not connected at all.
            failures += ExpectRejected("negative: parallel beam offset 2000mm in Y",
                Box(-100, 100, 1900, 2100, -350, 350), endPoint, outward,
                isColumn: false, name: "H700x200", maxFace: maxFace);

            // --- Negative: endpoint above the target in Z ---
            failures += ExpectRejected("negative: target 1000mm below endpoint in Z",
                Box(-100, 100, -1000, 1000, -1400, -1000), endPoint, outward,
                isColumn: false, name: "H700x200", maxFace: maxFace);

            // --- Positive: column exemption from the footprint filter ---
            // A 600mm column has a large axial footprint; the same numbers would be rejected
            // if it were a beam. This pins that the isColumn exemption is actually wired.
            failures += Expect("column exemption: 600mm column still accepted",
                Box(-300, 300, -300, 300, -2000, 2000), endPoint, outward,
                isColumn: true, name: "RC-C600", maxFace: maxFace,
                expectAccepted: true, expectMm: -300.0, expectT: true);

            // --- Control: the same 600mm-footprint box as a beam must be rejected ---
            // Paired with the case above, this proves the footprint filter fires on real
            // input rather than being dead code.
            failures += ExpectRejected("control: identical box as a beam is rejected on footprint",
                Box(-300, 301, -300, 300, -2000, 2000), endPoint, outward,
                isColumn: false, name: "SomeBeam", maxFace: maxFace);

            // --- Beams that run along Y, not X ---
            // All three calibration beams ran along X, so the lateral containment check was
            // written against world Y and never exercised for any other direction. The filter
            // has to be expressed in the beam's own frame (perpendicular-to-axis), otherwise a
            // Y-running beam compares the wrong coordinate: a column sitting 2000mm off to the
            // side still "contains" the endpoint in Y, its near face is inside the axial
            // window, and the column exemption waves it through as the alignment target.
            XYZ outwardY = new XYZ(0, 1, 0);

            failures += ExpectRejected("Y-beam: column 2000mm off the axis in X is rejected",
                Box(1900, 2500, -300, 300, -2000, 2000), endPoint, outwardY,
                isColumn: true, name: "RC-C600", maxFace: maxFace);

            // The same T joint as case1, rotated so our beam runs along +Y and the connecting
            // beam runs along X. The answer must not depend on which way the beam points.
            failures += Expect("Y-beam: case1 rotated 90deg still gives -100mm",
                Box(-1000, 1000, -100, 100, -350, 350), endPoint, outwardY,
                isColumn: false, name: "H700x200", maxFace: maxFace,
                expectAccepted: true, expectMm: -100.0, expectT: true);

            Console.WriteLine();
            Console.WriteLine(failures == 0
                ? "RESULT: GREEN (all checks passed)"
                : $"RESULT: RED ({failures} check(s) failed)");
            return failures == 0 ? 0 : 1;
        }

        private static int Expect(
            string label, List<XYZ> corners, XYZ endPoint, XYZ outward,
            bool isColumn, string name, double maxFace,
            bool expectAccepted, double expectMm, bool expectT)
        {
            var r = FramingAlignGeometry.EvaluateCandidate(corners, endPoint, outward, isColumn, name, maxFace);
            double actualMm = r.FaceAFeet * MM;
            bool ok = r.Accepted == expectAccepted
                      && Math.Abs(actualMm - expectMm) < 0.05
                      && r.IsThroughJoint == expectT;

            Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {label}");
            Console.WriteLine($"    expected: accepted={expectAccepted} ext={expectMm:+0.0;-0.0}mm joint={(expectT ? "T" : "L")}");
            Console.WriteLine($"    actual:   accepted={r.Accepted} ext={actualMm:+0.0;-0.0}mm joint={(r.IsThroughJoint ? "T" : "L")}"
                              + (r.Accepted ? "" : $" reason={r.RejectReason}"));
            return ok ? 0 : 1;
        }

        private static int ExpectRejected(
            string label, List<XYZ> corners, XYZ endPoint, XYZ outward,
            bool isColumn, string name, double maxFace)
        {
            var r = FramingAlignGeometry.EvaluateCandidate(corners, endPoint, outward, isColumn, name, maxFace);
            bool ok = !r.Accepted;
            Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {label}");
            Console.WriteLine($"    expected: rejected");
            Console.WriteLine($"    actual:   {(r.Accepted ? $"ACCEPTED ext={r.FaceAFeet * MM:+0.0;-0.0}mm" : $"rejected ({r.RejectReason})")}");
            return ok ? 0 : 1;
        }
    }
}
