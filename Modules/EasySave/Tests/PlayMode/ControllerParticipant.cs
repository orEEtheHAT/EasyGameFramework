using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace EasySave.Tests
{
    public sealed class ControllerParticipant : MonoBehaviour, ISaveParticipant
    {
        public string Key => "counter";
        public SaveScope Scope => SaveScope.Progress;
        public int Version => 1;
        public int RestoreOrder => 0;
        public bool AwaitRestore, EnteredRestore;
        public int Captures, Restores;
        public string Capture() { Captures++; return "{}"; }
        public SaveValidation Validate(string payload) => payload == "{}" ? SaveValidation.Valid : SaveValidation.Invalid("Invalid test payload.");
        public async Task RestoreAsync(string payload, CancellationToken cancellation)
        {
            EnteredRestore = true;
            if (AwaitRestore) await Task.Delay(Timeout.Infinite, cancellation);
            cancellation.ThrowIfCancellationRequested(); Restores++;
        }
    }
}
