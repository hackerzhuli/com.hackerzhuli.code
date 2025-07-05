using System;
using System.IO;
using System.Text;
using Hackerzhuli.Code.Editor.Hash;
using UnityEditor.TestTools.TestRunner.Api;

namespace Hackerzhuli.Code.Editor.Testing
{
    public static class TestAdaptorUtils
    {
        /// <summary>
        ///     Get the assembly name that this test belongs to, if it is an assembly, or not in an assembly, return empty string
        /// </summary>
        /// <param name="testAdaptor"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public static string GetAssemblyName(this ITestAdaptor testAdaptor)
        {
            if (testAdaptor.IsTestAssembly) return "";
            if (testAdaptor.Parent == null) return "";
            if (testAdaptor.Parent.IsTestAssembly) return Path.GetFileNameWithoutExtension(testAdaptor.Parent.FullName);
            return GetAssemblyName(testAdaptor.Parent);
        }

        /// <summary>
        ///     Get the test mode of this test, if it is an assembly, return TestMode.RunInEditMode
        /// </summary>
        /// <param name="testAdaptor"></param>
        /// <returns></returns>
        /// <remarks>
        ///     Some test adaptor may not have TestMode correctly set by test framework(eg. set to 0), so we need to fix it here
        /// </remarks>
        public static TestMode GetMode(this ITestAdaptor testAdaptor)
        {
            var mode = testAdaptor.TestMode;
            if (mode != default) return mode;
            if (testAdaptor.Parent == null) return mode;
            return testAdaptor.Parent.GetMode();
        }

        /// <summary>
        ///     Get the test node type of this test
        /// </summary>
        /// <param name="testAdaptor"></param>
        /// <returns></returns>
        public static TestNodeType GetNodeType(this ITestAdaptor testAdaptor)
        {
            // The code looks odd because sometimes methods have type info but sometimes methods don't have type info
            // It is inconsistent from Unity Test Framework
            // Do our best to be accurate
            if (testAdaptor.IsTestAssembly) return TestNodeType.Assembly;

            if (testAdaptor.TypeInfo != null)
            {
                if (testAdaptor.Arguments is { Length: > 0 }) return TestNodeType.TestCase;

                if (testAdaptor.Method == null) return TestNodeType.Class;

                return TestNodeType.Method;
            }

            if (testAdaptor.Arguments is { Length: > 0 }) return TestNodeType.TestCase;

            if (testAdaptor.Method != null) return TestNodeType.Method;

            if (testAdaptor.Parent == null) return TestNodeType.Solution;

            return TestNodeType.Namespace;
        }

        /// <summary>
        ///     Get the string representation of TestMode without allocation
        /// </summary>
        /// <param name="mode">The test mode</param>
        /// <returns>ReadOnlySpan of chars representing the mode</returns>
        public static string GetModeString(this TestMode mode)
        {
            return mode switch
            {
                TestMode.EditMode => "EditMode",
                TestMode.PlayMode => "PlayMode",
                _ => "Unknown"
            };
        }

        /// <summary>
        ///     Get a unique id for test that will persist across compiles and not conflict across test modes <br />
        ///     It is allocation free (in most cases) except for the return string
        /// </summary>
        /// <param name="testAdaptor"></param>
        /// <returns>A unique id</returns>
        public static unsafe string GetId(this ITestAdaptor testAdaptor)
        {
            // Optimization: totally allocation free (most of the time) except for the returned string
            // Create the original long ID format without string allocation
            var mode = GetModeString(GetMode(testAdaptor));
            var uniqueName = testAdaptor.UniqueName;

            // Use stackalloc for the combined string chars to avoid allocation if possible
            var totalLength = mode.Length + 1 + uniqueName.Length;
            // it can go to a few hundred characters but longer than that is unlikely
            var originalIdChars = totalLength < 1024 ? stackalloc char[totalLength] : new char[totalLength];

            // Manually construct the string: "mode/uniqueName"
            var pos = 0;
            mode.AsSpan().CopyTo(originalIdChars[pos..]);
            pos += mode.Length;
            originalIdChars[pos++] = '/';
            uniqueName.AsSpan().CopyTo(originalIdChars[pos..]);

            // Use stackalloc for temporary UTF8 bytes to avoid allocation if possible
            var maxByteCount = Encoding.UTF8.GetMaxByteCount(totalLength);
            // typically it is the same as totalLength as Unicode is rarely used in class/method names etc.
            var utf8Bytes = maxByteCount < 2048 ? stackalloc byte[maxByteCount] : new byte[maxByteCount];
            var actualByteCount = Encoding.UTF8.GetBytes(originalIdChars, utf8Bytes);
            var hash = xxHash64.ComputeHash(utf8Bytes, actualByteCount);

            // Convert to base64 for a shorter string representation using stackalloc
            Span<byte> hashBytes = stackalloc byte[8];
            BitConverter.TryWriteBytes(hashBytes, hash);
            return Convert.ToBase64String(hashBytes);
        }
    }
}