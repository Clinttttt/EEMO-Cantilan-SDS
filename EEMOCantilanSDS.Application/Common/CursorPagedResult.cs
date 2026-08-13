namespace EEMOCantilanSDS.Application.Common;

/// <summary>
/// One page of a cursor-paged read, with the cursor to ask for the next.
///
/// <para>
/// An application and API contract rather than a domain concept: a stall register does not know it is being read a page at a
/// time. It sat in Domain beside <see cref="Result{T}"/> and moved with it.
/// </para>
/// </summary>
public class CursorPagedResult<T>
{
    public List<T> Items { get; set; } = new();

    /// <summary>Pass back to ask for the page after this one; null when there is none.</summary>
    public DateTime? NextCursor { get; set; }

    public bool HasMore { get; set; }
}
