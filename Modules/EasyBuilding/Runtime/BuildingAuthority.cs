using System;
using System.Collections.Generic;
using UnityEngine;

namespace EasyBuilding
{
    public enum BuildingCommandKind { Edit, Connect, Disconnect, Deliver, Work, Pause, CancelConstruction }
    public enum BuildingCommandEditKind { Place, Move, Upgrade, Remove, Demolish }
    [Serializable]
    public sealed class BuildingCommandEdit
    {
        public BuildingCommandEditKind Kind;
        public string DefinitionId, InstanceId, SpaceId, SocketOwnerId, SocketId, SourceSocketId;
        public long ExpectedVersion = -1;
        public Vector3 Position;
        public Quaternion Rotation = Quaternion.identity;
        public Vector3 Scale = Vector3.one;
        public Vector3 SurfaceNormal = Vector3.up;
        public bool HasSurfaceNormal;
    }
    [Serializable]
    public sealed class BuildingCommand
    {
        public long Sequence;
        public long ExpectedRevision = -1;
        public BuildingCommandKind Kind;
        public BuildingCommandEdit[] Edits = Array.Empty<BuildingCommandEdit>();
        public string InstanceId, OtherInstanceId, SocketId, OtherSocketId, ConnectionId;
        public long ExpectedVersion = -1, OtherExpectedVersion = -1;
        public BuildingCost[] Materials = Array.Empty<BuildingCost>();
        public float Work;
        public bool Paused;
    }
    [Serializable]
    public sealed class BuildingCommandResult
    {
        public long Sequence, Revision;
        public bool Success;
        public PlacementFailure Failure;
        public string Reason;
        public string[] BuildingIds = Array.Empty<string>();
        internal BuildingCommandResult Copy() => new BuildingCommandResult { Sequence = Sequence, Revision = Revision, Success = Success, Failure = Failure,
            Reason = Reason, BuildingIds = (string[])BuildingIds.Clone() };
    }
    [Serializable]
    public sealed class BuildingAuthorityState
    {
        public List<BuildingSessionWatermark> Sessions = new List<BuildingSessionWatermark>();
    }
    [Serializable]
    public sealed class BuildingSessionWatermark
    {
        public string SessionId, ActorId;
        public long HighestSequence;
    }
    public sealed class BuildingAuthoritySession
    {
        internal BuildingAuthority Owner;
        internal readonly Dictionary<long, CachedCommand> Cached = new Dictionary<long, CachedCommand>();
        internal readonly Queue<long> Order = new Queue<long>();
        internal bool Executing, Closed;
        internal long Highest;
        public string Id { get; internal set; }
        public IBuildingActor Actor { get; internal set; }
        internal string AuthenticatedActorId;
        public long HighestSequence => Highest;
        internal sealed class CachedCommand { internal string Fingerprint; internal BuildingCommandResult Result; }
    }
    /// <summary>Transport-independent authority. The server supplies authenticated sessions and actors; commands cannot supply identity or prices.</summary>
    public sealed class BuildingAuthority : IDisposable
    {
        private readonly BuildingSystem world;
        private readonly BuildingRegistry catalog;
        private readonly Dictionary<string, BuildingAuthoritySession> sessions = new Dictionary<string, BuildingAuthoritySession>(StringComparer.Ordinal);
        private readonly Dictionary<string, BuildingSessionWatermark> retired = new Dictionary<string, BuildingSessionWatermark>(StringComparer.Ordinal);
        private readonly Queue<BuildingChangeSet> journal = new Queue<BuildingChangeSet>();
        private readonly Dictionary<string, ActorBudget> budgets = new Dictionary<string, ActorBudget>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> activeActors = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly ReachRule reachRule;
        private sealed class ActorBudget { internal double LastTime; internal float Requests, Work; }
        private int executing;
        private bool disposed;
        public int MaximumEdits = 64;
        public int MaximumCachedResponses = 128;
        public int MaximumJournalEntries = 256;
        public int MaximumSessions = 1024;
        public int MaximumCommandCharacters = 65536;
        public float MaximumDistance = 20;
        public float MinimumScale = 1, MaximumScale = 1;
        public float RequestsPerSecond = 10, RequestBurst = 16;
        public float WorkPerSecond = 2, WorkBurst = 2;
        public Func<double> Clock = () => Time.realtimeSinceStartupAsDouble;
        public long Revision => world.Revision;
        public BuildingAuthority(BuildingSystem world, BuildingRegistry catalog, BuildingAuthorityState replayState = null)
        {
            this.world = world != null ? world : throw new ArgumentNullException(nameof(world)); this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            if (world.IsReadOnly) throw new ArgumentException("An authority requires a writable world.");
            if (replayState?.Sessions != null) foreach (var saved in replayState.Sessions)
            {
                if (saved == null || string.IsNullOrWhiteSpace(saved.SessionId) || string.IsNullOrWhiteSpace(saved.ActorId) || saved.HighestSequence < 0 ||
                    !retired.TryAdd(saved.SessionId, new BuildingSessionWatermark { SessionId = saved.SessionId, ActorId = saved.ActorId, HighestSequence = saved.HighestSequence }))
                    throw new ArgumentException("Invalid or duplicated authority session watermark.");
            }
            reachRule = new ReachRule(this); world.AddRule(reachRule);
            world.Changed += Changed; world.Restored += Restored;
        }
        public BuildingAuthoritySession OpenSession(string persistentSessionId, IBuildingActor authenticatedActor)
        {
            Alive();
            ValidateSettings();
            if (string.IsNullOrWhiteSpace(persistentSessionId) || persistentSessionId.Length > 128 || authenticatedActor == null || string.IsNullOrWhiteSpace(authenticatedActor.Id) || sessions.ContainsKey(persistentSessionId))
                throw new ArgumentException("Supply a distinct server-authenticated session ID and actor.");
            retired.TryGetValue(persistentSessionId, out var previous);
            if (previous != null && previous.ActorId != authenticatedActor.Id) throw new InvalidOperationException("This session belongs to another authenticated actor.");
            if (previous == null && sessions.Count + retired.Count >= MaximumSessions) throw new InvalidOperationException("Authority session capacity reached. Persist replay state and start a new explicit server epoch.");
            double now = Clock(); if (double.IsNaN(now) || double.IsInfinity(now)) throw new InvalidOperationException("The authority clock must be finite.");
            if (!budgets.ContainsKey(authenticatedActor.Id)) budgets.Add(authenticatedActor.Id, new ActorBudget { LastTime = now, Requests = RequestBurst, Work = WorkBurst });
            var session = new BuildingAuthoritySession { Owner = this, Id = persistentSessionId, Actor = authenticatedActor, Highest = previous?.HighestSequence ?? 0,
                AuthenticatedActorId = authenticatedActor.Id };
            retired.Remove(persistentSessionId); sessions.Add(persistentSessionId, session); return session;
        }
        public void CloseSession(BuildingAuthoritySession session)
        {
            if (session == null || session.Owner != this || session.Closed) return;
            if (session.Executing) throw new InvalidOperationException("Close the session after its current request finishes.");
            session.Closed = true; sessions.Remove(session.Id);
            retired[session.Id] = new BuildingSessionWatermark { SessionId = session.Id, ActorId = session.AuthenticatedActorId, HighestSequence = session.Highest };
            session.Cached.Clear(); session.Order.Clear();
        }
        public BuildingCommandResult Execute(BuildingAuthoritySession session, BuildingCommand command)
        {
            if (disposed || session == null || session.Owner != this || session.Closed || command == null)
                return Failure(command?.Sequence ?? 0, PlacementFailure.PermissionDenied, "Use an active authenticated authority session.");
            if (session.Actor.Id != session.AuthenticatedActorId) return Failure(command.Sequence, PlacementFailure.PermissionDenied, "Authenticated actor identity changed. Open a new server session.");
            if ((command.Edits?.Length ?? 0) > MaximumEdits || (command.Materials?.Length ?? 0) > MaximumEdits)
                return Failure(command.Sequence, PlacementFailure.LimitExceeded, "The request exceeds the configured row budget.");
            if (session.Executing) return Failure(command.Sequence, PlacementFailure.Busy, "A request from this session is already running.");
            string fingerprint;
            try { ValidateSettings(); fingerprint = JsonUtility.ToJson(command); }
            catch (Exception exception) { return Failure(command.Sequence, PlacementFailure.InvalidState, exception.Message); }
            if (command.Sequence <= 0 || fingerprint.Length > MaximumCommandCharacters)
                return Failure(command.Sequence, PlacementFailure.InvalidState, "Invalid request sequence or command size.");
            if (session.Cached.TryGetValue(command.Sequence, out var cached))
                return cached.Fingerprint == fingerprint ? cached.Result.Copy() : Failure(command.Sequence, PlacementFailure.StaleRequest, "A sequence number cannot be reused with a different command.");
            if (command.Sequence <= session.Highest) return Failure(command.Sequence, PlacementFailure.StaleRequest, "This sequence was already processed or retired. Refresh state and use a new sequence.");
            session.Highest = command.Sequence; session.Executing = true; executing++;
            activeActors.TryGetValue(session.AuthenticatedActorId, out int activeCount); activeActors[session.AuthenticatedActorId] = activeCount + 1;
            BuildingCommandResult response;
            try
            {
                double now = Clock();
                var budget = budgets[session.AuthenticatedActorId];
                if (double.IsNaN(now) || double.IsInfinity(now) || now < budget.LastTime) throw new InvalidOperationException("The authority clock must be finite and monotonic.");
                double elapsed = now - budget.LastTime; budget.LastTime = now;
                budget.Requests = Mathf.Min(RequestBurst, budget.Requests + (float)(elapsed * RequestsPerSecond));
                budget.Work = Mathf.Min(WorkBurst, budget.Work + (float)(elapsed * WorkPerSecond));
                if (budget.Requests < 1) response = Failure(command.Sequence, PlacementFailure.LimitExceeded, "Request rate exceeded. Wait before sending a new command.");
                else
                {
                    budget.Requests -= 1;
                    response = command.ExpectedRevision >= 0 && command.ExpectedRevision != world.Revision
                        ? Failure(command.Sequence, PlacementFailure.StaleRequest, "World revision changed. Refresh the preview.")
                        : ExecuteCore(session, command);
                }
            }
            catch (AuthorityFailure exception) { response = Failure(command.Sequence, exception.Code, exception.Message); }
            catch (Exception exception) { response = Failure(command.Sequence, PlacementFailure.InvalidState, exception.Message); }
            finally
            {
                executing--; session.Executing = false;
                if (activeActors[session.AuthenticatedActorId] == 1) activeActors.Remove(session.AuthenticatedActorId); else activeActors[session.AuthenticatedActorId]--;
            }
            session.Cached.Add(command.Sequence, new BuildingAuthoritySession.CachedCommand { Fingerprint = fingerprint, Result = response.Copy() }); session.Order.Enqueue(command.Sequence);
            while (session.Order.Count > Math.Max(1, MaximumCachedResponses)) session.Cached.Remove(session.Order.Dequeue());
            return response;
        }
        private BuildingCommandResult ExecuteCore(BuildingAuthoritySession session, BuildingCommand command)
        {
            var actor = session.Actor;
            if (!BuildingMath.Finite(actor.Position)) return Failure(command.Sequence, PlacementFailure.PermissionDenied, "The server actor position is invalid.");
            if (command.Kind == BuildingCommandKind.Edit)
            {
                if (command.Edits == null || command.Edits.Length == 0 || command.Edits.Length > MaximumEdits) return Failure(command.Sequence, PlacementFailure.LimitExceeded, "Invalid edit count.");
                var edits = new BuildingEdit[command.Edits.Length];
                for (int i = 0; i < edits.Length; i++)
                {
                    var edit = command.Edits[i]; if (edit == null) throw new ArgumentException("An edit is missing.");
                    PlacedBuilding existing = null;
                    if (edit.Kind != BuildingCommandEditKind.Place) existing = Existing(edit.InstanceId, edit.ExpectedVersion, actor);
                    BuildingDefinition definition = existing != null ? existing.Definition : null;
                    if (edit.Kind == BuildingCommandEditKind.Place || edit.Kind == BuildingCommandEditKind.Upgrade)
                        if (!catalog.TryGet(edit.DefinitionId, out definition)) throw new InvalidOperationException("The requested definition is not in the server catalog.");
                    switch (edit.Kind)
                    {
                        case BuildingCommandEditKind.Place: edits[i] = BuildingEdit.Place(definition, Request(edit, actor)); break;
                        case BuildingCommandEditKind.Move: edits[i] = BuildingEdit.Move(existing, Request(edit, actor)); break;
                        case BuildingCommandEditKind.Upgrade: edits[i] = BuildingEdit.Upgrade(existing, definition); break;
                        case BuildingCommandEditKind.Remove: edits[i] = BuildingEdit.Remove(existing); break;
                        case BuildingCommandEditKind.Demolish: edits[i] = BuildingEdit.Demolish(existing); break;
                        default: throw new InvalidOperationException("Unsupported edit kind.");
                    }
                }
                bool success = world.TryExecute(edits, out var operation, actor);
                var result = From(command.Sequence, success, operation.Validation);
                if (success) { result.BuildingIds = new string[operation.Buildings.Count]; for (int i = 0; i < result.BuildingIds.Length; i++) result.BuildingIds[i] = operation.Buildings[i].InstanceId; }
                return result;
            }
            var building = Existing(command.InstanceId, command.ExpectedVersion, actor);
            bool applied; PlacementResult validation;
            switch (command.Kind)
            {
                case BuildingCommandKind.Deliver:
                    if (command.Materials == null || command.Materials.Length > MaximumEdits) throw new ArgumentException("Delivery exceeds the resource row budget.");
                    applied = world.TryDeliver(building, command.Materials, out validation, actor); break;
                case BuildingCommandKind.Work:
                    var workBudget = budgets[session.AuthenticatedActorId];
                    if (!BuildingMath.Finite(command.Work) || command.Work <= 0 || command.Work > workBudget.Work)
                        return Failure(command.Sequence, PlacementFailure.LimitExceeded, "Work exceeds the server's available work budget.");
                    workBudget.Work -= command.Work;
                    applied = world.TryWork(building, command.Work, out _, out validation, actor);
                    if (!applied) workBudget.Work = Mathf.Min(WorkBurst, workBudget.Work + command.Work); break;
                case BuildingCommandKind.Pause: applied = world.TryPauseConstruction(building, command.Paused, out validation, actor); break;
                case BuildingCommandKind.CancelConstruction: applied = world.TryCancelConstruction(building, out validation, actor); break;
                case BuildingCommandKind.Connect:
                {
                    PlacedBuilding target = string.IsNullOrEmpty(command.OtherInstanceId) ? null : Existing(command.OtherInstanceId, command.OtherExpectedVersion, actor);
                    var sourceSocket = BuildingSystem.GetSocket(building, command.SocketId);
                    var targetSocket = target != null ? BuildingSystem.GetSocket(target, command.OtherSocketId) : world.FindSavedSocket("", command.OtherSocketId);
                    if (targetSocket != null) Reach(targetSocket.transform.position, actor);
                    applied = world.TryConnect(sourceSocket, targetSocket, out var reason, actor);
                    validation = applied ? new PlacementResult(PlacementFailure.None, "Connected", default) : PlacementResult.Fail(PlacementFailure.InvalidConnection, reason); break;
                }
                case BuildingCommandKind.Disconnect:
                {
                    bool ownsLink = false; foreach (var link in building.Connections) if (link.Id == command.ConnectionId) { ownsLink = true; break; }
                    if (!ownsLink) throw new InvalidOperationException("The requested connection does not belong to this building.");
                    applied = world.TryDisconnect(command.ConnectionId, out var reason, actor);
                    validation = applied ? new PlacementResult(PlacementFailure.None, "Disconnected", default) : PlacementResult.Fail(PlacementFailure.InvalidConnection, reason); break;
                }
                default: return Failure(command.Sequence, PlacementFailure.UnsupportedOperation, "Unsupported authority command.");
            }
            return From(command.Sequence, applied, validation);
        }
        private PlacedBuilding Existing(string id, long version, IBuildingActor actor)
        {
            if (!world.TryGet(id, out var building)) throw new AuthorityFailure(PlacementFailure.NotFound, "Building no longer exists.");
            if (version < 0 || building.Version != version) throw new AuthorityFailure(PlacementFailure.StaleRequest, "Building revision changed. Refresh the target before retrying.");
            Reach(building.transform.position, actor); return building;
        }
        private PlacementRequest Request(BuildingCommandEdit edit, IBuildingActor actor)
        {
            if (!BuildingMath.Finite(edit.Scale) || edit.Scale.x < MinimumScale || edit.Scale.y < MinimumScale || edit.Scale.z < MinimumScale ||
                edit.Scale.x > MaximumScale || edit.Scale.y > MaximumScale || edit.Scale.z > MaximumScale) throw new InvalidOperationException("Requested scale is outside server limits.");
            var space = world.ResolveSavedSpace(edit.SpaceId);
            var socket = string.IsNullOrEmpty(edit.SocketId) ? null : world.FindSavedSocket(edit.SocketOwnerId, edit.SocketId);
            if (!string.IsNullOrEmpty(edit.SocketId) && socket == null) throw new InvalidOperationException("The target socket is missing.");
            Reach(socket != null ? socket.transform.position : edit.Position, actor);
            return new PlacementRequest(edit.Position, edit.Rotation, socket, space, edit.Scale, edit.HasSurfaceNormal ? (Vector3?)edit.SurfaceNormal : null, edit.SourceSocketId);
        }
        private void Reach(Vector3 position, IBuildingActor actor)
        {
            if (!BuildingMath.Finite(position) || !BuildingMath.Finite(MaximumDistance) || MaximumDistance <= 0 || (position - actor.Position).sqrMagnitude > MaximumDistance * MaximumDistance)
                throw new AuthorityFailure(PlacementFailure.PermissionDenied, "The operation is outside the server's interaction range.");
        }
        private BuildingCommandResult From(long sequence, bool success, PlacementResult validation)
            => new BuildingCommandResult { Sequence = sequence, Revision = world.Revision, Success = success, Failure = validation.Failure, Reason = validation.Reason };
        private BuildingCommandResult Failure(long sequence, PlacementFailure failure, string reason)
            => new BuildingCommandResult { Sequence = sequence, Revision = world != null ? world.Revision : 0, Failure = failure, Reason = reason };
        private void Changed(BuildingChangeSet change)
        {
            journal.Enqueue(BuildingChanges.Copy(change, true));
            while (journal.Count > Math.Max(1, MaximumJournalEntries)) journal.Dequeue();
        }
        private void Restored() => journal.Clear();
        public BuildingState CaptureSnapshot() { Alive(); return world.CaptureState(true); }
        public bool TryGetChangesSince(long revision, List<BuildingChangeSet> output)
        {
            Alive(); if (output == null) throw new ArgumentNullException(nameof(output)); output.Clear();
            if (revision == world.Revision) return true;
            long next = revision;
            foreach (var change in journal)
            {
                if (change.Revision <= revision) continue;
                if (change.BeforeRevision != next) { output.Clear(); return false; }
                output.Add(BuildingChanges.Copy(change, true)); next = change.Revision;
            }
            if (next == world.Revision) return true; output.Clear(); return false;
        }
        public BuildingAuthorityState CaptureReplayState()
        {
            Alive(); if (executing != 0) throw new InvalidOperationException("Save replay state between authority requests, alongside the world snapshot.");
            var state = new BuildingAuthorityState();
            foreach (var session in sessions.Values) state.Sessions.Add(new BuildingSessionWatermark { SessionId = session.Id, ActorId = session.AuthenticatedActorId, HighestSequence = session.Highest });
            foreach (var saved in retired.Values) state.Sessions.Add(new BuildingSessionWatermark { SessionId = saved.SessionId, ActorId = saved.ActorId, HighestSequence = saved.HighestSequence });
            return state;
        }
        private void Alive() { if (disposed || world == null) throw new ObjectDisposedException(nameof(BuildingAuthority)); }
        private void ValidateSettings()
        {
            if (MaximumEdits < 1 || MaximumCachedResponses < 1 || MaximumJournalEntries < 1 || MaximumSessions < 1 || MaximumCommandCharacters < 1 || Clock == null ||
                !BuildingMath.Finite(MaximumDistance) || MaximumDistance <= 0 || !BuildingMath.Finite(MinimumScale) || !BuildingMath.Finite(MaximumScale) || MinimumScale <= 0 || MaximumScale < MinimumScale ||
                !BuildingMath.Finite(RequestsPerSecond) || RequestsPerSecond <= 0 || !BuildingMath.Finite(RequestBurst) || RequestBurst < 1 ||
                !BuildingMath.Finite(WorkPerSecond) || WorkPerSecond < 0 || !BuildingMath.Finite(WorkBurst) || WorkBurst < 0)
                throw new InvalidOperationException("Authority limits must be finite and positive (work budgets may be zero).");
        }
        private sealed class AuthorityFailure : Exception
        {
            internal readonly PlacementFailure Code;
            internal AuthorityFailure(PlacementFailure code, string message) : base(message) => Code = code;
        }
        private sealed class ReachRule : IBuildingRule
        {
            private readonly BuildingAuthority owner;
            internal ReachRule(BuildingAuthority owner) => this.owner = owner;
            public bool Allows(in BuildingContext context, out string reason)
            {
                reason = null;
                if (owner.disposed || context.Actor == null || !owner.activeActors.ContainsKey(context.Actor.Id)) return true;
                if ((context.Pose.position - context.Actor.Position).sqrMagnitude <= owner.MaximumDistance * owner.MaximumDistance) return true;
                reason = "The resolved placement is outside the server's interaction range."; return false;
            }
        }
        public void Dispose()
        {
            if (disposed) return; if (executing != 0) throw new InvalidOperationException("Dispose the authority after requests finish."); disposed = true;
            if (world != null) { world.Changed -= Changed; world.Restored -= Restored; if (!world.IsBusy) world.RemoveRule(reachRule); }
            foreach (var session in sessions.Values) { session.Closed = true; session.Cached.Clear(); session.Order.Clear(); }
            sessions.Clear(); retired.Clear(); journal.Clear(); budgets.Clear(); activeActors.Clear();
        }
    }
}
