using System;
using System.Collections.Generic;
using EasyResources;

namespace EasySave.Integrations
{
    /// <summary>Atomic owner restore of several wallets. A named-reservations snapshot must be accompanied by
    /// its operation-owner participants. Restore those after wallets and rebind holds by ID; never pay again.</summary>
    public static class ResourceLedgerSave
    {
        [Serializable] private sealed class Snapshot { public int Version = 1; public Entry[] Ledgers; }
        [Serializable] private sealed class Entry { public string Key; public ResourceContainerState State; }
        public static ISaveParticipant Create(string key, string[] ledgerKeys, ResourceStore[] stores,
            IEnumerable<ResourceDefinition> definitions, bool includeReservations = false, Func<long> nowMilliseconds = null)
        {
            if (ledgerKeys == null || stores == null || ledgerKeys.Length != stores.Length || stores.Length == 0)
                throw new ArgumentException("Supply matching stable ledger keys and stores.");
            if (includeReservations && nowMilliseconds == null) throw new ArgumentException("Named reservations require the operation owners' millisecond clock.");
            var keys = (string[])ledgerKeys.Clone(); var owners = (ResourceStore[])stores.Clone();
            var catalog = new List<ResourceDefinition>(definitions ?? throw new ArgumentNullException(nameof(definitions)));
            var unique = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < keys.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(keys[i]) || !unique.Add(keys[i]) || owners[i] == null) throw new ArgumentException("Each ledger needs a distinct stable key and an owner.");
                for (int j = 0; j < i; j++) if (ReferenceEquals(owners[i], owners[j])) throw new ArgumentException("A resource store is registered twice.");
            }
            long Now() => nowMilliseconds?.Invoke() ?? 0;
            ResourceRestoreOptions Options(long now) => new ResourceRestoreOptions(resumeReservations: includeReservations, replaceActiveReservations: includeReservations, nowMilliseconds: now);
            ResourceContainerState[] States(Snapshot snapshot)
            {
                if (snapshot == null || snapshot.Version != 1 || snapshot.Ledgers == null || snapshot.Ledgers.Length != keys.Length) throw new ArgumentException("Resource ledger set/version differs.");
                var byKey = new Dictionary<string, ResourceContainerState>(StringComparer.Ordinal);
                foreach (var entry in snapshot.Ledgers)
                    if (entry == null || entry.Key == null || entry.State == null || !byKey.TryAdd(entry.Key, entry.State)) throw new ArgumentException("Missing/duplicate ledger state.");
                var result = new ResourceContainerState[keys.Length];
                for (int i = 0; i < keys.Length; i++) if (!byKey.TryGetValue(keys[i], out result[i])) throw new ArgumentException("Missing ledger: " + keys[i]);
                return result;
            }
            return new JsonSaveParticipant<Snapshot>(key, SaveScope.Progress, 1, () =>
            {
                long now = Now(); var snapshot = new Snapshot { Ledgers = new Entry[keys.Length] };
                for (int i = 0; i < keys.Length; i++) snapshot.Ledgers[i] = new Entry { Key = keys[i], State = owners[i].CaptureState(includeReservations, now) };
                return snapshot;
            }, snapshot =>
            {
                var states = States(snapshot); var options = Options(Now());
                for (int i = 0; i < owners.Length; i++) owners[i].ValidateState(states[i], catalog, options);
                return SaveValidation.Valid;
            }, snapshot => ResourceSnapshots.RestoreAll(owners, States(snapshot), catalog, Options(Now())), -100);
        }
    }
}
