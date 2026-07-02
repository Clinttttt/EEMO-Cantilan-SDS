# Blazor Admin Navigation Rendering Guidance for Opus 4.8

> Reference only. Do not trust this GPT/Codex note blindly.
> Before implementing anything, examine the current EEMO codebase, actual Blazor lifecycle behavior, affected pages, API clients, and recent commits yourself. If the verified code shows a safer or more specific solution, prefer the real code context over this note.

## Purpose

This document captures a navigation/perceived-performance issue observed in the EEMO web admin Blazor app:

```text
User clicks a sidebar/page link
URL changes immediately
Visible page content changes late
```

This is different from a link not working. The router has already accepted the navigation, but the target page feels delayed because the new page may be doing slow work before the user sees a useful shell/loading state.

The goal is to make navigation feel immediate:

```text
Click link
  -> URL updates
  -> new page header/skeleton appears immediately
  -> data loads asynchronously
  -> real content replaces skeleton
```

This guidance complements, but does not replace, the API-side `IMemoryCache` plan.

## Important distinction

Backend caching improves actual API response time.

Page rendering improvements improve perceived navigation speed.

Both are useful, but they solve different parts of the experience:

| Problem | Better tool |
|---|---|
| API aggregation is slow | API/query-handler caching |
| URL changes but old page visually stays too long | render page shell/skeleton earlier |
| page loads huge report/table before painting useful state | defer heavy work and render loading state |
| first admin load after login has auth/JWT delay | auth-ready handling and lightweight retry |

Do not assume caching alone will fix the navigation feeling if the page still waits too long before painting its own loading UI.

## Current codebase observations

Current app shape observed:

- Blazor Server interactive rendering is used with prerender disabled in routes/pages.
- Many admin pages load data in `OnInitializedAsync`.
- Many pages already have `Loading`, `_loading`, skeleton, or empty-state UI.
- Some pages perform retry loops for transient auth readiness after login.
- Heavy pages include reports, dashboard, facility views, follow-up queue, month-end report, export/report pages, audit trail, and settings.

Examples of pages that should be reviewed carefully:

- `EEMOCantilanSDS.Client\Components\Pages\Menus\Menu.razor`
- `EEMOCantilanSDS.Client\Components\Pages\Menus\Report.razor`
- `EEMOCantilanSDS.Client\Components\Pages\Reports\MonthEndReport.razor`
- `EEMOCantilanSDS.Client\Components\Pages\Reports\FollowUpQueue.razor`
- `EEMOCantilanSDS.Client\Components\Pages\Reports\PastFollowUpQueue.razor`
- `EEMOCantilanSDS.Client\Components\Pages\Menus\Facilities\NPM.razor`
- `EEMOCantilanSDS.Client\Components\Pages\Menus\Facilities\TCC.razor`
- `EEMOCantilanSDS.Client\Components\Pages\Menus\Facilities\SH.razor`
- `EEMOCantilanSDS.Client\Components\Pages\Menus\Facilities\TRM.razor`
- `EEMOCantilanSDS.Client\Components\Pages\Menus\Facilities\TPM.razor`
- `EEMOCantilanSDS.Client\Components\Pages\Menus\AuditTrail.razor`
- `EEMOCantilanSDS.Client\Components\Pages\Menus\Settings.razor`

Do not edit all pages blindly. Profile or inspect the slowest routes first.

## What likely causes the delayed visual navigation

If the URL changes first but content appears late, possible causes include:

1. Target page does heavy work in `OnInitializedAsync` before the first useful render.
2. Target page starts multiple API calls and waits before the skeleton is painted.
3. The page renders a large table/report immediately after data arrives.
4. Auth/setup checks or retry loops delay visible state.
5. Components under the page run additional `OnParametersSetAsync` loads before the page feels ready.
6. Blazor Server render batches are delayed because the first render is too large.

The presence of `OnInitializedAsync` is not automatically wrong. The issue is when the target page does not visibly switch to its own shell/loading state quickly.

## Avoid a blanket `OnAfterRenderAsync` rewrite

Some advice suggests moving API calls from `OnInitializedAsync` to `OnAfterRenderAsync`.

That can help in some cases, but it should not be applied blindly.

`OnAfterRenderAsync` is usually best for:

- JavaScript interop after DOM exists
- browser measurement
- focus/scroll behavior
- starting work that truly depends on the rendered DOM

For normal API loading, a safer first approach is:

- keep data loading structured in `LoadAsync`
- set loading state immediately
- yield or force a render before slow work
- then fetch data

Avoid creating duplicate requests, flicker, or confusing lifecycle flow.

## Recommended page-load pattern

For heavy routed pages, prefer a pattern where the page shell and skeleton can appear before the expensive API work completes.

Recommended style:

```csharp
private bool Loading = true;
private SomeDto? Data;

protected override async Task OnInitializedAsync()
{
    Loading = true;

    // Give Blazor a chance to paint the new route shell/loading state.
    await Task.Yield();

    await LoadAsync();
}

private async Task LoadAsync()
{
    try
    {
        var result = await Api.GetSomethingAsync();
        if (result.IsSuccess)
            Data = result.Value;
    }
    finally
    {
        Loading = false;
    }
}
```

For reloads triggered by filter/month/facility changes:

```csharp
private async Task LoadAsync()
{
    Loading = true;
    Data = null;

    // Paint the skeleton/empty shell before the API call.
    await InvokeAsync(StateHasChanged);

    var result = await Api.GetSomethingAsync();

    if (result.IsSuccess)
        Data = result.Value;

    Loading = false;
}
```

Use the actual project naming/style, but preserve the idea:

```text
set loading
paint shell/skeleton
do slow work
replace skeleton with real content
```

## Recommended route shell behavior

Each heavy page should show these immediately:

- topbar/header
- breadcrumb/current page label
- main action controls if safe
- skeleton cards/tables or a professional loading panel

Avoid making the user stare at the previous page while the new page fetches data.

Good perceived flow:

```text
Reports clicked
  -> Financial Reports header appears
  -> report filter shell appears
  -> loading cards/table skeleton appears
  -> API result fills the report
```

Bad perceived flow:

```text
Reports clicked
  -> URL changes
  -> old dashboard remains visually present
  -> after delay, full reports page suddenly appears
```

## Page-level candidates for first pass

Recommended first review order:

1. Dashboard/menu page
2. Financial Reports page
3. Month-End Report page
4. Facility pages, especially NPM/TCC
5. Follow-up Queue and Follow-up History
6. Audit Trail / Transactions / Settings

Start with the pages the user actually feels are delayed.

## What to inspect per page

For each candidate page, check:

- Does markup render a header/shell even while `Loading == true`?
- Is `Loading` set before API calls?
- Is the page awaiting API calls before its first visible skeleton?
- Are multiple sequential API calls blocking the first useful render?
- Can independent API calls run in parallel with `Task.WhenAll`?
- Is a huge table rendered all at once?
- Are child components doing their own slow `OnParametersSetAsync` loads?
- Are retry loops delaying the page without visual feedback?
- Are there route-level auth/setup checks running repeatedly?

## What not to do

Do not:

- move every API call to `OnAfterRenderAsync` blindly
- remove loading/skeleton states
- hide old content while new content has no placeholder
- introduce duplicated API calls
- cache failed/empty responses just to make navigation look fast
- solve perceived navigation only with backend caching
- refactor financial calculations while working on navigation rendering

## Relationship with `IMemoryCache`

The best user experience likely comes from both:

1. **Frontend/page rendering improvement**
   - show page shell/skeleton immediately
   - avoid blocking first useful render

2. **Backend/query-handler caching**
   - cache expensive dashboard/report DTOs
   - reduce aggregation time
   - invalidate after successful writes

Suggested order:

1. Add or fix immediate skeleton rendering on the slowest pages.
2. Implement small API-side `IMemoryCache` for dashboard/sidebar/financial report.
3. Re-test navigation feel.
4. Only then consider deeper page-specific optimization.

## Acceptance criteria

A navigation/rendering fix is acceptable if:

- URL and visible page title/header update almost immediately.
- Slow pages show a professional loading state instead of appearing stuck.
- No duplicate API calls are introduced.
- No financial/report data changes unintentionally.
- Auth-protected pages still redirect correctly.
- Existing loading/error/empty states still work.
- Heavy reports still print/export correctly.
- The solution is applied surgically to the pages that need it, not blindly across the whole app.

## Short instruction for Opus

Before implementing, verify the actual slow route(s). Do not trust this document blindly.

If the URL changes but visual content lags, first ensure the target page renders its shell/loading state before doing expensive API/data/render work.

Use `Task.Yield()` or `InvokeAsync(StateHasChanged)` carefully where it improves first paint, but do not convert the whole app to `OnAfterRenderAsync` without evidence.

Treat this as perceived-navigation optimization, separate from API caching.
