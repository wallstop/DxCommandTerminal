namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using UnityEngine;

    /*
        Test fixture only: draws an UnityEditor.Editor's OnInspectorGUI into
        the capture render target. IMGUI paints immediately during the game
        view's own repaint pass, so redirecting RenderTexture.active around
        editor.OnInspectorGUI() inside MonoBehaviour.OnGUI lands the
        inspector in the target at view coordinates - the target is
        Screen.width x Screen.height, so pixels map one-to-one. Layout
        events size the controls without drawing; only repaint events
        redirect, and every other event type is dropped so live game-view
        input can never reach the fixture inspector's controls.
        RepaintCount lets the scenario wait for real paint passes instead of
        guessing frame counts.
     */
    public sealed class InspectorDrawSurface : MonoBehaviour
    {
        public int RepaintCount { get; private set; }

#if UNITY_EDITOR
        private UnityEditor.Editor _editor;
        private RenderTexture _target;
        private float _width;
        private float _height;
        private bool _active;

        public void Begin(
            UnityEditor.Editor editor,
            RenderTexture target,
            float width,
            float height
        )
        {
            _editor = editor;
            _target = target;
            _width = width;
            _height = height;
            _active = true;
        }

        public void End()
        {
            _active = false;
        }

        private void OnGUI()
        {
            if (!_active || _editor == null || _target == null)
            {
                return;
            }

            Event frame = Event.current;
            if (frame == null)
            {
                return;
            }

            if (frame.type is not (EventType.Layout or EventType.Repaint))
            {
                return;
            }

            bool repaint = frame.type == EventType.Repaint;
            RenderTexture previous = null;
            if (repaint)
            {
                previous = RenderTexture.active;
                RenderTexture.active = _target;
            }

            try
            {
                GUILayout.BeginArea(new Rect(0f, 0f, _width, _height));
                _editor.OnInspectorGUI();
                GUILayout.EndArea();
            }
            finally
            {
                if (repaint)
                {
                    RenderTexture.active = previous;
                    ++RepaintCount;
                }
            }
        }
#else
        public void Begin(RenderTexture target, float width, float height) { }

        public void End() { }
#endif
    }
}
