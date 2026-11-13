using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace EasyBuilding.XR.Tests
{
    public sealed class BuildingXRTests
    {
        [UnityTest] public IEnumerator GrabEventsConfirmOnceAndCancelledTrackingDoesNotBuild()
        {
            var root = new GameObject("XR world"); var world = root.AddComponent<BuildingSystem>(); var placer = root.AddComponent<BuildingPlacer>(); placer.ShowHelp = false;
            var source = new GameObject("Source"); source.SetActive(false);
            var definition = ScriptableObject.CreateInstance<BuildingDefinition>(); definition.Prefab = source; definition.RequireSurface = false;
            var managerRoot = new GameObject("XR manager"); var manager = managerRoot.AddComponent<XRInteractionManager>();
            var handle = new GameObject("Construction handle"); handle.SetActive(false); handle.transform.position = Vector3.right * 200;
            handle.AddComponent<BoxCollider>().isTrigger = true; handle.AddComponent<Rigidbody>().isKinematic = true;
            var grab = handle.AddComponent<XRGrabInteractable>(); grab.interactionManager = manager; grab.throwOnDetach = false;
            var adapter = handle.AddComponent<BuildingXRGrabPlacement>(); adapter.Placer = placer; adapter.Definition = definition;
            try
            {
                handle.SetActive(true); yield return null; Assert.That(placer.ExternalTargeting, Is.True);
                grab.firstSelectEntered.Invoke(new SelectEnterEventArgs { interactableObject = grab });
                grab.lastSelectExited.Invoke(new SelectExitEventArgs { interactableObject = grab, isCanceled = false });
                Assert.That(world.Count, Is.EqualTo(1)); Assert.That(adapter.Result.IsValid, Is.True, adapter.Result.Reason);
                grab.lastSelectExited.Invoke(new SelectExitEventArgs { interactableObject = grab, isCanceled = false }); Assert.That(world.Count, Is.EqualTo(1));
                handle.transform.position += Vector3.right * 4;
                grab.firstSelectEntered.Invoke(new SelectEnterEventArgs { interactableObject = grab });
                grab.lastSelectExited.Invoke(new SelectExitEventArgs { interactableObject = grab, isCanceled = true });
                Assert.That(world.Count, Is.EqualTo(1)); Assert.That(placer.Session.Selected, Is.Null);
                adapter.enabled = false; Assert.That(placer.ExternalTargeting, Is.False);
            }
            finally { Object.Destroy(handle); Object.Destroy(managerRoot); Object.Destroy(root); Object.Destroy(source); Object.Destroy(definition); }
            yield return null;
        }
    }
}
