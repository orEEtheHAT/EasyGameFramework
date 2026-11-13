using System;
using System.Collections.Generic;

namespace EasyBuilding
{
    [Serializable]
    public sealed class BuildingPaletteState
    {
        public string[] Favorites = Array.Empty<string>();
        public string[] Slots = Array.Empty<string>();
    }
    /// <summary>UI-independent catalog, favorites and hotbar. Unknown mod IDs remain in saved preferences until the content returns.</summary>
    public sealed class BuildingPalette : IDisposable
    {
        private readonly BuildingRegistry catalog;
        private BuildingSession session;
        public BuildingSession Session => session;
        private readonly HashSet<string> favorites = new HashSet<string>(StringComparer.Ordinal);
        private readonly string[] slots;
        private readonly List<BuildingDefinition> visible = new List<BuildingDefinition>();
        private readonly IReadOnlyList<BuildingDefinition> visibleView;
        private string search = "", category = "";
        private bool favoritesOnly, disposed;
        public IReadOnlyList<BuildingDefinition> Visible => visibleView;
        public int SlotCount => slots.Length;
        public event Action Changed;
        public BuildingPalette(BuildingRegistry catalog, BuildingSession session, int hotbarSlots = 10)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog)); this.session = session ?? throw new ArgumentNullException(nameof(session));
            if (hotbarSlots < 1 || hotbarSlots > 64) throw new ArgumentOutOfRangeException(nameof(hotbarSlots));
            slots = new string[hotbarSlots]; visibleView = visible.AsReadOnly(); catalog.Changed += Refresh; Refresh();
            for (int i = 0; i < Math.Min(slots.Length, visible.Count); i++) slots[i] = visible[i].Id;
        }
        public void Filter(string text = "", string category = "", bool favoritesOnly = false)
        { Alive(); search = text ?? ""; this.category = category ?? ""; this.favoritesOnly = favoritesOnly; Refresh(); }
        public bool IsFavorite(string id) => id != null && favorites.Contains(id);
        /// <summary>Rebinds presentation without owning/cancelling either session. Null temporarily disables selection.</summary>
        public void SetSession(BuildingSession value)
        {
            Alive();
            if (value != null && value.IsDisposed) throw new ArgumentException("Use a live session or null.", nameof(value));
            if (ReferenceEquals(session, value)) return;
            session = value; Publish();
        }
        public bool ToggleFavorite(string id)
        {
            Alive(); if (string.IsNullOrWhiteSpace(id)) return false;
            bool enabled = favorites.Add(id); if (!enabled) favorites.Remove(id); Refresh(); return enabled;
        }
        public bool Select(string definitionId) { Alive(); return session != null && catalog.TryGet(definitionId, out var definition) && session.Select(definition); }
        public string GetSlotId(int index) { Alive(); return slots[index]; }
        public BuildingDefinition GetSlot(int index) { Alive(); catalog.TryGet(slots[index], out var definition); return definition; }
        public bool AssignSlot(int index, string definitionId)
        {
            Alive(); if (index < 0 || index >= slots.Length || !string.IsNullOrEmpty(definitionId) && !catalog.TryGet(definitionId, out _)) return false;
            slots[index] = definitionId ?? ""; Publish(); return true;
        }
        public bool SelectSlot(int index) { Alive(); return index >= 0 && index < slots.Length && Select(slots[index]); }
        public BuildingPaletteState CaptureState()
        {
            Alive(); var ids = new string[favorites.Count]; favorites.CopyTo(ids); Array.Sort(ids, StringComparer.Ordinal);
            return new BuildingPaletteState { Favorites = ids, Slots = (string[])slots.Clone() };
        }
        public void RestoreState(BuildingPaletteState state)
        {
            Alive(); if (state == null) throw new ArgumentNullException(nameof(state)); favorites.Clear();
            if (state.Favorites != null) foreach (var id in state.Favorites) if (!string.IsNullOrWhiteSpace(id)) favorites.Add(id);
            Array.Clear(slots, 0, slots.Length);
            if (state.Slots != null) Array.Copy(state.Slots, slots, Math.Min(slots.Length, state.Slots.Length)); Refresh();
        }
        private void Refresh()
        {
            if (disposed) return; catalog.Search(search, category, visible);
            if (favoritesOnly) for (int i = visible.Count - 1; i >= 0; i--) if (!favorites.Contains(visible[i].Id)) visible.RemoveAt(i);
            Publish();
        }
        private void Publish()
        { if (Changed != null) foreach (Action handler in Changed.GetInvocationList()) try { handler(); } catch (Exception exception) { UnityEngine.Debug.LogException(exception); } }
        private void Alive() { if (disposed) throw new ObjectDisposedException(nameof(BuildingPalette)); }
        public void Dispose() { if (disposed) return; disposed = true; catalog.Changed -= Refresh; visible.Clear(); Changed = null; }
    }
}
