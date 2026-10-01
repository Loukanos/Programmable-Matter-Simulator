namespace ParticleSim.Core
{
    public class Message
    {
        public Particle Sender { get; }
        public Particle Recipient { get; }
        public object Content { get; }
        public string OriginDirection { get; }
        public double? Payload { get; }

        public Message(Particle sender, Particle recipient, object content, string originDirection, double? payload = null)
        {
            Sender = sender;
            Recipient = recipient;
            Content = content;
            OriginDirection = originDirection;
            Payload = payload;
        }
    }
}
