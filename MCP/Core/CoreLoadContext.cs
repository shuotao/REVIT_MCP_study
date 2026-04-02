using System.IO;
using System.Reflection;
#if NET8_0_OR_GREATER
using System.Runtime.Loader;
#endif

namespace RevitMCP.Core
{
#if NET8_0_OR_GREATER
    public class CoreLoadContext : AssemblyLoadContext
    {
        private readonly string _baseDirectory;

        public CoreLoadContext(string baseDirectory) : base(isCollectible: true)
        {
            _baseDirectory = baseDirectory;
        }

        protected override Assembly Load(AssemblyName assemblyName)
        {
            string candidatePath = Path.Combine(_baseDirectory, assemblyName.Name + ".dll");
            if (File.Exists(candidatePath))
            {
                return LoadFromAssemblyPath(candidatePath);
            }

            return null;
        }
    }
#endif
}
