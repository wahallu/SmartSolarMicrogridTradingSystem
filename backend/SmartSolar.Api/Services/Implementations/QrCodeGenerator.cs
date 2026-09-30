/* =============================================================================
 * File        : QrCodeGenerator.cs
 * Project     : Smart Solar Microgrid Trading System - Central Web API
 * Module      : Services / Implementations
 * Description : Produces the two pieces of QR material a reservation needs once
 *               it is approved: the opaque token stored on the document, and the
 *               PNG image the prosumer's phone displays at the microgrid node.
 *               The renderer is PngByteQRCode, which is pure managed code, so
 *               the API runs unchanged on Windows, Linux and macOS containers.
 *               Rule R9 (token exists, Approved, inside the scan window) is
 *               enforced by ReservationService - this class only mints and draws.
 * Author      : Smart Solar Microgrid Team - SE4040 Enterprise Application Dev
 * Created     : 2026-09-04
 * References  : QRCoder PngByteQRCode renderer -
 *               https://github.com/codebude/QRCoder/wiki/Advanced-usage---QR-Code-renderers
 * =============================================================================
 */

using System.Security.Cryptography;
using QRCoder;
using SmartSolar.Api.Services.Interfaces;

namespace SmartSolar.Api.Services.Implementations;

/// <summary>
/// QRCoder backed implementation of <see cref="IQrCodeGenerator"/>. Registered as
/// a singleton because it holds no per request state.
/// </summary>
public class QrCodeGenerator : IQrCodeGenerator
{
    /// <summary>Smallest module size accepted; anything lower renders unscannable.</summary>
    private const int MinPixelsPerModule = 1;

    /// <summary>Largest module size accepted, so one request cannot allocate a huge bitmap.</summary>
    private const int MaxPixelsPerModule = 40;

    /// <summary>Number of random bytes behind a reservation token.</summary>
    private const int TokenSizeBytes = 32;

    /// <summary>
    /// Encodes the supplied payload as a QR image and returns the PNG bytes in
    /// their own prefix when they need one.
    /// </summary>
    /// <param name="payload">Exact string to encode, normally the reservation qrPayload JSON.</param>
    /// <param name="pixelsPerModule">Edge length in pixels of one QR module; clamped to 1..40.</param>
    /// <returns>Base64 encoded PNG image data.</returns>
    /// <exception cref="ArgumentException">Thrown when the payload is null, empty or blank.</exception>
    public string GeneratePngBase64(string payload, int pixelsPerModule = 10)
    {
        // Guard first: an empty payload would produce a QR image that scans to nothing.
        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new ArgumentException("A QR payload is required.", nameof(payload));
        }

        // Clamp the module size so a caller cannot ask for an unreadable or oversized image.
        var scale = pixelsPerModule < MinPixelsPerModule
            ? MinPixelsPerModule
            : pixelsPerModule > MaxPixelsPerModule
                ? MaxPixelsPerModule
                : pixelsPerModule;

        // ECC level Q recovers roughly 25 percent of the image, which matters because the
        // code is scanned off a phone screen outdoors, often with glare on the glass.
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);

        // PngByteQRCode is the cross platform renderer. System.Drawing is deliberately not
        // used: it is Windows only and unsupported on .NET 8 elsewhere.
        var png = new PngByteQRCode(data);
        return Convert.ToBase64String(png.GetGraphic(scale));
    }

    /// <summary>
    /// Mints the opaque token stored on an approved reservation and echoed back by
    /// the grid operator's scanner during verification.
    /// </summary>
    /// <returns>A 32 byte cryptographically random value in URL safe base64.</returns>
    public string CreateToken()
    {
        // 32 bytes from the OS CSPRNG - a token must never be guessable, because possessing
        // it is what proves a booking was approved when the operator scans it.
        var bytes = RandomNumberGenerator.GetBytes(TokenSizeBytes);

        // URL safe base64 matters because this token travels in two hostile places: inside the
        // QR payload JSON, and as a value the clients may put in a URL or query string. Plain
        // base64 uses '+', '/' and '=', which a URL encoder would rewrite - the scanned token
        // would then no longer match the stored one and R9 verification would fail.
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
