using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace EasyBuilding.AR.Tests
{
    public sealed class BuildingARTests
    {
        [UnityTest] public IEnumerator NoTrackedPlaneCannotConfirmOrLeaveStalePreview()
        {
            var root = new GameObject("AR world"); var world = root.AddComponent<BuildingSystem>(); var placer = root.AddComponent<BuildingPlacer>(); placer.ShowHelp = false;
            var adapter = root.AddComponent<BuildingARPlanePlacement>(); adapter.enabled = false; adapter.Placer = placer; adapter.enabled = true;
            var source = new GameObject("Source"); source.SetActive(false);
            var definition = ScriptableObject.CreateInstance<BuildingDefinition>(); definition.Prefab = source; definition.RequireSurface = false; placer.Session.Select(definition);
            try
            {
                yield return null; Assert.That(adapter.HasTrackedPlane, Is.False); Assert.That(adapter.TryConfirm(Vector2.zero), Is.False);
                Assert.That(adapter.Result.Failure, Is.EqualTo(PlacementFailure.MissingSurface)); Assert.That(world.Count, Is.Zero); Assert.That(placer.Session.Selected, Is.SameAs(definition));
                adapter.enabled = false; Assert.That(placer.ExternalTargeting, Is.False);
            }
            finally { Object.Destroy(root); Object.Destroy(source); Object.Destroy(definition); }
            yield return null;
        }
    }
}
