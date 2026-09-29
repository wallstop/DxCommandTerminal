namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using UI;

    /*
        The log view follows new output until the developer scrolls away, and
        follows again when they scroll back. The halves fail separately, so
        each is pinned on its own:

        - the trigger is the buffer version, not the view's child count. A
          ring buffer holds its count once full, so a child-count trigger
          stops following exactly when a session starts logging
          continuously, which is the normal case in Play Mode.
        - a pin is not read back as a developer's scroll when the layout pass
          after it grows the content.

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
                reaches in seconds with Unity log forwarding on.
             */
            LogTailFollower follower = AttachedFollower();
            (float, float, bool)[] passes = new (float, float, bool)[40];
            for (int i = 0; i < passes.Length; ++i)
            {
                passes[i] = (Extent, Extent, true);
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
        public void AResetDropsAPinThatDescribesAnEarlierView()
        {
            LogTailFollower follower = AttachedFollower();
            follower.Observe(Extent - 200f, Extent, true);
            Assert.That(follower.Detached, Is.True);

            follower.Reset();

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
