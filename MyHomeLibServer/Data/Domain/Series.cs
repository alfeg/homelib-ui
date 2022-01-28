namespace MyHomeLibServer.Data.Domain;

public class Series
{
    public int Id { get; set; }
    public string Name { get; set; }
    public HashSet<Author> Authors { get; set; }
}