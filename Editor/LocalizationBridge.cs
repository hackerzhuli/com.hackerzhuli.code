using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace Hackerzhuli.Code.Editor
{
    /// <summary>
    ///     Reflection based access to the optional com.unity.localization package.
    /// </summary>
    /// <remarks>
    ///     This package must build and run in projects that do not have com.unity.localization installed,
    ///     so it must not reference any Localization type at compile time: no assembly definition
    ///     reference, no package dependency. Everything goes through reflection instead, and when the
    ///     package is absent the locale messages simply answer with <c>localization_unavailable</c>.
    ///     The resolved members are cached, and the cache dies with the domain reload that would
    ///     invalidate it.
    /// </remarks>
    internal static class LocalizationBridge
    {
        internal const string UnavailableCode = "localization_unavailable";

        private const string LocalizationAssemblyName = "Unity.Localization";
        private const string SettingsTypeName = "UnityEngine.Localization.Settings.LocalizationSettings";

        private static bool _resolveAttempted;
        private static string _resolveError;
        private static PropertyInfo _hasSettingsProperty;
        private static PropertyInfo _availableLocalesProperty;
        private static PropertyInfo _selectedLocaleProperty;
        private static PropertyInfo _localesProperty;
        private static PropertyInfo _identifierProperty;
        private static PropertyInfo _identifierCodeProperty;
        private static PropertyInfo _localeNameProperty;
        private static PropertyInfo _sortOrderProperty;

        /// <summary>
        ///     The parts of a Locale this package reports, so callers never touch reflection results.
        /// </summary>
        internal readonly struct LocaleInfo
        {
            internal LocaleInfo(string code, string name, int sortOrder)
            {
                Code = code;
                Name = name;
                SortOrder = sortOrder;
            }

            /// <summary>
            ///     The locale identifier code, for example <c>en</c> or <c>zh-Hans</c>.
            /// </summary>
            internal string Code { get; }

            /// <summary>
            ///     The human readable name of the locale.
            /// </summary>
            internal string Name { get; }

            internal int SortOrder { get; }
        }

        /// <summary>
        ///     Gets the available locales and the currently selected one.
        /// </summary>
        /// <param name="locales">The available locales, ordered as the Localization system provides them.</param>
        /// <param name="selectedCode">The selected locale code, null when nothing is selected.</param>
        /// <param name="errorCode">The automation error code when this fails.</param>
        /// <param name="errorMessage">The human readable reason when this fails.</param>
        /// <returns>True when the locales could be read.</returns>
        internal static bool TryGetState(out List<LocaleInfo> locales, out string selectedCode,
            out string errorCode, out string errorMessage)
        {
            locales = null;
            selectedCode = null;

            if (!TryGetLocaleObjects(out var localeObjects, out errorCode, out errorMessage))
                return false;

            try
            {
                locales = new List<LocaleInfo>(localeObjects.Count);
                foreach (var localeObject in localeObjects)
                    locales.Add(Describe(localeObject));

                var selected = _selectedLocaleProperty.GetValue(null);
                selectedCode = selected == null ? null : Describe(selected).Code;
                return true;
            }
            catch (Exception exception)
            {
                errorCode = "internal_error";
                errorMessage = Unwrap(exception).Message;
                return false;
            }
        }

        /// <summary>
        ///     Changes the selected locale, matching on the identifier code first and the locale name second.
        /// </summary>
        /// <param name="codeOrName">The requested locale code or name, matched case insensitively.</param>
        /// <param name="errorCode">The automation error code when this fails.</param>
        /// <param name="errorMessage">The human readable reason when this fails.</param>
        /// <returns>True when the locale was selected.</returns>
        internal static bool TrySelect(string codeOrName, out string errorCode, out string errorMessage)
        {
            if (!TryGetLocaleObjects(out var localeObjects, out errorCode, out errorMessage))
                return false;

            try
            {
                var codes = new List<string>(localeObjects.Count);
                object match = null;
                foreach (var localeObject in localeObjects)
                {
                    var info = Describe(localeObject);
                    codes.Add(info.Code);
                    if (match == null && string.Equals(info.Code, codeOrName, StringComparison.OrdinalIgnoreCase))
                        match = localeObject;
                }

                if (match == null)
                    foreach (var localeObject in localeObjects)
                        if (string.Equals(Describe(localeObject).Name, codeOrName,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            match = localeObject;
                            break;
                        }

                if (match == null)
                {
                    errorCode = "unknown_locale";
                    errorMessage = codes.Count == 0
                        ? $"No locale matches '{codeOrName}', and this project has no available locales."
                        : $"No locale matches '{codeOrName}'. Available codes: {string.Join(", ", codes)}.";
                    return false;
                }

                _selectedLocaleProperty.SetValue(null, match);
                return true;
            }
            catch (Exception exception)
            {
                errorCode = "internal_error";
                errorMessage = Unwrap(exception).Message;
                return false;
            }
        }

        /// <summary>
        ///     Reads the available Locale objects from the Localization system.
        /// </summary>
        private static bool TryGetLocaleObjects(out List<object> localeObjects, out string errorCode,
            out string errorMessage)
        {
            localeObjects = null;
            errorCode = null;
            errorMessage = null;

            if (!TryResolve())
            {
                errorCode = UnavailableCode;
                errorMessage = _resolveError;
                return false;
            }

            try
            {
                if (!(bool)_hasSettingsProperty.GetValue(null))
                {
                    errorCode = UnavailableCode;
                    errorMessage =
                        "This project has no Localization Settings asset. Create one from Edit > Project Settings > Localization.";
                    return false;
                }

                var provider = _availableLocalesProperty.GetValue(null);
                if (provider == null)
                {
                    errorCode = UnavailableCode;
                    errorMessage = "The Localization Settings have no available locales provider.";
                    return false;
                }

                localeObjects = new List<object>();
                if (_localesProperty.GetValue(provider) is IEnumerable locales)
                    foreach (var locale in locales)
                        if (locale != null)
                            localeObjects.Add(locale);

                return true;
            }
            catch (Exception exception)
            {
                errorCode = "internal_error";
                errorMessage = Unwrap(exception).Message;
                return false;
            }
        }

        /// <summary>
        ///     Converts one Locale object into the plain data this package reports.
        /// </summary>
        private static LocaleInfo Describe(object locale)
        {
            var identifier = _identifierProperty.GetValue(locale);
            var code = identifier == null ? string.Empty : _identifierCodeProperty.GetValue(identifier) as string;
            var name = _localeNameProperty.GetValue(locale) as string;
            if (string.IsNullOrEmpty(name) && locale is UnityEngine.Object unityObject)
                name = unityObject.name;

            var sortOrder = Convert.ToInt32(_sortOrderProperty.GetValue(locale));
            return new LocaleInfo(code ?? string.Empty, name ?? string.Empty, sortOrder);
        }

        /// <summary>
        ///     Resolves and caches every Localization member this package uses.
        /// </summary>
        /// <returns>True when com.unity.localization is present and its API looks as expected.</returns>
        private static bool TryResolve()
        {
            if (_resolveAttempted)
                return _resolveError == null;

            _resolveAttempted = true;
            _resolveError =
                "This project does not have the Localization package (com.unity.localization) installed.";

            Assembly localizationAssembly = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                if (string.Equals(assembly.GetName().Name, LocalizationAssemblyName, StringComparison.Ordinal))
                {
                    localizationAssembly = assembly;
                    break;
                }

            if (localizationAssembly == null)
                return false;

            var settingsType = localizationAssembly.GetType(SettingsTypeName, false);
            if (settingsType == null)
                return false;

            const BindingFlags staticFlags = BindingFlags.Public | BindingFlags.Static;
            const BindingFlags instanceFlags = BindingFlags.Public | BindingFlags.Instance;

            _hasSettingsProperty = settingsType.GetProperty("HasSettings", staticFlags);
            _availableLocalesProperty = settingsType.GetProperty("AvailableLocales", staticFlags);
            _selectedLocaleProperty = settingsType.GetProperty("SelectedLocale", staticFlags);

            _localesProperty = _availableLocalesProperty?.PropertyType.GetProperty("Locales", instanceFlags);

            var localeType = _selectedLocaleProperty?.PropertyType;
            _identifierProperty = localeType?.GetProperty("Identifier", instanceFlags);
            _identifierCodeProperty = _identifierProperty?.PropertyType.GetProperty("Code", instanceFlags);
            _localeNameProperty = localeType?.GetProperty("LocaleName", instanceFlags);
            _sortOrderProperty = localeType?.GetProperty("SortOrder", instanceFlags);

            if (_hasSettingsProperty == null || _availableLocalesProperty == null ||
                _selectedLocaleProperty == null || _localesProperty == null || _identifierProperty == null ||
                _identifierCodeProperty == null || _localeNameProperty == null || _sortOrderProperty == null)
            {
                _resolveError =
                    "The installed Localization package does not expose the API this package expects.";
                return false;
            }

            _resolveError = null;
            return true;
        }

        /// <summary>
        ///     Unwraps the reflection wrapper so the client sees the real failure.
        /// </summary>
        private static Exception Unwrap(Exception exception)
        {
            return exception is TargetInvocationException invocation && invocation.InnerException != null
                ? invocation.InnerException
                : exception;
        }
    }
}
