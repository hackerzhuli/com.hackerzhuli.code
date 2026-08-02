using System.Globalization;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Hackerzhuli.Code.Editor
{
    /// <summary>
    ///     Converts Unity's version-specific object identifier to the opaque string used by the
    ///     automation protocol, and resolves that string back to an object.
    /// </summary>
    /// <remarks>
    ///     The protocol deliberately exposes neither the underlying Unity type nor its bit layout.
    ///     Identifiers are lowercase hexadecimal strings only to keep responses compact, and remain valid
    ///     only while Unity considers the underlying object identifier valid.
    /// </remarks>
    internal static class UnityObjectId
    {
        internal static string Get(Object value)
        {
#if UNITY_6000_5_OR_NEWER
            return EntityId.ToULong(value.GetEntityId()).ToString("x", CultureInfo.InvariantCulture);
#else
            return unchecked((uint)value.GetInstanceID()).ToString("x", CultureInfo.InvariantCulture);
#endif
        }

        internal static bool TryResolve(string value, out Object resolved)
        {
            resolved = null;
            if (string.IsNullOrWhiteSpace(value))
                return false;

#if UNITY_6000_5_OR_NEWER
            if (!ulong.TryParse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture,
                    out var raw))
                return false;

            resolved = EditorUtility.EntityIdToObject(EntityId.FromULong(raw));
#else
            if (!uint.TryParse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture,
                    out var raw))
                return false;

            resolved = EditorUtility.InstanceIDToObject(unchecked((int)raw));
#endif
            return true;
        }
    }
}
