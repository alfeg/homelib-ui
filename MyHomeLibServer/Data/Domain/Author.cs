namespace MyHomeLibServer.Data.Domain;

public class Author
{
    public int Id { get; set; }
    public string FirstName { get; set; }
    public string? MiddleName { get; set; }
    public string? LastName { get; set; }

    public List<Book> Books { get; set; }  = new();
    public HashSet<Series> Series { get; set; }  = new();

    public override string ToString()
    {
        return $"{FirstName} {MiddleName} {LastName}";
    }
}