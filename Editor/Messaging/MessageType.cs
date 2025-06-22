/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/
using System;

namespace Microsoft.Unity.VisualStudio.Editor.Messaging
{
	internal enum MessageType
	{
		None = 0,

		Ping,
		Pong,

		Play,
		Stop,
		Pause,
		Unpause,

		[Obsolete]
		Build,
		Refresh,

		Info,
		Error,
		Warning,

		[Obsolete]
		Open,
		[Obsolete]
		Opened,

		/// <summary>
		/// The version of this package
		/// </summary>
		Version,
		[Obsolete]
		UpdatePackage,

		ProjectPath,


        /// <summary>
        /// This message is a technical one for big messages, not intended to be used directly
        /// </summary>
        Tcp,

		RunStarted,
		RunFinished,
		TestStarted,
		TestFinished,
		TestListRetrieved,

		RetrieveTestList,
		ExecuteTests,

		ShowUsage,

		/// <summary>
		/// This is a message sent when the compilation is finished<br/>
		/// This is a new message and don't exist in the official package from Unity
		/// </summary>
		CompilationFinished = 100,

        /// <summary>
        /// The name of this package
		/// This is new and don't exist in the official package from Unity
        /// </summary>
        PackageName = 101,

        /// <summary>
        /// Notifies clients that we are online and ready to receive messages
		/// This can be due to after domain reload finished or Unity Editor start
        /// This is new and don't exist in the official package from Unity
        /// </summary>
        OnLine = 102,

        /// <summary>
        /// Notifies clients that we are going offline, and will not be able to receive messages
		/// This can be due to domain reload or Unity Editor shutdown
        /// This is new and don't exist in the official package from Unity
        /// </summary>
        OffLine = 103,
	}
}
