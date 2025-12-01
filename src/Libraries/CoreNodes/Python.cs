using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Autodesk.DesignScript.Runtime;
using Dynamo.PythonServices;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace DSCore
{
    /// <summary>
    /// Evaluate python code on any Python engine. Should only be used in VM
    /// </summary>
    [IsVisibleInDynamoLibrary(false)]
    public class PythonEvaluator
    {

        public class CachedCompilation : IDisposable
        {
            public string Code { get; }
            private readonly AssemblyLoadContext _loadContext;
            private MethodInfo _mainMethod;
            public IReadOnlyList<string> Parameters { get; private set; }

            public CachedCompilation(string code, Assembly assembly, AssemblyLoadContext loadContext, MethodInfo mainMethod)
            {
                Code = code;
                _loadContext = loadContext;
                _mainMethod = mainMethod;

                Parameters = _mainMethod.GetParameters()
                            .Select(p => p.Name)
                            .ToList()
                            .AsReadOnly();
            }

            public object Invoke(IList args)
            {
                return _mainMethod.Invoke(null, args.Cast<object>().ToArray());
            }

            public void Dispose()
            {
                _mainMethod = null;
                Parameters = null;
                _loadContext.Unload();
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        // Custom AssemblyLoadContext that supports unloading
        public class CollectibleAssemblyLoadContext : AssemblyLoadContext
        {
            public CollectibleAssemblyLoadContext() : base(isCollectible: true) { }
        }

        private static CachedCompilation Compile(string code)
        {
            var options = ScriptOptions.Default
                .WithImports("System", "System.Linq", "System.Collections.Generic")
                .WithReferences(AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location)));

            var script = CSharpScript.Create(code, options);
            var compilation = script.GetCompilation();

            using var assemblyStream = new MemoryStream();
            var emitResult = compilation.Emit(assemblyStream);

            if (!emitResult.Success)
            {
                var errors = string.Join("\n", emitResult.Diagnostics);
                throw new InvalidOperationException($"Compilation failed:\n{errors}");
            }

            assemblyStream.Seek(0, SeekOrigin.Begin);

            // Use collectible AssemblyLoadContext
            var context = new CollectibleAssemblyLoadContext();
            var assembly = context.LoadFromStream(assemblyStream);

            var mainMethod = assembly.GetTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                .FirstOrDefault(m => m.Name == "Main");

            if (mainMethod == null)
                throw new InvalidOperationException("No static Main method found.");

            return new CachedCompilation(code, assembly, context, mainMethod);
        }

        private static CachedCompilation CompileForGuidInternal(string GUID, string code)
        {
            CachedCompilation cached;
            _compilationCache.TryGetValue(GUID, out cached);
            if (cached == null || cached.Code != code){
                cached?.Dispose();
                _compilationCache.Remove(GUID);
                var newCompilation = Compile(code);
                _compilationCache[GUID] = newCompilation;
            }
            return _compilationCache[GUID];
        }

        public static IReadOnlyList<string> ParametersFromCode(string GUID, string code)
        {
            return CompileForGuidInternal(GUID, code).Parameters;
        }

        private static readonly Dictionary<string, CachedCompilation> _compilationCache = new();
        public static object Evaluate(string GUID, string engineName,
                                      string code,
                                      IList bindingNames,
                                      [ArbitraryDimensionArrayImport] IList bindingValues)
        {
            //if (!string.Equals(engineName, "CSharp", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Only C# engine is supported.");

            try
            {
                var args = (IList)bindingValues.Cast<object>().ToList()[1];

                return CompileForGuidInternal(GUID, code).Invoke(args);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Execution failed: {ex.Message}", ex);
            }
        }
        public static object GetTupleField(object tuple, int idx)
        {
            var type = tuple.GetType();

            var field = type.GetField("Item" + idx);

            if (field == null)
                throw new ArgumentException($"Field '{idx}' not found in tuple type '{type}'.");

            return field.GetValue(tuple);
        }
    }
}
