using Bunit;
using EEMOCantilanSDS.Mobile.Components.Shared;
using Microsoft.AspNetCore.Components.Web;

namespace EEMOCantilanSDS.Mobile.ComponentTests;

public sealed class MobileChoiceTests : TestContext
{
    private static readonly MobileChoice.Choice[] Options = [new("1", "January"), new("2", "February")];

    [Fact]
    public void Default_list_remains_in_page_flow_and_selection_closes_it()
    {
        string? selected = null;
        var view = RenderComponent<MobileChoice>(p => p.Add(x => x.Label, "Month").Add(x => x.Options, Options)
            .Add(x => x.ValueChanged, value => selected = value));
        Assert.Empty(view.FindAll(".mc-popover"));
        view.Find(".mc-trigger").Click();
        Assert.Single(view.FindAll(".mc-list"));
        view.FindAll(".mc-option")[1].Click();
        Assert.Equal("2", selected); Assert.Empty(view.FindAll(".mc-list"));
        Assert.Equal("false", view.Find(".mc-trigger").GetAttribute("aria-expanded"));
    }

    [Fact]
    public void Popover_is_opt_in_and_keeps_selection_toggle_and_escape_behavior()
    {
        string? selected = null; var openings = 0;
        var view = RenderComponent<MobileChoice>(p => p.Add(x => x.Label, "Month").Add(x => x.Options, Options)
            .Add(x => x.Popover, true).Add(x => x.Value, "1").Add(x => x.OnOpening, () => openings++)
            .Add(x => x.ValueChanged, value => selected = value));
        view.Find(".mc-trigger").Click();
        Assert.Equal(1, openings); Assert.Single(view.FindAll(".mc-popover.open .mc-list"));
        Assert.Equal("true", view.Find(".mc-option.selected").GetAttribute("aria-selected"));
        view.FindAll(".mc-option")[1].Click();
        Assert.Equal("2", selected); Assert.Empty(view.FindAll(".mc-list"));
        view.Find(".mc-trigger").Click(); view.Find(".mc-trigger").Click();
        Assert.Empty(view.FindAll(".mc-list"));
        view.Find(".mc-trigger").Click(); view.Find(".mc").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        Assert.Empty(view.FindAll(".mc-list"));
    }

    [Fact]
    public void Inline_callers_keep_their_visible_rows_even_if_popover_is_requested()
    {
        var view = RenderComponent<MobileChoice>(p => p.Add(x => x.Label, "Month").Add(x => x.Options, Options)
            .Add(x => x.Inline, true).Add(x => x.Popover, true));
        Assert.Single(view.FindAll(".mc-inline")); Assert.Empty(view.FindAll(".mc-popover"));
    }

    [Fact]
    public void Opening_a_period_selector_can_close_its_sibling()
    {
        var month = RenderComponent<MobileChoice>(p => p.Add(x => x.Label, "Month").Add(x => x.Options, Options).Add(x => x.Popover, true));
        var year = RenderComponent<MobileChoice>(p => p.Add(x => x.Label, "Year").Add(x => x.Options, Options).Add(x => x.Popover, true)
            .Add(x => x.OnOpening, month.Instance.Close));
        month.Find(".mc-trigger").Click(); year.Find(".mc-trigger").Click();
        Assert.Empty(month.FindAll(".mc-list")); Assert.Single(year.FindAll(".mc-list"));
    }
}
