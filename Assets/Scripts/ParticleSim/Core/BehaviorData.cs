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
    /// Behavior of a single particle type.
    /// Supports "onInit" (once at start) and "onStep" (every cycle).
    /// </summary>
    public class TypeBehavior
    {
        public List<ActionBlock> onInit = new();
        public List<ActionBlock> onStep = new();
    }

    /// <summary>
    /// The behavior JSON file is a map of: particleType -> TypeBehavior.
    /// Use BehaviorLoader to deserialize it into a
    /// Dictionary&lt;string, TypeBehavior&gt;.
    /// </summary>
}
