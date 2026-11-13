using System;
using System.Collections.Generic;
using UnityEngine;

namespace EasyBuilding
{
    /// <summary>Independent selection for one player, UI or AI. Disposing a session never destroys the world.</summary>
    public sealed class BuildingSession : IDisposable
    {
        private readonly BuildingSystem system;
        private long selectionVersion;
        private bool confirming, publishing;
        private enum NotificationKind { Selection, Confirmation, Disposal }
        private struct Notification { internal NotificationKind Kind; internal BuildingDefinition Definition; internal PlacedBuilding Building; }
        private readonly Queue<Notification> notifications = new Queue<Notification>();
        public BuildingSystem System => system;
        public IBuildingActor Actor { get; }
        public BuildingDefinition Selected { get; private set; }
        public bool IsDisposed { get; private set; }
        public bool RepeatPlacement { get; set; }
        /// <summary>Selection entered/changed/ended. Null means no selection. Notifications follow mutation order.</summary>
        public event Action<BuildingDefinition> SelectionChanged;
        public event Action<PlacedBuilding> Confirmed;
        public event Action<BuildingSession> Disposed;
        private bool ActorAvailable => !(Actor is UnityEngine.Object value) || value != null;
        internal BuildingSession(BuildingSystem system, IBuildingActor actor) { this.system = system; Actor = actor; }
        public bool Select(BuildingDefinition definition)
        {
            if (IsDisposed || system == null || system.IsBusy || !ActorAvailable) return false;
            if (Selected != definition || confirming && definition == null) { Selected = definition; selectionVersion++; Notify(new Notification { Kind = NotificationKind.Selection, Definition = definition }); }
            return definition != null;
        }
        public PlacementResult Check(PlacementRequest request) => IsDisposed || system == null || !ActorAvailable
            ? PlacementResult.Fail(PlacementFailure.Cancelled, "This building session is closed.")
            : system.Check(Selected, request, Actor);
        public bool TryConfirm(PlacementRequest request, out PlacedBuilding building, out PlacementResult result)
        {
            building = null;
            if (IsDisposed || system == null || !ActorAvailable) { result = PlacementResult.Fail(PlacementFailure.Cancelled, "This building session is closed or its actor was destroyed."); return false; }
            if (system.IsBusy || confirming) { result = PlacementResult.Fail(PlacementFailure.Busy, "A building operation is running."); return false; }
            var selected = Selected;
            if (selected == null) { result = PlacementResult.Fail(PlacementFailure.NoSelection, "Select a building first."); return false; }
            // Consume before post-commit callbacks can attempt a second confirmation.
            long version = selectionVersion; Selected = null; confirming = true;
            try
            {
                bool success = system.TryPlace(selected, request, out building, out result, Actor);
                if (!IsDisposed && selectionVersion == version && Selected == null)
                {
                    if (!success || RepeatPlacement) Selected = selected;
                    else { selectionVersion++; Notify(new Notification { Kind = NotificationKind.Selection }); }
                }
                if (success && !IsDisposed) Notify(new Notification { Kind = NotificationKind.Confirmation, Building = building });
                return success;
            }
            finally { confirming = false; }
        }
        public void Cancel()
        {
            // Cancellation only ends selection. An in-flight world transaction may finish, but must not restart repeat placement.
            if (IsDisposed || Selected == null && !confirming) return;
            Selected = null; selectionVersion++; Notify(new Notification { Kind = NotificationKind.Selection });
        }
        public void Dispose()
        {
            if (IsDisposed) return;
            bool selected = Selected != null; Selected = null; IsDisposed = true; selectionVersion++;
            if (system != null) system.ReleaseSession(this);
            if (selected) notifications.Enqueue(new Notification { Kind = NotificationKind.Selection });
            Notify(new Notification { Kind = NotificationKind.Disposal });
        }
        private void Notify(Notification notification)
        {
            notifications.Enqueue(notification); if (publishing) return;
            publishing = true;
            try
            {
                while (notifications.Count != 0)
                {
                    var next = notifications.Dequeue();
                    if (next.Kind == NotificationKind.Selection) Dispatch(SelectionChanged, next.Definition);
                    else if (next.Kind == NotificationKind.Confirmation) Dispatch(Confirmed, next.Building);
                    else { Dispatch(Disposed, this); SelectionChanged = null; Confirmed = null; Disposed = null; }
                }
            }
            finally { publishing = false; }
        }
        private static void Dispatch<T>(Action<T> handlers, T argument)
        {
            if (handlers == null) return;
            foreach (Action<T> handler in handlers.GetInvocationList()) try { handler(argument); } catch (Exception exception) { Debug.LogException(exception); }
        }
    }
}
