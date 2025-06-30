
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
    }
}
