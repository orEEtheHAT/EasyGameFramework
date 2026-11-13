using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EasySave
{
    public enum SaveScope { Progress = 0, Preferences = 1 }
    public enum SaveError
    {
        None, Busy, Cancelled, Disposed, WrongThread, InvalidRequest, NotFound,
        StorageError, CorruptData, UnsupportedVersion, IncompatibleParticipants,
        ValidationFailed, CaptureFailed, PartialRestore
    }

    public readonly struct SaveValidation
    {
        public bool IsValid { get; }
        public string Message { get; }
        private SaveValidation(bool valid, string message) { IsValid = valid; Message = message; }
        public static SaveValidation Valid => new SaveValidation(true, null);
        public static SaveValidation Invalid(string message) => new SaveValidation(false, message ?? "Invalid snapshot.");
    }

    /// <summary>Callbacks run on the service's owning thread. Capture and Validate must not mutate live state.
    /// Validate must check all data that Restore consumes. Restore may yield, but must honor cancellation.
    /// Stable keys/versions/order are captured at registration, not read from mutable scene names.</summary>
    public interface ISaveParticipant
    {
        string Key { get; }
        SaveScope Scope { get; }
        int Version { get; }
        int RestoreOrder { get; }
        string Capture();
        SaveValidation Validate(string payload);
        Task RestoreAsync(string payload, CancellationToken cancellation);
    }

    /// <summary>Explicit composition of owner adapters for SaveController. Called once per enable,
    /// never scans the scene and never captures or restores live state.</summary>
    public interface ISaveParticipantSource
    {
        string ConfigurationError { get; }
        ISaveParticipant[] CreateParticipants();
    }

    public readonly struct SaveAddress
    {
        public SaveScope Scope { get; }
        public string Slot { get; }
        public SaveAddress(SaveScope scope, string slot)
        { SaveNames.CheckScope(scope); SaveNames.CheckSlot(slot); Scope = scope; Slot = slot; }
        public override string ToString() => Scope + "/" + Slot;
    }

    public readonly struct StorageCommit
    {
        public bool Committed { get; }
        public string Warning { get; }
        public StorageCommit(bool committed, string warning = null) { Committed = committed; Warning = warning; }
    }

    /// <summary>Stores EasySave encoded bytes. null Read means missing. Operations must be bounded/cancellable.
    /// Cancellation before commit throws; after commit return Committed=true even if the token was cancelled.
    /// A backend must serialize conflicting writers across its instances and never expose a partial primary.</summary>
    public interface ISaveStorage
    {
        Task<byte[]> ReadAsync(SaveAddress address, bool backup, int maximumBytes, CancellationToken cancellation);
        Task<StorageCommit> WriteAsync(SaveAddress address, byte[] data, CancellationToken cancellation);
        Task<IReadOnlyList<string>> ListAsync(SaveScope scope, CancellationToken cancellation);
        Task<StorageCommit> DeleteAsync(SaveAddress address, CancellationToken cancellation);
    }

    public sealed class SaveStorageException : Exception
    {
        public SaveError Error { get; }
        public SaveStorageException(SaveError error, string message) : base(message) { Error = error; }
    }

    [Serializable]
    public sealed class SaveOptions
    {
        public int MaximumFileBytes = 16 * 1024 * 1024;
        public int MaximumPayloadBytes = 4 * 1024 * 1024;
        public int MaximumParticipants = 1024;
        public string QuickSlot = "quick";
        public string PreferencesSlot = "user";
        public bool RecoverBackup = true;
        internal SaveOptions Copy()
        {
            if (MaximumFileBytes < 1024 || MaximumFileBytes > 256 * 1024 * 1024 ||
                MaximumPayloadBytes < 1 || MaximumPayloadBytes > MaximumFileBytes ||
                MaximumParticipants < 1 || MaximumParticipants > 65536)
                throw new ArgumentException("Use a file budget of 1 KiB..256 MiB, a payload budget within it, and 1..65536 participants.");
            SaveNames.CheckSlot(QuickSlot); SaveNames.CheckSlot(PreferencesSlot);
            return (SaveOptions)MemberwiseClone();
        }
    }

    [Serializable]
    public sealed class SaveTimings
    {
        public double CaptureMilliseconds, SerializationMilliseconds, StorageMilliseconds, ValidationMilliseconds, RestoreMilliseconds;
        public int Bytes, Participants;
    }

    public sealed class SaveSlotInfo
    {
        public SaveAddress Address { get; internal set; }
        public string DisplayName { get; internal set; }
        public DateTime SavedUtc { get; internal set; }
        public int ParticipantCount { get; internal set; }
        public SaveError Error { get; internal set; }
        public string Message { get; internal set; }
        public bool RecoveredFromBackup { get; internal set; }
    }

    public sealed class SaveResult
    {
        public bool Success => Error == SaveError.None;
        public SaveError Error { get; internal set; }
        public string Message { get; internal set; }
        public bool Committed { get; internal set; }
        public bool RecoveredFromBackup { get; internal set; }
        /// <summary>True once any Restore callback has started, even if it throws before returning.</summary>
        public bool WorldMayHaveChanged { get; internal set; }
        public string FailedParticipant { get; internal set; }
        public IReadOnlyList<string> RestoredParticipants { get; internal set; } = Array.Empty<string>();
        public IReadOnlyList<SaveSlotInfo> Slots { get; internal set; } = Array.Empty<SaveSlotInfo>();
        public SaveTimings Timings { get; } = new SaveTimings();
        public override string ToString() => Success ? (RecoveredFromBackup ? "Success (backup recovered)" : "Success") : Error + ": " + Message;
        internal static SaveResult Fail(SaveError error, string message) => new SaveResult { Error = error, Message = message };
    }

    internal static class SaveNames
    {
        internal static void CheckScope(SaveScope scope)
        { if (scope != SaveScope.Progress && scope != SaveScope.Preferences) throw new ArgumentException("Unknown save scope."); }
        internal static void CheckSlot(string slot)
        {
            if (string.IsNullOrEmpty(slot) || slot.Length > 64) throw new ArgumentException("Slot must contain 1..64 lowercase letters, digits, underscores or hyphens.");
            for (int i = 0; i < slot.Length; i++)
                if (!(slot[i] >= 'a' && slot[i] <= 'z' || slot[i] >= '0' && slot[i] <= '9' || i > 0 && (slot[i] == '_' || slot[i] == '-')))
                    throw new ArgumentException("Slot must start with a lowercase letter/digit and contain only lowercase letters, digits, underscores or hyphens.");
            if (slot == "con" || slot == "prn" || slot == "aux" || slot == "nul" ||
                slot.Length == 4 && (slot.StartsWith("com", StringComparison.Ordinal) || slot.StartsWith("lpt", StringComparison.Ordinal)) && slot[3] >= '0' && slot[3] <= '9')
                throw new ArgumentException("Reserved filesystem slot name.");
        }
        internal static void CheckKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Length > 128) throw new ArgumentException("Participant key must contain 1..128 characters.");
            foreach (char c in key)
                if (!(char.IsLetterOrDigit(c) && c <= 127 || c == '.' || c == '_' || c == '-' || c == '/'))
                    throw new ArgumentException("Use a stable ASCII participant key: letters, digits, '.', '_', '-' or '/'.");
        }
    }
}
