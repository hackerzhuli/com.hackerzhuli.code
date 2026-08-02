using System.Net;
using Hackerzhuli.Code.Editor.Messaging;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using MessageType = Hackerzhuli.Code.Editor.Messaging.MessageType;

namespace Hackerzhuli.Code.Editor.Testing
{
    internal class EcsAutomationTests
    {
        [Test]
        public void MessageTypes_HaveStableValues()
        {
            Assert.That((int)MessageType.EcsWorldList, Is.EqualTo(124));
            Assert.That((int)MessageType.EcsSystemList, Is.EqualTo(125));
            Assert.That((int)MessageType.EcsSystemInspect, Is.EqualTo(126));
            Assert.That((int)MessageType.EcsEntityQuery, Is.EqualTo(127));
            Assert.That((int)MessageType.EcsEntityInspect, Is.EqualTo(128));
            Assert.That((int)MessageType.EcsVisualSnapshot, Is.EqualTo(129));
        }

        [Test]
        public void RemoteRequest_IsForbiddenAndEchoesEnvelope()
        {
            string response = null;
            var message = new Message
            {
                Type = MessageType.EcsWorldList,
                Origin = new IPEndPoint(IPAddress.Parse("203.0.113.1"), 1234),
                Value = "{\"requestId\":42}"
            };
            EcsAutomation.Process(message, (_, type, value) =>
            {
                Assert.That(type, Is.EqualTo(MessageType.EcsWorldList));
                response = value;
            });
            var json = JObject.Parse(response);
            Assert.That(json["requestId"]?.Value<int>(), Is.EqualTo(42));
            Assert.That(json["ok"]?.Value<bool>(), Is.False);
            Assert.That(json["error"]?["code"]?.Value<string>(), Is.EqualTo("forbidden"));
        }

        [Test]
        public void EditModeRequest_IsRejected()
        {
            Assert.That(EditorApplication.isPlaying, Is.False);
            string response = null;
            EcsAutomation.Process(new Message
            {
                Type = MessageType.EcsWorldList,
                Origin = new IPEndPoint(IPAddress.Loopback, 1234),
                Value = "{\"requestId\":\"edit\"}"
            }, (_, _, value) => response = value);
            Assert.That(JObject.Parse(response)["error"]?["code"]?.Value<string>(), Is.EqualTo("not_playing"));
        }
    }
}
