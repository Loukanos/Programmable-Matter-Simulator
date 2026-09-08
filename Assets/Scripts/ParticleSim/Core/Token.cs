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
    }

    /// <summary>
    /// One lexical unit of a condition or action expression. Lives in Core
    /// (not InputValidation) because it is also the argument representation
    /// carried by the parsed AST (<see cref="ConditionNode"/>/
    /// <see cref="ActionNode"/>) that SimulationEngine evaluates — the two
    /// modules must not depend on each other, so this shared shape lives
    /// where both can see it, same reasoning as <see cref="LanguageVocabulary"/>.
    /// </summary>
    public class Token
    {
        public TokenKind Kind;
        /// <summary>The token exactly as written in the source, quotes included.</summary>
        public string Text;
        /// <summary>The token's meaning: quoted strings lose their quotes, everything else matches Text.</summary>
        public string Value;
        /// <summary>Character offset in the source expression; only meaningful during parsing/validation, unused at evaluation time.</summary>
        public int Position;

        public override string ToString() => Text;
    }
}
