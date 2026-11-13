using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace EasySave
{
    /// <summary>Small adapter for JsonUtility-compatible DTOs. Validators must be read-only and complete.
    /// The DTO belongs to its mechanic; this adapter never stores a second live copy of that state.</summary>
    public sealed class JsonSaveParticipant<T> : ISaveParticipant
    {
        public string Key { get; }
        public SaveScope Scope { get; }
        public int Version { get; }
        public int RestoreOrder { get; }
        private readonly Func<T> capture;
        private readonly Func<T, SaveValidation> validate;
        private readonly Func<T, CancellationToken, Task> restore;
        public JsonSaveParticipant(string key, SaveScope scope, int version, Func<T> capture,
            Func<T, SaveValidation> validate, Action<T> restore, int restoreOrder = 0)
            : this(key, scope, version, capture, validate, (value, token) =>
              { token.ThrowIfCancellationRequested(); restore(value); return Task.CompletedTask; }, restoreOrder)
        { if (restore == null) throw new ArgumentNullException(nameof(restore)); }
        public JsonSaveParticipant(string key, SaveScope scope, int version, Func<T> capture,
            Func<T, SaveValidation> validate, Func<T, CancellationToken, Task> restore, int restoreOrder = 0)
        {
            SaveNames.CheckKey(key); SaveNames.CheckScope(scope);
            if (version < 1) throw new ArgumentOutOfRangeException(nameof(version));
            Key = key; Scope = scope; Version = version; RestoreOrder = restoreOrder;
            this.capture = capture ?? throw new ArgumentNullException(nameof(capture));
            this.validate = validate ?? throw new ArgumentNullException(nameof(validate));
            this.restore = restore ?? throw new ArgumentNullException(nameof(restore));
        }
        public string Capture()
        {
            var value = capture();
            if (ReferenceEquals(value, null))
                throw new InvalidOperationException("Participant '" + Key + "' returned a missing snapshot.");
            var payload = JsonUtility.ToJson(value);
            RequireJsonObjectPayload(payload);
            return payload;
        }
        public SaveValidation Validate(string payload) => validate(Parse(payload));
        public Task RestoreAsync(string payload, CancellationToken cancellation) => restore(Parse(payload), cancellation);
        private static T Parse(string payload)
        {
            RequireJsonObjectPayload(payload);
            var value = JsonUtility.FromJson<T>(payload);
            if (ReferenceEquals(value, null)) throw new ArgumentException("Missing snapshot payload.");
            return value;
        }
        private static void RequireJsonObjectPayload(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload) || !payload.TrimStart().StartsWith("{", StringComparison.Ordinal))
                throw new ArgumentException("Expected a JSON object payload.");
        }
    }
}
