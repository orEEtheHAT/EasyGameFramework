# Architecture

EasyGameFramework is one versioned UPM package. Modules retain narrow responsibilities and separate assemblies. Integration assemblies reference the modules they connect; core gameplay assemblies do not acquire reverse dependencies on their consumers.

`EasyCore` has no dependency on a gameplay module. Shared helpers belong there only when at least two consumers need the same semantics. Resource reservations, input contexts, movement speed modifiers and UI window handles have different lifetimes and stay with their owners.

The runtime has no obligatory global service locator or framework tick. Add the components required for a scenario and supply their references through the Inspector or explicit APIs. Input and UI context ownership support nested windows and multiple independent blockers. Definition assets supply initial configuration; running a game does not write its state back into those assets.

Saving is coordinated by EasySave. Each module validates, captures and restores its own state. Preflight catches malformed or incompatible data before mutation; it does not lock a changing world or promise atomic rollback across arbitrary user callbacks. Gameplay controls transient-command cancellation and restoration order. Progress slots and global preferences are independent.

EasySettings owns URP/display settings. EasyInput owns binding and sensitivity preferences; EasyFeedbacks owns audio/haptics; EasyLocalization owns locale selection. EasyUI presents and connects these APIs without introducing duplicate state.

ECM2, Cinemachine, VFX Graph and the New Input System are standard framework integration targets. ECM2 is installed by the developer under their own license. SDK-specific networking, XR/AR, platform services and remote storage remain extensions. Shipping an extension contract or demonstration does not claim full production support for every transport or headset.

Performance decisions use measured workloads. Burst-compatible numerical work may use Jobs, while Unity object access and render/UI configuration remain on the main thread. Normal component lifecycle, pooling and hot-loop allocation behavior are checked separately from initialization and test UI overhead.

Module maintainers work in their module and owned integration folders. Cross-module API changes are agreed before consumers are modified. Package metadata, shared setup, end-to-end examples and release verification are coordinated centrally.
