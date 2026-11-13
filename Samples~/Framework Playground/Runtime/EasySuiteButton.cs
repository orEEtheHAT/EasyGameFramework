using UnityEngine;
using UnityEngine.UI;

namespace EasySuite.Example
{
    [RequireComponent(typeof(Button))]
    public sealed class EasySuiteButton : MonoBehaviour
    {
        public EasySuiteCommand Command;
        private EasySuiteWindow window;
        public void Bind(EasySuiteWindow owner) => window = owner;
        private void OnEnable() => GetComponent<Button>().onClick.AddListener(Clicked);
        private void OnDisable() => GetComponent<Button>().onClick.RemoveListener(Clicked);
        private void Clicked() => window?.Invoke(Command);
    }
}
