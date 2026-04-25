using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace SwebKit.WinUI.Controls.Shared;

public sealed partial class SectionCard : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title),
        typeof(string),
        typeof(SectionCard),
        new PropertyMetadata(string.Empty, OnTitleChanged));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description),
        typeof(string),
        typeof(SectionCard),
        new PropertyMetadata(string.Empty, OnDescriptionChanged));

    public static readonly DependencyProperty HeaderContentProperty = DependencyProperty.Register(
        nameof(HeaderContent),
        typeof(object),
        typeof(SectionCard),
        new PropertyMetadata(null, OnHeaderContentChanged));

    public static readonly DependencyProperty BodyContentProperty = DependencyProperty.Register(
        nameof(BodyContent),
        typeof(object),
        typeof(SectionCard),
        new PropertyMetadata(null));

    public SectionCard()
    {
        InitializeComponent();
        UpdateTitleVisibility();
        UpdateDescriptionVisibility();
        UpdateHeaderVisibility();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
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

    private static void OnTitleChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        ((SectionCard)dependencyObject).UpdateTitleVisibility();
    }

    private static void OnDescriptionChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        ((SectionCard)dependencyObject).UpdateDescriptionVisibility();
    }

    private static void OnHeaderContentChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        ((SectionCard)dependencyObject).UpdateHeaderVisibility();
    }

    private void UpdateTitleVisibility()
    {
        TitleTextBlock.Visibility = string.IsNullOrWhiteSpace(Title)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void UpdateDescriptionVisibility()
    {
        DescriptionTextBlock.Visibility = string.IsNullOrWhiteSpace(Description)
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