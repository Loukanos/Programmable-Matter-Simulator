using System.Collections.Generic;

namespace ParticleSim.Core
{
    /// <summary>
    /// A block of actions to be executed during an event.
    /// In this first version only the "actions" list is supported.
    /// </summary>
    public class ActionBlock
    {
        public string condition;
        public List<string> actions = new();
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
    /// Behavior of a single particle type.
    /// Supports "onInit" (once at start), "onStep" (every cycle) and
    /// "onTouch"/"onTouch(DIR)" (every cycle the particle is in contact with
    /// a neighbor). Parsing of the onTouch key variants happens in
    /// BehaviorLoader, since it is JSON-shape-specific.
    /// </summary>
    public class TypeBehavior
    {
        public List<ActionBlock> onInit = new();
        public List<ActionBlock> onStep = new();
        public List<OnTouchEvent> onTouch = new();
    }

    /// <summary>
    /// The behavior JSON file is a map of: particleType -> TypeBehavior.
    /// Use BehaviorLoader to deserialize it into a
    /// Dictionary&lt;string, TypeBehavior&gt;.
    /// </summary>
}
