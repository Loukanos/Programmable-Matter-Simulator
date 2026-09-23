using System.Collections.Generic;

namespace ParticleSim.Core
{
    public class ActionBlock
    {
        public string condition;
        public List<string> actions = new();

        public ConditionNode ParsedCondition;
        public List<ActionNode> ParsedActions = new();
    }

    public class OnTouchEvent
    {
        public string direction;
        public List<ActionBlock> blocks = new();
    }

    public class OnMessageEvent
    {
        public string direction;
        public List<ActionBlock> blocks = new();
    }

    public class TypeBehavior
    {
        public List<ActionBlock> onInit = new();
        public List<ActionBlock> onStep = new();
        public List<OnTouchEvent> onTouch = new();
        public List<OnMessageEvent> onMessage = new();
    }
}
