using UnityEngine;

namespace EasyCombat
{
    [CreateAssetMenu(menuName = "EasyGameFramework/Combat/Execution/Tag", fileName = "CombatTag")]
    public sealed class CombatTagDefinition : ScriptableObject
    {
        public string DisplayName;
        [TextArea] public string Description;
    }
}
