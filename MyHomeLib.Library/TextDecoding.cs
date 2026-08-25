using System.Text;

namespace MyHomeLib.Library;

/// <summary>
/// INPX entries carry no encoding declaration. Flibusta ships UTF-8, older
/// MyHomeLib collections ship CP1251. Encoding is uniform within an entry, so
/// a strict UTF-8 decode either succeeds outright or proves the entry is CP1251.
/// </summary>
public static class TextDecoding
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static Encoding? _cp1251;

    private static Encoding Cp1251
    {
        get
        {
            if (_cp1251 is not null) return _cp1251;

            // Program.cs registers CodePagesEncodingProvider; register defensively so
            // the library also works from a console host or a test runner.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            _cp1251 = Encoding.GetEncoding(1251);
            return _cp1251;
        }
    }

    /// <summary>Decodes as UTF-8, falling back to CP1251 when the bytes are not valid UTF-8.</summary>
    public static string Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty) return string.Empty;

        string text;
        try
        {
            text = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            text = Cp1251.GetString(bytes);
        }

        // Real collections ship collection.info with a BOM; it would otherwise survive
        // into the collection title.
        return text.Length > 0 && text[0] == '﻿' ? text[1..] : text;
    }
}
