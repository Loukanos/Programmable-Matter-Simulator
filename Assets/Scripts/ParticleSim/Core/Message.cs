namespace ParticleSim.Core
{
    public class Message
    {
        public Particle Sender { get; }
        public Particle Recipient { get; }
        public object Content { get; }

        // Where the sender is, seen from the recipient: the opposite of the direction it was sent in.
        public string Direction { get; }

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
