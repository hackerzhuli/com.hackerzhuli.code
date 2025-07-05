using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using UnityEngine;

namespace Hackerzhuli.Code.Editor
{
	/// <summary>
	///     Represents information needed to open a file at a specific location.
	/// </summary>
	internal class FileOpenInfo
    {
	    /// <summary>
	    ///     Gets or sets the full path to the file.
	    /// </summary>
	    public string FilePath { get; set; }

	    /// <summary>
	    ///     Gets or sets the line number within the file (1-based).
	    /// </summary>
	    public int LineNumber { get; set; }
    }

	/// <summary>
	///     Provides utilities for working with Mono.Cecil to extract debug information from assemblies.
	///     This class helps locate source file positions for methods using debug symbols.
	///     IMPORTANT: This class implements IDisposable and must be disposed when no longer needed
	///     to prevent DLL file handles from remaining open, which can interfere with Unity's ability
	///     to recompile or update assemblies.
	/// </summary>
	/// <remarks>
	///     Usage pattern:
	///     <code>
	/// using (var helper = new MonoCecilHelper())
	/// {
	///     var fileInfo = helper.TryGetCecilFileOpenInfo(type, methodInfo);
	///     // Use fileInfo...
	/// } // Automatically disposes and releases file handles
	/// </code>
	/// </remarks>
	internal class MonoCecilHelper : IDisposable
    {
        private readonly Dictionary<string, AssemblyDefinition> _assemblyCache = new();
        private bool _disposed;

        /// <summary>
        ///     Releases all resources used by the MonoCecilHelper.
        ///     This will dispose all cached AssemblyDefinition objects and clear the cache.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        ///     Gets the first sequence point for a method definition, handling async/await state machines.
        /// </summary>
        /// <param name="methodDefinition">The method definition to analyze.</param>
        /// <returns>
        ///     The first non-hidden sequence point if found; otherwise, null.
        ///     For async methods, this will attempt to find the sequence point in the generated state machine.
        /// </returns>
        /// <remarks>
        ///     This method handles the complexity of async/await methods where the actual debug information
        ///     is stored in compiler-generated state machine classes rather than the original method.
        /// </remarks>
        private static SequencePoint GetMethodFirstSequencePoint(MethodDefinition methodDefinition)
        {
            if (methodDefinition == null)
            {
                Debug.Log(
                    "MethodDefinition cannot be null. Check if any method was found by name in its declaring type TypeDefinition.");
                return null;
            }

            if (!methodDefinition.HasBody || !methodDefinition.Body.Instructions.Any() ||
                methodDefinition.DebugInformation == null)
            {
                Debug.Log(
                    $"To get SequencePoints MethodDefinition for {methodDefinition.Name} must have MethodBody, DebugInformation and Instructions.");
                return null;
            }

            if (!methodDefinition.DebugInformation.HasSequencePoints)
            {
                // Try to find the actual method with sequence points in nested types (for state machines)
                MethodDefinition actualMethod = null;
                foreach (var nestedType in methodDefinition.DeclaringType.NestedTypes)
                {
                    foreach (var method in nestedType.Methods)
                        if (method.DebugInformation != null &&
                            method.DebugInformation.StateMachineKickOffMethod == methodDefinition && method.HasBody &&
                            method.Body.Instructions.Count > 0)
                        {
                            actualMethod = method;
                            break;
                        }

                    if (actualMethod != null)
                        break;
                }

                if (actualMethod != null)
                {
                    methodDefinition = actualMethod;
                }
                else
                {
                    Debug.Log("No SequencePoints for MethodDefinition for " + methodDefinition.Name);
                    return null;
                }
            }

            // Find the first non-hidden sequence point
            foreach (var instruction in methodDefinition.Body.Instructions)
            {
                var sequencePoint = methodDefinition.DebugInformation.GetSequencePoint(instruction);
                if (sequencePoint != null && !sequencePoint.IsHidden)
                    return sequencePoint;
            }

            return null;
        }

        /// <summary>
        ///     Reads an assembly definition from the specified path with debug symbol information.
        ///     This method uses instance-level caching to avoid reloading the same assembly multiple times
        ///     within the lifetime of this MonoCecilHelper instance.
        /// </summary>
        /// <param name="assemblyPath">The full path to the assembly file to read.</param>
        /// <returns>
        ///     An <see cref="AssemblyDefinition" /> with debug symbols loaded if successful; otherwise, null.
        /// </returns>
        /// <remarks>
        ///     This method configures the assembly reader to:
        ///     - Load debug symbols (PDB/MDB files)
        ///     - Use deferred reading mode for better performance
        ///     - Resolve dependencies from the assembly's directory
        ///     - Cache loaded assemblies within this instance to improve performance for repeated access
        /// </remarks>
        private AssemblyDefinition ReadAssembly(string assemblyPath)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(MonoCecilHelper));

            // Check if assembly is already cached
            if (_assemblyCache.TryGetValue(assemblyPath, out var cachedAssembly)) return cachedAssembly;

            using var assemblyResolver = new DefaultAssemblyResolver();
            assemblyResolver.AddSearchDirectory(Path.GetDirectoryName(assemblyPath));
            var parameters = new ReaderParameters
            {
                ReadSymbols = true,
                SymbolReaderProvider = new DefaultSymbolReaderProvider(false),
                AssemblyResolver = assemblyResolver,
                ReadingMode = ReadingMode.Deferred
            };
            try
            {
                var assemblyDefinition = AssemblyDefinition.ReadAssembly(assemblyPath, parameters);

                // Cache the loaded assembly
                _assemblyCache[assemblyPath] = assemblyDefinition;

                return assemblyDefinition;
            }
            catch (Exception ex)
            {
                Debug.Log(ex.Message);
                return null;
            }
        }

        /// <summary>
        ///     Attempts to get file opening information for a specific method using Mono.Cecil debug symbols.
        /// </summary>
        /// <param name="type">The type containing the method.</param>
        /// <param name="methodInfo">The method to locate in the source code.</param>
        /// <returns>
        ///     A <see cref="FileOpenInfo" /> object containing the file path and line number where the method is defined.
        ///     If the method cannot be located or debug symbols are not available, returns an object with empty/default values.
        /// </returns>
        /// <remarks>
        ///     This method:
        ///     1. Loads the assembly containing the specified type with debug symbols
        ///     2. Locates the method definition using the metadata token
        ///     3. Finds the first sequence point (source code location) for the method
        ///     4. Handles async/await methods by looking in compiler-generated state machines
        ///     The returned file path will be the original source file path, and the line number
        ///     will be 1-based as it appears in the source code.
        /// </remarks>
        public FileOpenInfo TryGetCecilFileOpenInfo(Type type, MethodInfo methodInfo)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(MonoCecilHelper));

            var assemblyDefinition = ReadAssembly(type.Assembly.Location);
            if (assemblyDefinition == null) return new FileOpenInfo();

            var firstSequencePoint =
                GetMethodFirstSequencePoint(
                    assemblyDefinition.MainModule.LookupToken(methodInfo.MetadataToken) as MethodDefinition);
            var cecilFileOpenInfo = new FileOpenInfo();
            if (firstSequencePoint != null)
            {
                cecilFileOpenInfo.LineNumber = firstSequencePoint.StartLine;
                cecilFileOpenInfo.FilePath = firstSequencePoint.Document.Url;
            }

            return cecilFileOpenInfo;
        }

        /// <summary>
        ///     Attempts to get file opening information for a specific type using Mono.Cecil debug symbols.
        /// </summary>
        /// <param name="type">The type to locate in the source code.</param>
        /// <returns>
        ///     A <see cref="FileOpenInfo" /> object containing the file path and line number where the type is defined.
        ///     If the type cannot be located or debug symbols are not available, returns an object with empty/default values.
        /// </returns>
        /// <remarks>
        ///     This method:
        ///     1. Loads the assembly containing the specified type with debug symbols
        ///     2. Locates the type definition using the metadata token
        ///     3. Finds the first method in the type and gets its sequence point
        ///     4. Returns the file location where the type is defined
        ///     The returned file path will be the original source file path, and the line number
        ///     will be 1-based as it appears in the source code.
        /// </remarks>
        public FileOpenInfo TryGetCecilTypeSourceLocation(Type type)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(MonoCecilHelper));

            var assemblyDefinition = ReadAssembly(type.Assembly.Location);
            if (assemblyDefinition == null) return new FileOpenInfo();

            try
            {
                // Find the type definition
                var typeDefinition =
                    assemblyDefinition.MainModule.Types.FirstOrDefault(t => t.FullName == type.FullName);
                if (typeDefinition == null) return new FileOpenInfo();

                // Find any method with debug information to get the file path
                string filePath = null;

                foreach (var method in typeDefinition.Methods)
                    if (method.HasBody && method.DebugInformation != null && method.DebugInformation.HasSequencePoints)
                    {
                        var firstSequencePoint = GetMethodFirstSequencePoint(method);
                        if (firstSequencePoint != null)
                        {
                            filePath = firstSequencePoint.Document.Url;
                            break; // We only need the file path
                        }
                    }

                // If we found the file path, return line 1 as a simple fallback
                if (!string.IsNullOrEmpty(filePath))
                    return new FileOpenInfo
                    {
                        LineNumber = 1, // Simple fallback: go to top of file
                        FilePath = filePath
                    };
            }
            catch (Exception ex)
            {
                Debug.Log($"Error getting type source location: {ex.Message}");
            }

            return new FileOpenInfo();
        }

        /// <summary>
        ///     Releases the unmanaged resources used by the MonoCecilHelper and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing">
        ///     true to release both managed and unmanaged resources; false to release only unmanaged
        ///     resources.
        /// </param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                foreach (var assembly in _assemblyCache.Values)
                    try
                    {
                        assembly?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        // Log the exception but continue disposing other assemblies
                        // to prevent one failure from affecting others
                        Debug.LogWarning($"Failed to dispose assembly: {ex.Message}");
                    }

                _assemblyCache.Clear();
                _disposed = true;
            }
        }

        /// <summary>
        ///     Gets the number of assemblies currently cached in this instance.
        /// </summary>
        /// <returns>The number of cached assemblies.</returns>
        public int GetCachedAssemblyCount()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(MonoCecilHelper));
            return _assemblyCache.Count;
        }

        /// <summary>
        ///     Gets information about cached assemblies for debugging purposes.
        /// </summary>
        /// <returns>A list of cached assembly paths.</returns>
        public List<string> GetCachedAssemblyPaths()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(MonoCecilHelper));
            return new List<string>(_assemblyCache.Keys);
        }
    }
}