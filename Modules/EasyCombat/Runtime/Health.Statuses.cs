using System;
using UnityEngine;

namespace EasyCombat
{
    public sealed partial class Health
    {
        [SerializeField, Tooltip("Allow statuses, stat/resistance modifiers and status control restrictions. Enabled by default; no status storage exists until used.")]
        private bool statusesEnabled = true;
        [SerializeField, Min(1)] private int maximumActiveStatuses = 32;
        [SerializeField, Range(1, 32)] private int maximumCatchUpTicksPerStatus = 8;
        [SerializeField, Min(1), Tooltip("Independent records, including expired records with overdue ticks. New applications fail when this budget is full.")]
        private int maximumIndependentStackRecords = 512;
        private StatusController statusEngine;
        private CombatSignal statusesChanged;
        private CombatSignal<StatusChange> statusLifecycle;

        public bool StatusesEnabled
        {
            get => statusesEnabled;
            set
            {
                if(statusesEnabled==value) return;
                statusesEnabled=value;
                if(!value) statusEngine?.Clear(StatusRemovalReason.Disabled);
                else statusEngine?.Reconcile();
            }
        }
        /// <summary>Stable advanced managed API; creating the facade creates no status collections or scheduler.</summary>
        public StatusController Statuses => statusEngine ?? (statusEngine=new StatusController(this));
        internal StatusController ExistingStatuses => statusEngine;
        internal bool HasAllocatedStatusStorage => statusEngine!=null && statusEngine.HasAllocatedStorage;
        public int StatusCount => statusEngine?.Count ?? 0;
        public int StatusRecordCount => statusEngine?.IndependentRecordCount ?? 0;
        public CombatRestriction StatusRestrictions => statusesEnabled ? statusEngine?.Restrictions ?? CombatRestriction.None : CombatRestriction.None;
        public int StatusCapacity
        {
            get => maximumActiveStatuses;
            set
            {
                if(value<1) throw new ArgumentOutOfRangeException(nameof(value));
                if(statusEngine!=null && (statusEngine.IsProcessing || value<statusEngine.Count))
                    throw new InvalidOperationException("Status capacity cannot shrink below the current groups or change during status processing.");
                maximumActiveStatuses=value;
            }
        }
        public int StatusCatchUpLimit
        {
            get => maximumCatchUpTicksPerStatus;
            set
            {
                if(value<1 || value>32) throw new ArgumentOutOfRangeException(nameof(value));
                if(statusEngine!=null && statusEngine.IsProcessing)
                    throw new InvalidOperationException("Change the status catch-up limit between status updates.");
                maximumCatchUpTicksPerStatus=value;
            }
        }
        public int StatusRecordCapacity
        {
            get => maximumIndependentStackRecords;
            set
            {
                if(value<1) throw new ArgumentOutOfRangeException(nameof(value));
                if(statusEngine!=null && (statusEngine.IsProcessing || value<statusEngine.IndependentRecordCount))
                    throw new InvalidOperationException("Status record capacity cannot shrink below current records or change during status processing.");
                maximumIndependentStackRecords=value;
            }
        }
        public event Action StatusesChanged
        {
            add { if(value==null) return; if(statusesChanged==null) statusesChanged=new CombatSignal(); statusesChanged.Add(value); }
            remove { statusesChanged?.Remove(value); }
        }
        public event Action<StatusChange> StatusLifecycle
        {
            add { if(value==null) return; if(statusLifecycle==null) statusLifecycle=new CombatSignal<StatusChange>(); statusLifecycle.Add(value); }
            remove { statusLifecycle?.Remove(value); }
        }
        internal bool HasStatusLifecycleListeners => statusLifecycle!=null && statusLifecycle.Count>0;
        internal void NotifyStatusesChanged() => statusesChanged?.Publish();
        internal void NotifyStatusLifecycle(StatusChange change) => statusLifecycle?.Publish(change);

        /// <summary>Apply a status without requiring a separate component. Disabled/inactive/dead receivers reject it.</summary>
        public bool ApplyStatus(StatusDefinition definition,GameObject source=null,DamageOrigin origin=default)
            => statusesEnabled && isActiveAndEnabled && IsAlive && definition!=null && Statuses.Apply(definition,source,origin);
        public bool RemoveStatus(StatusDefinition definition,StatusRemovalReason reason=StatusRemovalReason.Removed)
            => statusEngine!=null && statusEngine.Remove(definition,reason);
        public void ClearStatuses(StatusRemovalReason reason=StatusRemovalReason.Cleared) => statusEngine?.Clear(reason);
        public float GetStatMultiplier(CombatStatDefinition stat)
            => statusesEnabled ? statusEngine?.GetStatMultiplier(stat) ?? 1f : 1f;
        public float GetStatusResistanceModifier(DamageTypeDefinition type)
            => statusesEnabled ? statusEngine?.GetResistanceModifier(type) ?? 0f : 0f;
        internal void ReconcileStatuses() => statusEngine?.Reconcile();
        internal void DestroyStatuses() => statusEngine?.Destroy();
    }
}
