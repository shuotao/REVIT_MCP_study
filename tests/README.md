# tests

Two harnesses covering the parts of the add-in whose failure modes are silent — where the
code appears to work and produces a plausible wrong answer rather than an error.

They are plain console executables, not a unit-test framework. Exit code `0` means every
check passed; non-zero means at least one failed, so they drop into a script without a
runner. Each prints what it expected next to what it got, so a failure says which case
broke rather than just that something did.

## Running them

Build the add-in first — `MCP/bin/` is gitignored, so a fresh clone has none:

```powershell
cd MCP
dotnet build -c Release.R23 RevitMCP.csproj
```

Then:

```powershell
dotnet run -c Release --project tests\RevitMCP.Tests.SocketOrigin
```

To test another Revit year, pass the matching configuration:

```powershell
dotnet build -c Release tests\RevitMCP.Tests.SocketOrigin -p:RevitTestConfiguration=Release.R24
```

The Revit install is found by probing the usual `C:\Program Files\Autodesk\Revit <year>`
directories. Set `REVIT_TEST_DIR` to override.

**These cannot run in CI.** The repo's workflows are `ubuntu-latest`; these need .NET
Framework 4.8 and a local Revit install for `RevitAPI.dll`. Run them on a development
machine.

## RevitMCP.Tests.SocketOrigin

Starts the real `SocketService` from the built add-in and performs two raw RFC 6455
handshakes against it — one without an `Origin` header (what the Node bridge sends) and one
with (what a browser sends).

The expected results are `101` and `403`. Before the Origin check existed both returned
`101`, meaning any page open in the user's browser could connect to the add-in and issue
commands against the model being edited.

It listens on port **18964**, not the production 8964: it starts a real listener, and using
the live port would collide with a running add-in and pull its port-conflict handling —
including the process-kill path — into a test run. `ExclusiveLock` is disabled so the two
handshakes are independent; with the lock on, the second would fail with `409` for a reason
unrelated to what is being tested.

## RevitMCP.Tests.AreaFormulaNumbering

Covers the pure geometry and numbering logic behind `create_floor_area_formula`,
`renumber_parking_spaces` and `create_sequence_numbers`: polygon simplification (duplicate
and collinear vertex removal), shoelace area, rectangle/triangle classification, formula
term formatting and rounding, the skip-4 number sequence, letter labels, parking category
keyword matching, and the shared top-down/left-to-right ordering plus start-element rotation.

These are internal statics taking plain values, reached by reflection. Everything needing a
live `Document` -- boundary extraction from Revit, TextNote creation, parameter writes -- is
**not** covered here and the harness says so rather than implying it is.

53 checks. Six mutation probes were run against it on 2026-09-07 (disable collinear removal,
drop the right-angle triangle preference, switch to banker's rounding, make skip-4 test only
the units digit, accept a missing start element, chain row grouping off the previous point);
all six turned the suite red. A suite that has never been made to fail is not evidence.

## Adding to these

The add-in is referenced with `Private=false`, so it is never copied next to the harness and
a rebuilt `MCP/` is picked up without rebuilding the test. `tests/Directory.Build.props`
bakes the resolved path in as assembly metadata for the `AssemblyResolve` hook in
`tests/Shared/RevitAssemblies.cs`, which is linked into both projects.

Anything reachable without a live `Document` belongs here. Anything needing an open model
does not — say so in the output rather than pretending it was covered.
