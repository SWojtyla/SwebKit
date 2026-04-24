using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace SwebKit.WinUI.Controls.Shared;

public sealed partial class PageScaffold : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title),
        typeof(string),
        typeof(PageScaffold),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(
        nameof(Subtitle),
        typeof(string),
        typeof(PageScaffold),
        new PropertyMetadata(string.Empty, OnSubtitleChanged));

    public static readonly DependencyProperty HeaderContentProperty = DependencyProperty.Register(
        nameof(HeaderContent),
        typeof(object),
        typeof(PageScaffold),
        new PropertyMetadata(null, OnHeaderContentChanged));

    public static readonly DependencyProperty BodyContentProperty = DependencyProperty.Register(
        nameof(BodyContent),
        typeof(object),
        typeof(PageScaffold),
        new PropertyMetadata(null));

    public PageScaffold()
    {
        InitializeComponent();
        UpdateSubtitleVisibility();
        UpdateHeaderVisibility();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public object? HeaderContent
    {
        get => GetValue(HeaderContentProperty);
        set => SetValue(HeaderContentProperty, value);
    }

    public object? BodyContent
    {
        get => GetValue(BodyContentProperty);
        set => SetValue(BodyContentProperty, value);
    }

    private static void OnSubtitleChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        ((PageScaffold)dependencyObject).UpdateSubtitleVisibility();
    }

    private static void OnHeaderContentChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        ((PageScaffold)dependencyObject).UpdateHeaderVisibility();
    }

    private void UpdateSubtitleVisibility()
    {
        SubtitleTextBlock.Visibility = string.IsNullOrWhiteSpace(Subtitle)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void UpdateHeaderVisibility()
    {
        HeaderPresenter.Visibility = HeaderContent is null
            ? Visibility.Collapsed
            : Visibility.Visible;
    }
}