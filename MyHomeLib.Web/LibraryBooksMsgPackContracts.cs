using MessagePack;
using MyHomeLib.Library;

namespace MyHomeLib.Web;

[MessagePackObject]
public sealed record LibraryBooksMsgPackResponse(
    [property: Key("h")] string Hash,
    [property: Key("m")] LibraryBooksMsgPackMetadata Metadata,
    [property: Key("b")] IReadOnlyList<LibraryBooksMsgPackBookItem> Books);

[MessagePackObject]
public sealed record LibraryBooksMsgPackMetadata(
    [property: Key("d")] string? Description,
    [property: Key("v")] string? Version,
    [property: Key("t")] int TotalBooks);

[MessagePackObject]
public sealed record LibraryBooksMsgPackBookItem(
    [property: Key("i")] int Id,
    [property: Key("t")] string? Title,
    [property: Key("a")] string? Authors,
    [property: Key("s")] string? Series,
    [property: Key("n")] string? SeriesNo,
    [property: Key("l")] string? Lang,
    [property: Key("f")] string? File,
    [property: Key("e")] string? Ext,
    [property: Key("r")] string? ArchiveFile);

public static class LibraryBooksMsgPackMapper
{
    public static LibraryBooksMsgPackResponse ToMsgPack(this LibraryBooksResponse source)
    {
        var books = source.Books
            .Select(book => new LibraryBooksMsgPackBookItem(
                book.Id,
                book.Title,
                NormalizeAuthors(book.Authors),
                book.Series,
                book.SeriesNo,
                book.Lang,
                book.File,
                book.Ext,
                book.ArchiveFile))
            .ToList();

        return new LibraryBooksMsgPackResponse(
            source.Hash,
            new LibraryBooksMsgPackMetadata(
                source.Metadata.Description,
                source.Metadata.Version,
                source.Metadata.TotalBooks),
            books);
    }

    private static string? NormalizeAuthors(string? rawAuthors)
    {
        var parsed = new BookItem { Authors = rawAuthors }.ParsedAuthors();
        return parsed.Count == 0 ? null : string.Join(", ", parsed);
    }
}
