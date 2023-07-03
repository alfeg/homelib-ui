public static class StringExtensions
{
    public static string ReplaceInvalidFileNameChars(this string input, char replaceCharacter = '-')
    {
        foreach (char c in Path.GetInvalidFileNameChars().Union(new []{ '\'', '"'}))
        {
            input = input.Replace(c, replaceCharacter);
        }

        return input;
    }
}