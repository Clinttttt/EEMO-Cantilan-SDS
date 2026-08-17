using System.Reflection;

namespace EEMOCantilanSDS.ComponentTests;

/// <summary>
/// Whose seal appears on a municipality's sign-in page.
///
/// <para>
/// Reported from use, and confirmed against production before anything was changed: <c>/login?lgu=CARRASCAL</c> served
/// <c>/images/stalltrack-seal.png</c> with <c>alt="Carrascal seal"</c>. An LGU that has not uploaded a seal was given StallTrack's
/// own mark in the municipal seal slot — so the page showed the product's mark twice, one of them describing itself to a screen
/// reader as a seal belonging to the municipality. A vendor's mark does not stand in for a government seal, and an empty slot is
/// more honest than a borrowed one.
/// </para>
///
/// <para>
/// Cantilan's own seal and name remain the page's fallback, on the office's instruction (2026-08-16): it is the office this system
/// belongs to, and its values are the default rather than a stray hardcoded string. What is NOT acceptable is another LGU's page
/// presenting something that is not its own as if it were.
/// </para>
///
/// <para>
/// Asserted against the page's own declared paths rather than by rendering it. <c>Login.razor</c> resolves branding through an HTTP
/// client on initialise, and this rule is about which constant is treated as "not a real seal" — a fact the markup then acts on.
/// </para>
/// </summary>
public class LoginSealTests
{
    private static readonly Type LoginPage = typeof(EEMOCantilanSDS.Client.Components.Pages.Login);

    private static string Constant(string name) =>
        (string)LoginPage
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public)!
            .GetRawConstantValue()!;

    [Fact]
    public void TheNeutralPlaceholderIsNotAMunicipalSeal()
    {
        // The two paths must stay distinct, because telling them apart is exactly how the page knows whether it holds a real seal.
        // If they were ever made equal, every LGU would appear to have its own seal and the fault would return silently.
        Assert.NotEqual(Constant("NeutralSealPath"), Constant("DefaultSealPath"));
    }

    [Fact]
    public void AnLGUWithoutASealKeepsTheSlotButBorrowsNOBODYSMark()
    {
        // The office's instruction: the CARD stays, because onboarding is where an LGU uploads its seal and the slot is kept for
        // it to fill. What must not happen is filling it with StallTrack's mark under the municipality's name, which is what
        // production served for Carrascal. So the card renders either the LGU's own seal or a plainly empty slot — never a
        // borrowed one.
        var page = (EEMOCantilanSDS.Client.Components.Pages.Login)Activator.CreateInstance(LoginPage)!;
        var sealPath = LoginPage.GetField("SealPath", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var hasOwnSeal = LoginPage.GetProperty("HasOwnSeal", BindingFlags.NonPublic | BindingFlags.Instance)!;

        // As Cantilan, or any LGU that HAS uploaded a seal.
        Assert.True((bool)hasOwnSeal.GetValue(page)!);

        sealPath.SetValue(page, "data:image/png;base64,AAAA");     // another LGU's uploaded seal
        Assert.True((bool)hasOwnSeal.GetValue(page)!);

        sealPath.SetValue(page, Constant("NeutralSealPath"));      // an LGU with none — the waiting slot
        Assert.False((bool)hasOwnSeal.GetValue(page)!);
    }

    [Fact]
    public void TheMarkupNeverPutsSTALLTRACKSSealUnderAMunicipalitysName()
    {
        // Asserted against the markup, because that is where the fault was: an <img> whose src was the neutral placeholder and
        // whose alt named the municipality. The seal image may only ever be rendered when the LGU has one of its own, and the
        // waiting slot must be a placeholder rather than a picture.
        var markup = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "EEMOCantilanSDS.Client", "Components", "Pages", "Login.razor"));

        Assert.Contains("@if (HasOwnSeal)", markup);
        Assert.Contains("seal-placeholder", markup);

        // The constant itself stays — it is the sentinel that MEANS "this LGU has no seal", and the branding fallback assigns it.
        // What must never exist again is that path inside an <img>: that was the fault, an image of StallTrack's seal carrying an
        // alt attribute naming the municipality.
        var borrowed = markup
            .Split('\n')
            .Where(l => l.Contains("<img", StringComparison.OrdinalIgnoreCase)
                        && l.Contains("stalltrack-seal", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(borrowed);
    }

    [Fact]
    public void NoSCREENPutsStallTracksSealUnderAMunicipalitysName()
    {
        // Login was fixed first, and the same fallback turned out to be in four more places. Asserted across all of them, by
        // markup, because that is where the fault lives: an <img> whose src is the neutral sentinel and whose alt names the
        // municipality.
        //
        // AuthBrandPanel is the shared panel behind forgot-password, reset-password and verify-email, so one fix covered three
        // screens. AdminActivate was checked and renders only the StallTrack card, with no municipal seal to borrow.
        foreach (var page in new[]
                 {
                     Path.Combine("Components", "Pages", "Login.razor"),
                     Path.Combine("Components", "Pages", "AccountSetup.razor"),
                     Path.Combine("Components", "Pages", "Shared", "AuthBrandPanel.razor"),
                 })
        {
            var markup = File.ReadAllText(Path.Combine(RepositoryRoot(), "EEMOCantilanSDS.Client", page));

            Assert.Contains("HasOwnSeal", markup);
            Assert.Contains("seal-placeholder", markup);

            var borrowed = markup
                .Split('\n')
                .Where(l => l.Contains("<img", StringComparison.OrdinalIgnoreCase)
                            && l.Contains("stalltrack-seal", StringComparison.OrdinalIgnoreCase))
                .ToList();

            Assert.Empty(borrowed);
        }
    }

    [Fact]
    public void THEPRINTEDDocumentsDoNotCarryTheVendorsMarkEither()
    {
        // The worst of it, and the reason this went beyond the sign-in pages. BrandingState.SealPath is rendered at 31 places, most
        // of them PRINTED - official reports, the collection receipt, the stallholder list, a payor's history. An LGU with no seal
        // on file was issuing documents carrying StallTrack's emblem, labelled as its own seal.
        //
        // Fixed at the source rather than across 22 files: the fallback is now a waiting slot, so every one of those render sites is
        // correct without being touched.
        var branding = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "EEMOCantilanSDS.Client", "Services", "BrandingState.cs"));

        Assert.DoesNotContain("stalltrack-seal.png", branding);
        Assert.Contains("WaitingSealPath", branding);
        Assert.Contains("data:image/svg+xml;base64,", branding);

        // Cantilan's own seal is untouched — the office this system belongs to keeps its mark.
        Assert.Contains("LGU_CANTILAN_LOGO.jpg", branding);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EEMOCantilanSDS.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Fact]
    public void CantilansSealRemainsTheFallback()
    {
        // Recorded as a rule, not an accident. The office confirmed its own values are the intended default for this page.
        var page = (EEMOCantilanSDS.Client.Components.Pages.Login)Activator.CreateInstance(LoginPage)!;
        var sealPath = (string)LoginPage.GetField("SealPath", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page)!;

        Assert.Equal(Constant("DefaultSealPath"), sealPath);
    }
}
