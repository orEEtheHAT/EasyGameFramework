# Optional extensions

These directories are separate UPM packages. Unity does not import them when the main EasyGameFramework package is installed. Install the framework first, then select the specific extension needed by the project.

| Package path | Scope | Declared SDK |
| --- | --- | --- |
| `Addons~/Netcode` | EasyInteraction NGO command/actor adapters; EasyBuilding TCP transport demonstration | Netcode for GameObjects 2.7.0 |
| `Addons~/XR` | EasyInteraction and EasyBuilding XR interaction adapters | XR Interaction Toolkit 3.3.0 |
| `Addons~/AR` | EasyBuilding AR placement adapter | AR Foundation 6.3.0 |

After installing the framework from Git, an extension can be selected with its Git `?path=Addons~/...` package path at the same release tag. The SDK dependencies are declared in each extension's manifest. Configure the actual platform/provider, scene rig, authority and transport for your application.

These are focused extensions inherited from the module integrations. They do not turn the entire framework into a multiplayer, VR or AR game automatically. The Building TCP example is a separate transport demonstration, not an NGO synchronization implementation. Read the owning module's local README and tests before choosing it for a production topology.

The primary framework release checks desktop module composition independently from these SDKs. Device-specific interaction and networking topology need their own acceptance tests; base framework test results must not be reported as headset or network certification.

Declared versions match the existing adapter guards and Unity's published version lists: [Netcode](https://docs.unity3d.com/ja/6000.0/Manual/com.unity.netcode.gameobjects.html), [XR Interaction Toolkit](https://docs.unity3d.com/ja/current/Manual/com.unity.xr.interaction.toolkit.html), [AR Foundation](https://docs.unity3d.com/cn/6000.0/Manual/com.unity.xr.arfoundation.html).
