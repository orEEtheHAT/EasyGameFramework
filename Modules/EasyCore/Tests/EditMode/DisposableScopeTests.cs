using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace EasyCore.Tests
{
    public sealed class DisposableScopeTests
    {
        private sealed class Resource : IDisposable
        {
            public int Calls;
            public Action Cleanup;
            public void Dispose() { Calls++; Cleanup?.Invoke(); }
            public override bool Equals(object obj) => obj is Resource;
            public override int GetHashCode() => 0;
        }

        private sealed class Publisher
        {
            public event Action Changed;
            public void Raise() => Changed?.Invoke();
        }

        [Test]
        public void ReleasesInReverseOrderAndReturnsOriginalReference()
        {
            var order = new List<int>();
            var scope = new DisposableScope();
            var first = new Resource { Cleanup = () => order.Add(1) };
            Assert.That(scope.Own(first), Is.SameAs(first));
            scope.Defer(() => order.Add(2));
            var last = scope.Own(new Resource { Cleanup = () => order.Add(3) });
            Assert.That(scope.Count, Is.EqualTo(3));
            scope.Dispose();
            scope.Dispose();
            CollectionAssert.AreEqual(new[] { 3, 2, 1 }, order);
            Assert.That(first.Calls, Is.EqualTo(1));
            Assert.That(last.Calls, Is.EqualTo(1));
            Assert.That(scope.Count, Is.Zero);
            Assert.That(scope.IsDisposed, Is.True);
        }

        [Test]
        public void FailuresDoNotSkipOtherResourcesAndAreNotRetried()
        {
            var scope = new DisposableScope();
            var firstError = new InvalidOperationException("first");
            var lastError = new ArgumentException("last");
            var first = scope.Own(new Resource { Cleanup = () => throw firstError });
            var middle = scope.Own(new Resource());
            var last = scope.Own(new Resource { Cleanup = () => throw lastError });
            var error = Assert.Throws<AggregateException>(() => scope.Dispose());
            CollectionAssert.AreEqual(new Exception[] { lastError, firstError }, error.InnerExceptions);
            scope.Dispose();
            Assert.That(first.Calls + middle.Calls + last.Calls, Is.EqualTo(3));
            Assert.That(scope.IsDisposed && scope.Count == 0, Is.True);
        }

        [Test]
        public void DisposalIsReentrantAndClosedBeforeCallbacks()
        {
            var scope = new DisposableScope();
            var rejected = new Resource();
            scope.Defer(() =>
            {
                Assert.That(scope.IsDisposed && scope.Count == 0, Is.True);
                scope.Dispose();
                Assert.Throws<ObjectDisposedException>(() => scope.Own(rejected));
                Assert.Throws<ObjectDisposedException>(() => scope.Defer(() => { }));
            });
            scope.Dispose();
            Assert.That(rejected.Calls, Is.Zero, "Rejected ownership stays with the caller.");
        }

        [Test]
        public void RejectsNullSelfAndDuplicateReferencesWithoutUsingValueEquality()
        {
            using (var scope = new DisposableScope())
            {
                Assert.Throws<ArgumentNullException>(() => scope.Own<Resource>(null));
                Assert.Throws<ArgumentNullException>(() => scope.Defer(null));
                Assert.Throws<ArgumentException>(() => scope.Own(scope));
                var resource = scope.Own(new Resource());
                Assert.Throws<ArgumentException>(() => scope.Own(resource));
                scope.Own(new Resource()); // Equal by Equals, a different owned instance.
                Assert.That(scope.Count, Is.EqualTo(2));
                Assert.That(resource.Calls, Is.Zero);
            }
        }

        [Test]
        public void NestedScopesReleaseOnlyTheirOwnedResources()
        {
            var parent = new DisposableScope();
            var child = parent.Own(new DisposableScope());
            var childResource = child.Own(new Resource());
            var unrelated = new Resource();
            child.Dispose();
            parent.Dispose();
            Assert.That(childResource.Calls, Is.EqualTo(1));
            Assert.That(unrelated.Calls, Is.Zero);
        }

        [Test]
        public void RepeatedLifetimesUnsubscribeExactlyTheirOwnHandlers()
        {
            var publisher = new Publisher();
            int ownedCalls = 0, otherCalls = 0;
            publisher.Changed += () => otherCalls++;
            for (int i = 0; i < 3; i++)
            {
                var scope = new DisposableScope();
                Action handler = () => ownedCalls++;
                publisher.Changed += handler;
                scope.Defer(() => publisher.Changed -= handler);
                publisher.Raise();
                scope.Dispose();
                publisher.Raise();
            }
            Assert.That(ownedCalls, Is.EqualTo(3));
            Assert.That(otherCalls, Is.EqualTo(6));
        }

        [Test]
        public void NestedFailureStillReleasesParentResources()
        {
            var parent = new DisposableScope();
            var parentResource = parent.Own(new Resource());
            var child = parent.Own(new DisposableScope());
            var error = new InvalidOperationException("child cleanup failed");
            var childResource = child.Own(new Resource { Cleanup = () => throw error });
            var aggregate = Assert.Throws<AggregateException>(() => parent.Dispose());
            Assert.That(aggregate.Flatten().InnerExceptions, Is.EqualTo(new[] { error }));
            Assert.That(parentResource.Calls, Is.EqualTo(1));
            Assert.That(childResource.Calls, Is.EqualTo(1));
            Assert.That(parent.IsDisposed && child.IsDisposed, Is.True);
            parent.Dispose();
            Assert.That(parentResource.Calls + childResource.Calls, Is.EqualTo(2));
        }

        [Test]
        public void FailedRegistrationDuringCleanupDoesNotSkipEarlierCleanup()
        {
            var scope = new DisposableScope();
            var existing = scope.Own(new Resource());
            var rejected = new Resource();
            scope.Defer(() => scope.Own(rejected));
            var error = Assert.Throws<AggregateException>(() => scope.Dispose());
            Assert.That(error.InnerExceptions.Count, Is.EqualTo(1));
            Assert.That(error.InnerExceptions[0], Is.TypeOf<ObjectDisposedException>());
            Assert.That(existing.Calls, Is.EqualTo(1));
            Assert.That(rejected.Calls, Is.Zero);
            rejected.Dispose(); // The caller still owns the rejected resource.
            Assert.That(rejected.Calls, Is.EqualTo(1));
        }

        [Test]
        public void EmptyScopeClosesPermanently()
        {
            var scope = new DisposableScope();
            Assert.That(scope.IsDisposed, Is.False);
            scope.Dispose();
            scope.Dispose();
            var resource = new Resource();
            Assert.Throws<ObjectDisposedException>(() => scope.Own(resource));
            Assert.That(resource.Calls, Is.Zero);
        }
    }
}
