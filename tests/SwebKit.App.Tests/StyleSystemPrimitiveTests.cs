using Bunit;
using Bunit.JSInterop;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using SwebKit.App.Components.Shared;

namespace SwebKit.App.Tests;

public sealed class StyleSystemPrimitiveTests : TestContext
{
    public StyleSystemPrimitiveTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void AppButton_RendersVariantSizeClassAndAttributes()
    {
        var cut = RenderComponent<AppButton>(parameters => parameters
            .Add(component => component.Variant, "Danger")
            .Add(component => component.Size, "Small")
            .Add(component => component.CssClass, "extra-action")
            .Add(component => component.Type, "submit")
            .Add(component => component.Title, "Delete collection")
            .Add(component => component.IconStart, Icon("!"))
            .Add(component => component.IconEnd, Icon("->"))
            .Add(component => component.ChildContent, Content("Delete"))
            .AddUnmatched("aria-label", "Delete selected collection"));

        var button = cut.Find("button");

        Assert.Contains("app-button", button.ClassName, StringComparison.Ordinal);
        Assert.Contains("app-button--danger", button.ClassName, StringComparison.Ordinal);
        Assert.Contains("app-button--small", button.ClassName, StringComparison.Ordinal);
        Assert.Contains("extra-action", button.ClassName, StringComparison.Ordinal);
        Assert.Equal("submit", button.GetAttribute("type"));
        Assert.Equal("Delete collection", button.GetAttribute("title"));
        Assert.Equal("Delete selected collection", button.GetAttribute("aria-label"));
        Assert.Contains("Delete", button.TextContent, StringComparison.Ordinal);
        Assert.Contains("!", button.TextContent, StringComparison.Ordinal);
        Assert.Contains("->", button.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void AppButton_DisabledAddsDisabledStateAndSuppressesClick()
    {
        var clickCount = 0;
        var cut = RenderComponent<AppButton>(parameters => parameters
            .Add(component => component.Disabled, true)
            .Add(component => component.OnClick, _ => clickCount++)
            .Add(component => component.ChildContent, Content("Save")));

        var button = cut.Find("button");
        button.Click();

        Assert.NotNull(button.GetAttribute("disabled"));
        Assert.Contains("app-button--disabled", button.ClassName, StringComparison.Ordinal);
        Assert.Equal(0, clickCount);
    }

    [Fact]
    public void AppButton_LoadingAddsBusyDisabledStateAndSuppressesClick()
    {
        var clickCount = 0;
        var cut = RenderComponent<AppButton>(parameters => parameters
            .Add(component => component.Loading, true)
            .Add(component => component.OnClick, _ => clickCount++)
            .Add(component => component.ChildContent, Content("Refresh")));

        var button = cut.Find("button");
        button.Click();

        Assert.NotNull(button.GetAttribute("disabled"));
        Assert.Equal("true", button.GetAttribute("aria-busy"));
        Assert.Contains("app-button--loading", button.ClassName, StringComparison.Ordinal);
        Assert.NotEmpty(cut.FindAll(".app-button__spinner"));
        Assert.Equal(0, clickCount);
    }

    [Fact]
    public void AppIconButton_RendersAccessibleLabelDefaultTitleAndClasses()
    {
        var cut = RenderComponent<AppIconButton>(parameters => parameters
            .Add(component => component.Label, "Refresh resources")
            .Add(component => component.Variant, "Primary")
            .Add(component => component.Size, "Small")
            .Add(component => component.CssClass, "toolbar-icon")
            .Add(component => component.Icon, Icon("R")));

        var button = cut.Find("button");

        Assert.Contains("app-icon-button", button.ClassName, StringComparison.Ordinal);
        Assert.Contains("app-icon-button--primary", button.ClassName, StringComparison.Ordinal);
        Assert.Contains("app-icon-button--small", button.ClassName, StringComparison.Ordinal);
        Assert.Contains("toolbar-icon", button.ClassName, StringComparison.Ordinal);
        Assert.Equal("Refresh resources", button.GetAttribute("aria-label"));
        Assert.Equal("Refresh resources", button.GetAttribute("title"));
        Assert.Contains("R", button.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void AppIconButton_UsesExplicitTitleWhenProvided()
    {
        var cut = RenderComponent<AppIconButton>(parameters => parameters
            .Add(component => component.Label, "Delete rule")
            .Add(component => component.Title, "Delete monitoring rule")
            .Add(component => component.Variant, "Danger")
            .Add(component => component.Icon, Icon("X")));

        var button = cut.Find("button");

        Assert.Equal("Delete rule", button.GetAttribute("aria-label"));
        Assert.Equal("Delete monitoring rule", button.GetAttribute("title"));
        Assert.Contains("app-icon-button--danger", button.ClassName, StringComparison.Ordinal);
    }

    [Fact]
    public void AppIconButton_RequiresAccessibleLabel()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => RenderComponent<AppIconButton>());

        Assert.Contains("requires a non-empty Label", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AppIconButton_LoadingAddsBusyDisabledStateAndSuppressesClick()
    {
        var clickCount = 0;
        var cut = RenderComponent<AppIconButton>(parameters => parameters
            .Add(component => component.Label, "Refresh resources")
            .Add(component => component.Loading, true)
            .Add(component => component.OnClick, _ => clickCount++)
            .Add(component => component.Icon, Icon("R")));

        var button = cut.Find("button");
        button.Click();

        Assert.NotNull(button.GetAttribute("disabled"));
        Assert.Equal("true", button.GetAttribute("aria-busy"));
        Assert.Contains("app-icon-button--loading", button.ClassName, StringComparison.Ordinal);
        Assert.NotEmpty(cut.FindAll(".app-icon-button__spinner"));
        Assert.Equal(0, clickCount);
    }

    [Fact]
    public void FormField_RendersLabelRequiredHintErrorAndChildContent()
    {
        var cut = RenderComponent<FormField>(parameters => parameters
            .Add(component => component.Id, "environment-name")
            .Add(component => component.Label, "Environment")
            .Add(component => component.Required, true)
            .Add(component => component.Hint, "Choose a saved environment.")
            .Add(component => component.Error, "Environment is required.")
            .Add(component => component.CssClass, "request-field")
            .Add(component => component.ChildContent, builder =>
            {
                builder.OpenElement(0, "input");
                builder.AddAttribute(1, "id", "environment-name");
                builder.AddAttribute(2, "value", "Production");
                builder.CloseElement();
            }));

        var field = cut.Find(".app-form-field");
        var label = cut.Find("label.app-form-field__label");
        var hint = cut.Find("#environment-name-hint");
        var error = cut.Find("#environment-name-error");

        Assert.Contains("request-field", field.ClassName, StringComparison.Ordinal);
        Assert.Contains("app-form-field--required", field.ClassName, StringComparison.Ordinal);
        Assert.Contains("app-form-field--invalid", field.ClassName, StringComparison.Ordinal);
        Assert.Equal("environment-name", label.GetAttribute("for"));
        Assert.Contains("Environment", label.TextContent, StringComparison.Ordinal);
        Assert.Equal("*", cut.Find(".app-form-field__required").TextContent);
        Assert.Equal("Choose a saved environment.", hint.TextContent);
        Assert.Equal("Environment is required.", error.TextContent);
        Assert.Equal("alert", error.GetAttribute("role"));
        Assert.Equal("polite", error.GetAttribute("aria-live"));
        Assert.Equal("Production", cut.Find("input").GetAttribute("value"));
    }

    [Fact]
    public void AppSelect_RendersSelectWithCanonicalClassesOptionsAndSelectedValue()
    {
        var cut = RenderComponent<AppSelect>(parameters => parameters
            .Add(component => component.Id, "workspace-area")
            .Add(component => component.Name, "area")
            .Add(component => component.Value, "api-client")
            .Add(component => component.CssClass, "area-picker")
            .Add(component => component.ChildContent, Options(("aks", "AKS"), ("api-client", "API Client"))));

        var select = cut.Find("select");
        var options = cut.FindAll("option");

        Assert.Contains("app-native-control", select.ClassName, StringComparison.Ordinal);
        Assert.Contains("app-native-select", select.ClassName, StringComparison.Ordinal);
        Assert.Contains("app-select", select.ClassName, StringComparison.Ordinal);
        Assert.Contains("area-picker", select.ClassName, StringComparison.Ordinal);
        Assert.Equal("workspace-area", select.GetAttribute("id"));
        Assert.Equal("area", select.GetAttribute("name"));
        Assert.Equal("api-client", select.GetAttribute("value"));
        Assert.Equal(2, options.Count);
        Assert.Equal("aks", options[0].GetAttribute("value"));
        Assert.Equal("AKS", options[0].TextContent);
        Assert.Equal("api-client", options[1].GetAttribute("value"));
        Assert.Equal("API Client", options[1].TextContent);
    }

    [Fact]
    public void AppSelect_InvokesValueChangedOnChange()
    {
        string? changedValue = null;
        var cut = RenderComponent<AppSelect>(parameters => parameters
            .Add(component => component.Value, "dev")
            .Add(component => component.ValueChanged, value => changedValue = value)
            .Add(component => component.ChildContent, Options(("dev", "Dev"), ("prod", "Production"))));

        cut.Find("select").Change("prod");

        Assert.Equal("prod", changedValue);
    }

    [Fact]
    public void AppSelect_WithErrorSetsAriaInvalidTrueAndRendersError()
    {
        var cut = RenderComponent<AppSelect>(parameters => parameters
            .Add(component => component.Id, "environment-select")
            .Add(component => component.Label, "Environment")
            .Add(component => component.Hint, "Used for request variables.")
            .Add(component => component.Error, "Select an environment.")
            .Add(component => component.Value, "")
            .Add(component => component.ChildContent, Options(("", "Choose environment"), ("prod", "Production"))));

        var select = cut.Find("select");
        var error = cut.Find("#environment-select-error");

        Assert.NotEmpty(cut.FindAll(".app-form-field"));
        Assert.Equal("true", select.GetAttribute("aria-invalid"));
        Assert.Equal("environment-select-hint environment-select-error", select.GetAttribute("aria-describedby"));
        Assert.Contains("app-select--invalid", select.ClassName, StringComparison.Ordinal);
        Assert.Equal("Select an environment.", error.TextContent);
        Assert.Equal("alert", error.GetAttribute("role"));
    }

    [Fact]
    public void AppDropdown_RendersTriggerMenuBackdropAlignmentRoleLabelAndClosesOnBackdropClick()
    {
        var closeCount = 0;
        var cut = RenderComponent<AppDropdown>(parameters => parameters
            .Add(component => component.IsOpen, true)
            .Add(component => component.Alignment, "Start")
            .Add(component => component.Width, "240px")
            .Add(component => component.CssClass, "toolbar-dropdown")
            .Add(component => component.MenuCssClass, "resource-menu")
            .Add(component => component.Label, "Resource actions")
            .Add(component => component.OnClose, () => closeCount++)
            .Add(component => component.TriggerContent, Content("Open actions"))
            .Add(component => component.ChildContent, Content("Refresh resources")));

        var dropdown = cut.Find(".app-dropdown");
        var trigger = cut.Find(".app-dropdown__trigger");
        var menu = cut.Find("[role='menu']");

        Assert.Contains("toolbar-dropdown", dropdown.ClassName, StringComparison.Ordinal);
        Assert.Contains("Open actions", trigger.TextContent, StringComparison.Ordinal);
        Assert.NotEmpty(cut.FindAll(".app-dropdown__backdrop"));
        Assert.Contains("app-dropdown__menu", menu.ClassName, StringComparison.Ordinal);
        Assert.Contains("app-dropdown__menu--start", menu.ClassName, StringComparison.Ordinal);
        Assert.Contains("resource-menu", menu.ClassName, StringComparison.Ordinal);
        Assert.Equal("Resource actions", menu.GetAttribute("aria-label"));
        Assert.Equal("width: 240px;", menu.GetAttribute("style"));
        Assert.Equal("-1", menu.GetAttribute("tabindex"));
        Assert.Contains("Refresh resources", menu.TextContent, StringComparison.Ordinal);

        cut.Find(".app-dropdown__backdrop").Click();

        Assert.Equal(1, closeCount);
    }

    [Fact]
    public void AppDropdown_CloseOnBackdropFalseDoesNotInvokeCloseOnBackdropClick()
    {
        var closeCount = 0;
        var cut = RenderComponent<AppDropdown>(parameters => parameters
            .Add(component => component.IsOpen, true)
            .Add(component => component.CloseOnBackdrop, false)
            .Add(component => component.OnClose, () => closeCount++)
            .Add(component => component.ChildContent, Content("Pinned menu")));

        cut.Find(".app-dropdown__backdrop").Click();

        Assert.Equal(0, closeCount);
    }

    [Fact]
    public void AppDropdown_EscapeKeyClosesMenu()
    {
        var closeCount = 0;
        var cut = RenderComponent<AppDropdown>(parameters => parameters
            .Add(component => component.IsOpen, true)
            .Add(component => component.OnClose, () => closeCount++)
            .Add(component => component.ChildContent, Content("Menu item")));

        cut.Find("[role='menu']").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.Equal(1, closeCount);
    }

    [Fact]
    public void AppDropdown_NonEscapeKeyDoesNotCloseMenu()
    {
        var closeCount = 0;
        var cut = RenderComponent<AppDropdown>(parameters => parameters
            .Add(component => component.IsOpen, true)
            .Add(component => component.OnClose, () => closeCount++)
            .Add(component => component.ChildContent, Content("Menu item")));

        cut.Find("[role='menu']").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        Assert.Equal(0, closeCount);
    }


    [Fact]
    public void StatusBadge_RendersVariantSizeDotTitleContentAndCustomClass()
    {
        var cut = RenderComponent<StatusBadge>(parameters => parameters
            .Add(component => component.Variant, "Production")
            .Add(component => component.Size, "Small")
            .Add(component => component.Dot, true)
            .Add(component => component.CssClass, "environment-chip")
            .Add(component => component.Title, "Production environment")
            .Add(component => component.ChildContent, Content("Production"))
            .AddUnmatched("data-testid", "status-badge"));

        var badge = cut.Find("[data-testid='status-badge']");
        var dot = cut.Find(".app-status-badge__dot");

        Assert.Equal("SPAN", badge.TagName);
        Assert.Contains("app-status-badge", badge.ClassName, StringComparison.Ordinal);
        Assert.Contains("app-status-badge--production", badge.ClassName, StringComparison.Ordinal);
        Assert.Contains("app-status-badge--small", badge.ClassName, StringComparison.Ordinal);
        Assert.Contains("app-status-badge--with-dot", badge.ClassName, StringComparison.Ordinal);
        Assert.Contains("environment-chip", badge.ClassName, StringComparison.Ordinal);
        Assert.Equal("Production environment", badge.GetAttribute("title"));
        Assert.Equal("true", dot.GetAttribute("aria-hidden"));
        Assert.Contains("Production", badge.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void SegmentedControl_RendersItemsActiveClassAriaPressedAndLabel()
    {
        var cut = RenderComponent<SegmentedControl>(parameters => parameters
            .Add(component => component.Items, new[] { "Body", "Headers", "Auth" })
            .Add(component => component.Value, "Headers")
            .Add(component => component.Label, "Request sections")
            .Add(component => component.Size, "Small")
            .Add(component => component.CssClass, "request-segments")
            .Add(component => component.ItemCssClass, "request-segment")
            .AddUnmatched("data-testid", "segments"));

        var group = cut.Find("[data-testid='segments']");
        var buttons = cut.FindAll("button");

        Assert.Equal("group", group.GetAttribute("role"));
        Assert.Equal("Request sections", group.GetAttribute("aria-label"));
        Assert.Contains("app-segmented-control", group.ClassName, StringComparison.Ordinal);
        Assert.Contains("app-segmented-control--small", group.ClassName, StringComparison.Ordinal);
        Assert.Contains("request-segments", group.ClassName, StringComparison.Ordinal);
        Assert.Equal(3, buttons.Count);
        Assert.Equal("button", buttons[0].GetAttribute("type"));
        Assert.Equal("false", buttons[0].GetAttribute("aria-pressed"));
        Assert.Equal("true", buttons[1].GetAttribute("aria-pressed"));
        Assert.Equal("false", buttons[2].GetAttribute("aria-pressed"));
        Assert.Contains("app-segmented-control__item", buttons[1].ClassName, StringComparison.Ordinal);
        Assert.Contains("app-segmented-control__item--small", buttons[1].ClassName, StringComparison.Ordinal);
        Assert.Contains("app-segmented-control__item--active", buttons[1].ClassName, StringComparison.Ordinal);
        Assert.Contains("request-segment", buttons[1].ClassName, StringComparison.Ordinal);
        Assert.Contains("Body", buttons[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("Headers", buttons[1].TextContent, StringComparison.Ordinal);
        Assert.Contains("Auth", buttons[2].TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void SegmentedControl_InvokesValueChangedForInactiveOption()
    {
        string? changedValue = null;
        var cut = RenderComponent<SegmentedControl>(parameters => parameters
            .Add(component => component.Items, new[] { "Body", "Headers" })
            .Add(component => component.Value, "Body")
            .Add(component => component.ValueChanged, value => changedValue = value));

        cut.FindAll("button")[1].Click();

        Assert.Equal("Headers", changedValue);
    }

    [Fact]
    public void SegmentedControl_DoesNotInvokeValueChangedForActiveOption()
    {
        var changeCount = 0;
        var cut = RenderComponent<SegmentedControl>(parameters => parameters
            .Add(component => component.Items, new[] { "Body", "Headers" })
            .Add(component => component.Value, "Body")
            .Add(component => component.ValueChanged, _ => changeCount++));

        cut.FindAll("button")[0].Click();

        Assert.Equal(0, changeCount);
    }

    [Fact]
    public void SegmentedControl_DisabledAddsDisabledStateAndSuppressesClick()
    {
        var changeCount = 0;
        var cut = RenderComponent<SegmentedControl>(parameters => parameters
            .Add(component => component.Items, new[] { "Body", "Headers" })
            .Add(component => component.Value, "Body")
            .Add(component => component.Disabled, true)
            .Add(component => component.ValueChanged, _ => changeCount++));

        var group = cut.Find(".app-segmented-control");
        var buttons = cut.FindAll("button");
        buttons[1].Click();

        Assert.Contains("app-segmented-control--disabled", group.ClassName, StringComparison.Ordinal);
        Assert.NotNull(buttons[0].GetAttribute("disabled"));
        Assert.NotNull(buttons[1].GetAttribute("disabled"));
        Assert.Contains("app-segmented-control__item--disabled", buttons[1].ClassName, StringComparison.Ordinal);
        Assert.Equal(0, changeCount);
    }

    [Fact]
    public void SegmentedControl_WithNoItemsRendersEmptyGroup()
    {
        var cut = RenderComponent<SegmentedControl>(parameters => parameters
            .Add(component => component.Items, Array.Empty<string>()));

        var group = cut.Find(".app-segmented-control");

        Assert.Equal("group", group.GetAttribute("role"));
        Assert.Empty(cut.FindAll("button"));
    }

    [Fact]
    public void PageToolbar_RendersDensityWrapCustomClassesAndAllSlots()
    {
        var cut = RenderComponent<PageToolbar>(parameters => parameters
            .Add(component => component.Density, "Comfortable")
            .Add(component => component.Wrap, true)
            .Add(component => component.CssClass, "api-toolbar")
            .Add(component => component.LeadingContent, Content("Leading tools"))
            .Add(component => component.CenterContent, Content("Center filters"))
            .Add(component => component.TrailingContent, Content("Trailing actions")));

        var toolbar = cut.Find(".page-toolbar");

        Assert.Contains("page-toolbar--density-comfortable", toolbar.ClassName, StringComparison.Ordinal);
        Assert.Contains("page-toolbar--wrap", toolbar.ClassName, StringComparison.Ordinal);
        Assert.Contains("api-toolbar", toolbar.ClassName, StringComparison.Ordinal);
        Assert.Equal("Leading tools", cut.Find(".page-toolbar-leading").TextContent);
        Assert.Equal("Center filters", cut.Find(".page-toolbar-center").TextContent);
        Assert.Equal("Trailing actions", cut.Find(".page-toolbar-trailing").TextContent);
    }

    private static RenderFragment Content(string text) => builder => builder.AddContent(0, text);

    private static RenderFragment Icon(string text) => builder => builder.AddContent(0, text);

    private static RenderFragment Options(params (string Value, string Text)[] options) => builder =>
    {
        for (var index = 0; index < options.Length; index++)
        {
            var option = options[index];
            builder.OpenRegion(0);
            builder.OpenElement(1, "option");
            builder.AddAttribute(2, "value", option.Value);
            builder.AddContent(3, option.Text);
            builder.CloseElement();
            builder.CloseRegion();
        }
    };
}