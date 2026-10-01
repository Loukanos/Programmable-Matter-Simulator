using NUnit.Framework;

namespace Tests
{
    public class SetStateTests
    {
        [Test]
        public void SignedLiteral_AddsToCurrentValue()
        {
            TestSimulation sim = TestSimulation.RunSingleParticle("`setState('x', 5)`", "`setState('x', +1)`");
            Assert.That(sim.State(1, "x"), Is.EqualTo(6.0));
        }

        [Test]
        public void SignedLiteral_OnMissingVariable_StartsFromZero()
        {
            TestSimulation sim = TestSimulation.RunSingleParticle(null, "`setState('y', -2)`");
            Assert.That(sim.State(1, "y"), Is.EqualTo(-2.0));
        }

        [Test]
        public void SignedLiteral_OnNonNumericVariable_IsIgnored()
        {
            TestSimulation sim = TestSimulation.RunSingleParticle("`setState('x', 'abc')`", "`setState('x', +1)`");
            Assert.That(sim.State(1, "x"), Is.EqualTo("abc"));
        }

        [Test]
        public void QuotedSignedLiteral_IsAbsoluteAssignment()
        {
            TestSimulation sim = TestSimulation.RunSingleParticle("`setState('x', 5)`", "`setState('x', '-10')`");
            Assert.That(sim.State(1, "x"), Is.EqualTo(-10.0));
        }

        [Test]
        public void ReadFunction_IsAbsoluteAssignment()
        {
            TestSimulation sim = TestSimulation.RunSingleParticle("`setState('y', 7)`, `setState('x', 1)`", "`setState('x', state('y'))`");
            Assert.That(sim.State(1, "x"), Is.EqualTo(7.0));
        }

        [Test]
        public void ActionsInOneBlock_RunInSequence()
        {
            TestSimulation sim = TestSimulation.RunSingleParticle(null, "`setState('x', 1)`, `setState('x', +2)`, `setState('x', +3)`");
            Assert.That(sim.State(1, "x"), Is.EqualTo(6.0));
        }
    }
}
