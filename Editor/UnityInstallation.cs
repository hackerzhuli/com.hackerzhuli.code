/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.MPE;
using Assembly = UnityEditor.Compilation.Assembly;

namespace Hackerzhuli.Code.Editor
{
    internal static class UnityInstallation
    {
        private static readonly Lazy<bool> _lazyIsInSafeMode = new(() =>
        {
            // internal static extern bool isInSafeMode { get {} }
            var ieu = typeof(EditorUtility);
            var pinfo = ieu.GetProperty("isInSafeMode", BindingFlags.Static | BindingFlags.NonPublic);
            if (pinfo == null)
                return false;

            return Convert.ToBoolean(pinfo.GetValue(null));
        });

        public static bool IsMainUnityEditorProcess
        {
            get
            {
                if (AssetDatabase.IsAssetImportWorkerProcess())
                    return false;

                if (ProcessService.level == ProcessLevel.Secondary)
                    return false;

                return true;
            }
        }

        public static bool IsInSafeMode => _lazyIsInSafeMode.Value;

        public static Version LatestLanguageVersionSupported(Assembly assembly)
        {
            if (assembly?.compilerOptions != null &&
                Version.TryParse(assembly.compilerOptions.LanguageVersion, out var result))
                return result;

            // if parsing fails, we know at least we have support for 8.0
            return new Version(8, 0);
        }
    }
}