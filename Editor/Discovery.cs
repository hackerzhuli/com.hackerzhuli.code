/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Unity Technologies.
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

using System.Collections.Generic;
using System.IO;
using Hackerzhuli.Code.Editor.Code;

namespace Hackerzhuli.Code.Editor
{
    internal static class Discovery
    {
        public static IEnumerable<ICodeEditorInstallation> GetVisualStudioInstallations()
        {
            foreach (var installation in CodeInstallation.GetInstallations())
                yield return installation;
        }

        public static bool TryDiscoverInstallation(string editorPath, out ICodeEditorInstallation installation)
        {
            try
            {
                if (CodeInstallation.TryDiscoverInstallation(editorPath, out installation))
                    return true;
            }
            catch (IOException)
            {
                installation = null;
            }

            return false;
        }

        public static void Initialize()
        {
            CodeInstallation.Initialize();
        }
    }
}