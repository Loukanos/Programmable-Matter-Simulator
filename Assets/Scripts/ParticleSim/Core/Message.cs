namespace ParticleSim.Core
{
    /// <summary>
    /// Message exchanged between particles via sendMessage/checkMessage/onMessage
    /// (TCC section 2.2.11, Figure 8: Sender/Recipient/Content). Sender,
    /// Recipient, Content, Direction and Payload are all fixed at SEND time —
    /// delivery is always asynchronous (only visible the cycle after it was
    /// sent, see SimulationEngine.DeliverPendingMessages), and particle
    /// positions can change between the send cycle and the delivery cycle, so
    /// recomputing the direction at delivery time would give a different
    /// result than intended.
    /// </summary>
    public class Message
    {
        public Particle Sender { get; }
        public Particle Recipient { get; }
        public object Content { get; }

        /// <summary>
        /// Direction of the Sender relative to the Recipient (X+/X-/Y+/Y-/Z+/Z-),
        /// i.e. the origin direction as seen by the Recipient — the opposite
        /// of the direction passed to sendMessage(dir), which points from the
        /// Sender toward the target neighbor. Used by onMessage(dir).
        /// </summary>
        public string Direction { get; }

        /// <summary>
        /// Optional numeric value from sendMessage(dir, tag, payload) — the
        /// directed 3-argument form only (broadcast and the 2-argument
        /// directed form never carry one). Null when the message was sent
        /// without a payload; messageValue() then resolves to null too, and
        /// any comparison against it is false, same convention as a missing
        /// state key.
        /// </summary>
        public double? Payload { get; }

        public Message(Particle sender, Particle recipient, object content, string direction, double? payload = null)
        {
            Sender = sender;
            Recipient = recipient;
            Content = content;
            Direction = direction;
            Payload = payload;
        }
    }
}
