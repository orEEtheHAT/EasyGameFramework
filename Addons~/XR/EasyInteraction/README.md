# XR interaction addon

XRInteractionActor/XRInteractionAction for EasyInteraction in EasyGameFramework. Requires XR Interaction Toolkit3.2.2+ and explicit addon installation; root supplies the addon manifest. The base package does not import these assemblies.

Tests/XRAdapterTests.cs is the single canonical regression fixture. Tests validate manager/select callbacks, not a physical headset. Two hands use two Interaction Actors and Grab.MaxParticipants=2; do not add a competing XRGrabInteractable to the same Rigidbody.
