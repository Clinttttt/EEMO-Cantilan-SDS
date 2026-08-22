using EEMOCantilanSDS.Application.Queries.Municipalities.GetMunicipalitySeal;

namespace EEMOCantilanSDS.Testing.Application.Municipalities;

/// <summary>
/// Reading a stored seal into an image that can be served.
///
/// <para>
/// A seal is recorded on the municipality's own row as a base64 data URI, which is why every branding response used to
/// carry the whole image and why no caller could keep it: the portal leaves the seal out of the state it carries from a
/// prerendered page into the interactive one, because a large one can exceed the circuit's message limit and drop the
/// connection. Serving it as an ordinary image, with branding returning its address, is what removes that.
/// </para>
///
/// <para>
/// The value is supplied by an office and ends up in a Content-Type header, so what it is allowed to be matters. These
/// tests hold that line as much as they hold the decoding.
/// </para>
/// </summary>
public class MunicipalitySealDecodingTests
{
    // A one-pixel PNG, which is a real image and short enough to read here.
    private const string PngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg==";

    [Fact]
    public void AStoredSealIsReadIntoBytesAndItsType()
    {
        var seal = SealDataUri.Decode($"data:image/png;base64,{PngBase64}");

        Assert.NotNull(seal);
        Assert.Equal("image/png", seal!.ContentType);
        Assert.Equal(Convert.FromBase64String(PngBase64), seal.Content);
    }

    [Fact]
    public void TheSealsOwnBytesDecideItsETag()
    {
        // So a re-uploaded seal is a different address and a browser holding the old one asks again, while an unchanged
        // seal is never fetched twice. Two reads of the same seal must agree, and a different seal must not.
        var first = SealDataUri.Decode($"data:image/png;base64,{PngBase64}");
        var same = SealDataUri.Decode($"data:image/png;base64,{PngBase64}");
        var other = SealDataUri.Decode(
            "data:image/png;base64," + Convert.ToBase64String(new byte[] { 1, 2, 3, 4 }));

        Assert.Equal(first!.ETag, same!.ETag);
        Assert.NotEqual(first.ETag, other!.ETag);
        Assert.StartsWith("\"", first.ETag);
    }

    [Theory]
    [InlineData("/images/LGU_CANTILAN_LOGO.jpg")]        // a file the web host already serves
    [InlineData("https://example.gov.ph/seal.png")]      // an address, not an image
    [InlineData("")]
    [InlineData("data:image/png;base64,")]               // nothing to decode
    [InlineData("data:;base64,AAAA")]                    // no type stated
    [InlineData("data:image/png,notbase64")]             // not base64 at all
    [InlineData("data:image/png;base64,%%%not-base64%%%")]
    public void AnythingThatIsNotAnEmbeddedImageIsRefused(string stored)
    {
        Assert.Null(SealDataUri.Decode(stored));
    }

    [Theory]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=")]                    // not an image
    [InlineData("data:application/octet-stream;base64,AAAA")]
    [InlineData("data:image/png\r\nX-Injected: 1;base64,AAAA")]           // header injection through the type
    [InlineData("data:image/png; charset=utf-8;base64,AAAA")]
    public void ATypeThatIsNotPlainlyAnImageIsRefused(string stored)
    {
        // The stored value is office-supplied and this string becomes a response header. Only image/... and nothing
        // that could carry a second header or a parameter.
        Assert.Null(SealDataUri.Decode(stored));
    }
}
