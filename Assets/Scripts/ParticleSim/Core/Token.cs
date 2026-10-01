namespace ParticleSim.Core
{
    public enum TokenKind
    {
        Identifier,
        Number,
        QuotedString,
        Direction,
        RelationalOperator,
        LParen,
        RParen,
        Comma,
        Sign,
    }

    public class Token
    {
        public TokenKind Kind;
        public string Text;
        public string Value;
        public int Position;

        public override string ToString() => Text;
    }
}
