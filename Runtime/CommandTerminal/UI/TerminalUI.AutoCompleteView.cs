namespace WallstopStudios.DxCommandTerminal.UI
{
    using System;
    using System.Collections.Generic;
    using Backend;
    using UnityEngine;
    using UnityEngine.UIElements;

    public sealed partial class TerminalUI
    {
        private void RefreshAutoCompleteHints()
        {
            bool shouldDisplay =
                0 < _lastCompletionBuffer.Count
                && hintDisplayMode is HintDisplayMode.Always or HintDisplayMode.AutoCompleteOnly
                && _autoCompleteContainer != null;

            if (!shouldDisplay)
            {
                if (0 < _autoCompleteContainer?.childCount)
                {
                    _autoCompleteContainer.Clear();
                }

                _previousLastCompletionIndex = null;
                return;
            }

            int bufferLength = _lastCompletionBuffer.Count;
            if (_lastKnownHintsClickable != makeHintsClickable)
            {
                _autoCompleteContainer.Clear();
                _lastKnownHintsClickable = makeHintsClickable;
            }

            int currentChildCount = _autoCompleteContainer.childCount;

            bool dirty = _lastCompletionIndex != _previousLastCompletionIndex;
            bool contentsChanged = currentChildCount != bufferLength;
            if (contentsChanged)
            {
                dirty = true;
                if (currentChildCount < bufferLength)
                {
                    for (int i = currentChildCount; i < bufferLength; ++i)
                    {
                        string hint = _lastCompletionBuffer[i];
                        VisualElement hintElement;

                        if (makeHintsClickable)
                        {
                            int currentIndex = i;
                            string currentHint = hint;
                            Button hintButton = new(() =>
                            {
                                _input.CommandText = BuildCompletionText(currentHint);
                                _lastCompletionIndex = currentIndex;
                                _needsFocus = true;
                            })
                            {
                                text = hint,
                            };
                            hintElement = hintButton;
                        }
                        else
                        {
                            Label hintText = new(hint);
                            hintElement = hintText;
                        }

                        hintElement.name = $"SuggestionText{i}";
                        _autoCompleteContainer.Add(hintElement);

                        bool isSelected = i == _lastCompletionIndex;
                        hintElement.AddToClassList("terminal-button");
                        hintElement.EnableInClassList("autocomplete-item-selected", isSelected);
                        hintElement.EnableInClassList("autocomplete-item", !isSelected);
                    }
                }
                else if (bufferLength < currentChildCount)
                {
                    for (int i = currentChildCount - 1; bufferLength <= i; --i)
                    {
                        _autoCompleteContainer.RemoveAt(i);
                    }
                }
            }

            bool shouldUpdateCompletionIndex = false;
            try
            {
                shouldUpdateCompletionIndex = _autoCompleteContainer.childCount == bufferLength;
                if (shouldUpdateCompletionIndex)
                {
                    UpdateAutoCompleteView();
                }

                if (dirty)
                {
                    for (int i = 0; i < _autoCompleteContainer.childCount && i < bufferLength; ++i)
                    {
                        VisualElement hintElement = _autoCompleteContainer[i];
                        switch (hintElement)
                        {
                            case Button button:
                                button.text = _lastCompletionBuffer[i];
                                break;
                            case Label label:
                                label.text = _lastCompletionBuffer[i];
                                break;
                            case TextField textField:
                                textField.value = _lastCompletionBuffer[i];
                                break;
                        }

                        bool isSelected = i == _lastCompletionIndex;

                        hintElement.EnableInClassList("autocomplete-item-selected", isSelected);
                        hintElement.EnableInClassList("autocomplete-item", !isSelected);
                    }
                }
            }
            finally
            {
                if (shouldUpdateCompletionIndex)
                {
                    _previousLastCompletionIndex = _lastCompletionIndex;
                }
            }
        }

        private void ResetAutoComplete()
        {
            _lastKnownCommandText = _input.CommandText ?? string.Empty;
            _lastCompletionAnchorText = null;
            _lastCompletionAnchorCaretIndex = null;
            CommandAutoComplete autoComplete = ActiveAutoComplete;
            if (autoComplete == null)
            {
                _lastCompletionIndex = null;
                _previousLastCompletionIndex = null;
                _lastCompletionBuffer.Clear();
                _lastCompletionBufferTempCache.Clear();
                _lastCompletionBufferTempSet.Clear();
                return;
            }

            if (hintDisplayMode == HintDisplayMode.Always)
            {
                _lastCompletionBufferTempCache.Clear();
                int caret =
                    _commandInput != null
                        ? _commandInput.cursorIndex
                        : (_lastKnownCommandText?.Length ?? 0);
                autoComplete.Complete(
                    _lastKnownCommandText,
                    caret,
                    _lastCompletionBufferTempCache
                );
                bool equivalent =
                    _lastCompletionBufferTempCache.Count == _lastCompletionBuffer.Count;
                if (equivalent)
                {
                    _lastCompletionBufferTempSet.Clear();
                    foreach (string completion in _lastCompletionBuffer)
                    {
                        _lastCompletionBufferTempSet.Add(completion);
                    }

                    foreach (string completion in _lastCompletionBufferTempCache)
                    {
                        if (!_lastCompletionBufferTempSet.Contains(completion))
                        {
                            equivalent = false;
                            break;
                        }
                    }
                }

                if (!equivalent)
                {
                    _lastCompletionIndex = null;
                    _previousLastCompletionIndex = null;
                    _lastCompletionBuffer.Clear();
                    foreach (string completion in _lastCompletionBufferTempCache)
                    {
                        _lastCompletionBuffer.Add(completion);
                    }
                }
            }
            else
            {
                _lastCompletionIndex = null;
                _previousLastCompletionIndex = null;
                _lastCompletionBuffer.Clear();
            }
        }

        private string BuildCompletionText(string suggestion)
        {
            if (string.IsNullOrEmpty(suggestion))
            {
                return suggestion ?? string.Empty;
            }

            CommandAutoComplete autoComplete = ActiveAutoComplete;
            if (autoComplete == null || !autoComplete.LastCompletionUsedCompleter)
            {
                return suggestion;
            }

            string prefix = autoComplete.LastCompleterPrefix ?? string.Empty;
            return string.Concat(prefix, suggestion);
        }

        public void CompleteCommand(bool searchForward = true)
        {
            if (_state == TerminalState.Closed)
            {
                return;
            }

            try
            {
                CommandAutoComplete autoComplete = ActiveAutoComplete;
                if (autoComplete == null)
                {
                    return;
                }

                _lastKnownCommandText = _input.CommandText ?? string.Empty;
                _lastCompletionBufferTempCache.Clear();
                int caret =
                    _commandInput != null
                        ? _commandInput.cursorIndex
                        : (_lastKnownCommandText?.Length ?? 0);

                string completionSource = _lastCompletionAnchorText ?? _lastKnownCommandText;
                int completionCaret = _lastCompletionAnchorCaretIndex ?? caret;

                autoComplete.Complete(
                    completionSource,
                    completionCaret,
                    _lastCompletionBufferTempCache
                );

                bool equivalentBuffers = true;
                try
                {
                    int completionLength = _lastCompletionBufferTempCache.Count;
                    equivalentBuffers = _lastCompletionBuffer.Count == completionLength;
                    if (equivalentBuffers)
                    {
                        _lastCompletionBufferTempSet.Clear();
                        foreach (string item in _lastCompletionBuffer)
                        {
                            _lastCompletionBufferTempSet.Add(item);
                        }

                        foreach (string newCompletionItem in _lastCompletionBufferTempCache)
                        {
                            if (!_lastCompletionBufferTempSet.Contains(newCompletionItem))
                            {
                                equivalentBuffers = false;
                                break;
                            }
                        }
                    }

                    if (equivalentBuffers)
                    {
                        if (completionLength > 0)
                        {
                            if (_lastCompletionIndex == null)
                            {
                                _lastCompletionIndex = 0;
                            }
                            else if (searchForward)
                            {
                                _lastCompletionIndex =
                                    (_lastCompletionIndex + 1) % completionLength;
                            }
                            else
                            {
                                _lastCompletionIndex =
                                    (_lastCompletionIndex - 1 + completionLength)
                                    % completionLength;
                            }

                            string selection = _lastCompletionBuffer[_lastCompletionIndex.Value];
                            _input.CommandText = BuildCompletionText(selection);
                            if (_lastCompletionAnchorText == null)
                            {
                                _lastCompletionAnchorText = completionSource;
                                _lastCompletionAnchorCaretIndex = completionCaret;
                            }
                        }
                        else
                        {
                            _lastCompletionIndex = null;
                            _lastCompletionAnchorText = null;
                            _lastCompletionAnchorCaretIndex = null;
                        }
                    }
                    else
                    {
                        if (completionLength > 0)
                        {
                            _lastCompletionIndex = 0;
                            string selection = _lastCompletionBufferTempCache[0];
                            _input.CommandText = BuildCompletionText(selection);
                            _lastCompletionAnchorText = completionSource;
                            _lastCompletionAnchorCaretIndex = completionCaret;
                        }
                        else
                        {
                            _lastCompletionIndex = null;
                            _lastCompletionAnchorText = null;
                            _lastCompletionAnchorCaretIndex = null;
                        }
                    }
                }
                finally
                {
                    if (!equivalentBuffers)
                    {
                        _lastCompletionBuffer.Clear();
                        foreach (string item in _lastCompletionBufferTempCache)
                        {
                            _lastCompletionBuffer.Add(item);
                        }
                        _previousLastCompletionIndex = null;
                    }

                    _previousLastCompletionIndex ??= _lastCompletionIndex;
                }
            }
            finally
            {
                _needsFocus = true;
            }
        }
    }
}
