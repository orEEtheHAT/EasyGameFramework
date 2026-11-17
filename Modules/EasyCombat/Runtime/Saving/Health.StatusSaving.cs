using System;

namespace EasyCombat
{
    public sealed partial class Health
    {
        // Capability is Health-owned, never inferred from whether the lazy facade happened to be requested.
        internal SavedStatuses SaveStatuses(SaveCodec codec)
            => statusEngine!=null ? statusEngine.SaveCombatState(codec) : new SavedStatuses();

        internal Action PrepareStatusesRestore(SavedStatuses saved,SaveCodec codec,bool targetAlive,out Action validateCommit)
        {
            // Older snapshots without the optional component mean an empty native status state.
            saved=saved ?? new SavedStatuses();
            SaveCodec.Length(saved.Values,maximumActiveStatuses,"Status groups");
            SaveCodec.Need(targetAlive || saved.Values.Length==0,"Dead actors cannot retain active statuses.");
            SaveCodec.Need(statusesEnabled || saved.Values.Length==0,"Cannot restore active statuses while Health status support is disabled.");
            SaveCodec.Need(statusEngine==null || !statusEngine.IsProcessing,"Invalid/busy status restore.");
            if(saved.Values.Length==0)
            {
                validateCommit=()=>ValidateStatusesRestoreCommit(0,0);
                // No new facade/storage/scheduler during preparation or commit for an unused actor.
                return () => statusEngine?.RestoreEmptyState();
            }
            var candidate=statusEngine ?? new StatusController(this);
            var commit=candidate.PrepareCombatRestore(saved,codec,targetAlive,out var preparedRecords);
            var preparedGroups=saved.Values.Length;
            validateCommit=()=>ValidateStatusesRestoreCommit(preparedGroups,preparedRecords);
            return () =>
            {
                // Assignment occurs only after every actor/module has passed full world preflight.
                if(statusEngine==null) statusEngine=candidate;
                // Cancellation callbacks may have requested the stable facade after preflight.
                // Restore into that facade without replacing it or mutating only a detached candidate.
                commit(statusEngine);
            };
        }
        private void ValidateStatusesRestoreCommit(int groups,int records)
        {
            // Transient cancellation is user code and may change feature configuration after preflight.
            // Every actor runs this check before any saved Health/module state is committed.
            SaveCodec.Need(this!=null,"Actor was destroyed before status restore commit.");
            SaveCodec.Need(statusesEnabled || groups==0,"Cannot restore active statuses while Health status support is disabled.");
            SaveCodec.Need(groups<=maximumActiveStatuses,"Status groups exceed the current restore budget.");
            SaveCodec.Need(records<=maximumIndependentStackRecords,"Status records exceed the current restore budget.");
            SaveCodec.Need(statusEngine==null || !statusEngine.IsProcessing,"Invalid/busy status restore.");
        }
        internal void NotifyStatusesRestored() => NotifyStatusesChanged();
    }
}
