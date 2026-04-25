using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace SwebKit.WinUI.Controls.Shared;

public sealed partial class DetailPaneHost : UserControl
{
    public static readonly DependencyProperty MainContentProperty = DependencyProperty.Register(
        nameof(MainContent),
        typeof(object),
        typeof(DetailPaneHost),
        new PropertyMetadata(null));

    public static readonly DependencyProperty DetailContentProperty = DependencyProperty.Register(
        nameof(DetailContent),
        typeof(object),
        typeof(DetailPaneHost),
        new PropertyMetadata(null, OnDetailContentChanged));

    public static readonly DependencyProperty DetailHeaderContentProperty = DependencyProperty.Register(
        nameof(DetailHeaderContent),
        typeof(object),
        typeof(DetailPaneHost),
        new PropertyMetadata(null, OnDetailHeaderContentChanged));

    public static readonly DependencyProperty DetailWidthProperty = DependencyProperty.Register(
        nameof(DetailWidth),
        typeof(double),
        typeof(DetailPaneHost),
        new PropertyMetadata(420d, OnDetailWidthChanged));

    public DetailPaneHost()
    {
        InitializeComponent();
        UpdateDetailVisibility();
        UpdateDetailHeaderVisibility();
    }

    public object? MainContent
    {
        get => GetValue(MainContentProperty);
        set => SetValue(MainContentProperty, value);
    }

    public object? DetailContent
    {
        get => GetValue(DetailContentProperty);
        set => SetValue(DetailContentProperty, value);
    }

    public object? DetailHeaderContent
    {
        get => GetValue(DetailHeaderContentProperty);
        set => SetValue(DetailHeaderContentProperty, value);
    }

    public double DetailWidth
    {
        get => (double)GetValue(DetailWidthProperty);
        set => SetValue(DetailWidthProperty, value);
    }

    private static void OnDetailContentChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        ((DetailPaneHost)dependencyObject).UpdateDetailVisibility();
    }

    private static void OnDetailHeaderContentChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        ((DetailPaneHost)dependencyObject).UpdateDetailHeaderVisibility();
    }

    private static void OnDetailWidthChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        ((DetailPaneHost)dependencyObject).UpdateDetailVisibility();
    }

    private void UpdateDetailVisibility()
    {
        var hasDetail = DetailContent is not null;
        DetailBorder.Visibility = hasDetail ? Visibility.Visible : Visibility.Collapsed;
        DetailColumn.Width = hasDetail ? new GridLength(DetailWidth) : new GridLength(0);
    }

    private void UpdateDetailHeaderVisibility()
    {
        DetailHeaderPresenter.Visibility = DetailHeaderContent is null
            ? Visibility.Collapsed
            : Visibility.Visible;
    }
}