using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace SwebKit.WinUI.Controls.Shared;

public sealed partial class MetricCard : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title),
        typeof(string),
        typeof(MetricCard),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph),
        typeof(string),
        typeof(MetricCard),
        new PropertyMetadata(string.Empty, OnGlyphChanged));

    public static readonly DependencyProperty ValueTextProperty = DependencyProperty.Register(
        nameof(ValueText),
        typeof(string),
        typeof(MetricCard),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ValueLabelProperty = DependencyProperty.Register(
        nameof(ValueLabel),
        typeof(string),
        typeof(MetricCard),
        new PropertyMetadata(string.Empty, OnValueLabelChanged));

    public static readonly DependencyProperty DetailTextProperty = DependencyProperty.Register(
        nameof(DetailText),
        typeof(string),
        typeof(MetricCard),
        new PropertyMetadata(string.Empty, OnDetailTextChanged));

    public static readonly DependencyProperty TimestampTextProperty = DependencyProperty.Register(
        nameof(TimestampText),
        typeof(string),
        typeof(MetricCard),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TimestampVisibilityProperty = DependencyProperty.Register(
        nameof(TimestampVisibility),
        typeof(Visibility),
        typeof(MetricCard),
        new PropertyMetadata(Visibility.Collapsed));

    public static readonly DependencyProperty FooterContentProperty = DependencyProperty.Register(
        nameof(FooterContent),
        typeof(object),
        typeof(MetricCard),
        new PropertyMetadata(null, OnFooterContentChanged));

    public MetricCard()
    {
        InitializeComponent();
        UpdateGlyphVisibility();
        UpdateValueLabelVisibility();
        UpdateDetailVisibility();
        UpdateFooterVisibility();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public string ValueText
    {
        get => (string)GetValue(ValueTextProperty);
        set => SetValue(ValueTextProperty, value);
    }

    public string ValueLabel
    {
        get => (string)GetValue(ValueLabelProperty);
        set => SetValue(ValueLabelProperty, value);
    }

    public string DetailText
    {
        get => (string)GetValue(DetailTextProperty);
        set => SetValue(DetailTextProperty, value);
    }

    public string TimestampText
    {
        get => (string)GetValue(TimestampTextProperty);
        set => SetValue(TimestampTextProperty, value);
    }

    public Visibility TimestampVisibility
    {
        get => (Visibility)GetValue(TimestampVisibilityProperty);
        set => SetValue(TimestampVisibilityProperty, value);
    }

    public object? FooterContent
    {
        get => GetValue(FooterContentProperty);
        set => SetValue(FooterContentProperty, value);
    }

    private static void OnGlyphChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        ((MetricCard)dependencyObject).UpdateGlyphVisibility();
    }

    private static void OnValueLabelChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        ((MetricCard)dependencyObject).UpdateValueLabelVisibility();
    }

    private static void OnDetailTextChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        ((MetricCard)dependencyObject).UpdateDetailVisibility();
    }

    private static void OnFooterContentChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        ((MetricCard)dependencyObject).UpdateFooterVisibility();
    }

    private void UpdateGlyphVisibility()
    {
        GlyphTextBlock.Visibility = string.IsNullOrWhiteSpace(Glyph)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void UpdateValueLabelVisibility()
    {
        ValueLabelTextBlock.Visibility = string.IsNullOrWhiteSpace(ValueLabel)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void UpdateDetailVisibility()
    {
        DetailTextBlock.Visibility = string.IsNullOrWhiteSpace(DetailText)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void UpdateFooterVisibility()
    {
        FooterPresenter.Visibility = FooterContent is null
            ? Visibility.Collapsed
            : Visibility.Visible;
    }
}