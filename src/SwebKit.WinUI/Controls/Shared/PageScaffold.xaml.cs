using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace SwebKit.WinUI.Controls.Shared;

public sealed partial class PageScaffold : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title),
        typeof(string),
        typeof(PageScaffold),
        new PropertyMetadata(string.Empty, OnTitleChanged));

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

    public static readonly DependencyProperty ContextContentProperty = DependencyProperty.Register(
        nameof(ContextContent),
        typeof(object),
        typeof(PageScaffold),
        new PropertyMetadata(null, OnContextContentChanged));

    public static readonly DependencyProperty IsHeaderCompactProperty = DependencyProperty.Register(
        nameof(IsHeaderCompact),
        typeof(bool),
        typeof(PageScaffold),
        new PropertyMetadata(false, OnIsHeaderCompactChanged));

    public static readonly DependencyProperty BodyContentProperty = DependencyProperty.Register(
        nameof(BodyContent),
        typeof(object),
        typeof(PageScaffold),
        new PropertyMetadata(null));

    public PageScaffold()
    {
        InitializeComponent();
        UpdateTitleVisibility();
        UpdateSubtitleVisibility();
        UpdateHeaderTextVisibility();
        UpdateHeaderVisibility();
        UpdateContextVisibility();
        UpdateHeaderLayout();
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

    public object? ContextContent
    {
        get => GetValue(ContextContentProperty);
        set => SetValue(ContextContentProperty, value);
    }

    public bool IsHeaderCompact
    {
        get => (bool)GetValue(IsHeaderCompactProperty);
        set => SetValue(IsHeaderCompactProperty, value);
    }

    public object? BodyContent
    {
        get => GetValue(BodyContentProperty);
        set => SetValue(BodyContentProperty, value);
    }

    private static void OnTitleChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var scaffold = (PageScaffold)dependencyObject;
        scaffold.UpdateTitleVisibility();
        scaffold.UpdateHeaderTextVisibility();
        scaffold.UpdateHeaderVisibility();
    }

    private static void OnSubtitleChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var scaffold = (PageScaffold)dependencyObject;
        scaffold.UpdateSubtitleVisibility();
        scaffold.UpdateHeaderTextVisibility();
        scaffold.UpdateHeaderVisibility();
    }

    private static void OnHeaderContentChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        ((PageScaffold)dependencyObject).UpdateHeaderVisibility();
    }

    private static void OnContextContentChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        ((PageScaffold)dependencyObject).UpdateContextVisibility();
    }

    private static void OnIsHeaderCompactChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        ((PageScaffold)dependencyObject).UpdateHeaderLayout();
    }

    private void UpdateTitleVisibility()
    {
        TitleTextBlock.Visibility = string.IsNullOrWhiteSpace(Title)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void UpdateSubtitleVisibility()
    {
        SubtitleTextBlock.Visibility = string.IsNullOrWhiteSpace(Subtitle)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void UpdateHeaderTextVisibility()
    {
        HeaderTextStack.Visibility = string.IsNullOrWhiteSpace(Title) && string.IsNullOrWhiteSpace(Subtitle)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void UpdateHeaderVisibility()
    {
        var hasHeaderContent = HeaderContent is not null;
        HeaderPresenter.Visibility = hasHeaderContent ? Visibility.Visible : Visibility.Collapsed;
        HeaderGrid.Visibility = HeaderTextStack.Visibility == Visibility.Collapsed && !hasHeaderContent
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void UpdateContextVisibility()
    {
        ContextPresenter.Visibility = ContextContent is null
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void UpdateHeaderLayout()
    {
        RootGrid.Padding = GetThicknessResource(
            IsHeaderCompact ? "SwebKitCompactPagePadding" : "SwebKitPagePadding",
            IsHeaderCompact ? new Thickness(20, 8, 20, 16) : new Thickness(20, 12, 20, 16));

        HeaderTextStack.Spacing = GetDoubleResource(
            IsHeaderCompact ? "SwebKitCompactPageHeaderSpacing" : "SwebKitPageHeaderSpacing",
            IsHeaderCompact ? 2d : 4d);

        TitleTextBlock.FontSize = IsHeaderCompact ? 26 : 30;
        SubtitleTextBlock.MaxWidth = IsHeaderCompact ? 720 : 840;
        HeaderPresenter.VerticalAlignment = IsHeaderCompact ? VerticalAlignment.Center : VerticalAlignment.Top;
    }

    private static Thickness GetThicknessResource(string key, Thickness fallback)
    {
        return Application.Current.Resources.TryGetValue(key, out var value) && value is Thickness thickness
            ? thickness
            : fallback;
    }

    private static double GetDoubleResource(string key, double fallback)
    {
        return Application.Current.Resources.TryGetValue(key, out var value) && value is double resourceValue
            ? resourceValue
            : fallback;
    }
}