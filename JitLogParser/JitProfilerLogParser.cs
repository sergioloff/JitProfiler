using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace JitLogParser
{
    /// <summary>
    /// Parser for JIT profiler JSON log files that reconstructs MethodBase objects from captured metadata.
    /// </summary>
    public class JitProfilerLogParser
    {
        /// <summary>
        /// Parses the three profiler JSON log files and returns MethodBase objects for each JIT-compiled method.
        /// </summary>
        /// <param name="jitFilePath">Path to the jit.json file containing JITCompilationStarted events</param>
        /// <param name="modulesFilePath">Path to the modules.json file containing module/assembly mappings</param>
        /// <param name="enter3FilePath">Path to the enter3.json file containing detailed method metadata</param>
        /// <param name="executablePath">Path to the profiled executable (used to set assembly resolution context)</param>
        /// <param name="errors">Output parameter containing any parsing errors (multiline string)</param>
        /// <returns>Array of MethodBase objects, one per JIT-compiled method, or empty array if parsing fails</returns>
        public static MethodBase[] ParseProfilerLogs(string jitFilePath, string modulesFilePath, string enter3FilePath, string executablePath, out string errors)
        {
            var errorList = new List<string>();
            var methods = new List<MethodBase>();
            errors = "";

            try
            {
                // Step 1: Parse modules file to build ModuleID -> Assembly mapping
                var moduleMap = ParseModulesFile(modulesFilePath, errorList);

                // Step 2: Parse enter3 file to build FunctionID -> Enter3Message mapping
                var functionMap = ParseEnter3File(enter3FilePath, errorList);

                // Step 3: Parse jit file to get FunctionIDs that were JIT compiled
                var jitFunctionIds = ParseJitFile(jitFilePath, errorList);

                // Create custom assembly load context for resolution
                var loadContext = new ProfilerAssemblyLoadContext(executablePath);
                if (!String.IsNullOrEmpty(loadContext.ModuleInspectError))
                    errors += loadContext.ModuleInspectError + "\r\n";

                try
                {
                    // Step 4: For each JIT-compiled function, try to resolve its MethodBase
                    foreach (var functionId in jitFunctionIds)
                    {
                        if (functionMap.TryGetValue(functionId, out var enter3Message))
                        {
                            try
                            {
                                var methodBase = ResolveMethodBase(enter3Message, moduleMap, loadContext, errorList);
                                if (methodBase != null)
                                {
                                    methods.Add(methodBase);
                                }
                            }
                            catch (Exception ex)
                            {
                                errorList.Add($"Failed to resolve method for FunctionID 0x{functionId:X}: {ex.Message}");
                            }
                        }
                        else
                        {
                            errorList.Add($"FunctionID 0x{functionId:X} from JIT log not found in Enter3 log");
                        }
                    }
                }
                finally
                {
                    loadContext.Finish();
                }
            }
            catch (Exception ex)
            {
                errorList.Add($"Critical error during parsing: {ex.Message}");
            }

            errors += string.Join(Environment.NewLine, errorList);
            return methods.ToArray();
        }

        #region Assembly Load Context

        /// <summary>
        /// Custom assembly load context that resolves assemblies from the profiled application's directory
        /// </summary>
        private class ProfilerAssemblyLoadContext
        {
            private readonly string _exeDir;
            private readonly Dictionary<string, Assembly> _loadedAssemblies = new Dictionary<string, Assembly>();
            private readonly Dictionary<string, string> _assemblyMap;
            public readonly string ModuleInspectError;

            public ProfilerAssemblyLoadContext(string baseDirectory)
            {
                _exeDir = baseDirectory;
                _assemblyMap = GetAssemblyNameToFilePathMap(_exeDir, out ModuleInspectError);
            }

            public void Finish()
            {
                Environment.CurrentDirectory = _exeDir;
                _loadedAssemblies.Clear();
            }

            public Assembly LoadAssembly(ModuleMessage moduleMessage)
            {
                var ass = LoadAssemblyByName(moduleMessage);
                if (ass != null)
                    return ass;

                ass = LoadAssemblyByPath(moduleMessage.ModuleName);
                return ass;
            }

            /// <summary>
            /// Load assembly by name, using the profiled app's directory for resolution
            /// </summary>
            private Assembly LoadAssemblyByName(ModuleMessage moduleMessage)
            {
                string assemblyName = moduleMessage.AssemblyName;
                if (_loadedAssemblies.TryGetValue(assemblyName, out var cached))
                {
                    return cached;
                }

                Assembly assembly = null;
                try
                {
                    // First, try to load by assembly name (works for core libraries like mscorlib, System.*)
                    var asmName = new AssemblyName(assemblyName);
                    assembly = Assembly.Load(asmName);
                    if (assembly != null)
                    {
                        _loadedAssemblies[assemblyName] = assembly;
                        _loadedAssemblies[assembly.Location] = assembly;
                        return assembly;
                    }
                }
                catch
                {
                    // Ignore and try path-based loading
                }

                // is it known app assembly, get version-independent
                if (_assemblyMap.TryGetValue(moduleMessage.AssemblyName, out var path))
                {
                    return LoadAssemblyByPath(path);
                }

                return assembly;
            }

            /// <summary>
            /// Load assembly from specific module path (fallback for when assembly name resolution fails)
            /// </summary>
            private Assembly LoadAssemblyByPath(string modulePath)
            {
                if (_loadedAssemblies.TryGetValue(modulePath, out var cached))
                {
                    return cached;
                }

                try
                {
                    if (File.Exists(modulePath))
                    {
                        var assembly = Assembly.LoadFrom(modulePath);
                        if (assembly != null)
                        {
                            _loadedAssemblies[modulePath] = assembly;
                            _loadedAssemblies[assembly.FullName] = assembly;
                            return assembly;
                        }
                    }
                }
                catch
                {
                    // Ignore
                }

                return null;
            }
        }

        #endregion

        #region Parsing Methods

        /// <summary>
        /// Generic method to parse JSON log files line-by-line
        /// </summary>
        private static TResult ParseJsonLogFile<TMessage, TResult>(
            string filePath,
            string fileDescription,
            TResult defaultResult,
            Func<TMessage, TResult, bool> processMessage,
            List<string> errors)
        {
            if (!File.Exists(filePath))
            {
                errors.Add($"{fileDescription} file not found: {filePath}");
                return defaultResult;
            }

            try
            {
                var lines = File.ReadAllLines(filePath);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = false
                };

                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    try
                    {
                        var msg = JsonSerializer.Deserialize<TMessage>(line, options);
                        if (msg != null)
                        {
                            if (!processMessage(msg, defaultResult))
                            {
                                // If processMessage returns false, it means there was an error processing
                                // but we continue to the next line
                            }
                        }
                    }
                    catch (JsonException ex)
                    {
                        errors.Add($"JSON parse error in {fileDescription} file: {ex.Message} | Line: {line}");
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Error reading {fileDescription} file: {ex.Message}");
            }

            return defaultResult;
        }

        private static Dictionary<ulong, ModuleMessage> ParseModulesFile(string filePath, List<string> errors)
        {
            var moduleMap = new Dictionary<ulong, ModuleMessage>();
            return ParseJsonLogFile<ModuleMessage, Dictionary<ulong, ModuleMessage>>(
                filePath,
                "Modules",
                moduleMap,
                (msg, map) =>
                {
                    map[msg.ModuleID] = msg;
                    return true;
                },
                errors);
        }

        private static Dictionary<ulong, Enter3Message> ParseEnter3File(string filePath, List<string> errors)
        {
            var functionMap = new Dictionary<ulong, Enter3Message>();
            return ParseJsonLogFile<Enter3Message, Dictionary<ulong, Enter3Message>>(
                filePath,
                "Enter3",
                functionMap,
                (msg, map) =>
                {
                    map[msg.FunctionID] = msg;
                    return true;
                },
                errors);
        }

        private static HashSet<ulong> ParseJitFile(string filePath, List<string> errors)
        {
            var functionIds = new HashSet<ulong>();
            return ParseJsonLogFile<JitMessage, HashSet<ulong>>(
                filePath,
                "JIT",
                functionIds,
                (msg, set) =>
                {
                    set.Add(msg.FunctionID);
                    return true;
                },
                errors);
        }

        #endregion

        #region Method Resolution

        /// <summary>
        /// Loads assembly for a module, with error handling
        /// </summary>
        private static bool EnsureModuleAssemblyLoaded(ModuleMessage moduleMessage, ProfilerAssemblyLoadContext loadContext, List<string> errors)
        {
            if (moduleMessage.LoadedAssembly != null)
                return true;

            try
            {
                moduleMessage.LoadedAssembly = loadContext.LoadAssembly(moduleMessage);
                if (moduleMessage.LoadedAssembly == null)
                {
                    errors.Add($"Failed to load assembly {moduleMessage.AssemblyName} (path: {moduleMessage.ModuleName})");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to load assembly {moduleMessage.AssemblyName}: {ex.Message}");
                return false;
            }
        }

        private static MethodBase ResolveMethodBase(Enter3Message enter3Msg, Dictionary<ulong, ModuleMessage> moduleMap, ProfilerAssemblyLoadContext loadContext, List<string> errors)
        {
            // Get the module containing the declaring type
            if (!moduleMap.TryGetValue(enter3Msg.DeclaringTypeModuleID, out ModuleMessage typeModuleMessage))
            {
                errors.Add($"Module 0x{enter3Msg.DeclaringTypeModuleID:X} not found for declaring type token 0x{enter3Msg.DeclaringTypeToken:X}");
                return null;
            }

            // Load the assembly containing the declaring type if needed
            if (!EnsureModuleAssemblyLoaded(typeModuleMessage, loadContext, errors))
                return null;

            try
            {
                // Get the module from the assembly
                var typeModule = typeModuleMessage.LoadedAssembly.ManifestModule;

                // Resolve the declaring type using its token
                var declaringType = typeModule.ResolveType((int)enter3Msg.DeclaringTypeToken);

                // If the declaring type is generic and has type arguments, construct the closed generic type
                if (enter3Msg.DeclaringTypeArgCount > 0 && declaringType.IsGenericTypeDefinition)
                {
                    var typeArgs = ResolveTypeArguments(enter3Msg.DeclaringTypeArgs, moduleMap, loadContext, errors);
                    if (typeArgs == null)
                        return null;

                    declaringType = ConstructGenericType(declaringType, typeArgs, enter3Msg.DeclaringTypeArgCount, errors);
                    if (declaringType == null)
                        return null;
                }

                //// Now get the module containing the method
                //if (!moduleMap.TryGetValue(enter3Msg.ModuleID, out var methodModuleMessage))
                //{
                //    errors.Add($"Module 0x{enter3Msg.ModuleID:X} not found for method token 0x{enter3Msg.MethodToken:X}");
                //    return null;
                //}

                //if (!EnsureModuleAssemblyLoaded(methodModuleMessage, loadContext, errors))
                //    return null;

                //var methodModule = methodModuleMessage.LoadedAssembly.ManifestModule;

                // Resolve the method using the metadata token from the method's module
                MethodBase method;
                // If we constructed a closed generic declaring type, find the corresponding method on it
                if (enter3Msg.DeclaringTypeArgCount > 0 && declaringType.IsConstructedGenericType)
                {
                    var closedMethod = FindMethodOnClosedGenericType(declaringType, (int)enter3Msg.MethodToken);
                    if (closedMethod != null)
                    {
                        method = closedMethod;
                    }
                    else
                    {
                        errors.Add($"Could not find method with token 0x{enter3Msg.MethodToken:X} on closed generic type {declaringType}");
                        return null;
                    }
                }
                else
                {
                    method = declaringType.Module.ResolveMethod((int)enter3Msg.MethodToken);
                }

                // If the method itself is generic, try to construct it with the captured type arguments
                if (enter3Msg.MethodTypeArgCount > 0 && method is MethodInfo methodInfo && !methodInfo.IsConstructedGenericMethod)
                {
                    try
                    {
                        var methodTypeArgs = ResolveTypeArguments(enter3Msg.MethodTypeArgs, moduleMap, loadContext, errors);
                        if (methodTypeArgs != null)
                        {
                            method = ConstructGenericMethod(methodInfo, methodTypeArgs, enter3Msg.MethodTypeArgCount, errors);
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Failed to construct generic method {method.Name}: {ex.Message}");
                    }
                }

                return method;
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to resolve method token 0x{enter3Msg.MethodToken:X}: {ex.Message}");
                return null;
            }
        }

        private static MethodBase FindMethodOnClosedGenericType(Type closedGenericType, int metadataToken)
        {

            var bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

            // Search methods
            foreach (var method in closedGenericType.GetMethods(bindingFlags))
            {
                if (method.MetadataToken == metadataToken)
                    return method;
            }

            // Search constructors
            foreach (var ctor in closedGenericType.GetConstructors(bindingFlags))
            {
                if (ctor.MetadataToken == metadataToken)
                    return ctor;
            }

            return null;
        }

        /// <summary>
        /// Unified type resolution method that handles both single types and collections with recursive generic type construction
        /// </summary>
        private static Type ResolveTypeFromInfo(TypeArgMessage typeArgMsg, Dictionary<ulong, ModuleMessage> moduleMap, ProfilerAssemblyLoadContext loadContext, List<string> errors)
        {
            if (!moduleMap.TryGetValue(typeArgMsg.ModuleID, out var moduleMessage))
            {
                errors.Add($"Module 0x{typeArgMsg.ModuleID:X} not found for type 0x{typeArgMsg.TypeDef:X}");
                return null;
            }

            if (!EnsureModuleAssemblyLoaded(moduleMessage, loadContext, errors))
                return null;

            try
            {
                var module = moduleMessage.LoadedAssembly.ManifestModule;
                var type = module.ResolveType((int)typeArgMsg.TypeDef);

                // If this type has nested generic arguments, construct the closed generic type recursively
                if (typeArgMsg.Nested != null && typeArgMsg.Nested.Count > 0 && type.IsGenericTypeDefinition)
                {
                    var nestedTypes = ResolveTypeArguments(typeArgMsg.Nested, moduleMap, loadContext, errors);
                    if (nestedTypes == null)
                        return null;

                    type = ConstructGenericType(type, nestedTypes, typeArgMsg.Nested.Count, errors);
                }

                return type;
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to resolve type 0x{typeArgMsg.TypeDef:X} in module {moduleMessage.ModuleName}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Resolves a collection of type arguments by delegating to the unified type resolver
        /// </summary>
        private static Type[] ResolveTypeArguments(List<TypeArgMessage> typeArgMessages, Dictionary<ulong, ModuleMessage> moduleMap, ProfilerAssemblyLoadContext loadContext, List<string> errors)
        {
            var types = new List<Type>();
            foreach (var typeArgMsg in typeArgMessages)
            {
                var type = ResolveTypeFromInfo(typeArgMsg, moduleMap, loadContext, errors);
                if (type == null)
                {
                    errors.Add($"Failed to resolve type argument for ModuleID=0x{typeArgMsg.ModuleID:X}, TypeDef=0x{typeArgMsg.TypeDef:X}");
                    return null;
                }
                types.Add(type);
            }
            return types.ToArray();
        }

        /// <summary>
        /// Unified generic type construction with validation
        /// </summary>
        private static Type ConstructGenericType(Type genericTypeDefinition, Type[] typeArguments, int expectedCount, List<string> errors)
        {
            if (typeArguments.Length != expectedCount)
            {
                errors.Add($"Type argument count mismatch for {genericTypeDefinition.Name}: expected {expectedCount}, got {typeArguments.Length}");
                return null;
            }

            try
            {
                return genericTypeDefinition.MakeGenericType(typeArguments);
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to construct generic type {genericTypeDefinition.Name}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Unified generic method construction with validation
        /// </summary>
        private static MethodInfo ConstructGenericMethod(MethodInfo genericMethodDefinition, Type[] typeArguments, int expectedCount, List<string> errors)
        {
            if (typeArguments.Length != expectedCount)
            {
                errors.Add($"Method type argument count mismatch for {genericMethodDefinition.Name}: expected {expectedCount}, got {typeArguments.Length}");
                return null;
            }

            try
            {
                return genericMethodDefinition.MakeGenericMethod(typeArguments);
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to construct generic method {genericMethodDefinition.Name}: {ex.Message}");
                return null;
            }
        }

        #endregion

        public static Dictionary<string, string> GetAssemblyNameToFilePathMap(string folder, out string error)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var errorBuilder = new StringBuilder();

            foreach (var file in Directory.EnumerateFiles(folder, "*.dll", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var assemblyName = AssemblyName.GetAssemblyName(file);
                    map[assemblyName.Name] = file;
                }
                catch (BadImageFormatException)
                {
                    // Not a .NET managed assembly; skip without logging error
                    continue;
                }
                catch (Exception ex)
                {
                    // Collect error details for all other exceptions
                    errorBuilder.AppendLine($"{file}: {ex.GetType().Name} - {ex.Message}");
                    continue;
                }
            }

            error = errorBuilder.ToString();
            return map;
        }
    }
}
