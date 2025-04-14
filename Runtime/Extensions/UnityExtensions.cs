namespace CommandTerminal.Extensions
{
    using UnityEngine;
    using UnityEngine.UIElements;

    public static class UnityExtensions
    {
        public static void MoveElementToIndex(
            this VisualElement child,
            VisualElement parent,
            int targetIndex
        )
        {
            if (parent == null || child == null || child.parent != parent)
            {
                return;
            }

            int currentIndex = parent.IndexOf(child);
            if (currentIndex < 0)
            {
                return;
            }

            targetIndex = Mathf.Clamp(targetIndex, 0, parent.childCount);

            int effectiveTargetIndex = targetIndex;
            if (currentIndex < targetIndex)
            {
                effectiveTargetIndex = targetIndex - 1;
            }

            if (currentIndex == effectiveTargetIndex)
            {
                return;
            }

            parent.Remove(child);
            targetIndex = Mathf.Clamp(targetIndex, 0, parent.childCount);
            parent.Insert(targetIndex, child);
        }

        public static void BringToFront(this VisualElement child, VisualElement parent)
        {
            child.MoveElementToIndex(parent, parent.childCount);
        }

        public static void SendToBack(this VisualElement child, VisualElement parent)
        {
            child.MoveElementToIndex(parent, 0);
        }
    }
}
