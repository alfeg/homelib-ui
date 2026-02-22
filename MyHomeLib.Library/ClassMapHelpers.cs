using CsvHelper.Configuration;

namespace MyHomeLib.Library;

public static class ClassMapHelpers
{
    public static MemberMap<T, string[]> AsArray<T>(this MemberMap<T, string[]> map, int index)
    {
        return map.Index(index)
            .Convert(row => (row.Row
                .GetField(index) ?? string.Empty).Split(':', StringSplitOptions.RemoveEmptyEntries));
    }
}
