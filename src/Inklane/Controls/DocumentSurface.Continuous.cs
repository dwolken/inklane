using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Inklane.Core;

namespace Inklane.Controls;

public sealed partial class DocumentSurface
{
    private const double PageInset = 32, PageGap = 20;
    private readonly Canvas _pageStrip = new();
    private readonly Dictionary<int, PagePreview> _pagePreviews = [];
    private IReadOnlyList<PageData>? _pageSequence;
    private Func<int, Task<BitmapSource>>? _readPreview;
    private double[] _pageTops = [];
    private double _sequenceWidth;
    private int _activePage;
    private bool _resetSequenceView, _refreshQueued, _renderingPreviews;
    private sealed record PagePreview(PageData Page, Button Button)
    {
        public bool Requested { get; set; }
    }

    internal bool IsContinuous => _pageSequence != null;
    internal event Action<int>? PageActivated;

    internal void SetPageSequence(IReadOnlyList<PageData>? pages, int index,
        Func<int, Task<BitmapSource>>? readPreview, bool newDocument)
    {
        if (pages == null)
        {
            if (!IsContinuous) return;
            _pageSequence = null;
            _readPreview = null;
            _pagePreviews.Clear();
            _pageStrip.Children.Clear();
            _pageHost.Margin = new Thickness(PageInset);
            _scroll.Content = _pageHost;
            return;
        }

        if (!IsContinuous)
        {
            _scroll.Content = _pageStrip;
            _pageHost.Margin = new Thickness(0);
            _pageStrip.HorizontalAlignment = HorizontalAlignment.Center;
            _pageStrip.Children.Add(_pageHost);
            Panel.SetZIndex(_pageHost, 1);
            _resetSequenceView = true;
        }
        if (!ReferenceEquals(_pageSequence, pages))
        {
            foreach (var preview in _pagePreviews.Values) _pageStrip.Children.Remove(preview.Button);
            _pagePreviews.Clear();
        }
        _resetSequenceView |= newDocument;
        _pageSequence = pages;
        _readPreview = readPreview;
        _activePage = index;
        _sequenceWidth = pages.Max(page => page.Width);
        if (_pageTops.Length != pages.Count) _pageTops = new double[pages.Count];
        LayoutPageSequence();
    }

    private void LayoutPageSequence()
    {
        var pages = _pageSequence!;
        _pageStrip.Width = _sequenceWidth * _zoom + PageInset * 2;
        double top = PageInset;
        for (int i = 0; i < pages.Count; i++)
        {
            _pageTops[i] = top;
            top += pages[i].Height * _zoom + PageGap;
        }
        _pageStrip.Height = top - PageGap + PageInset;
        PlacePage(_pageHost, _activePage);
        foreach (var (index, preview) in _pagePreviews)
        {
            if (index >= pages.Count) continue;
            preview.Button.Width = preview.Page.Width * _zoom;
            preview.Button.Height = preview.Page.Height * _zoom;
            PlacePage(preview.Button, index);
        }
    }

    private void PlacePage(FrameworkElement visual, int index)
    {
        Canvas.SetTop(visual, _pageTops[index]);
        Canvas.SetLeft(visual, PageInset + (_sequenceWidth - _pageSequence![index].Width) * _zoom / 2);
    }

    internal void RevealActivePage()
    {
        if (!IsContinuous) return;
        ScrollTo(_scroll.HorizontalOffset, _pageTops[_activePage] - PageInset);
        QueueContinuousRefresh();
    }

    private void QueueContinuousRefresh()
    {
        if (!IsContinuous || _refreshQueued) return;
        _refreshQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _refreshQueued = false;
            RefreshVisiblePages();
        });
    }

    private void RefreshVisiblePages()
    {
        if (_pageSequence is not { } pages || !IsVisible || _scroll.ViewportHeight <= 0) return;
        var top = _scroll.VerticalOffset;
        var bottom = top + _scroll.ViewportHeight;
        double End(int index) => _pageTops[index] + pages[index].Height * _zoom;
        int first = Array.BinarySearch(_pageTops, top);
        first = first >= 0 ? first : Math.Max(0, ~first - 1);
        if (first + 1 < pages.Count && End(first) <= top) first++;
        int last = first;
        while (last + 1 < pages.Count && _pageTops[last + 1] < bottom) last++;

        foreach (var (index, preview) in _pagePreviews.ToArray())
        {
            if (index != _activePage && index >= first && index <= last && ReferenceEquals(preview.Page, pages[index])) continue;
            _pageStrip.Children.Remove(preview.Button);
            _pagePreviews.Remove(index);
        }
        for (int i = first; i <= last; i++)
        {
            if (i == _activePage || _pagePreviews.ContainsKey(i)) continue;
            int index = i;
            var label = string.Format(CultureInfo.CurrentCulture, Translate("ActivatePage"), index + 1);
            var button = new Button
            {
                Width = pages[i].Width * _zoom, Height = pages[i].Height * _zoom, Padding = new Thickness(0),
                BorderThickness = new Thickness(0), Background = Brushes.White, ClipToBounds = true,
                HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch,
                ToolTip = label
            };
            button.SetResourceReference(StyleProperty, "QuietButton");
            AutomationProperties.SetName(button, label);
            button.Click += (_, _) => { Focus(); PageActivated?.Invoke(index); };
            _pagePreviews.Add(i, new PagePreview(pages[i],button));
            PlacePage(button, i);
            _pageStrip.Children.Add(button);
        }
        RenderVisiblePreviews();

        // Leave the editor on the current page while any of it remains visible.
        // On a scrollbar jump, activate the first visible page after input ends.
        if (!IsMouseCaptureWithin && (_activePage < first || _activePage > last))
            PageActivated?.Invoke(first);
    }

    private async void RenderVisiblePreviews()
    {
        if (_renderingPreviews) return;
        _renderingPreviews = true;
        try
        {
            while (_pagePreviews.FirstOrDefault(pair => !pair.Value.Requested) is { Value: { } preview } request)
            {
                preview.Requested = true;
                try
                {
                    BitmapSource? image = _readPreview == null ? null : await _readPreview(request.Key);
                    if (!_pagePreviews.TryGetValue(request.Key, out var current) || current != preview) continue;
                    var drawing = new DrawingGroup { ClipGeometry = new RectangleGeometry(new Rect(0,0,preview.Page.Width,preview.Page.Height)) };
                    using (var dc = drawing.Open()) DrawPage(dc, preview.Page, image);
                    drawing.Freeze();
                    preview.Button.Content = new Image { Source = new DrawingImage(drawing), Stretch = Stretch.Fill, IsHitTestVisible = false };
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or
                    ArgumentException or NotSupportedException or System.Runtime.InteropServices.COMException)
                {
                    preview.Button.Content = new TextBlock { Text = Translate("PreviewUnavailable"), TextWrapping = TextWrapping.Wrap,
                        VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Brushes.Black };
                }
            }
        }
        finally { _renderingPreviews = false; }
    }
}
