using System;
using System.IO;
using System.Reflection;
using System.Linq;
using Autodesk.Revit.UI;
using RevitMCP.Contracts;

namespace RevitMCP.Core
{
    public class CoreRuntimeManager
    {
        private readonly object _sync = new object();
        private UIApplication _uiApplication;
        private IRevitMcpRuntime _runtime;

#if NET8_0_OR_GREATER
        private CoreLoadContext _loadContext;
    private string _shadowRuntimeDirectory;
#endif

        public bool IsLoaded => _runtime != null;
        public bool IsRunning => _runtime != null && _runtime.IsRunning;
        public bool IsConnected => _runtime != null && _runtime.IsConnected;

        public void SetUIApplication(UIApplication uiApplication)
        {
            _uiApplication = uiApplication;
        }

        public void EnsureLoaded()
        {
            if (_runtime != null)
            {
                return;
            }

            lock (_sync)
            {
                if (_runtime != null)
                {
                    return;
                }

                if (_uiApplication == null)
                {
                    throw new InvalidOperationException("UIApplication 尚未準備好");
                }

                string loaderPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string runtimePath = Path.Combine(loaderPath, "runtime");
                string sourceCoreAssemblyPath = Path.Combine(runtimePath, "RevitMCP.CoreRuntime.dll");

                if (!File.Exists(sourceCoreAssemblyPath))
                {
                    throw new FileNotFoundException($"找不到 CoreRuntime: {sourceCoreAssemblyPath}");
                }

#if NET8_0_OR_GREATER
                // Load from a shadow-copied runtime folder so deployed runtime DLLs
                // can be overwritten while Revit is running.
                _shadowRuntimeDirectory = CreateShadowRuntimeDirectory(runtimePath);
                string coreAssemblyPath = Path.Combine(_shadowRuntimeDirectory, "RevitMCP.CoreRuntime.dll");

                _loadContext = new CoreLoadContext(_shadowRuntimeDirectory);
                var assembly = _loadContext.LoadFromAssemblyPath(coreAssemblyPath);
#else
                var assembly = Assembly.LoadFrom(sourceCoreAssemblyPath);
#endif

                var type = assembly.GetType("RevitMCP.CoreRuntime.RevitMcpCoreRuntime");
                if (type == null)
                {
                    throw new InvalidOperationException("CoreRuntime 型別不存在: RevitMCP.CoreRuntime.RevitMcpCoreRuntime");
                }

                _runtime = (IRevitMcpRuntime)Activator.CreateInstance(type);
                _runtime.Initialize(_uiApplication);
                Logger.Info("CoreRuntime 已載入");
            }
        }

        public void StartService()
        {
            EnsureLoaded();
            _runtime.StartService();
        }

        public void StopService()
        {
            _runtime?.StopService();
        }

        public void ReloadCore()
        {
            bool wasRunning = IsRunning;
            UnloadCore();
            EnsureLoaded();

            if (wasRunning)
            {
                _runtime.StartService();
            }
        }

        public void Shutdown()
        {
            try
            {
                _runtime?.Shutdown();
            }
            catch (Exception ex)
            {
                Logger.Error($"Shutdown CoreRuntime 失敗: {ex.Message}", ex);
            }

            UnloadCore();
        }

        private void UnloadCore()
        {
            lock (_sync)
            {
                if (_runtime != null)
                {
                    try
                    {
                        _runtime.Shutdown();
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"關閉 CoreRuntime 失敗: {ex.Message}", ex);
                    }
                }

                _runtime = null;

#if NET8_0_OR_GREATER
                if (_loadContext != null)
                {
                    _loadContext.Unload();
                    _loadContext = null;

                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                }

                if (!string.IsNullOrWhiteSpace(_shadowRuntimeDirectory) && Directory.Exists(_shadowRuntimeDirectory))
                {
                    try
                    {
                        Directory.Delete(_shadowRuntimeDirectory, true);
                    }
                    catch
                    {
                        // Best-effort cleanup only.
                    }
                    finally
                    {
                        _shadowRuntimeDirectory = null;
                    }
                }
#endif
            }
        }

#if NET8_0_OR_GREATER
        private static string CreateShadowRuntimeDirectory(string sourceRuntimeDirectory)
        {
            string shadowRoot = Path.Combine(Path.GetTempPath(), "RevitMCP", "runtime-shadow");
            Directory.CreateDirectory(shadowRoot);

            string shadowDir = Path.Combine(shadowRoot, DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"));
            Directory.CreateDirectory(shadowDir);

            foreach (string file in Directory.EnumerateFiles(sourceRuntimeDirectory, "*.*", SearchOption.TopDirectoryOnly)
                                           .Where(f => f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                                                    || f.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)
                                                    || f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
            {
                string fileName = Path.GetFileName(file);
                string targetPath = Path.Combine(shadowDir, fileName);
                File.Copy(file, targetPath, true);
            }

            return shadowDir;
        }
#endif
    }
}
