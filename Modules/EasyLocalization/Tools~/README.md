Current unification validation/builds are coordinated by root after CUTOVER. These are optional measurement tools, not a reason to build duplicate projects during staging.

# Verification tools

Tools are excluded from ordinary package import. Core and production-integration tests are in `Tests/Editor`, `Tests/PlayMode` and `Tests/Integrations`; add `com.oreethehat.easy-game-framework` to the project's `testables` array and install Unity Test Framework 1.6.0.

For the standalone benchmark:

1. Install the single EasyGameFramework package and import its Localization **First Run** demo. Production adapters do not need a sample import.
2. Copy `Tools~/Verification` into `Assets/PackageTests/EasyLocalization/Verification`.
3. Call `EasyLocalization.Verification.Editor.VerificationBuilder.Create("Assets/PackageTests/EasyLocalization")` from an Editor script. It creates `Benchmark.unity` and ordinary first-run tables.
4. Build Benchmark.unity as a Windows Development player. Launch with `-easyLocalizationReport <absolute-path.json>`; the player writes checks and measurements and exits. `-batchmode -nographics` is suitable for CPU/text measurements, which do not include UI rendering.

`CleanInstallVerification.CreateAndBuild` is intended only for a disposable clean project: it sets New Input System, creates sample assets, replaces that project's build scene list and builds a verification player. It is not used on an existing production project.
