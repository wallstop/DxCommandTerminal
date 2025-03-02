mergeInto(LibraryManager.library, {
    DisableBrowserShortcuts: function() {
        document.addEventListener("keydown", function(event) {
            event.preventDefault(); // Stops default browser shortcuts
        }, false);
    }
});
