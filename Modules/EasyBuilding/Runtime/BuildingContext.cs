using System;
using UnityEngine;

namespace EasyBuilding
{
    [Flags]
    public enum BuildingAction
    {
        Place = 1, Move = 2, Upgrade = 4, Remove = 8, Repair = 16, Work = 32,
        Connect = 64, Pack = 128, Customize = 256, State = 512, All = 1023
    }

    public enum BuildingAccess { Everyone, Owner, Team }

    /// <summary>Supply authoritative identity/position in multiplayer. Null means a trusted system operation.</summary>
    public interface IBuildingActor
    {
        string Id { get; }
        string TeamId { get; }
        Vector3 Position { get; }
        bool IsAdministrator { get; }
        bool HasCapability(string capability);
    }

    public readonly struct BuildingContext
    {
        public readonly BuildingSystem System;
        public readonly BuildingDefinition Definition;
        public readonly PlacementRequest Request;
        public readonly Pose Pose;
        public readonly PlacedBuilding Existing;
        public readonly IBuildingActor Actor;
        public readonly BuildingAction Action;
        public readonly int RuleDepth;
        public BuildingContext(BuildingSystem system, BuildingDefinition definition, PlacementRequest request,
            Pose pose, PlacedBuilding existing, IBuildingActor actor, BuildingAction action, int ruleDepth = 0)
        {
            System = system; Definition = definition; Request = request; Pose = pose;
            Existing = existing; Actor = actor; Action = action; RuleDepth = ruleDepth;
        }
        public BuildingContext NestedRule() => new BuildingContext(System, Definition, Request, Pose, Existing, Actor, Action, RuleDepth + 1);
        public int CountAfter(BuildingDefinition definition = null, string tag = null, string ownerId = null) => System.CountAfter(this, definition, tag, ownerId);
    }

    public interface IBuildingRule
    {
        /// <summary>Pure main-thread query. Never spend resources or change the scene here.</summary>
        bool Allows(in BuildingContext context, out string reason);
    }

    public abstract class BuildingRule : ScriptableObject, IBuildingRule
    {
        public BuildingAction Actions = BuildingAction.Place | BuildingAction.Move | BuildingAction.Upgrade;
        public abstract bool Allows(in BuildingContext context, out string reason);
    }

    /// <summary>Optional data supplied by an installed state adapter (for example EasyCombat).</summary>
    public interface IBuildingCondition
    {
        float Condition { get; }
    }

    public interface IBuildingStatePart
    {
        string StateKey { get; }
        int StateVersion { get; }
        bool ReplicateState { get; }
        string CaptureBuildingState();
        /// <summary>Pure validation against authored configuration. Also called on the prefab during snapshot preflight;
        /// do not restore data, mutate live services, or depend on Awake/OnEnable having run.</summary>
        bool CanRestoreBuildingState(int version, string json, out string reason);
        /// <summary>Restore only this inactive instance's state. Do not charge resources or emit gameplay rewards.</summary>
        void RestoreBuildingState(int version, string json);
    }
    /// <summary>Optional target-side conversion for upgrades, such as retaining damage percentage with a new maximum health.</summary>
    public interface IBuildingUpgradeStatePart
    {
        bool TryPrepareUpgradeState(BuildingDefinition previousDefinition, in BuildingPartState previous, out BuildingPartState converted, out string reason);
    }

    [Serializable]
    public struct BuildingPartState
    {
        public string Key;
        public int Version;
        public string Json;
        public bool Replicate;
    }
}
