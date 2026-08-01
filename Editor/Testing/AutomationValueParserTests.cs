using System;
using NUnit.Framework;
using UnityEngine;

namespace Hackerzhuli.Code.Editor.Testing
{
    /// <summary>
    ///     Covers the string to value conversion shared by <c>UiSetValue</c> and <c>InvokeMethod</c>.
    /// </summary>
    [TestFixture]
    internal class AutomationValueParserTests
    {
        [Test]
        public void TryConvertValue_ConvertsCommonStrings()
        {
            AssertConverted(typeof(bool), " OFF ", false);
            AssertConverted(typeof(bool), "true", true);
            AssertConverted(typeof(int), "0x2A", 42);
            AssertConverted(typeof(long), "0b101010", 42L);
            AssertConverted(typeof(float), "25%", 0.25f);
            AssertConverted(typeof(double), "1.5e2", 150d);
            AssertConverted(typeof(SampleMode), "multi-line", SampleMode.MultiLine);
            AssertConverted(typeof(Vector2Int), "[4, -5]", new Vector2Int(4, -5));
            AssertConverted(typeof(Vector3), "(1, -2.5, 3e1)", new Vector3(1f, -2.5f, 30f));
            AssertConverted(typeof(Vector3), "x=1; y=-2.5; z=3e1",
                new Vector3(1f, -2.5f, 30f));
            AssertConverted(typeof(Rect), "{x:1,y:2,width:3,height:4}",
                new Rect(1f, 2f, 3f, 4f));

            Assert.That(AutomationValueParser.TryConvertValue(
                typeof(Color), "#12345678", null, out var colorValue, out _, out _), Is.True);
            Assert.That((Color32)(Color)colorValue, Is.EqualTo(new Color32(0x12, 0x34, 0x56, 0x78)));
        }

        [Test]
        public void TryConvertValue_PassesStringAndObjectThrough()
        {
            Assert.That(AutomationValueParser.TryConvertValue(
                typeof(string), " kept as is ", null, out var text, out _, out _), Is.True);
            Assert.That(text, Is.EqualTo(" kept as is "));

            Assert.That(AutomationValueParser.TryConvertValue(
                typeof(object), "42", null, out var boxed, out _, out _), Is.True);
            Assert.That(boxed, Is.EqualTo("42"));
        }

        [Test]
        public void TryConvertValue_ReportsInvalidValueWithTheTargetType()
        {
            Assert.That(AutomationValueParser.TryConvertValue(
                typeof(int), "not-a-number", null, out _, out var errorCode, out var error), Is.False);
            Assert.That(errorCode, Is.EqualTo("invalid_value"));
            Assert.That(error, Does.Contain("System.Int32"));
        }

        [Test]
        public void TryConvertValue_RejectsNullForAValueType()
        {
            Assert.That(AutomationValueParser.TryConvertValue(
                typeof(int), null, null, out _, out var errorCode, out _), Is.False);
            Assert.That(errorCode, Is.EqualTo("invalid_value"));

            Assert.That(AutomationValueParser.TryConvertValue(
                typeof(string), null, null, out var reference, out _, out _), Is.True);
            Assert.That(reference, Is.Null);
        }

        [Test]
        public void TryConvertValue_ReportsUnsupportedValueTypeWithoutAConverter()
        {
            Assert.That(AutomationValueParser.TryConvertValue(
                typeof(Texture2D), "anything", null, out _, out var errorCode, out _), Is.False);
            Assert.That(errorCode, Is.EqualTo("unsupported_value_type"));
        }

        private static void AssertConverted(Type type, string input, object expected)
        {
            Assert.That(AutomationValueParser.TryConvertValue(
                type, input, null, out var converted, out _, out var error), Is.True, error);
            Assert.That(converted, Is.EqualTo(expected));
            Assert.That(converted.GetType(), Is.EqualTo(type));
        }

        private enum SampleMode
        {
            FirstOption,
            MultiLine
        }
    }
}
