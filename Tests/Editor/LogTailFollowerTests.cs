namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using UI;

    /*
        The log view follows new output into view until the developer scrolls
        away, and follows again when they scroll back. The halves fail
        separately, so each is pinned on its own:

        - a pin the layout grew past is re-pinned, not read back as a
          developer's scroll. The layout that grows the extent runs after the
          write, so the value is still the pin when the extent has moved on.
        - a pin cannot outlive the extent it was taken at, or a cleared and
          refilled log reads as a developer's scroll and the tail never comes
          back.

        Wiring the buffer version to `newLogs` is the terminal's job and
        lives in TerminalUILogTailTests; this file is the decision.

        Every test drives `Observe` and the `Pin` that follows a request, the
        order the terminal uses, so a pass that asks for a pin ends holding
        the value the scroller would have clamped it to.
     */
    public sealed class LogTailFollowerTests
    {
        private const float Extent = 1000f;

        private static LogTailFollower AttachedFollower()
        {
            LogTailFollower follower = new();
            Assert.That(follower.Observe(Extent, Extent, true), Is.True);
            follower.Pin(Extent);
            Assert.That(follower.Detached, Is.False);
            return follower;
        }

        private static List<bool> Drive(
            ref LogTailFollower follower,
            params (float Value, float HighValue, bool NewLogs)[] passes
        )
        {
            List<bool> requests = new(passes.Length);
            for (int i = 0; i < passes.Length; ++i)
            {
                (float value, float highValue, bool newLogs) = passes[i];
                bool request = follower.Observe(value, highValue, newLogs);
                requests.Add(request);
                if (request)
                {
                    follower.Pin(highValue);
                }
            }

            return requests;
        }

        private static void AssertEveryRequest(List<bool> requests, bool expected, string because)
        {
            for (int i = 0; i < requests.Count; ++i)
            {
                Assert.That(requests[i], Is.EqualTo(expected), $"{because} (pass {i})");
            }
        }

        [Test]
        public void AFollowerThatHasSeenNoScrollFollows()
        {
            LogTailFollower follower = new();

            Assert.That(follower.Detached, Is.False, "A default follower follows its output");
            Assert.That(
                follower.Observe(0f, Extent, true),
                Is.True,
                "A view that has never scrolled takes new output"
            );
        }

        [Test]
        public void ASaturatedBufferStillFollowsNewOutput()
        {
            /*
                The view has held a full buffer's worth of children for a
                while, so every one of these passes rotates the ring without
                changing the child count - the state a Play Mode session
                reaches in seconds with Unity log forwarding on. The extent
                grows on some of them, which is the half a child-count
                trigger cannot answer: uniform lines leave the content height
                alone, and a line taller than the one it replaced does not.
             */
            LogTailFollower follower = AttachedFollower();
            (float, float, bool)[] passes = new (float, float, bool)[40];
            for (int i = 0; i < passes.Length; ++i)
            {
                passes[i] = (Extent, Extent + (i % 3 == 0 ? 40f : 0f), true);
            }

            List<bool> requests = Drive(ref follower, passes);

            Assert.That(requests, Has.Count.EqualTo(40));
            AssertEveryRequest(requests, true, "A saturated log still follows new output");
            Assert.That(follower.Detached, Is.False);
        }

        [Test]
        public void ScrollingUpDetachesTheTail()
        {
            LogTailFollower follower = AttachedFollower();

            bool request = follower.Observe(Extent - 200f, Extent, true);

            Assert.That(request, Is.False, "Output does not yank a scrolled view");
            Assert.That(follower.Detached, Is.True);
        }

        [Test]
        public void ADetachedTailIgnoresOutputThatArrivesWhileDetached()
        {
            LogTailFollower follower = AttachedFollower();
            follower.Observe(Extent - 200f, Extent, true);

            bool request = follower.Observe(Extent - 200f, Extent + 500f, true);

            Assert.That(
                request,
                Is.False,
                "Content growing under a scrolled view does not pull it down"
            );
            Assert.That(follower.Detached, Is.True);
        }

        [Test]
        public void ScrollingBackToTheEndFollowsAgain()
        {
            LogTailFollower follower = AttachedFollower();
            follower.Observe(Extent - 200f, Extent, true);

            bool request = follower.Observe(Extent + 500f, Extent + 500f, true);

            Assert.That(request, Is.True, "Reaching the end re-attaches the tail");
            Assert.That(follower.Detached, Is.False);
        }

        [Test]
        public void AGrownExtentAfterAPinIsRePinnedNotADeveloperScroll()
        {
            LogTailFollower follower = AttachedFollower();

            bool request = follower.Observe(Extent, Extent + 100f, false);

            Assert.That(request, Is.True, "The pin that laid out before the new line is re-pinned");
            Assert.That(
                follower.Detached,
                Is.False,
                "A pin the layout grew past is not a developer's scroll"
            );
        }

        [Test]
        public void NothingToScrollToNeverAsksForAPin()
        {
            LogTailFollower follower = new();

            Assert.That(
                follower.Observe(0f, 0f, true),
                Is.False,
                "An empty view has no end to pin to"
            );
        }

        [Test]
        public void AClearedViewFollowsOnceItRefills()
        {
            /*
                `clear-console` removes every child, so the scroller clamps to
                an empty view and the old pin would outlive the extent it was
                taken at. Refilling past the viewport then reads as a
                developer's scroll and the tail never comes back.
             */
            LogTailFollower follower = AttachedFollower();
            follower.Observe(0f, 0f, true);

            Assert.That(
                follower.Observe(0f, Extent / 5f, true),
                Is.True,
                "A view emptied and refilled is not a developer's scroll on the old one"
            );
        }

        [Test]
        public void AReattachedTailStillDetachesOnTheNextScrollAway()
        {
            /*
                Re-attaching by scrolling to the end takes no pin - there is
                nothing to show - so a pin taken against a smaller extent can
                still be the one the next scroll is measured against.
             */
            LogTailFollower follower = new();
            follower.Observe(Extent / 2f, Extent / 2f, true);
            follower.Pin(Extent / 2f);

            Assert.That(
                follower.Observe(Extent, Extent, false),
                Is.False,
                "Reaching the end with nothing new logs takes no pin"
            );
            Assert.That(
                follower.Observe(Extent - 100f, Extent, true),
                Is.False,
                "A re-attached tail detaches on the developer's next scroll away"
            );
            Assert.That(follower.Detached, Is.True);
        }

        [Test]
        public void RunningACommandReattachesADetachedTail()
        {
            LogTailFollower follower = AttachedFollower();
            follower.Observe(Extent - 200f, Extent, true);

            follower.Attach();
            bool request = follower.Observe(Extent - 200f, Extent, true);

            Assert.That(request, Is.True, "The command's output is what was asked for");
            Assert.That(follower.Detached, Is.False);
        }

        [Test]
        public void ANewViewDropsAPinThatDescribesAnEarlierOne()
        {
            LogTailFollower follower = AttachedFollower();
            follower.Observe(Extent - 200f, Extent, true);
            Assert.That(follower.Detached, Is.True);

            follower.Attach();

            Assert.That(follower.Detached, Is.False, "A rebuilt view follows its output");
            Assert.That(
                follower.Observe(0f, Extent, true),
                Is.True,
                "A view at zero is not a developer's scroll on the old one"
            );
        }

        [TestCase(1000f, 1000f, true, true, "At the end with new output")]
        [TestCase(1000f, 1000f, false, false, "At the end with nothing to show")]
        [TestCase(1000f, 1200f, false, true, "The extent grew after a pin")]
        [TestCase(1000f, 1200f, true, true, "The extent grew and a log arrived")]
        [TestCase(800f, 1000f, true, false, "The developer scrolled up")]
        [TestCase(800f, 1000f, false, false, "The developer scrolled up, nothing arrived")]
        [TestCase(0f, 0f, true, false, "Nothing to scroll to")]
        public void DecideWhetherToPin(
            float value,
            float highValue,
            bool newLogs,
            bool expected,
            string because
        )
        {
            LogTailFollower follower = AttachedFollower();

            bool request = follower.Observe(value, highValue, newLogs);

            Assert.That(request, Is.EqualTo(expected), because);
        }
    }
}
