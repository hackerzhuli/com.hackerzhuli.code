/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

using System.Linq;
using UnityEditor;
using UnityEngine;
using MessageType = Hackerzhuli.Code.Editor.Messaging.MessageType;
	
namespace Hackerzhuli.Code.Editor
{
	/// <summary>
	/// Static wrapper for Visual Studio integration that delegates to a ScriptableObject core implementation.
	/// </summary>
	[InitializeOnLoad]
	internal class VisualStudioIntegration
	{
		private static readonly VisualStudioIntegrationCore _core;

		static VisualStudioIntegration()
		{
			if (!VisualStudioEditor.IsEnabled)
				return;

			// Create or find the core ScriptableObject instance
			_core = GetOrCreateCore();

			EditorApplication.update += OnUpdate;
			AssemblyReloadEvents.afterAssemblyReload += OnAssemblyReload;
		}

		/// <summary>
		/// Gets or creates the core ScriptableObject instance.
		/// </summary>
		private static VisualStudioIntegrationCore GetOrCreateCore()
		{
			// Try to find existing instance first
			var existingCore = Resources.FindObjectsOfTypeAll<VisualStudioIntegrationCore>().FirstOrDefault();
			if (existingCore != null){
				//Debug.Log("reusing existing core");
				return existingCore;
			}

			// Create new instance if none exists
			var core = ScriptableObject.CreateInstance<VisualStudioIntegrationCore>();
			core.hideFlags = HideFlags.HideAndDontSave; // Don't save to scene or show in inspector
			return core;
		}

		private static void OnUpdate()
		{
			_core.Update();
		}

		/// <summary>
		/// Gets the package version.
		/// </summary>
		internal static string PackageVersion()
		{
			var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(VisualStudioIntegration).Assembly);
			return package.version;
		}

		private static void OnAssemblyReload()
		{
			_core.OnAssemblyReload();
		}

		/// <summary>
		/// Broadcasts a message to all connected clients.
		/// </summary>
		internal static void BroadcastMessage(MessageType type, string value)
		{
			_core.BroadcastMessage(type, value);
		}
	}
}
