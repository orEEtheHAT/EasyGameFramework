using System;

namespace EasyQuests
{
    /// <summary>Describes named facts used by a scripted scene interaction for Studio's Where used view.</summary>
    public interface INarrativeFactSource
    {
        void DescribeFacts(Action<WorldFactDatabase, string, FactAccess> add);
    }

    [Flags]
    public enum FactAccess { Read = 1, Write = 2 }
}
