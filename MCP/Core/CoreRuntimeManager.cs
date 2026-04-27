using System;
using System.Collections.Generic;
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
#endif
        private string _shadowRuntimeDirectory;

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
                // .NET 8+ (Revit 2025-2026): Use AssemblyLoadContext for better isolation
                // and support for explicit Unload()
                _shadowRuntimeDirectory = CreateShadowRuntimeDirectory(runtimePath);
                string coreAssemblyPath = Path.Combine(_shadowRuntimeDirectory, "RevitMCP.CoreRuntime.dll");
                Logger.Info($"Shadow-copy 路徑: {_shadowRuntimeDirectory}");

                _loadContext = new CoreLoadContext(_shadowRuntimeDirectory);
                var assembly = _loadContext.LoadFromAssemblyPath(coreAssemblyPath);
#else
                // .NET Framework 4.8 (Revit 2020-2024): Use shadow-copy + AppDomain.AssemblyResolve
                // to avoid DLL locking while supporting dynamic reload capability
                _shadowRuntimeDirectory = CreateShadowRuntimeDirectory(runtimePath);
                string coreAssemblyPath = Path.Combine(_shadowRuntimeDirectory, "RevitMCP.CoreRuntime.dll");
                Logger.Info($"Shadow-copy 路徑: {_shadowRuntimeDirectory}");

                AppDomain.CurrentDomain.AssemblyResolve += ResolveAssemblyFromShadow;
                    byte[] coreBytes = File.ReadAllBytes(sourceCoreAssemblyPath);
                    var assembly = Assembly.Load(coreBytes);
#endif

                var type = assembly.GetType("RevitMCP.CoreRuntime.RevitMcpCoreRuntime");
                if (type == null)
                {
                    throw new InvalidOperationException("CoreRuntime 型別不存在: RevitMCP.CoreRuntime.RevitMcpCoreRuntime");
                }

                _runtime = (IRevitMcpRuntime)Activator.CreateInstance(type);
                _runtime.Initialize(_uiApplication);
                _runtime.SetReloadCallback(() => Application.RequestReloadCoreFromBackground());
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
            Logger.Info("CoreRuntime 開始熱重載...");
            UnloadCore();
            EnsureLoaded();

            if (wasRunning)
            {
                _runtime.StartService();
            }
            Logger.Info("CoreRuntime 熱重載完成");
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
                    Logger.Info("CoreRuntime 卸載中...");
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
#else
                // .NET Framework: Unhook the assembly resolver
                AppDomain.CurrentDomain.AssemblyResolve -= ResolveAssemblyFromShadow;
#endif

                // Clean up shadow directory (both .NET Framework and .NET 8+)
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
            }
        }

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

#if !NET8_0_OR_GREATER
        /// <summary>
        /// AppDomain.AssemblyResolve handler for .NET Framework (Revit 2020-2024)
        /// to resolve assembly dependencies from the shadow-copied runtime directory.
        /// </summary>
        private Assembly ResolveAssemblyFromShadow(object sender, ResolveEventArgs args)
        {
            if (string.IsNullOrWhiteSpace(_shadowRuntimeDirectory))
            {
                return null;
            }

            // Extract simple name from the full assembly name (e.g., "RevitMCP.CoreRuntime" from "RevitMCP.CoreRuntime, Version=1.0...")
            string assemblyName = new AssemblyName(args.Name).Name;
            string assemblyPath = Path.Combine(_shadowRuntimeDirectory, assemblyName + ".dll");

            if (File.Exists(assemblyPath))
            {
                try
                {
                    return Assembly.LoadFrom(assemblyPath);
                }
                catch (Exception ex)
                {
                    Logger.Error($"無法從 shadow 目錄加載程式集 {assemblyName}: {ex.Message}", ex);
                }
            }

            return null;
        }
#endif
    }
}
