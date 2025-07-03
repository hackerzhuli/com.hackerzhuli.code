/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

using System;
using System.Collections.Generic;

namespace Hackerzhuli.Code.Editor.Code
{
    /// <summary>
    /// Represents a launch configuration item for VS Code launch.json file.
    /// </summary>
    [Serializable]
	public class CodeLaunchItem
	{
		/// <summary>
		/// The launch.json configuration items for VS Code depending on the extension installed.
		/// </summary>
		public static readonly CodeLaunchItem[] Items = new CodeLaunchItem[]
		{
			new() {
				ExtensionId = "hackerzhuli.unity-code",
				Name = "Attach to Unity Editor with Unity Code",
				Type = "unity-code",
				Request = "attach",
			},			
			new() {
				ExtensionId = CodeExtensionManager.DotRushExtensionId,
				Name = "Attach to Unity Editor with Dot Rush",
				Type = "unity",
				Request = "attach",
			},
			new() {
				ExtensionId = CodeExtensionManager.UnityExtensionId,
				Name = "Attach to Unity Editor",
				Type = "vstuc",
				Request = "launch"
			}
		};

		/// <summary>
		/// The extension ID that this launch configuration is associated with.
		/// </summary>
		public string ExtensionId { get; set; }
		
		/// <summary>
		/// The name of the launch configuration.
		/// </summary>
		public string Name { get; set; }
		
		/// <summary>
		/// The type of the launch configuration.
		/// </summary>
		public string Type { get; set; }
		
		/// <summary>
		/// The request type (e.g., "attach", "launch").
		/// </summary>
		public string Request { get; set; }
		
		/// <summary>
		/// Additional properties for the launch configuration.
		/// </summary>
		public Dictionary<string, object> AdditionalProperties { get; set; }
		
		public CodeLaunchItem()
		{
			AdditionalProperties = new Dictionary<string, object>();
		}
	}
}