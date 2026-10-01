using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests
{
    public class SendMessagePayloadTests
    {
        private const string SenderOfATextPayload =
            "`S`: { `onInit`: [ { `actions`: [ `setState('dir', 'X+')`, `sendMessage('X+', 'FS', state('dir'))` ] } ] }";

        [Test]
        public void NonNumericComputedPayload_IsDroppedWithAWarning_AndTheMessageStillArrives()
        {
            LogAssert.Expect(LogType.Warning,
                new Regex(@"sendMessage payload dropped: state\(\) did not produce a number for tag 'FS' \(X\+\)"));

            string scenario = Json.Scenario(3, (1, "S", 1, 1, 1), (2, "R", 2, 1, 1));
            string behavior = Json.Of(
                $"{{ {SenderOfATextPayload}, " +
                "`R`: { `onMessage`: [ { `condition`: `checkMessage('FS')`, `actions`: [ `setState('got', true)`, `setState('value', messageValue())` ] } ] } }");

            TestSimulation sim = TestSimulation.Load(scenario, behavior);
            sim.Step();

            Assert.That(sim.State(2, "got"), Is.EqualTo(true));
            Assert.That(sim.State(2, "value"), Is.Null);
        }

        [Test]
        public void NoWarning_WhenTheSendReachesNobody()
        {
            var warnings = new List<string>();
            Application.LogCallback collect = (message, _, type) => { if (type == LogType.Warning) warnings.Add(message); };
            Application.logMessageReceived += collect;
            try
            {
                TestSimulation sim = TestSimulation.Load(Json.Scenario(3, (1, "S", 1, 1, 1)), Json.Of($"{{ {SenderOfATextPayload} }}"));
                sim.Step();
            }
            finally
            {
                Application.logMessageReceived -= collect;
            }

            Assert.That(warnings.Exists(w => w.Contains("payload dropped")), Is.False);
        }
    }
}
