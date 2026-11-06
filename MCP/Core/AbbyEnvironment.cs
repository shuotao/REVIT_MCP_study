using System;
using System.IO;

namespace RevitMCP.Core
{
    internal static class AbbyEnvironment
    {
        internal static string ResolvePython(string requested = "python")
        {
            if (!string.IsNullOrWhiteSpace(requested) &&
                !string.Equals(requested, "python", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(requested, "python.exe", StringComparison.OrdinalIgnoreCase))
                return requested;
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AbbyMCP", "tools", "python", "python.exe");
            return File.Exists(path) ? path : (string.IsNullOrWhiteSpace(requested) ? "python" : requested);
        }
    }
}
