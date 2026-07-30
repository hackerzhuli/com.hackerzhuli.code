using System.Net;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Hackerzhuli.Code.Editor
{
    /// <summary>
    ///     The request and response envelope shared by all loopback-only automation messages.
    /// </summary>
    /// <remarks>
    ///     Every automation request is a JSON object carrying an opaque <c>requestId</c>, and every
    ///     response reuses the message type of its request, so a client can correlate the two.
    ///     Game View automation (values 107-113), scene messages and locale messages all use this
    ///     envelope; only the result property names and the mode gating differ between them.
    /// </remarks>
    internal static class AutomationProtocol
    {
        /// <summary>
        ///     Parses an automation request and extracts its request id.
        /// </summary>
        /// <param name="value">The raw message value, expected to be a JSON object.</param>
        /// <param name="request">The parsed request object, null when parsing failed.</param>
        /// <param name="requestId">The request id to echo back, null when it is missing or invalid.</param>
        /// <param name="error">A human readable reason when parsing failed.</param>
        /// <returns>True when the request is a JSON object with a valid request id.</returns>
        internal static bool TryParseRequest(string value, out JObject request, out JToken requestId,
            out string error)
        {
            request = null;
            requestId = null;
            error = null;
            if (string.IsNullOrWhiteSpace(value))
            {
                error = "The request value must be a JSON object.";
                return false;
            }

            try
            {
                request = JObject.Parse(value);
                requestId = request["requestId"]?.DeepClone();
                if (requestId == null || requestId.Type == JTokenType.Null ||
                    (requestId.Type != JTokenType.String && requestId.Type != JTokenType.Integer &&
                     requestId.Type != JTokenType.Float))
                {
                    error = "requestId must be a string or number.";
                    return false;
                }

                return true;
            }
            catch (JsonException exception)
            {
                error = $"Invalid JSON request: {exception.Message}";
                return false;
            }
        }

        /// <summary>
        ///     Reads just the request id of a request, so a rejected request can still be answered.
        /// </summary>
        /// <param name="value">The raw message value.</param>
        /// <returns>The request id, or null when it cannot be read.</returns>
        internal static JToken TryReadRequestId(string value)
        {
            try
            {
                return JObject.Parse(value ?? string.Empty)["requestId"]?.DeepClone();
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        ///     Determines whether a request originates from this machine.
        /// </summary>
        /// <param name="endPoint">The endpoint the request came from.</param>
        /// <returns>True when the endpoint is a loopback address.</returns>
        internal static bool IsLoopback(IPEndPoint endPoint)
        {
            if (endPoint?.Address == null)
                return false;
            var address = endPoint.Address;
            if (address.IsIPv4MappedToIPv6)
                address = address.MapToIPv4();
            return IPAddress.IsLoopback(address);
        }

        /// <summary>
        ///     Builds a success response, optionally carrying a single result property.
        /// </summary>
        /// <param name="requestId">The request id to echo back.</param>
        /// <param name="propertyName">The result property name, or null for a bare acknowledgement.</param>
        /// <param name="propertyValue">The result property value.</param>
        /// <returns>The serialized JSON response.</returns>
        internal static string Success(JToken requestId, string propertyName = null, string propertyValue = null)
        {
            var result = new JObject
            {
                ["requestId"] = requestId?.DeepClone() ?? JValue.CreateNull(),
                ["ok"] = true
            };
            if (propertyName != null)
                result[propertyName] = propertyValue;
            return result.ToString(Formatting.None);
        }

        /// <summary>
        ///     Builds a failure response.
        /// </summary>
        /// <param name="requestId">The request id to echo back.</param>
        /// <param name="code">The machine readable error code.</param>
        /// <param name="message">The human readable error message.</param>
        /// <returns>The serialized JSON response.</returns>
        internal static string Error(JToken requestId, string code, string message)
        {
            return new JObject
            {
                ["requestId"] = requestId?.DeepClone() ?? JValue.CreateNull(),
                ["ok"] = false,
                ["error"] = new JObject
                {
                    ["code"] = code,
                    ["message"] = message
                }
            }.ToString(Formatting.None);
        }

        /// <summary>
        ///     Escapes a string for use inside a double quoted YAML scalar.
        /// </summary>
        /// <param name="value">The raw string.</param>
        /// <returns>The escaped string, without the surrounding quotes.</returns>
        internal static string EscapeYamlString(string value)
        {
            var builder = new StringBuilder(value.Length);
            foreach (var character in value)
            {
                switch (character)
                {
                    case '\\': builder.Append("\\\\"); break;
                    case '"': builder.Append("\\\""); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (character < 0x20)
                            builder.Append("\\u").Append(((int)character).ToString("x4"));
                        else
                            builder.Append(character);
                        break;
                }
            }

            return builder.ToString();
        }
    }
}
