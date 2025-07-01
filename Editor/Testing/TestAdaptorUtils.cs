
using System.IO;
using UnityEditor.TestTools.TestRunner.Api;

namespace Hackerzhuli.Code.Editor.Testing{
    public static class TestUtils
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
            if (testAdaptor.Parent == null)
            {
                return TestNodeType.Solution;
            }
            else if (testAdaptor.FullName.EndsWith(".dll"))
            {
                return TestNodeType.Assembly;
            }
            else if (testAdaptor.TypeInfo == null)
            {
                return TestNodeType.Namespace;
            }
            else if (testAdaptor.Method == null)
            {
                return TestNodeType.Class;
            }
            else if(testAdaptor.Arguments == null || testAdaptor.Arguments.Length == 0)
            {
                return TestNodeType.Method;
            }else{
                return TestNodeType.TestCase;
            }
        }

        /// <summary>
        /// Get a unique id for test that will persist across compiles and not conflict across test modes
        /// </summary>
        /// <param name="testAdaptor"></param>
        /// <returns></returns>
        public static string GetId(this ITestAdaptor testAdaptor){
            return $"{GetMode(testAdaptor)}/{testAdaptor.UniqueName}";
        }
    }
}
