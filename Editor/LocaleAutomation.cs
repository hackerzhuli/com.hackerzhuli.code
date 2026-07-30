using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;
using Hackerzhuli.Code.Editor.Messaging;
using Newtonsoft.Json.Linq;
using UnityEditor;
using MessageType = Hackerzhuli.Code.Editor.Messaging.MessageType;

namespace Hackerzhuli.Code.Editor
{
    /// <summary>
    ///     Handles loopback-only locale messages: listing the available locales and selecting one.
    /// </summary>
    /// <remarks>
    ///     Stateless, and called from the editor main thread by <see cref="CodeEditorIntegrationCore" />.
    ///     Both messages require Play Mode: the runtime locale list is only populated while playing, and
    ///     the selected locale is a runtime concept. All Localization access goes through
    ///     <see cref="LocalizationBridge" />, so this package keeps working without com.unity.localization.
    /// </remarks>
    internal static class LocaleAutomation
    {
        /// <summary>
        ///     Processes a locale message and answers the requesting client.
        /// </summary>
        /// <param name="message">The incoming message.</param>
        /// <param name="answer">The callback used to send the response.</param>
        internal static void Process(Message message, Action<IPEndPoint, MessageType, string> answer)
        {
            if (!AutomationProtocol.IsLoopback(message.Origin))
            {
                Reply(answer, message, AutomationProtocol.Error(
                    AutomationProtocol.TryReadRequestId(message.Value), "forbidden",
                    "Locale requests are only accepted from loopback clients."));
                return;
            }

            if (!AutomationProtocol.TryParseRequest(message.Value, out var request, out var requestId,
                    out var parseError))
            {
                Reply(answer, message, AutomationProtocol.Error(requestId, "invalid_request", parseError));
                return;
            }

            if (!EditorApplication.isPlaying)
            {
                ReplyError(answer, message, requestId, "not_playing",
                    "Locale messages are only available while the Editor is in Play Mode.");
                return;
            }

            try
            {
                switch (message.Type)
                {
                    case MessageType.LocaleList:
                        ReplyWithLocales(answer, message, requestId);
                        break;
                    case MessageType.LocaleSelect:
                        ProcessSelect(answer, message, request, requestId);
                        break;
                }
            }
            catch (Exception exception)
            {
                Reply(answer, message,
                    AutomationProtocol.Error(requestId, "internal_error", exception.Message));
            }
        }

        /// <summary>
        ///     Changes the selected locale and answers with the resulting state.
        /// </summary>
        private static void ProcessSelect(Action<IPEndPoint, MessageType, string> answer, Message message,
            JObject request, JToken requestId)
        {
            var code = request.Value<string>("code");
            if (string.IsNullOrWhiteSpace(code))
            {
                ReplyError(answer, message, requestId, "invalid_request",
                    "A non-empty locale code is required.");
                return;
            }

            if (!LocalizationBridge.TrySelect(code.Trim(), out var errorCode, out var errorMessage))
            {
                ReplyError(answer, message, requestId, errorCode, errorMessage);
                return;
            }

            ReplyWithLocales(answer, message, requestId);
        }

        /// <summary>
        ///     Answers with the available locales and the selected one, used by both messages so a client
        ///     always sees the result of what it asked for.
        /// </summary>
        private static void ReplyWithLocales(Action<IPEndPoint, MessageType, string> answer, Message message,
            JToken requestId)
        {
            if (!LocalizationBridge.TryGetState(out var locales, out var selectedCode, out var errorCode,
                    out var errorMessage))
            {
                ReplyError(answer, message, requestId, errorCode, errorMessage);
                return;
            }

            Reply(answer, message, AutomationProtocol.Success(requestId, "locales",
                BuildLocalesYaml(locales, selectedCode)));
        }

        /// <summary>
        ///     Builds the compact YAML document describing the available locales.
        /// </summary>
        /// <param name="locales">The available locales.</param>
        /// <param name="selectedCode">The selected locale code, null when nothing is selected.</param>
        /// <returns>The YAML document.</returns>
        internal static string BuildLocalesYaml(IReadOnlyList<LocalizationBridge.LocaleInfo> locales,
            string selectedCode)
        {
            var builder = new StringBuilder();
            builder.Append("selectedLocale: ")
                .Append(selectedCode == null ? "null" : Quote(selectedCode))
                .Append('\n');

            if (locales.Count == 0)
            {
                builder.Append("locales: []");
                return builder.ToString();
            }

            builder.Append("locales:");
            foreach (var locale in locales)
            {
                builder.Append("\n  - code: ").Append(Quote(locale.Code));
                builder.Append("\n    name: ").Append(Quote(locale.Name));
                builder.Append("\n    sortOrder: ")
                    .Append(locale.SortOrder.ToString(CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }

        private static string Quote(string value)
        {
            return string.Concat("\"", AutomationProtocol.EscapeYamlString(value ?? string.Empty), "\"");
        }

        private static void ReplyError(Action<IPEndPoint, MessageType, string> answer, Message message,
            JToken requestId, string code, string errorMessage)
        {
            Reply(answer, message, AutomationProtocol.Error(requestId, code, errorMessage));
        }

        private static void Reply(Action<IPEndPoint, MessageType, string> answer, Message message,
            string payload)
        {
            answer(message.Origin, message.Type, payload);
        }
    }
}
