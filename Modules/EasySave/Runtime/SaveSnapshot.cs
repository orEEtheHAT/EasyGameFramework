namespace EasySave
{
    /// <summary>Opaque immutable capture for checkpoints/tests. Owns serialized values, never live Unity objects.
    /// Restore uses the same migrations and complete preflight as disk Load. No filesystem access.</summary>
    public sealed class SaveSnapshot
    {
        internal SaveDocument Document { get; }
        public SaveScope Scope => Document.Address.Scope;
        /// <summary>Read a bounded, checksum-validated file for inspection without restoring participants.</summary>
        public static SaveSnapshot InspectFile(byte[] data, SaveOptions options = null) =>
            new SaveSnapshot(SaveFormat.Inspect(data, (options ?? new SaveOptions()).Copy()));
        public System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<string,string>> ReadPayloads()
        {
            foreach (var entry in Document.Entries)
                yield return new System.Collections.Generic.KeyValuePair<string,string>(entry.Key,entry.Payload);
        }
        public int ParticipantCount => Document.Entries.Count;
        internal SaveSnapshot(SaveDocument document) { Document = document; }
    }
}
