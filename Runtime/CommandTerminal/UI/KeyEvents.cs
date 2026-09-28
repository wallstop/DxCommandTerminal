namespace WallstopStudios.DxCommandTerminal.UI
{
    using UnityEngine.UIElements;

    /*
        One definition of "this surface handled that key", shared by both
        console surfaces.

        The two halves are not the same guarantee, and both are wanted.
        StopPropagation ends the event where it stands: no ancestor, and -
        because a trickle-down callback runs before the target's own phase -
        not the focused element either, which is the only thing below a
        TextField that could still act on the key. IgnoreEvent (Unity 6) or
        PreventDefault (older) is the focus controller's half: it keeps the
        controller from moving panel focus off what the user is typing into.

        A surface that answers a key its sibling also answers must call this
        rather than its own version of it. That is the whole reason it is
        shared: the two used to differ, and a key handled in one surface and
        only half-handled in the other is a defect that reads as a flaky
        double action (PR #180 review).
     */
    internal static class KeyEvents
    {
        /*
            The element supplies the panel. A null panel is tolerated rather
            than guarded at every call site: an event only arrives through a
            dispatched element, so there is always one, and a null-check here
            keeps a teardown race from throwing inside a key handler.
         */
        public static void Consume(VisualElement element, KeyDownEvent evt)
        {
            evt.StopPropagation();

            IPanel panel = element?.panel;
            if (panel == null)
            {
                return;
            }

#if UNITY_6000_0_OR_NEWER
            panel.focusController.IgnoreEvent(evt);
#else
            evt.PreventDefault();
#endif
        }
    }
}
