using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace EasyInteraction
{
    /// <summary>Lazy target-owned static-mesh data. Viewers own style, cameras and materials.</summary>
    internal sealed class FocusOutlineGeometry : IDisposable
    {
        private readonly InteractionTarget target;
        private readonly List<Renderer> discovered = new List<Renderer>();
        private List<Part> parts = new List<Part>();
        private List<Part> rebuiltParts = new List<Part>();
        private bool disposed;
        private struct Part
        {
            public MeshRenderer Renderer;
            public MeshFilter Filter;
            public FocusOutlineMeshCache.Entry Lease;
        }

        internal FocusOutlineGeometry(InteractionTarget target)
        {
            this.target = target;
            Refresh();
        }

        internal bool IsDisposed => disposed;
        internal int RendererCount => parts.Count;
        internal int UnsupportedRendererCount { get; private set; }
        internal int SmoothedRendererCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < parts.Count; i++)
                    if (HasCurrentLease(parts[i]) && parts[i].Lease.Smoothed) count++;
                return count;
            }
        }
        internal int FallbackRendererCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < parts.Count; i++)
                    if (IsRenderable(parts[i]) && (HasCurrentLease(parts[i]) ? !parts[i].Lease.Smoothed : !parts[i].Filter.sharedMesh.isReadable)) count++;
                return count;
            }
        }

        internal void Refresh()
        {
            if (disposed || !target) return;
            rebuiltParts.Clear();
            UnsupportedRendererCount = 0;
            var renderers = target.OutlineRenderers;
            bool automatic = renderers == null || renderers.Length == 0;
            if (automatic) target.GetComponentsInChildren(true, discovered);
            int count = automatic ? discovered.Count : renderers.Length;
            for (int i = 0; i < count; i++)
            {
                var source = automatic ? discovered[i] : renderers[i];
                if (!source || (automatic && source.GetComponentInParent<InteractionTarget>() != target)) continue;
                if (!(source is MeshRenderer meshRenderer) || !source.TryGetComponent<MeshFilter>(out var filter) || !filter.sharedMesh || !filter.sharedMesh.HasVertexAttribute(VertexAttribute.Normal))
                {
                    UnsupportedRendererCount++;
                    continue;
                }
                bool duplicate = false;
                for (int j = 0; j < rebuiltParts.Count; j++)
                    if (rebuiltParts[j].Renderer == meshRenderer) { duplicate = true; break; }
                if (duplicate) continue;
                var part = new Part { Renderer = meshRenderer, Filter = filter };
                for (int j = 0; j < parts.Count; j++)
                {
                    var previous = parts[j];
                    if (previous.Renderer != meshRenderer || !HasCurrentLease(previous)) continue;
                    part.Lease = previous.Lease;
                    previous.Lease = null;
                    parts[j] = previous;
                    break;
                }
                rebuiltParts.Add(part);
            }
            discovered.Clear();
            ReleaseMeshes(parts);
            var oldParts = parts;
            parts = rebuiltParts;
            rebuiltParts = oldParts;
            rebuiltParts.Clear();
        }

        /// <summary>Release invalid/replaced sources even when no viewer focuses this target.</summary>
        internal void Maintain()
        {
            if (disposed) return;
            for (int i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                if (part.Lease == null || HasCurrentLease(part)) continue;
                FocusOutlineMeshCache.Release(part.Lease);
                part.Lease = null;
                parts[i] = part;
            }
        }

        internal bool CanRender(Camera camera)
        {
            if (disposed || !camera || !camera.gameObject.activeInHierarchy) return false;
            for (int i = 0; i < parts.Count; i++)
                if (IsRenderable(parts[i]) && (camera.cullingMask & (1 << parts[i].Renderer.gameObject.layer)) != 0) return true;
            return false;
        }

        internal int Draw(Material material, Camera camera)
        {
            if (disposed || !target || !target.isActiveAndEnabled || !target.HighlightWhenFocused || target.IsHeldForHighlight || !material || !camera || !camera.gameObject.activeInHierarchy) return 0;
            Maintain();
            int rendered = 0;
            for (int i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                if (!IsRenderable(part) || (camera.cullingMask & (1 << part.Renderer.gameObject.layer)) == 0) continue;
                if (part.Lease == null)
                {
                    part.Lease = FocusOutlineMeshCache.Acquire(part.Filter.sharedMesh);
                    parts[i] = part;
                }
                var mesh = part.Lease == null ? null : part.Lease.Mesh;
                if (!mesh) continue;
                var source = part.Renderer;
                var matrix = source.localToWorldMatrix;
                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                {
                    Graphics.DrawMesh(mesh, matrix, material, source.gameObject.layer, camera, submesh, null, ShadowCastingMode.Off, false, null, LightProbeUsage.Off, null);
                    rendered++;
                }
            }
            return rendered;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            ReleaseMeshes(parts);
            ReleaseMeshes(rebuiltParts);
            parts.Clear();
            rebuiltParts.Clear();
            discovered.Clear();
            UnsupportedRendererCount = 0;
        }

        private static bool IsRenderable(Part part) => part.Renderer && part.Renderer.enabled && !part.Renderer.forceRenderingOff &&
            part.Renderer.gameObject.activeInHierarchy && part.Renderer.shadowCastingMode != ShadowCastingMode.ShadowsOnly &&
            part.Filter && part.Filter.sharedMesh && part.Filter.sharedMesh.HasVertexAttribute(VertexAttribute.Normal);

        private static bool HasCurrentLease(Part part) => part.Lease != null && part.Lease.Mesh && part.Renderer && part.Filter &&
            part.Filter.sharedMesh && ReferenceEquals(part.Lease.Source, part.Filter.sharedMesh);

        private static void ReleaseMeshes(List<Part> values)
        {
            for (int i = 0; i < values.Count; i++)
            {
                var part = values[i];
                FocusOutlineMeshCache.Release(part.Lease);
                part.Lease = null;
                values[i] = part;
            }
        }
    }
}
