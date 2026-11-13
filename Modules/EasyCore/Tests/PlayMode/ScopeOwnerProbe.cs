using System;
using UnityEngine;

namespace EasyCore.Tests
{
    public sealed class ScopeOwnerProbe : MonoBehaviour
    {
        private DisposableScope scope;
        public int Acquired, Released, Notifications;
        public event Action Signal;

        private void OnEnable()
        {
            scope = new DisposableScope();
            Acquired++;
            scope.Defer(() => Released++);
            Signal += OnSignal;
            scope.Defer(() => Signal -= OnSignal);
        }

        private void OnSignal() => Notifications++;
        public void Raise() => Signal?.Invoke();
        private void OnDisable()
        {
            var current = scope;
            scope = null;
            current?.Dispose();
        }
    }
}
