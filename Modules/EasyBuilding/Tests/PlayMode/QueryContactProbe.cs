using UnityEngine;
namespace EasyBuilding.PlayModeTests
{
    public sealed class QueryContactProbe : MonoBehaviour
    {
        public int Contacts;
        private void OnTriggerEnter(Collider _) => Contacts++;
        private void OnCollisionEnter(Collision _) => Contacts++;
    }
}
