/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

using System;

namespace Hackerzhuli.Code.Editor.Messaging
{
    /// <summary>
    ///     Represents a log entry that occurred during Unity's compilation process.
    ///     This class is designed for JSON serialization to communicate log entries to IDE clients.
    /// </summary>
    [Serializable]
    public class Log
    {
        /// <summary>
        ///     The complete log message as logged by Unity.
        /// </summary>
        public string Message;

        /// <summary>
        ///     The stack trace associated with the log entry, if available.
        /// </summary>
        public string StackTrace;

        /// <summary>
        ///     The timestamp when the log entry was captured as Unix timestamp (milliseconds since epoch).
        /// </summary>
        public long Timestamp;

        /// <summary>
        ///     Initializes a new instance of the Log class.
        /// </summary>
        public Log()
        {
        }

        /// <summary>
        ///     Initializes a new instance of the Log class with the specified parameters.
        /// </summary>
        /// <param name="message">The log message.</param>
        /// <param name="stackTrace">The stack trace associated with the log entry.</param>
        /// <param name="timestamp">The timestamp when the log entry occurred as Unix timestamp.</param>
        public Log(string message, string stackTrace, long timestamp)
        {
            Message = message;
            StackTrace = stackTrace;
            Timestamp = timestamp;
        }
    }

    /// <summary>
    ///     Container class for JSON serialization of log entry arrays.
    ///     Unity's JsonUtility requires a wrapper class for array serialization.
    /// </summary>
    [Serializable]
    public class LogContainer
    {
        /// <summary>
        ///     Array of log entries.
        /// </summary>
        public Log[] Logs;
    }
}