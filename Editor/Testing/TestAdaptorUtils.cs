
using System;
using System.IO;
using UnityEditor.Experimental.GraphView;
using UnityEditor.TestTools.TestRunner.Api;
using Hackerzhuli.Code.Editor.Hash;
using System.Text;

namespace Hackerzhuli.Code.Editor.Testing{
    public static class TestAdaptorUtils
    {
        /// <summary>
        /// Get the assembly name that this test belongs to, if it is an assembly, or not in an assembly, return empty string
        /// </summary>
        /// <param name="testAdaptor"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public static string GetAssemblyName(this ITestAdaptor testAdaptor)
        {
            if(testAdaptor.IsTestAssembly){
				return "";
			}
			if(testAdaptor.Parent == null){
				return "";
			}
			if(testAdaptor.Parent.IsTestAssembly){
				return Path.GetFileNameWithoutExtension(testAdaptor.Parent.FullName);
			}
			return GetAssemblyName(testAdaptor.Parent);
        }

        /// <summary>
        /// Get the test mode of this test, if it is an assembly, return TestMode.RunInEditMode
        /// </summary>
        /// <param name="testAdaptor"></param>
        /// <returns></returns>
        /// <remarks>
        /// Some test adaptor may not have TestMode correctly set by test framework(eg. set to 0), so we need to fix it here
        /// </remarks>
        public static TestMode GetMode(this ITestAdaptor testAdaptor){
            var mode = testAdaptor.TestMode;
            if(mode != default){
                return mode;
            }
            if(testAdaptor.Parent == null){
                return mode;
            }
            return testAdaptor.Parent.GetMode();
        }

        /// <summary>
        /// Get the test node type of this test
        /// </summary>
        /// <param name="testAdaptor"></param>
        /// <returns></returns>
        public static TestNodeType GetNodeType(this ITestAdaptor testAdaptor)
        {
            // The code looks odd because sometimes methods have type info but sometimes methods don't have type info
            // It is inconsistent from Unity Test Framework
            // Do our best to be accurate
            if (testAdaptor.IsTestAssembly)
            {
                return TestNodeType.Assembly;
            }
            else if (testAdaptor.TypeInfo != null)
            {
                if(testAdaptor.Arguments is {Length: > 0})
                {
                    return TestNodeType.TestCase;
                }
                else if(testAdaptor.Method == null)
                {
                    return TestNodeType.Class;
                }
                else
                {
                    return TestNodeType.Method;
                }
            }
            else if(testAdaptor.Arguments is {Length: > 0})
            {
                return TestNodeType.TestCase;
            }
            else if(testAdaptor.Method != null)
            {
                return TestNodeType.Method;
            }
            else if (testAdaptor.Parent == null)
            {
                return TestNodeType.Solution;
            }
            else 
            {
                return TestNodeType.Namespace;
            }
        }

        /// <summary>
        /// Get a unique id for test that will persist across compiles and not conflict across test modes
        /// </summary>
        /// <param name="testAdaptor"></param>
        /// <returns></returns>
        public static string GetId(this ITestAdaptor testAdaptor){
            // Create the original long ID format(which can be 100 bytes or longer)
            var originalId = $"{GetMode(testAdaptor)}/{testAdaptor.UniqueName}";

            var bytes = Encoding.UTF8.GetBytes(originalId);
            
            // Hash it using xxHash64 to get a fixed-size hash
            var hash = xxHash64.ComputeHash(bytes);
            
            // Convert to base64 for a shorter string representation
            var hashBytes = BitConverter.GetBytes(hash);
            return Convert.ToBase64String(hashBytes);
        }
    }
}
