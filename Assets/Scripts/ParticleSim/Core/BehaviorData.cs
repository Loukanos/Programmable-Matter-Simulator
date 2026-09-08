using System.Collections.Generic;

namespace ParticleSim.Core
{
    /// <summary>
    /// A block of actions to be executed during an event. "condition" and
    /// "actions" are the raw JSON text, deserialized directly by
    /// Newtonsoft.Json; "ParsedCondition"/"ParsedActions" are the AST built
    /// from that text once, by BehaviorLoader after validation passes — the
    /// interpreter (SimulationEngine.ConditionEvaluator/ProcessActions) only
    /// ever walks the parsed form, never re-parses the raw string.
    /// </summary>
    public class ActionBlock
    {
        public string condition;
        public List<string> actions = new();

        public ConditionNode ParsedCondition;
        public List<ActionNode> ParsedActions = new();
    }

    /// <summary>
    /// One "onTouch"/"onTouch(DIR)" event entry: the blocks that run while
    /// the particle is in contact with a neighbor. Direction is null for a
    /// bare "onTouch" (any of the six faces triggers it), or one of
    /// X+/X-/Y+/Y-/Z+/Z- when the event key is parameterized, e.g. "onTouch(X+)".
    /// A type may declare several onTouch entries with different directions.
    /// </summary>
    public class OnTouchEvent
    {
        public string direction;
        public List<ActionBlock> blocks = new();
    }

    /// <summary>
    /// One "onMessage"/"onMessage(DIR)" event entry: the blocks that run
    /// while the particle's current-cycle inbox holds at least one message.
    /// Direction is null for a bare "onMessage" (a message from any origin
    /// direction triggers it), or one of X+/X-/Y+/Y-/Z+/Z- when the event key
    /// is parameterized, matching against Message.Direction. Mirrors
    /// OnTouchEvent's shape; kept as a separate type since onTouch and
    /// onMessage are distinct triggers even though their JSON shape and
    /// per-cycle evaluation point (alongside onStep, never during onInit)
    /// coincide.
    /// </summary>
    public class OnMessageEvent
    {
        public string direction;
        public List<ActionBlock> blocks = new();
    }

    /// <summary>
    /// Behavior of a single particle type.
    /// Supports "onInit" (once at start), "onStep" (every cycle),
    /// "onTouch"/"onTouch(DIR)" (every cycle the particle is in contact with
    /// a neighbor) and "onMessage"/"onMessage(DIR)" (every cycle the
    /// particle's inbox holds a message). Parsing of the onTouch/onMessage
    /// key variants happens in BehaviorLoader, since it is JSON-shape-specific.
    /// </summary>
    public class TypeBehavior
    {
        public List<ActionBlock> onInit = new();
        public List<ActionBlock> onStep = new();
        public List<OnTouchEvent> onTouch = new();
        public List<OnMessageEvent> onMessage = new();
    }

    /// <summary>
    /// The behavior JSON file is a map of: particleType -> TypeBehavior.
    /// Use BehaviorLoader to deserialize it into a
    /// Dictionary&lt;string, TypeBehavior&gt;.
    /// </summary>
}
