using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace EasyInteraction.Tests
{
    public sealed class FocusOutlineMeshTests
    {
        private readonly List<GameObject> roots = new List<GameObject>();
        private readonly List<Object> assets = new List<Object>();
        private InteractionActor actor;
        private InteractionFocusHighlighter highlighter;
        private const BindingFlags fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        [SetUp] public void SetUp()
        {
            var owner = new GameObject("Outline mesh observer"); roots.Add(owner);
            owner.transform.position = new Vector3(0, 1600, 0);
            actor = owner.AddComponent<InteractionActor>(); actor.AutomaticUpdate = false; actor.View = owner.transform; owner.AddComponent<Camera>().enabled = false;
            actor.Range = 10; actor.RequireVisibility = false;
            highlighter = owner.AddComponent<InteractionFocusHighlighter>();
        }

        [TearDown] public void TearDown()
        {
            for (int i = roots.Count - 1; i >= 0; i--) if (roots[i]) Object.DestroyImmediate(roots[i]);
            roots.Clear();
            foreach (var asset in assets) if (asset) Object.DestroyImmediate(asset);
            assets.Clear();
        }

        private InteractionTarget Item(Vector3 offset, Mesh source = null)
        {
            var item = GameObject.CreatePrimitive(PrimitiveType.Cube); roots.Add(item);
            item.name = "Smooth outlined cube";
            item.transform.position = actor.View.position + offset;
            if (source) item.GetComponent<MeshFilter>().sharedMesh = source;
            return item.AddComponent<InteractionTarget>();
        }

        private void Draw(InteractionTarget outline)
        {
            Assert.IsTrue(actor.SelectTarget(outline.GetComponent<InteractionTarget>()));
            highlighter.RefreshHighlight();
            Tick(outline);
        }

        private void Tick(InteractionTarget target)
        {
            OutlineTestAccess.Tick(target, "Update")();
            OutlineTestAccess.Tick(highlighter)();
        }
        private static Mesh DrawMesh(InteractionTarget target) => OutlineTestAccess.Mesh(target);
        private Mesh Copy(Mesh source)
        {
            var copy = Object.Instantiate(source); assets.Add(copy); return copy;
        }

        private Texture2D Readback(RenderTexture target)
        {
            var image = new Texture2D(256, 256, TextureFormat.RGBA32, false); assets.Add(image);
            var previous = RenderTexture.active;
            try { RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, 256, 256), 0, 0); image.Apply(); }
            finally { RenderTexture.active = previous; }
            return image;
        }

        private static void CountPixels(Color32[] pixels, out int gold, out int cyan, out int magenta)
        {
            gold = cyan = magenta = 0;
            foreach (var p in pixels)
            {
                if (p.r > 150 && p.g > 70 && p.b < 100) gold++;
                if (p.r < 90 && p.g > 150 && p.b > 150) cyan++;
                if (p.r > 150 && p.g < 90 && p.b > 150) magenta++;
            }
        }

        [Test] public void BuiltinCubeUsesOneContinuousExtrusionDirectionAtEverySharedCornerWithoutChangingSource()
        {
            var outline = Item(Vector3.forward * 3);
            var source = outline.GetComponent<MeshFilter>().sharedMesh;
            Assert.IsTrue(source.isReadable, "Stock Cube must exercise the readable hard-normal path.");
            var originalVertices = source.vertices;
            var originalNormals = source.normals;
            var originalTriangles = source.triangles;
            var originalUv = source.uv;
            var originalMaterial = outline.GetComponent<Renderer>().sharedMaterial;
            Draw(outline);
            var mesh = DrawMesh(outline);
            Assert.IsNotNull(mesh); Assert.AreNotSame(source, mesh);
            Assert.AreEqual(1, highlighter.SmoothedRendererCount);
            Assert.AreEqual(0, highlighter.FallbackRendererCount);
            var vertices = mesh.vertices; var normals = mesh.normals;
            int weldedPairs = 0, hardNormalPairs = 0;
            for (int i = 0; i < vertices.Length; i++)
            {
                Assert.Less(Vector3.Distance(normals[i], vertices[i].normalized), .0001f, "Cube extrusion must point out along its shared corner diagonal.");
                for (int j = i + 1; j < vertices.Length; j++)
                    if (vertices[i] == vertices[j])
                    {
                        weldedPairs++;
                        if (Vector3.Dot(originalNormals[i], originalNormals[j]) < .9f) hardNormalPairs++;
                        Assert.Less(Vector3.Distance(normals[i], normals[j]), .0001f, "A seam must extrude to the same world point on both faces.");
                    }
            }
            Assert.Greater(weldedPairs, 0); Assert.Greater(hardNormalPairs, 0, "Positive control: stock normals must actually contain hard seams.");
            CollectionAssert.AreEqual(originalVertices, source.vertices);
            CollectionAssert.AreEqual(originalNormals, source.normals);
            CollectionAssert.AreEqual(originalTriangles, source.triangles);
            CollectionAssert.AreEqual(originalUv, source.uv);
            CollectionAssert.AreEqual(originalVertices, mesh.vertices);
            CollectionAssert.AreEqual(originalTriangles, mesh.triangles);
            Assert.AreSame(originalMaterial, outline.GetComponent<Renderer>().sharedMaterial);
        }

        [UnityTest] public IEnumerator SharedSourceReusesOnePrivateMeshThroughRefreshAndDestroysItAfterLastConsumer()
        {
            var first = Item(new Vector3(-1, 0, 3));
            var source = Copy(first.GetComponent<MeshFilter>().sharedMesh);
            first.GetComponent<MeshFilter>().sharedMesh = source;
            var second = Item(new Vector3(1, 0, 3), source);
            Draw(first); var mesh = DrawMesh(first);
            Draw(second); Assert.AreSame(mesh, DrawMesh(second));
            first.RefreshOutlineRenderers();
            Assert.AreSame(mesh, DrawMesh(first), "Refreshing the same parts must retain the warm shared mesh.");
            first.enabled = false;
            Assert.IsTrue(mesh, "Another enabled consumer still owns the cached mesh.");
            Draw(second); Assert.AreSame(mesh, DrawMesh(second));
            second.enabled = false;
            yield return null;
            Assert.IsFalse(mesh, "Last consumer release must destroy only the owned outline copy.");
            Assert.IsTrue(source);
        }

        [UnityTest] public IEnumerator SourceReplacementAndRemovalReleaseOldMeshEvenAfterFocusIsLost()
        {
            var outline = Item(Vector3.forward * 3);
            var filter = outline.GetComponent<MeshFilter>();
            filter.sharedMesh = Copy(filter.sharedMesh);
            Draw(outline); var oldCopy = DrawMesh(outline);
            var replacement = Copy(filter.sharedMesh);
            filter.sharedMesh = replacement;
            actor.SelectTarget(null); highlighter.RefreshHighlight(); Tick(outline);
            yield return null;
            Assert.IsFalse(oldCopy);
            Draw(outline); var newCopy = DrawMesh(outline);
            Assert.AreNotSame(replacement, newCopy); Assert.IsTrue(newCopy);
            filter.sharedMesh = null; Tick(outline);
            yield return null;
            Assert.IsFalse(newCopy);
            Assert.IsTrue(replacement);
            Assert.AreEqual(0, highlighter.RenderedPartCount);
        }

        [UnityTest] public IEnumerator NonReadableMeshFallsBackWithoutCpuReadCloneOrSourceDestruction()
        {
            var outline = Item(Vector3.forward * 3);
            var filter = outline.GetComponent<MeshFilter>();
            var source = Copy(filter.sharedMesh); source.UploadMeshData(true);
            Assert.IsFalse(source.isReadable);
            filter.sharedMesh = source; outline.RefreshOutlineRenderers(); Draw(outline);
            Assert.AreEqual(1, highlighter.FallbackRendererCount);
            Assert.AreEqual(0, highlighter.SmoothedRendererCount);
            Assert.AreSame(source, DrawMesh(outline));
            Assert.Greater(highlighter.RenderedPartCount, 0);
            outline.enabled = false; yield return null;
            Assert.IsTrue(source, "Raw fallback source is owned by the application, never the outline.");
        }

        [UnityTest] public IEnumerator UrpStockCubeRendersAConnectedOutlineWithRealSourcePixels()
        {
            var outline = Item(Vector3.forward * 3);
            outline.gameObject.layer = 31;
            outline.transform.rotation = Quaternion.Euler(15, 35, 10);
            highlighter.OutlineColor = Color.cyan; highlighter.Width = .065f;
            var sourceShader = Shader.Find("Universal Render Pipeline/Unlit");
            Assert.IsNotNull(sourceShader);
            var sourceMaterial = new Material(sourceShader); assets.Add(sourceMaterial);
            sourceMaterial.SetColor("_BaseColor", new Color(1, .55f, .05f, 1));
            outline.GetComponent<Renderer>().sharedMaterial = sourceMaterial;
            var cameraObject = new GameObject("Outline regression camera"); roots.Add(cameraObject);
            cameraObject.transform.position = actor.View.position;
            var camera = cameraObject.AddComponent<Camera>();
            highlighter.CameraOverride = camera; camera.enabled = false; camera.orthographic = true; camera.orthographicSize = 1.2f;
            camera.nearClipPlane = .1f; camera.farClipPlane = 10;
            camera.cullingMask = 1 << 31; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            var rt = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32); assets.Add(rt); rt.Create();
            var otherCameraObject = new GameObject("Other observer camera"); roots.Add(otherCameraObject);
            otherCameraObject.transform.position = camera.transform.position;
            var otherCamera = otherCameraObject.AddComponent<Camera>();
            otherCamera.enabled = false; otherCamera.orthographic = true; otherCamera.orthographicSize = 1.2f;
            otherCamera.nearClipPlane = .1f; otherCamera.farClipPlane = 10;
            otherCamera.cullingMask = 1 << 31; otherCamera.clearFlags = CameraClearFlags.SolidColor; otherCamera.backgroundColor = Color.black;
            var otherActor = otherCameraObject.AddComponent<InteractionActor>(); otherActor.View = otherCamera.transform;
            otherActor.AutomaticUpdate = false; otherActor.Range = 10; otherActor.RequireVisibility = false;
            var otherObserver = otherCameraObject.AddComponent<InteractionFocusHighlighter>();
            otherObserver.OutlineColor = Color.magenta; otherObserver.Width = .065f;
            var otherRt = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32); assets.Add(otherRt); otherRt.Create();
            yield return null; // Ensure the actual URP is initialized, without changing pipeline settings.
            Draw(outline);
            var request = new RenderPipeline.StandardRequest { destination = rt };
            Assert.IsTrue(RenderPipeline.SupportsRenderRequest(camera, request));
            RenderPipeline.SubmitRenderRequest(camera, request);
            var image = Readback(rt);
            var pixels = image.GetPixels32();
            var mask = new bool[pixels.Length]; int cyan = 0, gold = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                var p = pixels[i];
                if (p.r < 90 && p.g > 150 && p.b > 150) { mask[i] = true; cyan++; }
                if (p.r > 150 && p.g > 70 && p.b < 100) gold++;
            }
            Assert.Greater(gold, 500, "Positive GPU/readback control: ordinary source must actually render, not an all-zero image.");
            Assert.Greater(cyan, 100, "Production DrawMesh outline must produce visible pixels.");
            RenderPipeline.SubmitRenderRequest(otherCamera, new RenderPipeline.StandardRequest { destination = otherRt });
            CountPixels(Readback(otherRt).GetPixels32(), out int otherGold, out int otherCyan, out int otherMagenta);
            Assert.Greater(otherGold, 500, "The other camera must actually render the same source mesh.");
            Assert.AreEqual(0, otherCyan, "An unfocused camera must not receive another observer's cyan outline.");
            Assert.AreEqual(0, otherMagenta);
            Assert.IsTrue(otherActor.SelectTarget(outline)); otherObserver.RefreshHighlight();
            Tick(outline); OutlineTestAccess.Tick(otherObserver)();
            RenderPipeline.SubmitRenderRequest(otherCamera, new RenderPipeline.StandardRequest { destination = otherRt });
            CountPixels(Readback(otherRt).GetPixels32(), out otherGold, out otherCyan, out otherMagenta);
            Assert.Greater(otherGold, 500); Assert.Greater(otherMagenta, 100); Assert.AreEqual(0, otherCyan);
            RenderPipeline.SubmitRenderRequest(camera, request);
            CountPixels(Readback(rt).GetPixels32(), out int firstGold, out int firstCyan, out int firstMagenta);
            Assert.Greater(firstGold, 500); Assert.Greater(firstCyan, 100); Assert.AreEqual(0, firstMagenta, "Observer colour must remain local to its camera.");
            int components = 0;
            var queue = new Queue<int>();
            for (int i = 0; i < mask.Length; i++)
            {
                if (!mask[i]) continue;
                int area = 0; queue.Enqueue(i); mask[i] = false;
                while (queue.Count > 0)
                {
                    int index = queue.Dequeue(); area++;
                    int x = index % 256, y = index / 256;
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || nx >= 256 || ny < 0 || ny >= 256) continue;
                        int next = ny * 256 + nx;
                        if (mask[next]) { mask[next] = false; queue.Enqueue(next); }
                    }
                }
                if (area >= 5) components++;
            }
#if UNITY_EDITOR
            var folder = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../Audits/EasyInteraction/OutlineSmooth-20261116"));
            System.IO.Directory.CreateDirectory(folder);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder, "cube-smooth.png"), image.EncodeToPNG());
#endif
            Assert.AreEqual(1, components, "A hard-normal stock cube must have one connected outline, without detached face plates.");
        }
    }
}
