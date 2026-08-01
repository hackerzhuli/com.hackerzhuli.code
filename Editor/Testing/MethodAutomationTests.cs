using System;
using System.Globalization;
using System.Net;
using Hackerzhuli.Code.Editor.Messaging;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using MessageType = Hackerzhuli.Code.Editor.Messaging.MessageType;

namespace Hackerzhuli.Code.Editor.Testing
{
    /// <summary>
    ///     Covers <see cref="MethodAutomation" />, driving the real handler against a fixture type.
    /// </summary>
    [TestFixture]
    internal class MethodAutomationTests
    {
        private static readonly IPEndPoint Loopback = new(IPAddress.Loopback, 12345);
        private static readonly IPEndPoint Remote = new(IPAddress.Parse("203.0.113.7"), 12345);

        private const string SampleTypeName =
            "Hackerzhuli.Code.Editor.Testing.MethodAutomationTests+Sample";

        [Test]
        public void MessageTypes_HaveStableProtocolValues()
        {
            Assert.That((int)MessageType.InvokeMethod, Is.EqualTo(123));
        }

        [Test]
        public void Invoke_ConvertsArgumentsAndReturnsTheResult()
        {
            var response = Invoke(SampleTypeName, "Echo", "\"hi\"");

            Assert.That(response.Value<bool>("ok"), Is.True, response.ToString());
            Assert.That(response.Value<string>("result"), Is.EqualTo("hi"));
        }

        [Test]
        public void Invoke_AcceptsAnAssemblyQualifiedTypeName()
        {
            var response = Invoke(typeof(Sample).AssemblyQualifiedName, "Half");

            Assert.That(response.Value<bool>("ok"), Is.True, response.ToString());
            Assert.That(response.Value<string>("result"), Is.EqualTo("1.5"));
        }

        [Test]
        public void Invoke_FormatsTheResultInTheInvariantCulture()
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                var response = Invoke(SampleTypeName, "Half");

                Assert.That(response.Value<bool>("ok"), Is.True, response.ToString());
                Assert.That(response.Value<string>("result"), Is.EqualTo("1.5"));
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        [Test]
        public void Invoke_PicksTheFirstOverloadThatAcceptsTheArguments()
        {
            // Both overloads accept whole numbers, and Int32 sorts before Single.
            var integers = Invoke(SampleTypeName, "Add", "\"1\"", "\"2\"");
            Assert.That(integers.Value<bool>("ok"), Is.True, integers.ToString());
            Assert.That(integers.Value<string>("result"), Is.EqualTo("3"));

            // Only the float overload can take these, so the int one is skipped.
            var floats = Invoke(SampleTypeName, "Add", "\"1.5\"", "\"2.5\"");
            Assert.That(floats.Value<bool>("ok"), Is.True, floats.ToString());
            Assert.That(floats.Value<string>("result"), Is.EqualTo("4"));
        }

        [Test]
        public void Invoke_OmitsTheResultForAVoidMethodAndKeepsNullForAReferenceOne()
        {
            var nothing = Invoke(SampleTypeName, "DoNothing");
            Assert.That(nothing.Value<bool>("ok"), Is.True, nothing.ToString());
            Assert.That(nothing.ContainsKey("result"), Is.False,
                "A void method has no result to report.");

            var nullResult = Invoke(SampleTypeName, "Nothing");
            Assert.That(nullResult.Value<bool>("ok"), Is.True, nullResult.ToString());
            Assert.That(nullResult.ContainsKey("result"), Is.True);
            Assert.That(nullResult["result"]?.Type, Is.EqualTo(JTokenType.Null));
        }

        [Test]
        public void Invoke_TreatsMissingArgumentsAsNone()
        {
            var response = Send(MessageType.InvokeMethod,
                $"{{\"requestId\":\"inv-1\",\"typeName\":\"{SampleTypeName}\",\"methodName\":\"Half\"}}",
                Loopback);

            Assert.That(response.Value<bool>("ok"), Is.True, response.ToString());
        }

        [Test]
        public void Invoke_ReportsTheCauseWhenTheMethodThrows()
        {
            var response = Invoke(SampleTypeName, "Fail");

            AssertError(response, "invocation_failed");
            Assert.That(ErrorMessage(response), Does.Contain("boom"));
            Assert.That(ErrorMessage(response), Does.Contain("InvalidOperationException"));
        }

        [Test]
        public void Invoke_RejectsAnUnknownType()
        {
            AssertError(Invoke("MyGame.NoSuchType", "Half"), "unknown_type");
        }

        [Test]
        public void Invoke_RejectsMethodsItCannotAddress()
        {
            AssertError(Invoke(SampleTypeName, "NoSuchMethod"), "unknown_method");
            AssertError(Invoke(SampleTypeName, "Hidden"), "unknown_method");
            AssertError(Invoke(SampleTypeName, "Instance"), "unknown_method");
            AssertError(Invoke(SampleTypeName, "Echo"), "unknown_method");
        }

        [Test]
        public void Invoke_RejectsSignaturesItCannotSupply()
        {
            AssertError(Invoke(SampleTypeName, "Ref", "\"1\""), "unsupported_method");
            AssertError(Invoke(SampleTypeName, "Generic", "\"1\""), "unsupported_method");
            AssertError(Invoke(SampleTypeName, "Params", "\"1\""), "unsupported_method");
        }

        [Test]
        public void Invoke_ReportsAConversionFailurePerArgument()
        {
            var response = Invoke(SampleTypeName, "Echo", "42");

            AssertError(response, "invalid_request");
            Assert.That(ErrorMessage(response), Does.Contain("args[0]"));
        }

        [Test]
        public void Invoke_RequiresATypeNameAndAMethodName()
        {
            AssertError(Send(MessageType.InvokeMethod,
                "{\"requestId\":\"inv-1\",\"methodName\":\"Half\"}", Loopback), "invalid_request");
            AssertError(Send(MessageType.InvokeMethod,
                $"{{\"requestId\":\"inv-1\",\"typeName\":\"{SampleTypeName}\"}}", Loopback),
                "invalid_request");
        }

        [Test]
        public void Invoke_RejectsNonLoopbackCallersAndMalformedRequests()
        {
            AssertError(Send(MessageType.InvokeMethod,
                $"{{\"requestId\":\"inv-1\",\"typeName\":\"{SampleTypeName}\",\"methodName\":\"Half\"}}",
                Remote), "forbidden");
            AssertError(Send(MessageType.InvokeMethod, "not json", Loopback), "invalid_request");
            AssertError(Send(MessageType.InvokeMethod, "{\"methodName\":\"Half\"}", Loopback),
                "invalid_request");
        }

        private static JObject Invoke(string typeName, string methodName, params string[] jsonArguments)
        {
            var args = jsonArguments.Length == 0
                ? string.Empty
                : $",\"args\":[{string.Join(",", jsonArguments)}]";
            var escapedType = typeName?.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return Send(MessageType.InvokeMethod,
                $"{{\"requestId\":\"inv-1\",\"typeName\":\"{escapedType}\"," +
                $"\"methodName\":\"{methodName}\"{args}}}", Loopback);
        }

        private static JObject Send(MessageType type, string value, IPEndPoint origin)
        {
            string payload = null;
            var message = new Message { Type = type, Value = value, Origin = origin };

            MethodAutomation.Process(message, (endPoint, responseType, responseValue) =>
            {
                Assert.That(endPoint, Is.EqualTo(origin));
                Assert.That(responseType, Is.EqualTo(type));
                payload = responseValue;
            });

            Assert.That(payload, Is.Not.Null, "The handler did not answer the request.");
            return JObject.Parse(payload);
        }

        private static void AssertError(JObject response, string expectedCode)
        {
            Assert.That(response.Value<bool>("ok"), Is.False, response.ToString());
            Assert.That(response["error"]?["code"]?.Value<string>(), Is.EqualTo(expectedCode),
                response.ToString());
        }

        private static string ErrorMessage(JObject response)
        {
            return response["error"]?["message"]?.Value<string>() ?? string.Empty;
        }

        /// <summary>
        ///     The invocation target, covering one case of every rule the handler applies.
        /// </summary>
        private class Sample
        {
            public static int Add(int a, int b)
            {
                return a + b;
            }

            public static float Add(float a, float b)
            {
                return a + b;
            }

            public static string Echo(string value)
            {
                return value;
            }

            public static float Half()
            {
                return 1.5f;
            }

            public static string Nothing()
            {
                return null;
            }

            public static void DoNothing()
            {
            }

            public static string Fail()
            {
                throw new InvalidOperationException("boom");
            }

            public static int Ref(ref int value)
            {
                return value;
            }

            public static T Generic<T>(T value)
            {
                return value;
            }

            public static int Params(params int[] values)
            {
                return values.Length;
            }

            private static int Hidden()
            {
                return 1;
            }

            public int Instance()
            {
                return 1;
            }
        }
    }
}
