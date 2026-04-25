using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace SwebKit.WinUI.Controls.Shared;

public sealed partial class StateView : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title),
        typeof(string),
        typeof(StateView),
        new PropertyMetadata(string.Empty, OnTitleChanged));

    public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(
        nameof(Message),
        typeof(string),
        typeof(StateView),
        new PropertyMetadata(string.Empty, OnMessageChanged));

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph),
        typeof(string),
        typeof(StateView),
        new PropertyMetadata(string.Empty, OnGlyphChanged));

    public static readonly DependencyProperty BodyContentProperty = DependencyProperty.Register(
        nameof(BodyContent),
        typeof(object),
        typeof(StateView),
        new PropertyMetadata(null, OnBodyContentChanged));

    public static readonly DependencyProperty ActionContentProperty = DependencyProperty.Register(
        nameof(ActionContent),
        typeof(object),
        typeof(StateView),
        new PropertyMetadata(null, OnActionContentChanged));

    public StateView()
    {
        InitializeComponent();
        UpdateTitleVisibility();
        UpdateMessageVisibility();
        UpdateGlyphVisibility();
        UpdateBodyVisibility();
        UpdateActionVisibility();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public object? BodyContent
    {
        get => GetValue(BodyContentProperty);
        set => SetValue(BodyContentProperty, value);
    }

    public object? ActionContent
    {
        get => GetValue(ActionContentProperty);
        set => SetValue(ActionContentProperty, value);
    }

    private static void OnTitleChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        ((StateView)dependencyObject).UpdateTitleVisibility();
    }

    private static void OnMessageChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        ((StateView)dependencyObject).UpdateMessageVisibility();
    }

    private static void OnGlyphChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        ((StateView)dependencyObject).UpdateGlyphVisibility();
    }

    private static void OnBodyContentChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        ((StateView)dependencyObject).UpdateBodyVisibility();
    }

    private static void OnActionContentChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        ((StateView)dependencyObject).UpdateActionVisibility();
    }

    private void UpdateTitleVisibility()
    {
        TitleTextBlock.Visibility = string.IsNullOrWhiteSpace(Title)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void UpdateMessageVisibility()
    {
        MessageTextBlock.Visibility = string.IsNullOrWhiteSpace(Message)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void UpdateGlyphVisibility()
    {
        var isVisible = !string.IsNullOrWhiteSpace(Glyph);
        GlyphContainer.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        GlyphTextBlock.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateBodyVisibility()
    {
        BodyPresenter.Visibility = BodyContent is null
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void UpdateActionVisibility()
    {
        ActionPresenter.Visibility = ActionContent is null
            ? Visibility.Collapsed
            : Visibility.Visible;
    }
}