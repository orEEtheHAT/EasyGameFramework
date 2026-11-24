using UnityEngine;

namespace EasyQuests.UI
{
    [AddComponentMenu("")]
    public sealed class QuestSearchRing : QuestMarkerGraphic
    {
        protected override QuestGlyph InitialShape => QuestGlyph.SearchRing;
    }
}
