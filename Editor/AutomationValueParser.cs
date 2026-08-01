using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using static Hackerzhuli.Code.Editor.AutomationValueFormatter;

namespace Hackerzhuli.Code.Editor
{
    /// <summary>
    ///     Converts the string arguments of an automation request into the strongly typed values a
    ///     Unity API expects.
    /// </summary>
    /// <remarks>
    ///     The inverse of <see cref="AutomationValueFormatter" />, and shared by every message that
    ///     takes a value from a client as text: <c>UiSetValue</c> assigns it to a field, and
    ///     <c>InvokeMethod</c> passes it as a method parameter. Both reflect over types the package
    ///     knows nothing about, so the same rules apply throughout: the invariant culture everywhere,
    ///     a forgiving surface for the handful of types a human or an agent actually types by hand,
    ///     and a refusal rather than a guess for anything else.
    /// </remarks>
    internal static class AutomationValueParser
    {
        /// <summary>
        ///     Matches one number of a multi component value such as a vector, a rect or a color.
        /// </summary>
        private static readonly Regex NumericComponentPattern = new(
            @"[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        ///     Converts a string to the declared type of a field or parameter.
        /// </summary>
        /// <param name="declaredType">The type the value has to be assignable to.</param>
        /// <param name="input">The value as written by the client, may be null.</param>
        /// <param name="currentValue">
        ///     The value being replaced, used only to recover the concrete enum type when
        ///     <paramref name="declaredType" /> is the abstract <see cref="Enum" />.
        /// </param>
        /// <param name="converted">The converted value on success, null otherwise.</param>
        /// <param name="errorCode">
        ///     <c>invalid_value</c> when the string does not describe a value of that type, or
        ///     <c>unsupported_value_type</c> when no safe conversion to that type exists at all.
        /// </param>
        /// <param name="errorMessage">A human readable reason when the conversion failed.</param>
        /// <returns>True when the value was converted.</returns>
        internal static bool TryConvertValue(Type declaredType, string input, object currentValue,
            out object converted, out string errorCode, out string errorMessage)
        {
            converted = null;
            errorCode = "invalid_value";
            errorMessage = null;
            if (declaredType == null)
            {
                errorCode = "unsupported_value_type";
                errorMessage = "The field does not expose a value type.";
                return false;
            }

            var nullableType = Nullable.GetUnderlyingType(declaredType);
            var valueType = nullableType ?? declaredType;
            if (input == null)
            {
                if (!declaredType.IsValueType || nullableType != null)
                    return true;

                errorMessage = $"A null value cannot be assigned to {GetFriendlyTypeName(declaredType)}.";
                return false;
            }

            if (valueType == typeof(string) || valueType == typeof(object))
            {
                converted = input;
                return true;
            }

            var text = input.Trim();
            if (nullableType != null &&
                (text.Length == 0 || string.Equals(text, "null", StringComparison.OrdinalIgnoreCase)))
                return true;

            if (valueType == typeof(char))
            {
                var unquoted = text.Length == 3 &&
                               (text[0] == '\'' && text[2] == '\'' ||
                                text[0] == '"' && text[2] == '"')
                    ? text.Substring(1, 1)
                    : text;
                if (unquoted.Length == 1)
                {
                    converted = unquoted[0];
                    return true;
                }

                return InvalidConversion(declaredType, input, out errorMessage);
            }

            if (valueType == typeof(bool))
            {
                switch (text.ToLowerInvariant())
                {
                    case "true":
                    case "1":
                    case "yes":
                    case "y":
                    case "on":
                    case "checked":
                    case "enabled":
                        converted = true;
                        return true;
                    case "false":
                    case "0":
                    case "no":
                    case "n":
                    case "off":
                    case "unchecked":
                    case "disabled":
                        converted = false;
                        return true;
                    default:
                        return InvalidConversion(declaredType, input, out errorMessage,
                            "Use true/false, 1/0, yes/no, on/off, or checked/unchecked.");
                }
            }

            if (IsIntegerType(valueType))
            {
                if (TryParseInteger(text, valueType, out converted))
                    return true;
                return InvalidConversion(declaredType, input, out errorMessage,
                    "Decimal, 0x hexadecimal, and 0b binary forms are supported.");
            }

            if (valueType == typeof(float) || valueType == typeof(double) ||
                valueType == typeof(decimal))
            {
                if (TryParseFloatingPoint(text, valueType, out converted))
                    return true;
                return InvalidConversion(declaredType, input, out errorMessage,
                    "Invariant decimal, scientific notation, and percentages are supported.");
            }

            var enumType = valueType == typeof(Enum) && currentValue is Enum currentEnum
                ? currentEnum.GetType()
                : valueType;
            if (enumType.IsEnum)
            {
                if (TryParseEnum(enumType, text, out converted))
                    return true;
                return InvalidConversion(enumType, input, out errorMessage,
                    $"Expected one of: {string.Join(", ", Enum.GetNames(enumType))}.");
            }

            if (TryConvertUnityValue(valueType, text, out converted, out var unityTypeSupported))
                return true;
            if (unityTypeSupported)
                return InvalidConversion(declaredType, input, out errorMessage);

            if (valueType == typeof(Guid) && Guid.TryParse(text, out var guid))
            {
                converted = guid;
                return true;
            }

            if (typeof(UnityEngine.Object).IsAssignableFrom(valueType))
            {
                errorCode = "unsupported_value_type";
                errorMessage =
                    $"Unity object references cannot be resolved from a string for {GetFriendlyTypeName(valueType)}.";
                return false;
            }

            if (TryInvokeStringParser(valueType, text, out converted, out var parserFound))
                return true;
            if (parserFound)
                return InvalidConversion(declaredType, input, out errorMessage);

            errorCode = "unsupported_value_type";
            errorMessage =
                $"No string converter is available for {GetFriendlyTypeName(valueType)}.";
            return false;
        }

        /// <summary>
        ///     Unwraps the exception a reflected call actually threw.
        /// </summary>
        /// <param name="exception">The exception reflection reported.</param>
        /// <returns>
        ///     The inner exception of a <see cref="TargetInvocationException" />, the exception itself
        ///     otherwise. A failing static constructor stays a <see cref="TypeInitializationException" />,
        ///     which is a distinction worth reporting.
        /// </returns>
        internal static Exception UnwrapReflectionException(Exception exception)
        {
            return exception is TargetInvocationException { InnerException: not null }
                ? exception.InnerException
                : exception;
        }

        /// <summary>
        ///     Converts a string to one of the Unity value types that are written as components.
        /// </summary>
        /// <param name="type">The target type.</param>
        /// <param name="input">The trimmed input.</param>
        /// <param name="converted">The converted value on success.</param>
        /// <param name="supported">True when this method is responsible for the type at all.</param>
        /// <returns>True when the value was converted.</returns>
        private static bool TryConvertUnityValue(Type type, string input, out object converted,
            out bool supported)
        {
            converted = null;
            supported = type == typeof(Color) || type == typeof(Color32) ||
                        type == typeof(LayerMask) ||
                        type == typeof(Vector2) || type == typeof(Vector2Int) ||
                        type == typeof(Vector3) || type == typeof(Vector3Int) ||
                        type == typeof(Vector4) || type == typeof(Quaternion) ||
                        type == typeof(Rect) || type == typeof(RectInt) ||
                        type == typeof(Bounds) || type == typeof(BoundsInt);
            if (!supported)
                return false;

            if (type == typeof(Color) || type == typeof(Color32))
            {
                if (!TryParseColor(input, out var color))
                    return false;
                converted = type == typeof(Color) ? (object)color : (Color32)color;
                return true;
            }

            if (type == typeof(LayerMask))
            {
                if (!TryParseInteger(input, typeof(int), out var layerValue))
                    return false;
                converted = new LayerMask { value = (int)layerValue };
                return true;
            }

            var expectedComponents = type == typeof(Vector2) || type == typeof(Vector2Int) ? 2 :
                type == typeof(Vector3) || type == typeof(Vector3Int) ? 3 :
                type == typeof(Vector4) || type == typeof(Quaternion) ||
                type == typeof(Rect) || type == typeof(RectInt) ? 4 :
                type == typeof(Bounds) || type == typeof(BoundsInt) ? 6 : 0;
            if (expectedComponents == 0 ||
                !TryExtractNumericComponents(input, expectedComponents, out var values))
                return false;

            if (type == typeof(Vector2))
                converted = new Vector2(values[0], values[1]);
            else if (type == typeof(Vector3))
                converted = new Vector3(values[0], values[1], values[2]);
            else if (type == typeof(Vector4))
                converted = new Vector4(values[0], values[1], values[2], values[3]);
            else if (type == typeof(Quaternion))
                converted = new Quaternion(values[0], values[1], values[2], values[3]);
            else if (type == typeof(Rect))
                converted = new Rect(values[0], values[1], values[2], values[3]);
            else if (type == typeof(Bounds))
                converted = new Bounds(
                    new Vector3(values[0], values[1], values[2]),
                    new Vector3(values[3], values[4], values[5]));
            else if (!TryConvertToIntComponents(values, out var integers))
                return false;
            else if (type == typeof(Vector2Int))
                converted = new Vector2Int(integers[0], integers[1]);
            else if (type == typeof(Vector3Int))
                converted = new Vector3Int(integers[0], integers[1], integers[2]);
            else if (type == typeof(RectInt))
                converted = new RectInt(integers[0], integers[1], integers[2], integers[3]);
            else if (type == typeof(BoundsInt))
                converted = new BoundsInt(
                    new Vector3Int(integers[0], integers[1], integers[2]),
                    new Vector3Int(integers[3], integers[4], integers[5]));
            return converted != null;
        }

        /// <summary>
        ///     Parses an integer written in decimal, hexadecimal or binary, with optional digit separators.
        /// </summary>
        /// <param name="input">The input.</param>
        /// <param name="type">The integral target type, used for the range check.</param>
        /// <param name="converted">The converted value on success.</param>
        /// <returns>True when the value fits the target type.</returns>
        private static bool TryParseInteger(string input, Type type, out object converted)
        {
            converted = null;
            var text = input.Replace("_", string.Empty).Trim();
            try
            {
                decimal number;
                var negative = text.StartsWith("-", StringComparison.Ordinal);
                var unsignedText = negative ? text.Substring(1) : text;
                if (unsignedText.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    number = Convert.ToUInt64(unsignedText.Substring(2), 16);
                    if (negative)
                        number = -number;
                }
                else if (unsignedText.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
                {
                    number = Convert.ToUInt64(unsignedText.Substring(2), 2);
                    if (negative)
                        number = -number;
                }
                else if (!decimal.TryParse(text, NumberStyles.Integer,
                             CultureInfo.InvariantCulture, out number))
                {
                    return false;
                }

                if (decimal.Truncate(number) != number)
                    return false;
                if (type == typeof(byte))
                    converted = checked((byte)number);
                else if (type == typeof(sbyte))
                    converted = checked((sbyte)number);
                else if (type == typeof(short))
                    converted = checked((short)number);
                else if (type == typeof(ushort))
                    converted = checked((ushort)number);
                else if (type == typeof(int))
                    converted = checked((int)number);
                else if (type == typeof(uint))
                    converted = checked((uint)number);
                else if (type == typeof(long))
                    converted = checked((long)number);
                else if (type == typeof(ulong))
                    converted = checked((ulong)number);
                return converted != null && decimal.Truncate(number) == number;
            }
            catch (Exception exception) when (exception is FormatException or OverflowException)
            {
                return false;
            }
        }

        /// <summary>
        ///     Parses a floating point number, tolerating a type suffix and a percentage sign.
        /// </summary>
        /// <param name="input">The input.</param>
        /// <param name="type">The floating point target type.</param>
        /// <param name="converted">The converted value on success.</param>
        /// <returns>True when the value was parsed.</returns>
        private static bool TryParseFloatingPoint(string input, Type type, out object converted)
        {
            converted = null;
            var text = input.Replace("_", string.Empty).Trim();
            var percentage = text.EndsWith("%", StringComparison.Ordinal);
            if (percentage)
                text = text.Substring(0, text.Length - 1).Trim();
            if (text.EndsWith("f", StringComparison.OrdinalIgnoreCase) ||
                text.EndsWith("d", StringComparison.OrdinalIgnoreCase) ||
                text.EndsWith("m", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(0, text.Length - 1);

            const NumberStyles styles = NumberStyles.Float | NumberStyles.AllowThousands;
            if (type == typeof(float) &&
                float.TryParse(text, styles, CultureInfo.InvariantCulture, out var single))
            {
                converted = percentage ? single / 100f : single;
                return true;
            }

            if (type == typeof(double) &&
                double.TryParse(text, styles, CultureInfo.InvariantCulture, out var doubleValue))
            {
                converted = percentage ? doubleValue / 100d : doubleValue;
                return true;
            }

            if (type == typeof(decimal) &&
                decimal.TryParse(text, styles, CultureInfo.InvariantCulture, out var decimalValue))
            {
                converted = percentage ? decimalValue / 100m : decimalValue;
                return true;
            }

            return false;
        }

        /// <summary>
        ///     Parses an enum from its underlying number, or from one or more member names.
        /// </summary>
        /// <param name="enumType">The concrete enum type.</param>
        /// <param name="input">The input, where <c>,</c>, <c>|</c> and <c>+</c> all combine flags.</param>
        /// <param name="converted">The converted value on success.</param>
        /// <returns>True when every requested member exists.</returns>
        /// <remarks>
        ///     Member names are matched loosely, ignoring case and any non alphanumeric character, so
        ///     <c>lower-left</c> resolves to <c>LowerLeft</c>.
        /// </remarks>
        private static bool TryParseEnum(Type enumType, string input, out object converted)
        {
            converted = null;
            if (TryParseInteger(input, Enum.GetUnderlyingType(enumType), out var numeric))
            {
                converted = Enum.ToObject(enumType, numeric);
                return true;
            }

            var requestedParts = Regex.Split(input, @"\s*[,|+]\s*")
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .ToArray();
            if (requestedParts.Length == 0)
                return false;

            var names = Enum.GetNames(enumType);
            var resolvedNames = new List<string>();
            foreach (var requestedPart in requestedParts)
            {
                var normalized = NormalizeIdentifier(requestedPart);
                var match = names.FirstOrDefault(name =>
                    string.Equals(NormalizeIdentifier(name), normalized,
                        StringComparison.OrdinalIgnoreCase));
                if (match == null)
                    return false;
                resolvedNames.Add(match);
            }

            try
            {
                converted = Enum.Parse(enumType, string.Join(",", resolvedNames), true);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        /// <summary>
        ///     Reduces an identifier to its lowercase alphanumeric characters, so separators and casing
        ///     do not have to match.
        /// </summary>
        /// <param name="value">The identifier.</param>
        /// <returns>The normalized identifier.</returns>
        private static string NormalizeIdentifier(string value)
        {
            return new string(value.Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant).ToArray());
        }

        /// <summary>
        ///     Parses a color written as a name, a hex string, or three to four numeric components.
        /// </summary>
        /// <param name="input">The input.</param>
        /// <param name="color">The parsed color on success.</param>
        /// <returns>True when the value was parsed.</returns>
        /// <remarks>
        ///     Numeric components are read as bytes when any of the three color channels exceeds one,
        ///     because <c>255,128,0</c> and <c>1,0.5,0</c> are both common ways to write a color.
        /// </remarks>
        private static bool TryParseColor(string input, out Color color)
        {
            var text = input.Trim();
            var lower = text.ToLowerInvariant();
            switch (lower)
            {
                case "transparent": color = Color.clear; return true;
                case "black": color = Color.black; return true;
                case "white": color = Color.white; return true;
                case "red": color = Color.red; return true;
                case "green": color = Color.green; return true;
                case "blue": color = Color.blue; return true;
                case "yellow": color = Color.yellow; return true;
                case "cyan": color = Color.cyan; return true;
                case "magenta": color = Color.magenta; return true;
                case "gray":
                case "grey": color = Color.gray; return true;
            }

            if (Regex.IsMatch(text, @"^[0-9a-fA-F]{3,4}$|^[0-9a-fA-F]{6}$|^[0-9a-fA-F]{8}$"))
                text = string.Concat("#", text);
            if (ColorUtility.TryParseHtmlString(text, out color))
                return true;

            if (!TryExtractNumericComponents(text, 3, 4, out var values))
            {
                color = default;
                return false;
            }

            var byteRgb = values.Take(3).Any(component => component > 1f);
            var red = byteRgb ? values[0] / 255f : values[0];
            var green = byteRgb ? values[1] / 255f : values[1];
            var blue = byteRgb ? values[2] / 255f : values[2];
            var alpha = values.Length == 4
                ? values[3] > 1f ? values[3] / 255f : values[3]
                : 1f;
            if (new[] { red, green, blue, alpha }.Any(component => component < 0f || component > 1f))
            {
                color = default;
                return false;
            }

            color = new Color(red, green, blue, alpha);
            return true;
        }

        /// <summary>
        ///     Extracts exactly the expected number of numeric components.
        /// </summary>
        /// <param name="input">The input.</param>
        /// <param name="expectedCount">The required number of components.</param>
        /// <param name="values">The components on success.</param>
        /// <returns>True when exactly that many components were found.</returns>
        private static bool TryExtractNumericComponents(string input, int expectedCount,
            out float[] values)
        {
            return TryExtractNumericComponents(input, expectedCount, expectedCount, out values);
        }

        /// <summary>
        ///     Extracts the numeric components of a value, ignoring whatever separates them.
        /// </summary>
        /// <param name="input">The input.</param>
        /// <param name="minimumCount">The smallest acceptable number of components.</param>
        /// <param name="maximumCount">The largest acceptable number of components.</param>
        /// <param name="values">The components on success.</param>
        /// <returns>True when the component count is within range and every component parsed.</returns>
        private static bool TryExtractNumericComponents(string input, int minimumCount,
            int maximumCount, out float[] values)
        {
            var matches = NumericComponentPattern.Matches(input);
            if (matches.Count < minimumCount || matches.Count > maximumCount)
            {
                values = null;
                return false;
            }

            values = new float[matches.Count];
            for (var index = 0; index < matches.Count; index++)
                if (!float.TryParse(matches[index].Value, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out values[index]))
                {
                    values = null;
                    return false;
                }
            return true;
        }

        /// <summary>
        ///     Converts floating point components to integers, rejecting anything that is not whole.
        /// </summary>
        /// <param name="values">The components.</param>
        /// <param name="integers">The converted components on success.</param>
        /// <returns>True when every component is a whole number within the integer range.</returns>
        private static bool TryConvertToIntComponents(float[] values, out int[] integers)
        {
            integers = new int[values.Length];
            for (var index = 0; index < values.Length; index++)
            {
                if (values[index] < int.MinValue || values[index] > int.MaxValue ||
                    !Mathf.Approximately(values[index], Mathf.Round(values[index])))
                {
                    integers = null;
                    return false;
                }
                integers[index] = Mathf.RoundToInt(values[index]);
            }
            return true;
        }

        /// <summary>
        ///     Converts a string through the type's own parser, as a last resort.
        /// </summary>
        /// <param name="type">The target type.</param>
        /// <param name="input">The input.</param>
        /// <param name="converted">The converted value on success.</param>
        /// <param name="parserFound">
        ///     True when the type does declare a parser, which separates "this type cannot be parsed at
        ///     all" from "this value is not valid for it".
        /// </param>
        /// <returns>True when the parser accepted the value.</returns>
        private static bool TryInvokeStringParser(Type type, string input, out object converted,
            out bool parserFound)
        {
            converted = null;
            parserFound = false;
            try
            {
                var parseWithProvider = type.GetMethod("Parse",
                    BindingFlags.Public | BindingFlags.Static, null,
                    new[] { typeof(string), typeof(IFormatProvider) }, null);
                if (parseWithProvider != null)
                {
                    parserFound = true;
                    converted = parseWithProvider.Invoke(null,
                        new object[] { input, CultureInfo.InvariantCulture });
                    return true;
                }

                var parse = type.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(string) }, null);
                if (parse != null)
                {
                    parserFound = true;
                    converted = parse.Invoke(null, new object[] { input });
                    return true;
                }

                var constructor = type.GetConstructor(new[] { typeof(string) });
                if (constructor == null)
                    return false;
                parserFound = true;
                converted = constructor.Invoke(new object[] { input });
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        ///     Determines whether a type is one of the integral types.
        /// </summary>
        /// <param name="type">The type to test.</param>
        /// <returns>True for the signed and unsigned integral types.</returns>
        private static bool IsIntegerType(Type type)
        {
            return type == typeof(byte) || type == typeof(sbyte) ||
                   type == typeof(short) || type == typeof(ushort) ||
                   type == typeof(int) || type == typeof(uint) ||
                   type == typeof(long) || type == typeof(ulong);
        }

        /// <summary>
        ///     Builds the message for a value that does not describe the target type.
        /// </summary>
        /// <param name="type">The target type.</param>
        /// <param name="input">The rejected input.</param>
        /// <param name="errorMessage">The message to report.</param>
        /// <param name="hint">An optional hint about the accepted forms.</param>
        /// <returns>Always false, so a caller can return it directly.</returns>
        private static bool InvalidConversion(Type type, string input, out string errorMessage,
            string hint = null)
        {
            errorMessage =
                $"Could not convert {QuoteYamlString(input)} to {GetFriendlyTypeName(type)}.";
            if (!string.IsNullOrEmpty(hint))
                errorMessage = string.Concat(errorMessage, " ", hint);
            return false;
        }

        /// <summary>
        ///     Names a type the way a client would write it.
        /// </summary>
        /// <param name="type">The type to name.</param>
        /// <returns>The full name, or the simple name for a type that has none.</returns>
        private static string GetFriendlyTypeName(Type type)
        {
            return type.FullName ?? type.Name;
        }
    }
}
