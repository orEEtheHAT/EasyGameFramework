using UnityEngine;

namespace EasyBuilding.PlayModeTests
{
    public sealed class LifecycleProbe : MonoBehaviour
    {
        public static int Awakes;
        private void Awake() => Awakes++;
    }
}
