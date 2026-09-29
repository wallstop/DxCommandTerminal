namespace WallstopStudios.DxCommandTerminal.UI
{
    /*
        Whether the log view follows its own tail: new output scrolls into
        view until the developer scrolls away, and follows again when they
        scroll back to the end.

        The trigger is "the buffer gained an entry", never "the view has more
        children". A ring buffer holds its count once full, so a child-count
        trigger stops following exactly when a session starts logging
        continuously, which is the normal case in Play Mode.

        `Detached` is false on a default instance, so a terminal that has not
        observed a scroll yet follows its output.
     */
    internal struct LogTailFollower
    {
        /*
            Slack between the scroller's value and its high value that still
            reads as the end: the engine rounds a scroll to whole points, so
            the pinned value can sit a fraction under the extent.
         */
        public const float EndTolerance = 0.5f;

        public bool Detached { get; private set; }

        private float? _pinned;

        /*
            A developer's scroll, or a request to pin the view to the end.
            `value` and `highValue` are the scroller's, read once by the
            caller; `newLogs` says the buffer gained an entry since the last
            observation.
         */
        public bool Observe(float value, float highValue, bool newLogs)
        {
            if (highValue - EndTolerance <= value)
            {
                /* At the end, or nothing to scroll: following. */
                Detached = false;
            }
            else if (_pinned.HasValue && value < _pinned.GetValueOrDefault())
            {
                /* Moved up from a pin this terminal made: the developer scrolled. */
                Detached = true;
            }

            /*
                A pin whose content has since grown is re-pinned. The layout
                pass that grows the extent runs after the write, so without
                this the view would rest a line short of the new end until
                the next log arrived - and, with the pin still equal to the
                value, nothing would read as a developer scroll either.
             */
            return !Detached && 0f < highValue && (newLogs || value < highValue);
        }

        /*
            Records the value a pin landed on. The write is clamped to the
            extent the scroller holds now, and that clamped number is what
            separates a developer's scroll from the layout pass that follows.
         */
        public void Pin(float value)
        {
            _pinned = value;
        }

        /*
            A command the developer just ran is an explicit request for its
            output. The pin goes with the detach: the view is still scrolled
            up, and without dropping it the next pass would read the old
            position as a developer's scroll and detach again.
         */
        public void Attach()
        {
            Detached = false;
            _pinned = null;
        }

        /* A rebuilt log view starts empty, so no earlier pin describes it. */
        public void Reset()
        {
            Detached = false;
            _pinned = null;
        }
    }
}
