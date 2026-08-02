using System;
using System.Net;
using Hackerzhuli.Code.Editor.Messaging;
using Newtonsoft.Json.Linq;
using UnityEditor;
using MessageType = Hackerzhuli.Code.Editor.Messaging.MessageType;

namespace Hackerzhuli.Code.Editor
{
    /// <summary>Version-independent routing and availability checks for ECS debugging messages.</summary>
    internal static class EcsAutomation
    {
        internal sealed class Result
        {
            internal readonly bool Ok;
            internal readonly string Value;
            internal readonly string ErrorCode;
            internal readonly string ErrorMessage;

            private Result(bool ok, string value, string errorCode, string errorMessage)
            {
                Ok = ok;
                Value = value;
                ErrorCode = errorCode;
                ErrorMessage = errorMessage;
            }

            internal static Result Success(string yaml) => new(true, yaml, null, null);
            internal static Result Error(string code, string message) => new(false, null, code, message);
        }

        internal delegate Result Handler(MessageType type, JObject request);
        private static Handler _handler;

        /// <summary>Called by the optional Entities integration assembly after it loads.</summary>
        internal static void Register(Handler handler) => _handler = handler;

        internal static void Process(Message message, Action<IPEndPoint, MessageType, string> answer)
        {
            var requestId = AutomationProtocol.TryReadRequestId(message.Value);
            if (!AutomationProtocol.IsLoopback(message.Origin))
            {
                Reply(answer, message, AutomationProtocol.Error(requestId, "forbidden",
                    "ECS requests are only accepted from loopback clients."));
                return;
            }

            if (!AutomationProtocol.TryParseRequest(message.Value, out var request, out requestId,
                    out var parseError))
            {
                Reply(answer, message, AutomationProtocol.Error(requestId, "invalid_request", parseError));
                return;
            }

            if (!EditorApplication.isPlaying)
            {
                Reply(answer, message, AutomationProtocol.Error(requestId, "not_playing",
                    "ECS debugging messages are only available in Play Mode."));
                return;
            }

            if (_handler == null)
            {
                Reply(answer, message, AutomationProtocol.Error(requestId, "entities_unavailable",
                    "Install com.unity.entities and com.unity.entities.graphics 1.4.0 or newer to enable ECS debugging."));
                return;
            }

            try
            {
                var result = _handler(message.Type, request);
                var response = result.Ok
                    ? AutomationProtocol.Success(requestId, "result", result.Value)
                    : AutomationProtocol.Error(requestId, result.ErrorCode, result.ErrorMessage);
                Reply(answer, message, response);
            }
            catch (Exception exception)
            {
                Reply(answer, message, AutomationProtocol.Error(requestId, "internal_error", exception.Message));
            }
        }

        private static void Reply(Action<IPEndPoint, MessageType, string> answer, Message request, string value)
        {
            answer(request.Origin, request.Type, value);
        }
    }
}
