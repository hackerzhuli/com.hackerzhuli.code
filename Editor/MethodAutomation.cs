using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Reflection;
using Hackerzhuli.Code.Editor.Messaging;
using Newtonsoft.Json.Linq;
using UnityEditor;
using MessageType = Hackerzhuli.Code.Editor.Messaging.MessageType;

namespace Hackerzhuli.Code.Editor
{
    /// <summary>
    ///     Handles the loopback-only <see cref="MessageType.InvokeMethod" /> message: calling a public
    ///     static method of the project by name.
    /// </summary>
    /// <remarks>
    ///     Stateless, and called from the editor main thread by <see cref="CodeEditorIntegrationCore" />.
    ///     Every argument arrives as a string and is converted by
    ///     <see cref="AutomationValueParser" />, the same converter <c>UiSetValue</c> uses, so the set
    ///     of types a client can express is the same in both messages. The return value is reported as
    ///     a string as well, which keeps the response shape independent of what the project returns.
    ///     <para>
    ///         Unlike the Game View messages this works in Edit Mode too, because a static method does
    ///         not depend on anything the player loop provides.
    ///     </para>
    /// </remarks>
    internal static class MethodAutomation
    {
        /// <summary>
        ///     The size at which a return value is truncated, so one method cannot flood a response.
        /// </summary>
        private const int ResultLengthLimit = 64 * 1024;

        /// <summary>
        ///     Processes an invoke message and answers the requesting client.
        /// </summary>
        /// <param name="message">The incoming message.</param>
        /// <param name="answer">The callback used to send the response.</param>
        internal static void Process(Message message, Action<IPEndPoint, MessageType, string> answer)
        {
            if (!AutomationProtocol.IsLoopback(message.Origin))
            {
                Reply(answer, message, AutomationProtocol.Error(
                    AutomationProtocol.TryReadRequestId(message.Value), "forbidden",
                    "Invoke requests are only accepted from loopback clients."));
                return;
            }

            if (!AutomationProtocol.TryParseRequest(message.Value, out var request, out var requestId,
                    out var parseError))
            {
                Reply(answer, message, AutomationProtocol.Error(requestId, "invalid_request", parseError));
                return;
            }

            // A domain reload in the middle of a request invalidates the resolved method and drops
            // the response entirely, so refuse rather than start.
            if (EditorApplication.isCompiling)
            {
                ReplyError(answer, message, requestId, "busy",
                    "Unity is compiling scripts, retry once compilation has finished.");
                return;
            }

            try
            {
                switch (message.Type)
                {
                    case MessageType.InvokeMethod:
                        ProcessInvoke(answer, message, request, requestId);
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
        ///     Resolves the requested method, converts the arguments, and invokes it.
        /// </summary>
        /// <param name="answer">The callback used to send the response.</param>
        /// <param name="message">The incoming message.</param>
        /// <param name="request">The parsed request.</param>
        /// <param name="requestId">The request id to echo back.</param>
        private static void ProcessInvoke(Action<IPEndPoint, MessageType, string> answer,
            Message message, JObject request, JToken requestId)
        {
            var typeName = request.Value<string>("typeName");
            if (string.IsNullOrWhiteSpace(typeName))
            {
                ReplyError(answer, message, requestId, "invalid_request",
                    "A non-empty typeName is required.");
                return;
            }

            var methodName = request.Value<string>("methodName");
            if (string.IsNullOrWhiteSpace(methodName))
            {
                ReplyError(answer, message, requestId, "invalid_request",
                    "A non-empty methodName is required.");
                return;
            }

            if (!TryReadArguments(request, out var arguments, out var argumentError))
            {
                ReplyError(answer, message, requestId, "invalid_request", argumentError);
                return;
            }

            if (!TryResolveType(typeName, out var type, out var typeErrorCode, out var typeError))
            {
                ReplyError(answer, message, requestId, typeErrorCode, typeError);
                return;
            }

            if (!TryResolveMethod(type, methodName, arguments, out var method, out var converted,
                    out var methodErrorCode, out var methodError))
            {
                ReplyError(answer, message, requestId, methodErrorCode, methodError);
                return;
            }

            object result;
            try
            {
                result = method.Invoke(null, converted);
            }
            catch (Exception exception)
            {
                // The full stack trace is only useful in the log, the client gets the cause.
                FileLogger.LogError($"InvokeMethod {type.FullName}.{methodName} threw: {exception}");
                var cause = AutomationValueParser.UnwrapReflectionException(exception);
                ReplyError(answer, message, requestId, "invocation_failed",
                    $"{cause.GetType().FullName}: {cause.Message}");
                return;
            }

            if (method.ReturnType == typeof(void))
            {
                Reply(answer, message, AutomationProtocol.Success(requestId));
                return;
            }

            Reply(answer, message, AutomationProtocol.Success(requestId, "result", FormatResult(result)));
        }

        /// <summary>
        ///     Reads the argument strings of a request.
        /// </summary>
        /// <param name="request">The parsed request.</param>
        /// <param name="arguments">The arguments, empty when the property is absent.</param>
        /// <param name="error">A human readable reason when the property is malformed.</param>
        /// <returns>True when the request carries no arguments or a valid array of them.</returns>
        /// <remarks>
        ///     A null item is kept as null, which is how a client passes null to a reference parameter.
        ///     Anything else has to be a string, because a JSON number or boolean would let a client
        ///     believe the type of the literal matters, when only the target parameter type does.
        /// </remarks>
        private static bool TryReadArguments(JObject request, out string[] arguments, out string error)
        {
            arguments = Array.Empty<string>();
            error = null;
            var token = request["args"];
            if (token == null || token.Type == JTokenType.Null)
                return true;

            if (token is not JArray array)
            {
                error = "args must be an array of strings.";
                return false;
            }

            var values = new string[array.Count];
            for (var index = 0; index < array.Count; index++)
            {
                var item = array[index];
                if (item.Type == JTokenType.Null)
                    continue;
                if (item.Type != JTokenType.String)
                {
                    error = $"args[{index}] must be a string, every argument is passed as text.";
                    return false;
                }

                values[index] = item.Value<string>();
            }

            arguments = values;
            return true;
        }

        /// <summary>
        ///     Resolves a type by name across the loaded assemblies.
        /// </summary>
        /// <param name="typeName">
        ///     A namespace qualified name such as <c>MyGame.Cheats</c>, an assembly qualified name, or a
        ///     nested type written with <c>+</c>.
        /// </param>
        /// <param name="type">The resolved type on success.</param>
        /// <param name="errorCode">The error code when resolution failed.</param>
        /// <param name="error">A human readable reason when resolution failed.</param>
        /// <returns>True when exactly one type matches.</returns>
        /// <remarks>
        ///     Each assembly is asked for the name directly rather than enumerated, which is a hashed
        ///     lookup per assembly instead of a walk over every type in the domain, and cannot fail on
        ///     an assembly whose types do not all load.
        /// </remarks>
        private static bool TryResolveType(string typeName, out Type type, out string errorCode,
            out string error)
        {
            type = null;
            errorCode = "unknown_type";
            error = null;

            var name = typeName.Trim();
            var matches = new List<Type>();

            // Honors an assembly qualified name, and finds the types of this assembly and of corlib.
            var direct = SafeGetType(() => Type.GetType(name, false, false));
            if (direct != null)
                matches.Add(direct);

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic)
                    continue;
                var candidate = SafeGetType(() => assembly.GetType(name, false, false));
                if (candidate != null && !matches.Contains(candidate))
                    matches.Add(candidate);
            }

            if (matches.Count == 0)
            {
                error = $"No type named '{name}' is loaded. " +
                        "Use the full namespace, and '+' before a nested type.";
                return false;
            }

            if (matches.Count > 1)
            {
                errorCode = "ambiguous_type";
                error = $"'{name}' matches {matches.Count} loaded types. " +
                        "Pass an assembly qualified name instead: " +
                        string.Join("; ", matches.Select(match => match.AssemblyQualifiedName));
                return false;
            }

            type = matches[0];
            if (!type.ContainsGenericParameters)
                return true;

            errorCode = "unsupported_type";
            error = $"'{type.FullName}' is an open generic type and cannot be invoked on.";
            type = null;
            return false;
        }

        /// <summary>
        ///     Runs a type lookup that is allowed to fail.
        /// </summary>
        /// <param name="lookup">The lookup to run.</param>
        /// <returns>The type, or null when the name is malformed or its assembly cannot be loaded.</returns>
        private static Type SafeGetType(Func<Type> lookup)
        {
            try
            {
                return lookup();
            }
            catch (Exception exception) when (exception is ArgumentException or
                                                  BadImageFormatException or
                                                  System.IO.FileLoadException or
                                                  System.IO.FileNotFoundException)
            {
                return null;
            }
        }

        /// <summary>
        ///     Selects the overload to call and converts the arguments for it.
        /// </summary>
        /// <param name="type">The declaring type.</param>
        /// <param name="methodName">The method name.</param>
        /// <param name="arguments">The arguments as written by the client.</param>
        /// <param name="method">The selected method on success.</param>
        /// <param name="converted">The converted arguments on success.</param>
        /// <param name="errorCode">The error code when no overload could be called.</param>
        /// <param name="error">A human readable reason when no overload could be called.</param>
        /// <returns>True when an overload was selected and every argument converted.</returns>
        /// <remarks>
        ///     Candidates are ordered by their signature rather than left in reflection order, so which
        ///     overload wins does not depend on the order the runtime happens to report members in, and
        ///     the first one that accepts every argument is used.
        /// </remarks>
        private static bool TryResolveMethod(Type type, string methodName, string[] arguments,
            out MethodInfo method, out object[] converted, out string errorCode, out string error)
        {
            method = null;
            converted = null;
            errorCode = "unknown_method";
            error = null;

            var named = type
                .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                .Where(candidate => !candidate.IsSpecialName &&
                                    string.Equals(candidate.Name, methodName, StringComparison.Ordinal))
                .ToList();
            if (named.Count == 0)
            {
                error = $"'{type.FullName}' has no public static method named '{methodName}'.";
                return false;
            }

            var candidates = named
                .Where(candidate => candidate.GetParameters().Length == arguments.Length)
                .OrderBy(DescribeParameters, StringComparer.Ordinal)
                .ToList();
            if (candidates.Count == 0)
            {
                error = $"No overload of '{type.FullName}.{methodName}' takes {arguments.Length} " +
                        $"argument(s). Available: {DescribeOverloads(named)}.";
                return false;
            }

            var invocable = candidates.Where(IsInvocable).ToList();
            if (invocable.Count == 0)
            {
                errorCode = "unsupported_method";
                error = $"'{type.FullName}.{methodName}' cannot be invoked. Generic methods and " +
                        "ref, out, in, pointer and params parameters are not supported. " +
                        $"Rejected: {DescribeOverloads(candidates)}.";
                return false;
            }

            string lastErrorCode = null;
            string lastError = null;
            foreach (var candidate in invocable)
            {
                if (TryConvertArguments(candidate, arguments, out var values, out lastErrorCode,
                        out lastError))
                {
                    method = candidate;
                    converted = values;
                    return true;
                }
            }

            errorCode = lastErrorCode ?? "invalid_value";
            error = invocable.Count == 1
                ? lastError
                : $"{lastError} No overload of '{type.FullName}.{methodName}' accepts these " +
                  $"arguments. Tried: {DescribeOverloads(invocable)}.";
            return false;
        }

        /// <summary>
        ///     Determines whether a method can be called with plain converted arguments.
        /// </summary>
        /// <param name="method">The method to test.</param>
        /// <returns>True when nothing about its signature needs more than a value per parameter.</returns>
        private static bool IsInvocable(MethodInfo method)
        {
            if (method.IsGenericMethodDefinition || method.ContainsGenericParameters)
                return false;

            return method.GetParameters().All(parameter =>
                !parameter.ParameterType.IsByRef &&
                !parameter.ParameterType.IsPointer &&
                !parameter.IsDefined(typeof(ParamArrayAttribute), false));
        }

        /// <summary>
        ///     Converts every argument to the parameter type of one overload.
        /// </summary>
        /// <param name="method">The overload being considered.</param>
        /// <param name="arguments">The arguments as written by the client.</param>
        /// <param name="converted">The converted arguments on success.</param>
        /// <param name="errorCode">The converter's error code for the first argument that failed.</param>
        /// <param name="error">A human readable reason naming the argument that failed.</param>
        /// <returns>True when every argument converted.</returns>
        private static bool TryConvertArguments(MethodInfo method, string[] arguments,
            out object[] converted, out string errorCode, out string error)
        {
            var parameters = method.GetParameters();
            var values = new object[parameters.Length];
            converted = null;
            for (var index = 0; index < parameters.Length; index++)
            {
                if (AutomationValueParser.TryConvertValue(parameters[index].ParameterType,
                        arguments[index], null, out values[index], out errorCode, out var reason))
                    continue;

                error = $"args[{index}] ('{parameters[index].Name}'): {reason}";
                return false;
            }

            converted = values;
            errorCode = null;
            error = null;
            return true;
        }

        /// <summary>
        ///     Formats a return value as the string the client receives.
        /// </summary>
        /// <param name="value">The value the method returned, may be null.</param>
        /// <returns>The value as text, truncated when it is very long.</returns>
        /// <remarks>
        ///     The invariant culture is used rather than the Editor's, so a float does not come back as
        ///     <c>1,5</c> to a machine client that runs the Editor in a German locale.
        /// </remarks>
        private static string FormatResult(object value)
        {
            if (value == null)
                return null;

            string text;
            try
            {
                text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? value.ToString();
            }
            catch (Exception exception) when (exception is FormatException or InvalidCastException)
            {
                text = value.ToString();
            }

            if (text == null || text.Length <= ResultLengthLimit)
                return text;

            return string.Concat(text.Substring(0, ResultLengthLimit), "…(truncated)");
        }

        /// <summary>
        ///     Describes the parameter types of a method, as the key candidates are ordered by.
        /// </summary>
        /// <param name="method">The method to describe.</param>
        /// <returns>The parameter type names, comma separated.</returns>
        private static string DescribeParameters(MethodInfo method)
        {
            return string.Join(",", method.GetParameters()
                .Select(parameter => parameter.ParameterType.FullName ?? parameter.ParameterType.Name));
        }

        /// <summary>
        ///     Describes a set of overloads for an error message.
        /// </summary>
        /// <param name="methods">The overloads to describe.</param>
        /// <returns>The signatures, semicolon separated.</returns>
        private static string DescribeOverloads(IEnumerable<MethodInfo> methods)
        {
            return string.Join("; ", methods.Select(method =>
                $"{method.Name}({DescribeParameters(method)})"));
        }

        /// <summary>
        ///     Answers the client with a failure response.
        /// </summary>
        private static void ReplyError(Action<IPEndPoint, MessageType, string> answer, Message message,
            JToken requestId, string code, string errorMessage)
        {
            Reply(answer, message, AutomationProtocol.Error(requestId, code, errorMessage));
        }

        /// <summary>
        ///     Answers the client with a serialized response.
        /// </summary>
        private static void Reply(Action<IPEndPoint, MessageType, string> answer, Message message,
            string payload)
        {
            answer(message.Origin, message.Type, payload);
        }
    }
}
