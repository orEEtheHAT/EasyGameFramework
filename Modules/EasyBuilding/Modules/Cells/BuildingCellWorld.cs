using System;
using System.Collections.Generic;
using UnityEngine;

namespace EasyBuilding.Cells
{
    [Serializable] public struct BuildingCellEdit
    {
        public Vector3Int Position;
        public int Value;
        public BuildingCellEdit(Vector3Int position, int value) { Position = position; Value = value; }
    }
    [Serializable] public sealed class BuildingCellState
    {
        public int Version = 1;
        public string[] Palette;
        public List<BuildingCellChunkState> Chunks = new List<BuildingCellChunkState>();
    }
    [Serializable] public sealed class BuildingCellChunkState { public Vector3Int Position; public string Data; }
    public interface IBuildingCellRule { bool Allows(BuildingCellWorld world, in BuildingCellEdit edit, IBuildingActor actor, out string reason); }
    public sealed class BuildingCellStroke
    {
        internal BuildingCellWorld Owner;
        internal BuildingCellEdit[] Before, After;
        internal BuildingCost[] Paid;
        internal long Revision;
        internal bool Undone;
        public int Count => After.Length;
    }
    /// <summary>Sparse 16³ chunks of compact cell values, shared by voxel and Tilemap renderers. All mutation runs on the main thread.</summary>
    [DisallowMultipleComponent, AddComponentMenu("EasyGameFramework/Building/Cells/Cell World")]
    public sealed class BuildingCellWorld : MonoBehaviour
    {
        public const int ChunkSize = 16, ChunkVolume = 4096;
        public BuildingCellPalette Palette;
        [Min(1)] public int MaximumStrokeCells = 4096;
        [Min(1)] public int MaximumSaveChunks = 4096;
        [Tooltip("Optional component implementing IBuildingEconomy.")] public MonoBehaviour EconomyProvider;
        public IBuildingEconomy Economy { get; set; }
        public IBuildingCellRule Rule { get; set; }
        public long Revision { get; private set; }
        public bool IsReadOnly;
        private sealed class Chunk { internal ushort[] Values = new ushort[ChunkVolume]; internal int Count; }
        private Dictionary<Vector3Int, Chunk> chunks = new Dictionary<Vector3Int, Chunk>();
        private bool busy;
        public int ChunkCount => chunks.Count;
        public event Action<IReadOnlyList<BuildingCellEdit>> Changed;
        public event Action Restored;
        public static Vector3Int ChunkOf(Vector3Int p) => new Vector3Int(p.x >> 4, p.y >> 4, p.z >> 4);
        private static int Index(Vector3Int p) => (p.x & 15) | ((p.y & 15) << 4) | ((p.z & 15) << 8);
        public static Vector3Int PositionInChunk(Vector3Int chunk, int index) => chunk * ChunkSize + new Vector3Int(index & 15, (index >> 4) & 15, index >> 8);
        public int Get(Vector3Int position) => chunks.TryGetValue(ChunkOf(position), out var chunk) ? chunk.Values[Index(position)] : 0;
        /// <summary>Copies one compact chunk into a reusable buffer. Missing chunks produce zeroes; no allocation after the caller creates the buffer.</summary>
        public bool CopyChunk(Vector3Int position, ushort[] output)
        {
            if (output == null || output.Length != ChunkVolume) throw new ArgumentException("Use a buffer of exactly ChunkVolume values.", nameof(output));
            if (chunks.TryGetValue(position, out var chunk)) { Array.Copy(chunk.Values, output, ChunkVolume); return true; }
            Array.Clear(output, 0, ChunkVolume); return false;
        }
        public void GetChunks(List<Vector3Int> output) { output.Clear(); output.AddRange(chunks.Keys); }
        public void GetCells(List<BuildingCellEdit> output)
        {
            output.Clear(); foreach (var pair in chunks) for (int i = 0; i < ChunkVolume; i++) if (pair.Value.Values[i] != 0)
                output.Add(new BuildingCellEdit(PositionInChunk(pair.Key, i), pair.Value.Values[i]));
        }
        public bool TryApply(IReadOnlyList<BuildingCellEdit> edits, out BuildingCellStroke stroke, out string reason, IBuildingActor actor = null)
            => Apply(edits, null, out stroke, out reason, actor);
        private bool Apply(IReadOnlyList<BuildingCellEdit> edits, BuildingCost[] credits, out BuildingCellStroke stroke, out string reason, IBuildingActor actor)
        {
            stroke = null; reason = null;
            if (busy || IsReadOnly || Palette == null || edits == null || edits.Count > MaximumStrokeCells || edits.Count == 0)
            { reason = "Use a writable idle Cell World, a palette and a nonempty stroke within Maximum Stroke Cells."; return false; }
            if ((reason = Palette.ConfigurationError) != null) return false;
            busy = true; IBuildingCostReservation reservation = null;
            try
            {
                var before = new List<BuildingCellEdit>(); var after = new List<BuildingCellEdit>();
                var positions = new HashSet<Vector3Int>(); var costs = new Dictionary<string, long>(StringComparer.Ordinal);
                foreach (var edit in edits)
                {
                    if (!positions.Add(edit.Position) || edit.Value < 0 || edit.Value > Palette.Types.Length) throw new InvalidOperationException("Cell positions must be distinct and values must exist in the palette.");
                    if (Rule != null && !Rule.Allows(this, edit, actor, out reason)) return false;
                    int previous = Get(edit.Position); if (previous == edit.Value) continue;
                    before.Add(new BuildingCellEdit(edit.Position, previous)); after.Add(edit);
                    if (credits == null && edit.Value != 0 && Palette.Types[edit.Value - 1].Costs != null)
                        foreach (var cost in Palette.Types[edit.Value - 1].Costs)
                        { costs.TryGetValue(cost.ResourceId, out long sum); costs[cost.ResourceId] = checked(sum + cost.Amount); }
                }
                var charges = new List<BuildingCost>(); foreach (var pair in costs) if (pair.Value > 0) charges.Add(new BuildingCost(pair.Key, pair.Value));
                var bill = new BuildingBill(charges, credits, actor);
                var economy = Economy ?? EconomyProvider as IBuildingEconomy;
                if (!bill.IsEmpty && (economy == null || !economy.TryReserve(bill, out reservation, out reason) || reservation == null))
                { reason = reason ?? "Assign an IBuildingEconomy provider for paid cell types."; return false; }
                // Allocate all new chunks and result arrays before payment. Empty prepared chunks are not published.
                var prepared = new Dictionary<Vector3Int, Chunk>();
                foreach (var edit in after)
                {
                    var key = ChunkOf(edit.Position);
                    if (edit.Value != 0 && !chunks.ContainsKey(key) && !prepared.ContainsKey(key)) prepared.Add(key, new Chunk());
                }
                chunks.EnsureCapacity(chunks.Count + prepared.Count);
                var result = new BuildingCellStroke { Owner = this, Before = before.ToArray(), After = after.ToArray(), Paid = charges.ToArray(), Revision = Revision + 1 };
                if (Rule != null) foreach (var edit in after) if (!Rule.Allows(this, edit, actor, out reason)) return false;
                if (reservation != null && !reservation.TryCommit(out reason)) { reason = reason ?? "Cell payment was declined."; return false; }
                foreach (var pair in prepared) chunks.Add(pair.Key, pair.Value);
                foreach (var edit in after)
                {
                    var key = ChunkOf(edit.Position); if (!chunks.TryGetValue(key, out var chunk)) continue;
                    int index = Index(edit.Position); if (chunk.Values[index] != 0) chunk.Count--; if (edit.Value != 0) chunk.Count++;
                    chunk.Values[index] = (ushort)edit.Value;
                }
                foreach (var edit in after) { var key = ChunkOf(edit.Position); if (chunks.TryGetValue(key, out var chunk) && chunk.Count == 0) chunks.Remove(key); }
                Revision++; stroke = result; Publish(after); return true;
            }
            catch (Exception exception) { reason = exception.Message; return false; }
            finally
            {
                try { reservation?.Cancel(); } catch (Exception exception) { Debug.LogException(exception, this); }
                finally { busy = false; }
            }
        }
        public bool TryUndo(BuildingCellStroke stroke, out string reason, IBuildingActor actor = null)
        {
            reason = null;
            if (stroke == null || stroke.Owner != this || stroke.Undone || stroke.Revision != Revision) { reason = "Undo the latest stroke once, before another cell mutation."; return false; }
            if (!Apply(stroke.Before, stroke.Paid, out _, out reason, actor)) return false;
            stroke.Undone = true; return true;
        }
        private void Publish(IReadOnlyList<BuildingCellEdit> edits)
        {
            if (Changed != null) foreach (Action<IReadOnlyList<BuildingCellEdit>> handler in Changed.GetInvocationList()) try { handler(edits); } catch (Exception exception) { Debug.LogException(exception, this); }
        }
        public BuildingCellState Capture()
        {
            if (Palette == null || Palette.ConfigurationError != null || chunks.Count > MaximumSaveChunks) throw new InvalidOperationException("Assign a valid cell palette and keep the snapshot within Maximum Save Chunks.");
            var state = new BuildingCellState { Palette = new string[Palette.Types.Length] };
            for (int i = 0; i < state.Palette.Length; i++) state.Palette[i] = Palette.Types[i].Id;
            foreach (var pair in chunks)
            {
                var bytes = new byte[ChunkVolume * 2]; for (int i = 0; i < ChunkVolume; i++) { bytes[i * 2] = (byte)pair.Value.Values[i]; bytes[i * 2 + 1] = (byte)(pair.Value.Values[i] >> 8); }
                state.Chunks.Add(new BuildingCellChunkState { Position = pair.Key, Data = Convert.ToBase64String(bytes) });
            }
            return state;
        }
        /// <summary>Atomic replacement. Palette IDs are remapped, so reordering types does not corrupt saved blocks.</summary>
        public bool TryRestore(BuildingCellState state, out string reason)
        {
            reason = null;
            if (busy || Palette == null || state == null || state.Version != 1 || state.Palette == null || state.Palette.Length > ushort.MaxValue || state.Chunks == null || state.Chunks.Count > MaximumSaveChunks || Palette.ConfigurationError != null)
            { reason = "Use a supported cell snapshot and a valid palette while idle."; return false; }
            busy = true;
            try
            {
                var mapping = new int[state.Palette.Length + 1]; var ids = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < state.Palette.Length; i++)
                { if (!ids.Add(state.Palette[i]) || (mapping[i + 1] = Palette.Find(state.Palette[i])) == 0) throw new InvalidOperationException("A saved cell type is missing or duplicated."); }
                var staged = new Dictionary<Vector3Int, Chunk>();
                foreach (var saved in state.Chunks)
                {
                    if (saved == null || saved.Data == null || saved.Data.Length != 10924) throw new InvalidOperationException("A saved cell chunk has an invalid length.");
                    var bytes = Convert.FromBase64String(saved.Data); if (bytes.Length != ChunkVolume * 2) throw new InvalidOperationException("Invalid cell chunk bytes.");
                    var chunk = new Chunk();
                    for (int i = 0; i < ChunkVolume; i++)
                    {
                        int value = bytes[i * 2] | bytes[i * 2 + 1] << 8;
                        if (value >= mapping.Length) throw new InvalidOperationException("A chunk refers to a nonexistent palette entry.");
                        chunk.Values[i] = (ushort)mapping[value]; if (value != 0) chunk.Count++;
                    }
                    if (!staged.TryAdd(saved.Position, chunk)) throw new InvalidOperationException("Duplicate saved chunk coordinates.");
                }
                chunks = staged; Revision++;
                if (Restored != null) foreach (Action handler in Restored.GetInvocationList()) try { handler(); } catch (Exception exception) { Debug.LogException(exception, this); }
                return true;
            }
            catch (Exception exception) { reason = exception.Message; return false; }
            finally { busy = false; }
        }
    }
}
