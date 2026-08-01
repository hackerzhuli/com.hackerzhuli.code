using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Hackerzhuli.Code.Editor
{
    /// <summary>
    ///     Formats arbitrary runtime values as the compact YAML scalars used by every automation response.
    /// </summary>
    /// <remarks>
    ///     Shared by UI Toolkit inspections and GameObject inspections, which both reflect over types the
    ///     package knows nothing about and therefore need the same defensive formatting rules:
    ///     invariant culture everywhere, round-trip <c>"R"</c> floats, and a single line per value.
    ///     A <see cref="Object" /> is never expanded, it is reduced to its type, name and instance id, so a
    ///     value can reference another object without the formatter ever recursing into an object graph.
    /// </remarks>
    internal static class AutomationValueFormatter
    {
        /// <summary>
        ///     The number of items of a collection that are written before the rest is summarized.
        /// </summary>
        /// <remarks>
        ///     Without a cap a single large array, such as a mesh's vertex list, would dominate or even
        ///     overflow a response.
        /// </remarks>
        private const int EnumerableItemLimit = 20;

        /// <summary>
        ///     Formats a value as a single YAML scalar or inline collection.
        /// </summary>
        /// <param name="value">The value to format, may be null.</param>
        /// <returns>The formatted value, never containing a line break.</returns>
        internal static string FormatValue(object value)
        {
            return FormatValue(value, 0);
        }

        /// <summary>
        ///     Formats a value, tracking how deep inside a collection it is.
        /// </summary>
        /// <param name="value">The value to format, may be null.</param>
        /// <param name="depth">0 for a value in its own right, 1 for an item of a collection.</param>
        /// <returns>The formatted value, never containing a line break.</returns>
        /// <remarks>
        ///     The <see cref="Object" /> and <see cref="VisualElement" /> cases must come before the
        ///     <see cref="IEnumerable" /> one. <see cref="Transform" /> enumerates its children and
        ///     <see cref="VisualElement" /> its child elements, so testing for a collection first would
        ///     turn a reference to one into a list of something else entirely.
        /// </remarks>
        private static string FormatValue(object value, int depth)
        {
            if (value == null)
                return "null";

            switch (value)
            {
                case string text:
                    return QuoteYamlString(text);
                case char character:
                    return QuoteYamlString(character.ToString());
                case bool boolean:
                    return boolean ? "true" : "false";
                case Enum enumeration:
                    return enumeration.ToString();
                case byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal:
                    return Convert.ToString(value, CultureInfo.InvariantCulture);
                case LayerMask layerMask:
                    return layerMask.value.ToString(CultureInfo.InvariantCulture);
                case Vector2 vector2:
                    return FormatVector2(vector2);
                case Vector3 vector3:
                    return FormatVector3(vector3);
                case Vector4 vector4:
                    return FormatVector4(vector4);
                case Quaternion quaternion:
                    return FormatQuaternion(quaternion);
                case Vector2Int vector2Int:
                    return FormatVector2Int(vector2Int);
                case Vector3Int vector3Int:
                    return FormatVector3Int(vector3Int);
                case Bounds bounds:
                    return FormatBounds(bounds);
                case Rect rect:
                    return FormatRect(rect);
                case Color color:
                    return FormatColor(color);
                case VisualElement visualElement:
                    return QuoteYamlString(
                        $"{visualElement.GetType().Name}(name={visualElement.name})");
                case Object unityObject:
                    return QuoteYamlString(
                        $"{unityObject.GetType().Name}(name={unityObject.name},instanceId={unityObject.GetInstanceID()})");
                case IEnumerable enumerable:
                    // A collection of collections is summarized rather than expanded, because the
                    // result has to stay on one line.
                    return depth == 0
                        ? FormatEnumerable(enumerable)
                        : QuoteYamlString($"<{enumerable.GetType().Name}>");
                case IFormattable formattable:
                    return QuoteYamlString(formattable.ToString(null, CultureInfo.InvariantCulture));
                default:
                    return QuoteYamlString(value.ToString());
            }
        }

        /// <summary>
        ///     Wraps a string in a double quoted, escaped YAML scalar.
        /// </summary>
        /// <param name="value">The raw string, may be null.</param>
        /// <returns>The quoted scalar.</returns>
        internal static string QuoteYamlString(string value)
        {
            return string.Concat("\"", AutomationProtocol.EscapeYamlString(value ?? string.Empty), "\"");
        }

        /// <summary>
        ///     Formats a collection of strings as an inline YAML sequence.
        /// </summary>
        /// <param name="values">The strings to format.</param>
        /// <returns>The inline sequence.</returns>
        internal static string FormatStringCollection(IEnumerable<string> values)
        {
            return string.Concat("[", string.Join(",",
                values.Select(value => $"\"{AutomationProtocol.EscapeYamlString(value ?? string.Empty)}\"")), "]");
        }

        /// <summary>
        ///     Formats an arbitrary collection as an inline YAML sequence, summarizing a long tail.
        /// </summary>
        /// <param name="values">The collection to format.</param>
        /// <returns>The inline sequence, at most <see cref="EnumerableItemLimit" /> items plus a summary.</returns>
        internal static string FormatEnumerable(IEnumerable values)
        {
            var items = new List<string>();
            var total = 0;
            foreach (var value in values)
            {
                total++;
                if (items.Count < EnumerableItemLimit)
                    items.Add(FormatValue(value, 1));
            }

            if (total > items.Count)
                items.Add($"\"...{total - items.Count} more\"");

            return string.Concat("[", string.Join(",", items), "]");
        }

        /// <summary>
        ///     Formats a two component vector as an inline YAML sequence.
        /// </summary>
        internal static string FormatVector2(Vector2 value)
        {
            return string.Concat("[", Number(value.x), ",", Number(value.y), "]");
        }

        /// <summary>
        ///     Formats a three component vector as an inline YAML sequence.
        /// </summary>
        internal static string FormatVector3(Vector3 value)
        {
            return string.Concat("[", Number(value.x), ",", Number(value.y), ",", Number(value.z), "]");
        }

        /// <summary>
        ///     Formats a four component vector as an inline YAML sequence.
        /// </summary>
        internal static string FormatVector4(Vector4 value)
        {
            return string.Concat("[", Number(value.x), ",", Number(value.y), ",", Number(value.z), ",",
                Number(value.w), "]");
        }

        /// <summary>
        ///     Formats a quaternion as its raw components, not as Euler angles, so no conversion is implied.
        /// </summary>
        internal static string FormatQuaternion(Quaternion value)
        {
            return string.Concat("[", Number(value.x), ",", Number(value.y), ",", Number(value.z), ",",
                Number(value.w), "]");
        }

        /// <summary>
        ///     Formats a two component integer vector as an inline YAML sequence.
        /// </summary>
        internal static string FormatVector2Int(Vector2Int value)
        {
            return string.Concat("[", value.x.ToString(CultureInfo.InvariantCulture), ",",
                value.y.ToString(CultureInfo.InvariantCulture), "]");
        }

        /// <summary>
        ///     Formats a three component integer vector as an inline YAML sequence.
        /// </summary>
        internal static string FormatVector3Int(Vector3Int value)
        {
            return string.Concat("[", value.x.ToString(CultureInfo.InvariantCulture), ",",
                value.y.ToString(CultureInfo.InvariantCulture), ",",
                value.z.ToString(CultureInfo.InvariantCulture), "]");
        }

        /// <summary>
        ///     Formats bounds as an inline YAML mapping of its center and size.
        /// </summary>
        internal static string FormatBounds(Bounds value)
        {
            return string.Concat("{center: ", FormatVector3(value.center), ", size: ",
                FormatVector3(value.size), "}");
        }

        /// <summary>
        ///     Formats a rect as an inline YAML sequence of x, y, width and height.
        /// </summary>
        internal static string FormatRect(Rect value)
        {
            return string.Concat("[", Number(value.x), ",", Number(value.y), ",", Number(value.width), ",",
                Number(value.height), "]");
        }

        /// <summary>
        ///     Formats a color as a hex string, dropping a fully opaque alpha channel.
        /// </summary>
        internal static string FormatColor(Color value)
        {
            var rgba = ColorUtility.ToHtmlStringRGBA(value);
            var hex = rgba.EndsWith("FF", StringComparison.Ordinal)
                ? rgba.Substring(0, 6)
                : rgba;
            return QuoteYamlString(string.Concat("#", hex));
        }

        /// <summary>
        ///     Appends an indented <c>name: value</c> line.
        /// </summary>
        /// <param name="builder">The document being built.</param>
        /// <param name="indent">The indent level, two spaces each.</param>
        /// <param name="name">The property name.</param>
        /// <param name="value">The property value, formatted by <see cref="FormatValue" />.</param>
        internal static void AppendYamlValue(StringBuilder builder, int indent, string name, object value)
        {
            builder.Append(' ', indent * 2);
            builder.Append(name);
            builder.Append(": ");
            builder.Append(FormatValue(value));
            builder.Append('\n');
        }

        /// <summary>
        ///     Formats a float with round-trip precision in the invariant culture.
        /// </summary>
        private static string Number(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}
