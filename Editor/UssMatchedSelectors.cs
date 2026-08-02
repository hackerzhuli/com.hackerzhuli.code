using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hackerzhuli.Code.Editor
{
    /// <summary>
    ///     Reports which USS rules match a <see cref="VisualElement" />, the way the "Matching Selectors"
    ///     section of Unity's UI Toolkit Debugger does.
    /// </summary>
    /// <remarks>
    ///     Selector matching is not part of the public UI Toolkit API, so this runs Unity's own matcher,
    ///     <c>StyleSelectorHelper.FindMatches</c>, through reflection, and does the small amount of work
    ///     around it that the matcher expects a caller to do: walk the ancestors, push them into the
    ///     match context and stack up their style sheets. The editor once had a debugger helper for
    ///     exactly that, <c>UnityEditor.UIElements.Debugger.MatchedRulesExtractor</c>, but it was removed
    ///     in Unity 6000.2, while everything used here is unchanged across 6000.0 to 6000.3.
    ///     Unity 6000.5 moved the matcher into the generic
    ///     <c>StyleSelectorHelper&lt;TProfilerType&gt;</c>; both layouts are supported here. Every reflection
    ///     step is still optional: when a Unity version renames or moves one of the
    ///     members, <see cref="TryGetMatchedRules" /> reports why instead of throwing, so an inspection
    ///     keeps all of its other sections.
    /// </remarks>
    internal static class UssMatchedSelectors
    {
        private const string ExtensionsTypeName = "UnityEngine.UIElements.StyleSheets.StyleSheetExtensions";

        private const BindingFlags AnyInstance =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private const BindingFlags AnyStatic =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static bool _initialized;
        private static string _initializationError;

        private static ConstructorInfo _contextConstructor;
        private static object _processResult;
        private static FieldInfo _contextCurrentElement;
        private static FieldInfo _contextAncestorFilter;
        private static MethodInfo _contextAddStyleSheet;
        private static PropertyInfo _flattenedImports;
        private static MethodInfo _pushElement;
        private static MethodInfo _findMatches;
        private static Type _recordListType;
        private static MethodInfo _recordListSort;
        private static object _recordComparison;
        private static FieldInfo _recordSheet;
        private static FieldInfo _recordComplexSelector;
        private static PropertyInfo _complexSelectorSpecificity;
        private static PropertyInfo _specificityIdScore;
        private static PropertyInfo _specificityClassScore;
        private static PropertyInfo _specificityTypeScore;
        private static PropertyInfo _complexSelectorRule;
        private static PropertyInfo _complexSelectorSelectors;
        private static PropertyInfo _selectorParts;
        private static PropertyInfo _selectorPreviousRelationship;
        private static PropertyInfo _partType;
        private static PropertyInfo _partValue;
        private static FieldInfo _ruleLine;
        private static PropertyInfo _ruleProperties;
        private static PropertyInfo _propertyName;
        private static PropertyInfo _propertyLine;
        private static PropertyInfo _propertyValues;
        private static PropertyInfo _handleValueType;
        private static MethodInfo _readAsString;
        private static MethodInfo _readColor;

        /// <summary>
        ///     One declaration inside a matched rule, exactly as it is written in the USS source.
        /// </summary>
        /// <remarks>
        ///     Shorthands are not expanded, so <see cref="Name" /> can be either a longhand such as
        ///     <c>margin-left</c> or a shorthand such as <c>margin</c>, whichever the author wrote.
        /// </remarks>
        internal readonly struct UssDeclaration
        {
            internal UssDeclaration(string name, string value, int line)
            {
                Name = name;
                Value = value;
                Line = line;
            }

            internal string Name { get; }
            internal string Value { get; }

            /// <summary>
            ///     The line the declaration is written on, 1 based, or 0 when the style sheet carries no
            ///     line information.
            /// </summary>
            internal int Line { get; }
        }

        /// <summary>
        ///     One USS rule whose selector matches the inspected element.
        /// </summary>
        internal sealed class UssMatchedRule
        {
            internal UssMatchedRule(string selector, string source, int specificity,
                IReadOnlyList<UssDeclaration> declarations)
            {
                Selector = selector;
                Source = source;
                Specificity = specificity;
                Declarations = declarations;
            }

            /// <summary>
            ///     The selector as written, for example <c>.hud-group .toolbar-button</c>.
            /// </summary>
            internal string Selector { get; }

            /// <summary>
            ///     Where the rule lives, as <c>path:line</c>, for example
            ///     <c>Assets/UI/GameScreen.uss:63</c>. Style sheets without a project asset path, such as
            ///     the built in runtime theme, are identified by their name instead.
            /// </summary>
            internal string Source { get; }

            /// <summary>
            ///     Unity's selector specificity, which decides who wins between two rules in the same
            ///     style sheet.
            /// </summary>
            internal int Specificity { get; }

            internal IReadOnlyList<UssDeclaration> Declarations { get; }
        }

        /// <summary>
        ///     Collects the USS rules matching <paramref name="element" />, ordered the way Unity applies
        ///     them: from lowest to highest priority, so a later rule overrides an earlier one that
        ///     declares the same property.
        /// </summary>
        /// <param name="element">The element to match against, which does not need to be in a panel.</param>
        /// <param name="rules">The matched rules, empty when no selector matches.</param>
        /// <param name="error">Why matching could not run, when this returns false.</param>
        /// <returns>True when matching ran, even when nothing matched.</returns>
        internal static bool TryGetMatchedRules(VisualElement element,
            out IReadOnlyList<UssMatchedRule> rules, out string error)
        {
            rules = Array.Empty<UssMatchedRule>();

            if (element == null)
            {
                error = "No element to match.";
                return false;
            }

            if (!Initialize(out error))
                return false;

            try
            {
                var context = _contextConstructor.Invoke(new[] { _processResult });
                _contextCurrentElement.SetValue(context, element);
                PushAncestors(element, context);

                var records = (IList)Activator.CreateInstance(_recordListType);
                _findMatches.Invoke(null, new[] { context, records });
                // The matcher reports in lookup order, so sort it into the order Unity applies.
                _recordListSort.Invoke(records, new[] { _recordComparison });

                var matched = new List<UssMatchedRule>(records.Count);
                foreach (var record in records)
                {
                    var rule = BuildRule(record);
                    if (rule != null)
                        matched.Add(rule);
                }

                rules = matched;
                error = null;
                return true;
            }
            catch (Exception exception)
            {
                var cause = exception is TargetInvocationException { InnerException: not null }
                    ? exception.InnerException
                    : exception;
                error = $"{cause.GetType().Name}: {cause.Message}";
                return false;
            }
        }

        /// <summary>
        ///     Prepares the match context the way Unity's own style traversal does: ancestors before
        ///     descendants, each pushed into the filter the matcher tests candidates against, and each
        ///     one's style sheets stacked up so a rule is attributed to the right sheet.
        /// </summary>
        private static void PushAncestors(VisualElement element, object context)
        {
            var parent = element.hierarchy.parent;
            if (parent != null)
                PushAncestors(parent, context);

            _pushElement.Invoke(_contextAncestorFilter.GetValue(context), new object[] { element });

            var sheets = element.styleSheets;
            for (var index = 0; index < sheets.count; index++)
            {
                var sheet = sheets[index];
                if (sheet == null)
                    continue;

                // A sheet's imports go on the stack ahead of the sheet itself, so that what the sheet
                // declares wins over what it imported. A theme such as UnityDefaultRuntimeTheme.tss
                // declares nothing at all and is only a wrapper around its imports, so skipping this
                // loses every built in control style.
                if (_flattenedImports.GetValue(sheet) is IList imports)
                    foreach (var import in imports)
                        if (import != null)
                            _contextAddStyleSheet.Invoke(context, new[] { import });

                _contextAddStyleSheet.Invoke(context, new object[] { sheet });
            }
        }

        /// <summary>
        ///     Stands in for the callback the matcher reports pseudo state dependencies to, which only
        ///     a live panel has any use for. It is generic so that it can be bound to the internal
        ///     result type without naming it.
        /// </summary>
        private static void IgnoreMatchResult<TResult>(VisualElement element, TResult result)
        {
        }

        private static UssMatchedRule BuildRule(object record)
        {
            var complexSelector = _recordComplexSelector.GetValue(record);
            if (complexSelector == null)
                return null;

            var sheet = _recordSheet.GetValue(record) as StyleSheet;
            var rule = _complexSelectorRule.GetValue(complexSelector);
            var line = rule != null ? (int)_ruleLine.GetValue(rule) : 0;

            return new UssMatchedRule(
                BuildSelectorText(complexSelector),
                BuildSource(sheet, line),
                GetSpecificity(complexSelector),
                BuildDeclarations(sheet, rule));
        }

        private static int GetSpecificity(object complexSelector)
        {
            var specificity = _complexSelectorSpecificity.GetValue(complexSelector);
            // Up to Unity 6000.4 specificity is an int. Unity 6000.5 wraps it in the
            // Specificity value type, whose implicit int conversion exposes a new packed bit layout.
            // Rebuild the previous public value so inspections stay stable across Unity versions.
            return specificity is int value
                ? value
                : 1 + (byte)_specificityIdScore.GetValue(specificity) * 100 +
                (byte)_specificityClassScore.GetValue(specificity) * 10 +
                (byte)_specificityTypeScore.GetValue(specificity);
        }

        /// <summary>
        ///     Rebuilds the selector text from its parsed parts, the same way the UI Toolkit Debugger
        ///     does, because Unity keeps no copy of the original source text.
        /// </summary>
        private static string BuildSelectorText(object complexSelector)
        {
            var selectors = (Array)_complexSelectorSelectors.GetValue(complexSelector);
            if (selectors == null)
                return string.Empty;

            var builder = new StringBuilder();
            foreach (var selector in selectors)
            {
                // StyleSelectorRelationship: 1 is Child, 2 is Descendent.
                switch ((int)_selectorPreviousRelationship.GetValue(selector))
                {
                    case 1:
                        builder.Append(" > ");
                        break;
                    case 2:
                        builder.Append(' ');
                        break;
                }

                var parts = (Array)_selectorParts.GetValue(selector);
                if (parts == null)
                    continue;

                foreach (var part in parts)
                {
                    // StyleSelectorType: 3 is Class, 4 and 5 are pseudo classes, 6 is ID. Wildcard and
                    // Type parts already carry their own text.
                    switch ((int)_partType.GetValue(part))
                    {
                        case 3:
                            builder.Append('.');
                            break;
                        case 4:
                        case 5:
                            builder.Append(':');
                            break;
                        case 6:
                            builder.Append('#');
                            break;
                    }

                    builder.Append((string)_partValue.GetValue(part));
                }
            }

            return builder.ToString();
        }

        private static string BuildSource(StyleSheet sheet, int line)
        {
            if (sheet == null)
                return line > 0 ? $"<unknown>:{line.ToString(CultureInfo.InvariantCulture)}" : "<unknown>";

            var path = AssetDatabase.GetAssetPath(sheet);
            // Built in style sheets such as the default runtime theme live outside the project, where a
            // path would say nothing useful.
            if (string.IsNullOrEmpty(path) || path.StartsWith("Library/", StringComparison.OrdinalIgnoreCase))
                path = sheet.name;

            return line > 0 ? $"{path}:{line.ToString(CultureInfo.InvariantCulture)}" : path;
        }

        private static IReadOnlyList<UssDeclaration> BuildDeclarations(StyleSheet sheet, object rule)
        {
            if (rule == null)
                return Array.Empty<UssDeclaration>();

            var properties = (Array)_ruleProperties.GetValue(rule);
            if (properties == null || properties.Length == 0)
                return Array.Empty<UssDeclaration>();

            var declarations = new List<UssDeclaration>(properties.Length);
            foreach (var property in properties)
                declarations.Add(new UssDeclaration(
                    (string)_propertyName.GetValue(property),
                    BuildDeclarationValue(sheet, property),
                    (int)_propertyLine.GetValue(property)));

            return declarations;
        }

        /// <summary>
        ///     Rebuilds a declaration value from its parsed value handles, close to how it was written.
        /// </summary>
        /// <remarks>
        ///     A parsed value keeps no end marker for a function, so the closing parentheses of any open
        ///     function are added at the end. That is exact for the common cases, a single function such
        ///     as <c>rgb(...)</c> or a nested <c>var(...)</c>, and can misplace a parenthesis only when
        ///     one value contains several separate function calls.
        /// </remarks>
        private static string BuildDeclarationValue(StyleSheet sheet, object property)
        {
            var handles = (Array)_propertyValues.GetValue(property);
            if (sheet == null || handles == null || handles.Length == 0)
                return string.Empty;

            var builder = new StringBuilder();
            var openFunctions = 0;
            foreach (var handle in handles)
            {
                var text = ReadHandle(sheet, handle, ref openFunctions);
                if (string.IsNullOrEmpty(text))
                    continue;

                var isSeparator = text == ",";
                var afterFunctionStart = builder.Length > 0 && builder[builder.Length - 1] == '(';
                if (builder.Length > 0 && !isSeparator && !afterFunctionStart)
                    builder.Append(' ');

                builder.Append(text);
            }

            builder.Append(')', openFunctions);
            return builder.ToString();
        }

        private static string ReadHandle(StyleSheet sheet, object handle, ref int openFunctions)
        {
            try
            {
                // StyleValueType: 4 is Color, 10 is Function.
                switch ((int)_handleValueType.GetValue(handle))
                {
                    case 4:
                        // Written back as hex, the way colors are reported everywhere else.
                        return FormatColor((Color)_readColor.Invoke(sheet, new[] { handle }));
                    case 10:
                        openFunctions++;
                        return (string)_readAsString.Invoke(null, new[] { sheet, handle }) + "(";
                    default:
                        return (string)_readAsString.Invoke(null, new[] { sheet, handle });
                }
            }
            catch (Exception)
            {
                return "<unreadable>";
            }
        }

        private static string FormatColor(Color value)
        {
            var rgba = ColorUtility.ToHtmlStringRGBA(value);
            return string.Concat("#",
                rgba.EndsWith("FF", StringComparison.Ordinal) ? rgba.Substring(0, 6) : rgba);
        }

        private static bool Initialize(out string error)
        {
            if (_initialized)
            {
                error = _initializationError;
                return _initializationError == null;
            }

            _initialized = true;
            _initializationError = Bind();
            error = _initializationError;
            return _initializationError == null;
        }

        private static string Bind()
        {
            try
            {
                var uiElements = typeof(VisualElement).Assembly;
                var contextType = uiElements.GetType("UnityEngine.UIElements.StyleMatchingContext", false);
                var filterType = uiElements.GetType("UnityEngine.UIElements.AncestorFilter", false);
                var helperType = uiElements.GetType("UnityEngine.UIElements.StyleSheets.StyleSelectorHelper", false);
                var genericHelperType = uiElements.GetType(
                    "UnityEngine.UIElements.StyleSheets.StyleSelectorHelper`1", false);
                var noOpProfilerType = uiElements.GetType(
                    "UnityEngine.UIElements.StyleSheets.NoOpStyleProfiler", false);
                // Unity 6000.5 left a non-generic StyleSelectorHelper shell in place, but moved all
                // matching methods to StyleSelectorHelper<TProfilerType>. Use the same no-op profiler
                // specialization as Unity's own MatchedRulesExtractor when that layout is available.
                if (genericHelperType != null && noOpProfilerType != null)
                    helperType = genericHelperType.MakeGenericType(noOpProfilerType);
                var resultType = uiElements.GetType("UnityEngine.UIElements.StyleSheets.MatchResultInfo", false);
                var extensionsType = uiElements.GetType(ExtensionsTypeName, false);
                var recordType = uiElements.GetType("UnityEngine.UIElements.StyleSheets.SelectorMatchRecord", false);
                var complexSelectorType = uiElements.GetType("UnityEngine.UIElements.StyleComplexSelector", false);
                var selectorType = uiElements.GetType("UnityEngine.UIElements.StyleSelector", false);
                var partType = uiElements.GetType("UnityEngine.UIElements.StyleSelectorPart", false);
                var ruleType = uiElements.GetType("UnityEngine.UIElements.StyleRule", false);
                var propertyType = uiElements.GetType("UnityEngine.UIElements.StyleProperty", false);
                var handleType = uiElements.GetType("UnityEngine.UIElements.StyleValueHandle", false);
                if (contextType == null || filterType == null || helperType == null ||
                    resultType == null || extensionsType == null || recordType == null ||
                    complexSelectorType == null || selectorType == null || partType == null ||
                    ruleType == null || propertyType == null || handleType == null)
                    return "The internal UI Toolkit style sheet types are not available in this Unity version.";

                _recordListType = typeof(List<>).MakeGenericType(recordType);
                var processResultType = typeof(Action<,>).MakeGenericType(typeof(VisualElement), resultType);
                _contextConstructor = contextType.GetConstructor(AnyInstance, null,
                    new[] { processResultType }, null);
                _processResult = Delegate.CreateDelegate(processResultType,
                    typeof(UssMatchedSelectors)
                        .GetMethod(nameof(IgnoreMatchResult), AnyStatic)
                        ?.MakeGenericMethod(resultType));
                _contextCurrentElement = contextType.GetField("currentElement", AnyInstance);
                _contextAncestorFilter = contextType.GetField("ancestorFilter", AnyInstance);
                _contextAddStyleSheet = contextType.GetMethod("AddStyleSheet", AnyInstance, null,
                    new[] { typeof(StyleSheet) }, null);
                _flattenedImports = typeof(StyleSheet).GetProperty("flattenedRecursiveImports", AnyInstance);
                _pushElement = filterType.GetMethod("PushElement", AnyInstance, null,
                    new[] { typeof(VisualElement) }, null);
                _findMatches = helperType.GetMethod("FindMatches", AnyStatic, null,
                    new[] { contextType, _recordListType }, null);
                var comparisonType = typeof(Comparison<>).MakeGenericType(recordType);
                _recordListSort = _recordListType.GetMethod("Sort", new[] { comparisonType });
                var compare = recordType.GetMethod("Compare", AnyStatic, null,
                    new[] { recordType, recordType }, null);
                if (compare != null)
                    _recordComparison = Delegate.CreateDelegate(comparisonType, compare);
                _recordSheet = recordType.GetField("sheet", AnyInstance);
                _recordComplexSelector = recordType.GetField("complexSelector", AnyInstance);
                _complexSelectorSpecificity = complexSelectorType.GetProperty("specificity", AnyInstance);
                if (_complexSelectorSpecificity != null &&
                    _complexSelectorSpecificity.PropertyType != typeof(int))
                {
                    var specificityType = _complexSelectorSpecificity.PropertyType;
                    _specificityIdScore = specificityType.GetProperty("idScore", AnyInstance);
                    _specificityClassScore = specificityType.GetProperty("classScore", AnyInstance);
                    _specificityTypeScore = specificityType.GetProperty("typeScore", AnyInstance);
                }
                _complexSelectorRule = complexSelectorType.GetProperty("rule", AnyInstance);
                _complexSelectorSelectors = complexSelectorType.GetProperty("selectors", AnyInstance);
                _selectorParts = selectorType.GetProperty("parts", AnyInstance);
                _selectorPreviousRelationship = selectorType.GetProperty("previousRelationship", AnyInstance);
                _partType = partType.GetProperty("type", AnyInstance);
                _partValue = partType.GetProperty("value", AnyInstance);
                _ruleLine = ruleType.GetField("line", AnyInstance);
                _ruleProperties = ruleType.GetProperty("properties", AnyInstance);
                _propertyName = propertyType.GetProperty("name", AnyInstance);
                _propertyLine = propertyType.GetProperty("line", AnyInstance);
                _propertyValues = propertyType.GetProperty("values", AnyInstance);
                _handleValueType = handleType.GetProperty("valueType", AnyInstance);
                _readAsString = extensionsType.GetMethod("ReadAsString", AnyStatic);
                _readColor = typeof(StyleSheet).GetMethod("ReadColor", AnyInstance, null,
                    new[] { handleType }, null);

                var missing = FindMissingMember();
                return missing == null
                    ? null
                    : $"The internal UI Toolkit member '{missing}' is not available in this Unity version.";
            }
            catch (Exception exception)
            {
                return $"{exception.GetType().Name}: {exception.Message}";
            }
        }

        private static string FindMissingMember()
        {
            if (_contextConstructor == null) return "StyleMatchingContext..ctor";
            if (_processResult == null) return "StyleMatchingContext.processResult";
            if (_contextCurrentElement == null) return "StyleMatchingContext.currentElement";
            if (_contextAncestorFilter == null) return "StyleMatchingContext.ancestorFilter";
            if (_contextAddStyleSheet == null) return "StyleMatchingContext.AddStyleSheet";
            if (_flattenedImports == null) return "StyleSheet.flattenedRecursiveImports";
            if (_pushElement == null) return "AncestorFilter.PushElement";
            if (_findMatches == null) return "StyleSelectorHelper.FindMatches";
            if (_recordListSort == null) return "List<SelectorMatchRecord>.Sort";
            if (_recordComparison == null) return "SelectorMatchRecord.Compare";
            if (_recordSheet == null) return "SelectorMatchRecord.sheet";
            if (_recordComplexSelector == null) return "SelectorMatchRecord.complexSelector";
            if (_complexSelectorSpecificity == null) return "StyleComplexSelector.specificity";
            if (_complexSelectorSpecificity.PropertyType != typeof(int) &&
                (_specificityIdScore == null || _specificityClassScore == null ||
                 _specificityTypeScore == null))
                return "Specificity scores";
            if (_complexSelectorRule == null) return "StyleComplexSelector.rule";
            if (_complexSelectorSelectors == null) return "StyleComplexSelector.selectors";
            if (_selectorParts == null) return "StyleSelector.parts";
            if (_selectorPreviousRelationship == null) return "StyleSelector.previousRelationship";
            if (_partType == null) return "StyleSelectorPart.type";
            if (_partValue == null) return "StyleSelectorPart.value";
            if (_ruleLine == null) return "StyleRule.line";
            if (_ruleProperties == null) return "StyleRule.properties";
            if (_propertyName == null) return "StyleProperty.name";
            if (_propertyLine == null) return "StyleProperty.line";
            if (_propertyValues == null) return "StyleProperty.values";
            if (_handleValueType == null) return "StyleValueHandle.valueType";
            if (_readAsString == null) return "StyleSheetExtensions.ReadAsString";
            if (_readColor == null) return "StyleSheet.ReadColor";
            return null;
        }
    }
}
