namespace WallstopStudios.DxCommandTerminal.Tests.Runtime.Allocation
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Helper;
    using NUnit.Framework;

    /*
        Contract tests for the rented string-builder helper: the whole
        rent-use-return cycle allocates nothing, the scope is copy-safe
        (exactly-once return across copies, including a stale copy disposed
        after the slot was re-rented), and it is re-entrant (nested scopes
        get distinct builders that all survive).
    */
    public sealed class CachedStringBuilderTests
    {
        [Test]
        public void RentUseReturnCycleIsAllocationFree()
        {
            AllocationAssertions.AssertZeroAllocations(
                "cached string builder rent-use-return (warm slot)",
                () =>
                {
                    using CachedStringBuilder.Scope scope = CachedStringBuilder.Rent(64);
                    scope.Builder.Append("warm-rent");
                }
            );
        }

        [Test]
        public void ReturnedBuildersAreReusedAndCleared()
        {
            StringBuilder first;
            using (CachedStringBuilder.Scope scope = CachedStringBuilder.Rent(64))
            {
                first = scope.Builder;
                first.Append("reuse-check");
            }

            using (CachedStringBuilder.Scope scope = CachedStringBuilder.Rent(64))
            {
                Assert.AreSame(
                    first,
                    scope.Builder,
                    "A disposed builder must come back from its slot"
                );
                Assert.AreEqual(0, first.Length, "Released builders must be cleared");
            }
        }

        [Test]
        public void DisposingACopyIsANoOp()
        {
            CachedStringBuilder.Scope scope = CachedStringBuilder.Rent(64);
            StringBuilder builder = scope.Builder;
            builder.Append("copy-check");

            // A copy of the scope shares the lease: exactly one dispose wins.
            CachedStringBuilder.Scope copy = scope;
            scope.Dispose();
            copy.Dispose();

            using (CachedStringBuilder.Scope next = CachedStringBuilder.Rent(64))
            {
                Assert.AreSame(
                    builder,
                    next.Builder,
                    "The builder must return exactly once and stay usable"
                );
                Assert.AreEqual(0, next.Builder.Length, "The winning claim must clear the builder");
            }
        }

        [Test]
        public void StaleCopyDisposedAfterReRentDoesNotCorruptTheNewRenter()
        {
            CachedStringBuilder.Scope scope = CachedStringBuilder.Rent(64);
            StringBuilder staleCopyBuilder = scope.Builder;
            scope.Dispose();

            using (CachedStringBuilder.Scope next = CachedStringBuilder.Rent(64))
            {
                Assert.AreSame(
                    staleCopyBuilder,
                    next.Builder,
                    "Sanity: the re-rent must reuse the recycled slot's builder"
                );
                next.Builder.Append("live-data");

                // The stale copy holds the old generation: its dispose must lose.
                scope.Dispose();

                Assert.AreEqual(
                    "live-data",
                    next.Builder.ToString(),
                    "A stale copy's dispose must not clear the new renter's builder"
                );
            }
        }

        [Test]
        public void NestedScopesAreIndependentAndAllSurvive()
        {
            StringBuilder outerBuilder;
            StringBuilder innerBuilder;
            using (CachedStringBuilder.Scope outer = CachedStringBuilder.Rent(64))
            {
                outerBuilder = outer.Builder;
                outerBuilder.Append("outer");
                using (CachedStringBuilder.Scope inner = CachedStringBuilder.Rent(64))
                {
                    innerBuilder = inner.Builder;
                    Assert.AreNotSame(
                        outerBuilder,
                        innerBuilder,
                        "A nested scope must not share the outer scope's builder"
                    );
                    innerBuilder.Append("inner");
                }

                Assert.AreEqual(
                    "outer",
                    outerBuilder.ToString(),
                    "Disposing the inner scope must not disturb the outer builder"
                );
            }

            /*
                The free list is LIFO and the outer scope returned last, so
                the first two re-rents must hand back exactly the outer and
                inner builders - held simultaneously here, since each return
                puts its builder back at the head.
             */
            List<CachedStringBuilder.Scope> held = new();
            HashSet<StringBuilder> returned = new();
            try
            {
                for (int i = 0; i < 2; ++i)
                {
                    CachedStringBuilder.Scope scope = CachedStringBuilder.Rent(64);
                    returned.Add(scope.Builder);
                    held.Add(scope);
                }
            }
            finally
            {
                foreach (CachedStringBuilder.Scope scope in held)
                {
                    scope.Dispose();
                }
            }

            Assert.IsTrue(
                returned.Contains(outerBuilder) && returned.Contains(innerBuilder),
                "Both nested builders must survive their scopes and come back re-rentable"
            );
        }

        [Test]
        public void RentHoldsTheRequestedCapacity()
        {
            using (CachedStringBuilder.Scope scope = CachedStringBuilder.Rent(1024))
            {
                Assert.GreaterOrEqual(
                    scope.Builder.Capacity,
                    1024,
                    "Rent must ensure the requested capacity"
                );
            }
        }

        [Test]
        public void OversizedBuilderIsReclaimedNotPinned()
        {
            StringBuilder oversized;
            using (
                CachedStringBuilder.Scope scope = CachedStringBuilder.Rent(
                    CachedStringBuilder.MaxRetainedBuilderCapacity + 1
                )
            )
            {
                oversized = scope.Builder;
                Assert.GreaterOrEqual(
                    oversized.Capacity,
                    CachedStringBuilder.MaxRetainedBuilderCapacity,
                    "Sanity: the oversized rent must hold its requested capacity"
                );
            }

            using (CachedStringBuilder.Scope scope = CachedStringBuilder.Rent(16))
            {
                Assert.AreNotSame(
                    oversized,
                    scope.Builder,
                    "An oversized builder must be dropped for the GC, not pooled"
                );
                Assert.LessOrEqual(
                    scope.Builder.Capacity,
                    CachedStringBuilder.MaxRetainedBuilderCapacity,
                    "The replacement builder must be small again"
                );
            }
        }
    }
}
