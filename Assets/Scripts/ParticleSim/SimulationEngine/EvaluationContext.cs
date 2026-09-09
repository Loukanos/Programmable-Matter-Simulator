using ParticleSim.Core;

namespace ParticleSim.SimulationEngine
{
    /// <summary>
    /// Everything ConditionEvaluator/ValueResolver need to evaluate one
    /// block's condition and actions for one particle. Created once per
    /// block (not once per particle/cycle) by SimulationEngine.ProcessActions
    /// and reused across that block's condition and all of its actions, so
    /// MatchedMessage — set as a side effect of a successful checkMessage
    /// inside the condition — is still visible to messageValue() in that same
    /// block's actions.
    /// </summary>
    public class EvaluationContext
    {
        public Particle Particle;
        public IGridQuery Grid;

        /// <summary>
        /// The Message a checkMessage(tag) call matched while evaluating this
        /// block's condition, if any — what messageValue() resolves to. With
        /// more than one checkMessage in the same condition (via And/Or), the
        /// first one that matches, evaluated left-to-right, wins (in
        /// practice always the same single candidate — see CurrentMessage).
        /// </summary>
        public Message MatchedMessage;

        /// <summary>
        /// Set by SimulationEngine.Step() only while processing an
        /// onMessage/onMessage(DIR) block: an onMessage event's blocks are
        /// evaluated once per message in the inbox that matches the event's
        /// direction filter — never once for the whole event regardless of
        /// how many messages arrived — so that N messages carrying the same
        /// tag from N different senders in the same cycle are each seen on
        /// their own pass, instead of collapsing into a single evaluation
        /// that can only ever look at one of them. When set, checkMessage(tag)
        /// checks only this one message instead of scanning the whole inbox
        /// (see ConditionEvaluator.EvaluateCheckMessage) — this is what makes
        /// each pass "about" a specific message. Null for onInit/onStep/
        /// onTouch blocks, where checkMessage still means "does any message
        /// in the inbox match" (existence check), since those aren't tied to
        /// a specific message occurrence.
        /// </summary>
        public Message CurrentMessage;
    }
}
