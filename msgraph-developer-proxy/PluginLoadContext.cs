using System.Reflection;
using System.Runtime.Loader;

namespace Microsoft.Graph.DeveloperProxy {
    class PluginLoadContext : AssemblyLoadContext {
        private readonly AssemblyDependencyResolver _resolver;

        public PluginLoadContext(string pluginPath) {
            _resolver = new AssemblyDependencyResolver(pluginPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName) {
            string? assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
            if (assemblyPath is not null) {
                return LoadFromAssemblyPath(assemblyPath);
            }

            return null;
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName) {
            string? libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            if (libraryPath is not null) {
                return LoadUnmanagedDllFromPath(libraryPath);
            }

            return IntPtr.Zero;
        }
    }
}
