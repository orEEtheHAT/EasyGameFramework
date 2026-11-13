using EasyCombat;
using UnityEngine;

namespace EasyGameFramework.Editor
{
    /// <summary>Authoring manifest for repeatable Playground setup; never used by gameplay.</summary>
    public sealed class FrameworkCombatSampleAssets : ScriptableObject
    {
        [SerializeField] private string folder;
        [SerializeField] private AttackDefinition fireball;
        [SerializeField] private CombatStatDefinition movementSpeed;
        [SerializeField] private bool complete;
        public string Folder => folder;
        public AttackDefinition Fireball => fireball;
        public CombatStatDefinition MovementSpeed => movementSpeed;
        public bool Complete => complete;
        internal void Initialize(string folder,AttackDefinition fireball,CombatStatDefinition movementSpeed)
        { this.folder=folder; this.fireball=fireball; this.movementSpeed=movementSpeed; complete=true; }
    }
}
