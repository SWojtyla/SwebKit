using Microsoft.AspNetCore.Components.WebView.Maui;

namespace SwebKit.App;

public partial class MainPage : ContentPage
{
	public MainPage()
	{
		InitializeComponent();
		blazorWebView.RootComponents.Add(new RootComponent
		{
			Selector = "#app",
			ComponentType = typeof(Components.Routes)
		});
	}
}
