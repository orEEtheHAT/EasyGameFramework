This addon is stored outside the base import in Addons~/XR/EasyBuilding. Its package manifest/distribution is managed by the framework coordinator. Do not infer headset/device/production-network validation from source availability.

# XR grab placement

**External dependency:** XR Interaction Toolkit 3.3.x and an XR rig; Building module from EasyGameFramework.

Requires **XR Interaction Toolkit 3.3+ (3.x)**. Use the project's configured XR Origin, interactors and Interaction Manager.

1. Put Building System + Building Placer on a persistent scene object. Assign its actor for permissions. Show Help may be disabled for VR.
2. Create a separate construction handle with XR Grab Interactable, kinematic Rigidbody and **trigger colliders**. These let the handle be grabbed without blocking its own placement preview. Keep the final building prefab separate.
3. Add **EasyBuilding > XR > Grab Placement** to the handle. Assign Placer and Definition. Pose Source optionally selects the handle pivot, Space selects a platform/ship.
4. Grab, position, release. The last hand releasing confirms once; cancelled selection does not build. Result contains the common validator's explanation.

The adapter enables External Targeting and restores its previous value on disable. Use one targeting adapter per Placer. The sample submits unit scale; grab scaling is not copied. Controller profiles, haptics and rig ergonomics belong to the XR application.

Automated tests exercise toolkit select events, duplicate release, tracking cancellation and cleanup. Physical-headset tracking still needs device verification. [XRGrabInteractable reference](https://docs.unity3d.com/Packages/com.unity.xr.interaction.toolkit@3.3/api/UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable.html).
