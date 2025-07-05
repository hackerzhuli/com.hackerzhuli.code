/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Unity Technologies.
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

using System;
using System.Collections.Generic;

namespace Hackerzhuli.Code.Editor.ProjectGeneration
{
    internal enum GeneratorStyle
    {
        SDK = 1,
        Legacy = 2
    }

    internal static class GeneratorFactory
    {
        private static readonly Dictionary<GeneratorStyle, IGenerator> _generators = new();

        static GeneratorFactory()
        {
            _generators.Add(GeneratorStyle.SDK, new SdkStyleProjectGeneration());
            _generators.Add(GeneratorStyle.Legacy, new LegacyStyleProjectGeneration());
        }

        public static IGenerator GetInstance(GeneratorStyle style)
        {
            var forceStyleString = OnSelectingCSProjectStyle();
            if (forceStyleString != null && Enum.TryParse<GeneratorStyle>(forceStyleString, out var forceStyle))
                style = forceStyle;

            if (_generators.TryGetValue(style, out var result))
                return result;

            throw new ArgumentException("Unknown generator style");
        }

        private static string OnSelectingCSProjectStyle()
        {
            foreach (var method in TypeCacheHelper.GetPostProcessorCallbacks(nameof(OnSelectingCSProjectStyle)))
            {
                var retValue = method.Invoke(null, Array.Empty<object>());
                if (method.ReturnType != typeof(string))
                    continue;

                return retValue as string;
            }

            return null;
        }
    }
}