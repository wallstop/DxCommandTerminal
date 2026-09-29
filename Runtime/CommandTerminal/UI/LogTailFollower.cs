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
            reads as the end. Both are floats read out of float layout, and a
            scripted scroll can land a fraction under the extent, so an exact
            comparison would detach the tail for no reason a developer could
            see. A wheel notch is several points, so this is far below any
            scroll they make.
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
                /*
                    At the end, or nothing to scroll: following, and the pin
                    describes this position. Refreshing it here is what keeps
                    a pin from outliving the extent it was taken at - a
                    cleared log clamps the scroller to an empty view, and the
                    old pin would then read the refilled view as a
                    developer's scroll and detach the tail for good.
                 */
                Detached = false;
                _pinned = value;
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
            Follow the tail again from a position the developer did not pick:
            a command they just ran, whose output they asked for, or a log
            view rebuilt from nothing. The pin goes with the detach, or the
            view still parked where it was would read as their scroll on the
            next pass.
         */
        public void Attach()
        {
            Detached = false;
            _pinned = null;
        }
    }
}
