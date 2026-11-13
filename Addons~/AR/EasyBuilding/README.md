This addon is stored outside the base import in Addons~/AR/EasyBuilding. Its package manifest/distribution is managed by the framework coordinator. Do not infer headset/device/production-network validation from source availability.

# AR plane placement

**External dependency:** AR Foundation 6.3.x and a platform provider; Building module from EasyGameFramework.

Requires **AR Foundation 6.3+ (6.x)** and a platform XR provider.

1. Configure AR Session, XR Origin/camera, plane manager and AR Raycast Manager for the target platform.
2. Add Building System + Building Placer on a persistent scene object; set Starting Building.
3. Add **EasyBuilding > AR > Plane Placement**; assign Placer and Raycasts. Touch chooses the screen point; center aiming is optional. Yaw rotates within the plane.
4. Without plane physics colliders, disable Require Surface on the definition: the adapter validates the tracked plane; world overlaps, zones and payment still apply. With plane colliders, configure Surface Layers and keep Require Surface enabled.
5. On the device find a tracked plane, then tap. Confirmation repeats the raycast and rejects missing tracking.

A persistent Building Space may live under the application's ARAnchor/XR Origin. Provider anchor persistence and relocalization belong to the AR application; BuildingState stores Space ID/local pose. One targeting adapter per Placer; UI gates use SetInputBlocked.

Automated tests cover the no-tracking guard. Real camera permissions, tracking and anchors require a phone/tablet test. [ARRaycastManager reference](https://docs.unity3d.com/Packages/com.unity.xr.arfoundation@6.3/api/UnityEngine.XR.ARFoundation.ARRaycastManager.html).
