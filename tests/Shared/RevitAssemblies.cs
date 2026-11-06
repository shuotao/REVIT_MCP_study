using System;
using System.IO;
using System.Reflection;

namespace RevitMCP.Tests
{
    /// <summary>
    /// Makes RevitAPI.dll / RevitAPIUI.dll loadable outside Revit.
    ///
    /// The Nice3point packages give the compiler reference assemblies but nothing that runs,
    /// so a harness that touches RevitMCP.dll needs the real ones off a local Revit install.
    /// </summary>
    internal static class RevitAssemblies
    {
        /// <summary>Set REVIT_TEST_DIR to point at a Revit install other than the ones probed below.</summary>
        private const string DirEnvVar = "REVIT_TEST_DIR";

        private static readonly string[] ProbeDirs =
        {
            @"C:\Program Files\Autodesk\Revit 2023",
            @"C:\Program Files\Autodesk\Revit 2024",
            @"C:\Program Files\Autodesk\Revit 2025",
            @"C:\Program Files\Autodesk\Revit 2026",
            @"C:\Program Files\Autodesk\Revit 2022",
        };

        /// <summary>
        /// Resolves the Revit install directory, or returns null with a printable reason.
        /// Probing order puts the configured directory first so a machine with several Revit
        /// years installed does not silently test against whichever one sorts first.
        /// </summary>
        internal static string ResolveRevitDir(out string error)
        {
            error = null;

            string configured = Environment.GetEnvironmentVariable(DirEnvVar);
            if (!string.IsNullOrWhiteSpace(configured))
            {
                if (File.Exists(Path.Combine(configured, "RevitAPI.dll"))) return configured;
                error = $"{DirEnvVar} is set to \"{configured}\" but RevitAPI.dll is not there.";
                return null;
            }

            foreach (var dir in ProbeDirs)
            {
                if (File.Exists(Path.Combine(dir, "RevitAPI.dll"))) return dir;
            }

            error = "No Revit install found. Looked in:" + Environment.NewLine
                    + string.Join(Environment.NewLine, ProbeDirs) + Environment.NewLine
                    + $"Set {DirEnvVar} to the directory holding RevitAPI.dll.";
            return null;
        }

        /// <summary>
        /// Installs an AssemblyResolve hook for the Revit assemblies. Returns false (having
        /// printed why) when no install was found, so the caller can exit rather than fail
        /// later with a TypeLoadException that says nothing useful.
        /// </summary>
        internal static bool TryInstallResolver()
        {
            string error;
            string revitDir = ResolveRevitDir(out error);
            if (revitDir == null)
            {
                Console.Error.WriteLine(error);
                return false;
            }

            Console.WriteLine($"[harness] Revit assemblies: {revitDir}");

            // The add-in is referenced with Private=false so it is never copied next to the
            // harness -- that stops a rebuilt MCP from being shadowed by a stale copy, but it
            // also leaves the runtime unable to find it. Directory.Build.props bakes the
            // resolved path in as assembly metadata; read it back here.
            string addinPath = GetBakedAddinPath();
            if (addinPath != null && !File.Exists(addinPath))
            {
                Console.Error.WriteLine($"Add-in not built: {addinPath} is missing."
                    + Environment.NewLine
                    + "  cd MCP && dotnet build -c Release.R23 RevitMCP.csproj");
                return false;
            }
            if (addinPath != null)
            {
                Console.WriteLine($"[harness] Add-in under test: {addinPath}");
            }

            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                string name = new AssemblyName(args.Name).Name;

                if (addinPath != null && string.Equals(name, "RevitMCP", StringComparison.OrdinalIgnoreCase))
                {
                    return Assembly.LoadFrom(addinPath);
                }

                string candidate = Path.Combine(revitDir, name + ".dll");
                return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
            };
            return true;
        }

        private static string GetBakedAddinPath()
        {
            var entry = Assembly.GetEntryAssembly();
            if (entry == null) return null;

            foreach (var attr in entry.GetCustomAttributes(typeof(AssemblyMetadataAttribute), false))
            {
                var meta = (AssemblyMetadataAttribute)attr;
                if (meta.Key == "RevitMcpAssembly") return meta.Value;
            }
            return null;
        }
    }
}
