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
    public void AnLGUWithoutASealShowsNOSealCard()
    {
        // The behaviour itself: HasOwnSeal is false exactly when the seal path is the neutral placeholder, and the markup omits
        // the card on that basis.
        var page = (EEMOCantilanSDS.Client.Components.Pages.Login)Activator.CreateInstance(LoginPage)!;
        var sealPath = LoginPage.GetField("SealPath", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var hasOwnSeal = LoginPage.GetProperty("HasOwnSeal", BindingFlags.NonPublic | BindingFlags.Instance)!;

        // As Cantilan, or any LGU that HAS uploaded a seal.
        Assert.True((bool)hasOwnSeal.GetValue(page)!);

        sealPath.SetValue(page, "data:image/png;base64,AAAA");     // another LGU's uploaded seal
        Assert.True((bool)hasOwnSeal.GetValue(page)!);

        sealPath.SetValue(page, Constant("NeutralSealPath"));      // an LGU with none
        Assert.False((bool)hasOwnSeal.GetValue(page)!);
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
