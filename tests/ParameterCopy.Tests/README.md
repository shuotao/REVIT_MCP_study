# Parameter-copy command tests

This executable compiles the production ParameterCopy command and RevitCompatibility
source against small Revit API doubles. It verifies default write guards, exact
group matching, skips, type compatibility, value preservation, counts, and atomic
failure reporting. Transactions in the doubles restore an in-memory snapshot.

Run: dotnet run --project tests/ParameterCopy.Tests/ParameterCopy.Tests.csproj -c Release

These are command-level tests, not an Autodesk Revit integration test.
The live preview entry point is scripts/preview-parameter-copy.ps1.
It uses the official MCP SDK and the registered tool; it never writes the model.
