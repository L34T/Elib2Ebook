using System;

namespace Core.Types.Book;

public class Author
{
    public Author(string name, Uri url = null)
    {
        Name = name;
        Url = url;
    }

    /// <summary>
    ///     Имя автора
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    ///     Url профиля автора
    /// </summary>
    public Uri Url { get; set; }
}
