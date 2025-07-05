/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Unity Technologies.
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

namespace Hackerzhuli.Code.Editor.ProjectGeneration
{
    public interface IGUIDGenerator
    {
        string ProjectGuid(string projectName, string assemblyName);
        string SolutionGuid(string projectName, ScriptingLanguage scriptingLanguage);
    }

    internal class GUIDProvider : IGUIDGenerator
    {
        public string ProjectGuid(string projectName, string assemblyName)
        {
            return SolutionGuidGenerator.GuidForProject(projectName + assemblyName);
        }

        public string SolutionGuid(string projectName, ScriptingLanguage scriptingLanguage)
        {
            return SolutionGuidGenerator.GuidForSolution(projectName, scriptingLanguage);
        }
    }
}