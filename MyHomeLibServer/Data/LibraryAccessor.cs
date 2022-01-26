using MyHomeLib.Library;

namespace MyHomeLibServer.Data;

public class LibraryAccessor
{
    public InpxLibrary Library { get; private set; }

    public void SetLibrary(InpxLibrary inpxLibrary)
    {
        this.Library = inpxLibrary;
    }
}