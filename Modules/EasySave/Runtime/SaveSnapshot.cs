namespace EasySave
{
    /// <summary>Opaque immutable capture for checkpoints/tests. Owns serialized values, never live Unity objects.
    /// Restore uses the same migrations and complete preflight as disk Load. No filesystem access.</summary>
    public sealed class SaveSnapshot
    {
        internal SaveDocument Document { get; }
        public SaveScope Scope => Document.Address.Scope;
        public int ParticipantCount => Document.Entries.Count;
        internal SaveSnapshot(SaveDocument document) { Document = document; }
    }
}
