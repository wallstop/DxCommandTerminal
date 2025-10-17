namespace WallstopStudios.DxCommandTerminal.UI
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using Backend;
    using UnityEngine.UIElements;

    public sealed partial class TerminalUI
    {
        private sealed class HistoryListAdapter
        {
            private readonly TerminalUI _owner;
            private readonly IList<LogItem> _items;
            private readonly IList _itemsList;

            internal HistoryListAdapter(TerminalUI owner, IList<LogItem> items)
            {
                _owner = owner;
                _items = items;
                _itemsList =
                    items as IList
                    ?? throw new ArgumentException(
                        "HistoryListAdapter requires IList backing storage.",
                        nameof(items)
                    );
            }

            internal ListView ListView { get; private set; }

            internal ScrollView ScrollView { get; private set; }

            internal void EnsureInitialized(VisualElement container)
            {
                if (ListView == null)
                {
                    ListView = CreateListView();
                    container.Add(ListView);
                }

                if (ScrollView == null)
                {
                    EnsureScrollViewReady();
                }
            }

            internal void InjectForTests(ListView listView, ScrollView scrollView)
            {
                if (listView != null)
                {
                    ListView = listView;
                    ConfigureListView(ListView);
                }

                if (scrollView != null)
                {
                    ScrollView = scrollView;
                    ConfigureScrollView();
                }
            }

            internal void EnsureScrollViewReady()
            {
                if (ScrollView != null)
                {
                    return;
                }

                ScrollView candidate = ListView?.Q<ScrollView>();
                if (candidate == null)
                {
                    ListView?.schedule.Execute(EnsureScrollViewReady).ExecuteLater(0);
                    return;
                }

                ScrollView = candidate;
                ConfigureScrollView();
            }

            private ListView CreateListView()
            {
                ListView listView = new ListView
                {
                    virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight,
                    selectionType = SelectionType.None,
                    showAlternatingRowBackgrounds = AlternatingRowBackground.None,
                    name = "LogListView",
                };
                ConfigureListView(listView);
                return listView;
            }

            private void ConfigureListView(ListView listView)
            {
                listView.makeItem = _owner.CreateLogListItem;
                listView.bindItem = _owner.BindLogListItem;
                listView.itemsSource = _itemsList;
                TerminalUI.ConfigureEmptyLabel(listView);
            }

            private void ConfigureScrollView()
            {
                if (ScrollView == null)
                {
                    return;
                }

                _owner._logScrollView = ScrollView;
                _owner.InitializeScrollView(ScrollView);
                ScrollView.AddToClassList("log-scroll-view");
                TerminalUI.ConfigureEmptyLabel(ListView);

                _owner._logViewport = ScrollView.contentViewport;
                if (_owner._logViewport != null)
                {
                    _owner._logViewport.style.flexGrow = 1f;
                    _owner._logViewport.style.flexShrink = 1f;
                    _owner._logViewport.style.minHeight = 0f;
                    _owner._logViewport.style.overflow = Overflow.Hidden;
                }

                VisualElement logContent = ScrollView.contentContainer;
                if (logContent != null)
                {
                    logContent.style.flexDirection = FlexDirection.Column;
                    logContent.style.alignItems = Align.Stretch;
                    logContent.style.minHeight = 0f;
                    logContent.style.justifyContent = Justify.FlexStart;
                    logContent.RegisterCallback<GeometryChangedEvent>(
                        _owner.OnLogContentGeometryChanged
                    );
                }
            }

            internal void SetJustification(Justify justify)
            {
                VisualElement content = ScrollView?.contentContainer;
                if (content != null)
                {
                    content.style.justifyContent = justify;
                }
            }

            internal void ConfigureEmptyLabel()
            {
                TerminalUI.ConfigureEmptyLabel(ListView);
            }

            internal void Rebuild()
            {
                ListView?.Rebuild();
            }

            internal void RefreshItems()
            {
                ListView?.RefreshItems();
            }
        }
    }
}
