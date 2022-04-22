using System;

namespace BlocklyBridge
{
    public class Scriptable : Attribute
    {

    }

    public class ScriptableBehavior : Scriptable
    {
        public readonly string name;
        public readonly int color;
        public ScriptableBehavior(string name, int color)
        {
            this.name = name;
            this.color = color;
        }
    }

    public class ScriptableMethod : Scriptable
    {
    }

    public class ScriptableProperty : Scriptable
    {
    }

    public class ScriptableEvent : Scriptable
    {
        public readonly bool stackable;

        public ScriptableEvent(bool stackable = false)
        {
            this.stackable = stackable;
        }
    }

}