using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Hackerzhuli.Code.Editor.Messaging;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using MessageType = Hackerzhuli.Code.Editor.Messaging.MessageType;

namespace Hackerzhuli.Code.Editor.Testing
{
    [TestFixture]
    internal class LocaleAutomationTests
    {
        private static readonly IPEndPoint Loopback = new(IPAddress.Loopback, 12345);
        private static readonly IPEndPoint Remote = new(IPAddress.Parse("203.0.113.7"), 12345);

        [Test]
        public void MessageTypes_HaveStableProtocolValues()
        {
            Assert.That((int)MessageType.LocaleList, Is.EqualTo(116));
            Assert.That((int)MessageType.LocaleSelect, Is.EqualTo(117));
        }

        [Test]
        public void BuildLocalesYaml_UsesFixedFieldOrderAndEscaping()
        {
            var locales = new List<LocalizationBridge.LocaleInfo>
            {
                new("en", "English", 0),
                new("zh-Hans", "Chinese \"Simplified\"", 10)
            };

            var yaml = LocaleAutomation.BuildLocalesYaml(locales, "zh-Hans");

            Assert.That(yaml, Is.EqualTo(
                "selectedLocale: \"zh-Hans\"\n" +
                "locales:\n" +
                "  - code: \"en\"\n" +
                "    name: \"English\"\n" +
                "    sortOrder: 0\n" +
                "  - code: \"zh-Hans\"\n" +
                "    name: \"Chinese \\\"Simplified\\\"\"\n" +
                "    sortOrder: 10"));
        }

        [Test]
        public void BuildLocalesYaml_ReportsEmptyStateAndNoSelection()
        {
            var yaml = LocaleAutomation.BuildLocalesYaml(new List<LocalizationBridge.LocaleInfo>(), null);

            Assert.That(yaml, Is.EqualTo("selectedLocale: null\nlocales: []"));
        }

        [Test]
        public void LocaleMessages_RequirePlayMode()
        {
            var list = Send(MessageType.LocaleList, "{\"requestId\":\"locale-1\"}", Loopback);
            Assert.That(list.Value<bool>("ok"), Is.False);
            Assert.That(list["error"]?["code"]?.Value<string>(), Is.EqualTo("not_playing"));

            var select = Send(MessageType.LocaleSelect, "{\"requestId\":\"locale-2\",\"code\":\"en\"}",
                Loopback);
            Assert.That(select["error"]?["code"]?.Value<string>(), Is.EqualTo("not_playing"));
        }

        /// <summary>
        ///     Guards the reflection member names in <see cref="LocalizationBridge" />, which the compiler
        ///     cannot check because this package deliberately does not reference com.unity.localization.
        /// </summary>
        [Test]
        public void LocalizationBridge_ResolvesTheLocalizationApiWhenThePackageIsInstalled()
        {
            var installed = AppDomain.CurrentDomain.GetAssemblies()
                .Any(assembly => assembly.GetName().Name == "Unity.Localization");

            var resolved = LocalizationBridge.TryGetState(out _, out _, out var errorCode,
                out var errorMessage);

            if (!installed)
            {
                Assert.That(resolved, Is.False);
                Assert.That(errorCode, Is.EqualTo(LocalizationBridge.UnavailableCode));
                return;
            }

            // With the package installed, every member must resolve. Not having a Localization Settings
            // asset is the one legitimate reason this can still fail.
            Assert.That(resolved || errorMessage.Contains("Localization Settings asset"), Is.True,
                $"Unexpected failure: {errorCode} - {errorMessage}");
        }

        [Test]
        public void Requests_FromNonLoopbackClientsAreForbidden()
        {
            var response = Send(MessageType.LocaleList, "{\"requestId\":\"locale-3\"}", Remote);

            Assert.That(response.Value<bool>("ok"), Is.False);
            Assert.That(response["error"]?["code"]?.Value<string>(), Is.EqualTo("forbidden"));
        }

        [Test]
        public void Requests_WithoutRequestIdAreRejected()
        {
            var response = Send(MessageType.LocaleList, "{}", Loopback);

            Assert.That(response.Value<bool>("ok"), Is.False);
            Assert.That(response["error"]?["code"]?.Value<string>(), Is.EqualTo("invalid_request"));
        }

        /// <summary>
        ///     Sends a message through the real handler and returns the parsed response.
        /// </summary>
        private static JObject Send(MessageType type, string value, IPEndPoint origin)
        {
            string payload = null;
            var message = new Message { Type = type, Value = value, Origin = origin };

            LocaleAutomation.Process(message, (endPoint, responseType, responseValue) =>
            {
                Assert.That(endPoint, Is.EqualTo(origin));
                Assert.That(responseType, Is.EqualTo(type));
                payload = responseValue;
            });

            Assert.That(payload, Is.Not.Null, "The handler did not answer the request.");
            return JObject.Parse(payload);
        }
    }
}
