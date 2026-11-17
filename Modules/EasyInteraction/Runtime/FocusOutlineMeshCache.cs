using System.Collections.Generic;
using UnityEngine;

namespace EasyInteraction
{
    /// <summary>Private static-mesh snapshots used only for outline extrusion; source assets are never changed.</summary>
    internal static class FocusOutlineMeshCache
    {
        internal sealed class Entry
        {
            internal readonly Mesh Source;
            internal Mesh Mesh;
            internal bool Smoothed;
            internal int References;

            internal Entry(Mesh source) => Source = source;
        }

        private static readonly Dictionary<Mesh, Entry> entries = new Dictionary<Mesh, Entry>();

        internal static Entry Acquire(Mesh source)
        {
            if (!source) return null;
            if (!entries.TryGetValue(source, out var entry))
            {
                entry = new Entry(source);
                Build(entry);
                entries.Add(source, entry);
            }
            else if (!entry.Mesh)
                Build(entry);
            entry.References++;
            return entry;
        }

        internal static void Release(Entry entry)
        {
            if (entry == null || entry.References <= 0) return;
            if (--entry.References > 0) return;
            // A destroyed Unity Mesh compares equal to null. Its retained dictionary key must still be removed.
            if (entries.TryGetValue(entry.Source, out var current) && ReferenceEquals(current, entry))
                entries.Remove(entry.Source);
            if (entry.Smoothed && entry.Mesh)
            {
                if (Application.isPlaying) Object.Destroy(entry.Mesh); else Object.DestroyImmediate(entry.Mesh);
            }
            entry.Mesh = null;
        }

        private static void Build(Entry entry)
        {
            var source = entry.Source;
            entry.Mesh = source;
            entry.Smoothed = false;
            if (!source || !source.isReadable) return;
            var vertices = source.vertices;
            var normals = source.normals;
            if (vertices.Length == 0 || normals.Length != vertices.Length) return;
            // Vector3.Equals/GetHashCode use exact components. Do not weld merely nearby surfaces.
            var sums = new Dictionary<Vector3, Vector3>(vertices.Length);
            for (int i = 0; i < vertices.Length; i++)
            {
                sums.TryGetValue(vertices[i], out var sum);
                sums[vertices[i]] = sum + normals[i];
            }
            for (int i = 0; i < normals.Length; i++)
            {
                var smooth = sums[vertices[i]];
                if (smooth.sqrMagnitude < .000000000001f) smooth = normals[i];
                normals[i] = smooth.sqrMagnitude > .000000000001f ? smooth.normalized : Vector3.up;
            }
            var clone = Object.Instantiate(source);
            clone.name = "EasyInteraction Outline Mesh (" + source.name + ")";
            clone.hideFlags = HideFlags.HideAndDontSave;
            clone.normals = normals;
            entry.Mesh = clone;
            entry.Smoothed = true;
        }
    }
}
