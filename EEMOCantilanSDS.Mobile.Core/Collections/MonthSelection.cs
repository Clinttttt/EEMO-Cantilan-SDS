namespace EEMOCantilanSDS.Mobile.Collections;

/// <summary>
/// The months a collector has explicitly chosen on a space account. One month is the ordinary single item (its amount can be a part payment);
/// several become one item each. The collector decides which months — a month at a time, or a whole year by its heading — and nothing here
/// spreads a payment across months for them: how a payment is allocated across obligations is the office's decision, not the app's.
/// </summary>
/// <remarks>
/// Keys are the server's selection keys ("OPERATION|account|yyyy-MM-dd"), so an ordinal sort is the account's own chronological order and a key
/// can never be in the selection twice.
/// </remarks>
public sealed class MonthSelection
{
    private readonly HashSet<string> _keys = new(StringComparer.Ordinal);

    public IReadOnlySet<string> Keys => _keys;
    public int Count => _keys.Count;
    public bool Contains(string key) => _keys.Contains(key);

    /// <summary>Exactly one month: the single-item flow, with an amount the collector can lower.</summary>
    public string? Single => _keys.Count == 1 ? _keys.First() : null;

    /// <summary>Two or more months: one item each, every one at that month's own balance.</summary>
    public bool IsMulti => _keys.Count >= 2;

    /// <summary>Selects or clears one month. True when it is now selected.</summary>
    public bool Toggle(string key) => _keys.Remove(key) ? false : _keys.Add(key);

    /// <summary>A year heading: clears the whole set of months when every one is already chosen, otherwise chooses all of them.</summary>
    public void ToggleMany(IReadOnlyList<string> keys)
    {
        if (keys.Count == 0) return;
        if (keys.All(_keys.Contains)) foreach (var key in keys) _keys.Remove(key);
        else foreach (var key in keys) _keys.Add(key);
    }

    /// <summary>The chosen months oldest first.</summary>
    public IReadOnlyList<string> Ordered() => _keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

    /// <summary>The sum of the chosen months' own balances, as stated by <paramref name="balanceOf"/> (the server's amount); a month it no longer offers counts as nothing.</summary>
    public decimal Total(Func<string, decimal?> balanceOf) => _keys.Sum(k => balanceOf(k) ?? 0m);

    public void Clear() => _keys.Clear();

    /// <summary>Makes <paramref name="key"/> the only chosen month (opening an existing item to edit it), or empties the selection.</summary>
    public void Reset(string? key = null)
    {
        _keys.Clear();
        if (key is not null) _keys.Add(key);
    }
}
