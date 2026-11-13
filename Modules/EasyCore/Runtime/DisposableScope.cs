using System;
using System.Collections.Generic;

namespace EasyCore
{
    /// <summary>
    /// Owns reference-type resources and deferred cleanup on one calling thread.
    /// Dispose releases in reverse registration order and attempts every cleanup.
    /// Not reusable or thread-safe. Create a new scope for each enable/lifetime.
    /// </summary>
    public sealed class DisposableScope : IDisposable
    {
        private List<IDisposable> resources;

        public bool IsDisposed { get; private set; }
        /// <summary>Number of owned registrations. Zero as soon as disposal starts.</summary>
        public int Count => resources?.Count ?? 0;

        /// <summary>
        /// Transfers ownership on success and returns the same reference. Null, this scope,
        /// duplicate references and a disposed scope are rejected without taking ownership.
        /// Do not externally dispose owned resources or create cyclic ownership.
        /// Registration may allocate; this is lifetime setup, not a per-frame pool.
        /// </summary>
        public T Own<T>(T resource) where T : class, IDisposable
        {
            ThrowIfDisposed();
            if (ReferenceEquals(resource, null)) throw new ArgumentNullException(nameof(resource));
            if (ReferenceEquals(resource, this)) throw new ArgumentException("A scope cannot own itself.", nameof(resource));
            if (resources != null)
            {
                for (int i = 0; i < resources.Count; i++)
                    if (ReferenceEquals(resources[i], resource))
                        throw new ArgumentException("This resource is already owned by this scope.", nameof(resource));
            }
            if (resources == null) resources = new List<IDisposable>();
            resources.Add(resource);
            return resource;
        }

        /// <summary>
        /// Registers cleanup such as event unsubscription. The delegate and its captures are
        /// retained until disposal. Each registration runs once, even if it throws.
        /// </summary>
        public void Defer(Action cleanup)
        {
            ThrowIfDisposed();
            if (cleanup == null) throw new ArgumentNullException(nameof(cleanup));
            Own(new Cleanup(cleanup));
        }

        /// <summary>
        /// Closes the scope before invoking callbacks. Reentrant/repeated Dispose is harmless;
        /// new registrations fail. Cleanup exceptions are collected in release order and thrown
        /// as AggregateException after all resources have been attempted. No retry or logging.
        /// </summary>
        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            var pending = resources;
            resources = null;
            if (pending == null) return;

            List<Exception> errors = null;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var resource = pending[i];
                pending[i] = null;
                try { resource.Dispose(); }
                catch (Exception exception)
                {
                    if (errors == null) errors = new List<Exception>();
                    errors.Add(exception);
                }
            }
            pending.Clear();
            if (errors != null) throw new AggregateException("One or more owned resources failed to release.", errors);
        }

        private void ThrowIfDisposed()
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(DisposableScope), "Create a new scope for the next lifetime.");
        }

        private sealed class Cleanup : IDisposable
        {
            private Action action;
            internal Cleanup(Action action) { this.action = action; }
            public void Dispose()
            {
                var current = action;
                action = null;
                current?.Invoke();
            }
        }
    }
}
