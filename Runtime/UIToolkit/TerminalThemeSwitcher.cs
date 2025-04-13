namespace CommandTerminal.UIToolkit
{
    using System.Collections.Generic;
    using System.Linq; // Needed for .Any()
    using UnityEngine;
    using UnityEngine.UIElements;

    public class TerminalThemeSwitcher : MonoBehaviour
    {
        [Tooltip("The UIDocument component containing the terminal UI.")]
        public UIDocument uiDocument;

        [Tooltip("The *name* of the root VisualElement for the terminal in the UXML.")]
        public string terminalRootElementName = "TerminalRoot";

        private VisualElement terminalRoot;

        // Define the theme classes exactly as used in the USS files
        private readonly List<string> themeClasses = new List<string>
        {
            "dark-theme",
            "light-theme",
            "solarized-dark-theme",
            "nord-theme",
            "monokai-theme",
        };

        // Public enum for easy selection in Inspector or code
        public enum Theme
        {
            Dark,
            Light,
            SolarizedDark,
            Nord,
            Monokai,
        }

        [Tooltip("Set the default theme to apply on start.")]
        public Theme defaultTheme = Theme.Dark;

        internal void OnEnable()
        {
            // Ensure we have the UIDocument reference
            if (uiDocument == null)
            {
                Debug.LogError(
                    $"{nameof(TerminalThemeSwitcher)}: UIDocument is not assigned in the Inspector.",
                    this
                );
                this.enabled = false; // Disable script if setup is incorrect
                return;
            }

            // The rootVisualElement might not be ready in OnEnable,
            // so we use RegisterCallback for better timing.
            uiDocument.rootVisualElement.RegisterCallback<GeometryChangedEvent>(OnRootReady);
        }

        void OnDisable()
        {
            // Unregister the callback when the component is disabled or destroyed
            if (uiDocument != null && uiDocument.rootVisualElement != null)
            {
                uiDocument.rootVisualElement.UnregisterCallback<GeometryChangedEvent>(OnRootReady);
            }
            // Optional: Clear the theme class if desired when disabled?
            // if(terminalRoot != null) { ... remove theme classes ... }
        }

        private void OnRootReady(GeometryChangedEvent evt)
        {
            // We only need to run this query once after the geometry is first computed.
            uiDocument.rootVisualElement.UnregisterCallback<GeometryChangedEvent>(OnRootReady);

            terminalRoot = uiDocument.rootVisualElement.Q<VisualElement>(terminalRootElementName);

            if (terminalRoot == null)
            {
                Debug.LogError(
                    $"{nameof(TerminalThemeSwitcher)}: Could not find VisualElement with name '{terminalRootElementName}' in the UIDocument.",
                    this
                );
                this.enabled = false; // Disable script if element not found
            }
            else
            {
                // Apply the default theme if no theme is currently set
                if (!themeClasses.Any(c => terminalRoot.ClassListContains(c)))
                {
                    SetTheme(defaultTheme);
                }
                Debug.Log(
                    $"{nameof(TerminalThemeSwitcher)}: Found terminal root '{terminalRootElementName}' and initialized theme.",
                    this
                );
            }
        }

        /// <summary>
        /// Applies the specified theme to the terminal's root element.
        /// </summary>
        /// <param name="theme">The theme to apply.</param>
        public void SetTheme(Theme theme)
        {
            if (terminalRoot == null)
            {
                Debug.LogError(
                    $"{nameof(TerminalThemeSwitcher)}: Terminal root element not found or not ready yet. Cannot set theme.",
                    this
                );
                // Optional: Try to find it again? Only if OnRootReady might not have run yet.
                // terminalRoot = uiDocument?.rootVisualElement?.Q<VisualElement>(terminalRootElementName);
                // if (terminalRoot == null) return;
                return; // Exit if still null
            }

            // 1. Remove all *other* theme classes to ensure only one is active
            string targetThemeClass = GetThemeClassName(theme);
            foreach (string themeClass in themeClasses)
            {
                // Only remove if it's NOT the target class we want to apply
                if (themeClass != targetThemeClass && terminalRoot.ClassListContains(themeClass))
                {
                    terminalRoot.RemoveFromClassList(themeClass);
                }
            }

            // 2. Add the new theme class if it's not already present
            if (
                !string.IsNullOrEmpty(targetThemeClass)
                && !terminalRoot.ClassListContains(targetThemeClass)
            )
            {
                terminalRoot.AddToClassList(targetThemeClass);
                Debug.Log($"Applied theme: {targetThemeClass}");
            }
            else if (string.IsNullOrEmpty(targetThemeClass))
            {
                Debug.LogWarning($"Could not get class name for theme {theme}");
            }
        }

        /// <summary>
        /// Gets the corresponding USS class name for a Theme enum value.
        /// </summary>
        private string GetThemeClassName(Theme theme)
        {
            switch (theme)
            {
                case Theme.Dark:
                    return "dark-theme";
                case Theme.Light:
                    return "light-theme";
                case Theme.SolarizedDark:
                    return "solarized-dark-theme";
                case Theme.Nord:
                    return "nord-theme";
                case Theme.Monokai:
                    return "monokai-theme";
                default:
                    Debug.LogWarning($"Unknown theme enum value: {theme}");
                    return null;
            }
        }

        // --- Example public methods to be called from UI Buttons or other scripts ---
        // Link these methods to Button click events in the UI Builder or via code.
        public void SetDarkTheme() => SetTheme(Theme.Dark);

        public void SetLightTheme() => SetTheme(Theme.Light);

        public void SetSolarizedDarkTheme() => SetTheme(Theme.SolarizedDark);

        public void SetNordTheme() => SetTheme(Theme.Nord);

        public void SetMonokaiTheme() => SetTheme(Theme.Monokai);
    }
}
