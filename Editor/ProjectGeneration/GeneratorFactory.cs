/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Unity Technologies.
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

using System;

namespace Hackerzhuli.Code.Editor.ProjectGeneration
{
    internal enum GeneratorStyle
    {
        SDK = 1,
		Legacy = 2,
    }

    internal static class GeneratorFactory
    {
		private static readonly SdkStyleProjectGeneration _sdkStyleProjectGeneration = new SdkStyleProjectGeneration();
		private static readonly LegacyStyleProjectGeneration _legacyStyleProjectGeneration = new LegacyStyleProjectGeneration();

        public static IGenerator GetInstance(GeneratorStyle style)
        {
            var forceStyleString = OnSelectingCSProjectStyle();
            if (forceStyleString != null && Enum.TryParse<GeneratorStyle>(forceStyleString, out var forceStyle))
                style = forceStyle;

			if (style == GeneratorStyle.SDK)
				return _sdkStyleProjectGeneration;
			if (style == GeneratorStyle.Legacy)
				return _legacyStyleProjectGeneration;

            throw new ArgumentException("Unknown generator style");
        }

        private static string OnSelectingCSProjectStyle()
        {
            foreach (var method in TypeCacheHelper.GetPostProcessorCallbacks(nameof(OnSelectingCSProjectStyle)))
            {
				object retValue = method.Invoke(null, Array.Empty<object>());
                if (method.ReturnType != typeof(string))
                    continue;

                return retValue as string;
            }

            return null;
        }
    }
}
