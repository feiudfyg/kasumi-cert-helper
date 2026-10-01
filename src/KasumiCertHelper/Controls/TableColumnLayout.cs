using System.ComponentModel;
using System.Globalization;

namespace KasumiCertHelper.Controls;

/// <summary>
/// Shared pixel widths for the columns of a table. One instance is handed to every row so a drag
/// on the header divider resizes the whole table at once. Widths are persisted as a comma
/// separated string so a layout survives restarts.
/// </summary>
public sealed class TableColumnLayout : INotifyPropertyChanged
{
    public const int MaxColumns = 5;

    private readonly double[] _widths = new double[MaxColumns];

    public TableColumnLayout(double minimum, params double[] widths)
    {
        Minimum = minimum;
        for (int i = 0; i < MaxColumns; i++)
        {
            _widths[i] = i < widths.Length ? widths[i] : 0;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public double Minimum { get; }

    public double this[int index]
    {
        get => index >= 0 && index < MaxColumns ? _widths[index] : 0;
        set
        {
            if (index < 0 || index >= MaxColumns)
            {
                return;
            }

            double clamped = Math.Max(Minimum, Math.Round(value));
            if (Math.Abs(_widths[index] - clamped) < 0.5)
            {
                return;
            }

            _widths[index] = clamped;
            Raise("W" + index.ToString(CultureInfo.InvariantCulture));
            Raise("Item[]");
        }
    }

    public double W0 => _widths[0];

    public double W1 => _widths[1];

    public double W2 => _widths[2];

    public double W3 => _widths[3];

    public double W4 => _widths[4];

    public double Total
    {
        get
        {
            double total = 0;
            foreach (double width in _widths)
            {
                total += width;
            }
            return total;
        }
    }

    /// <summary>
    /// Shrinks every column proportionally so a table never hides content behind a horizontal
    /// scrollbar when the pane becomes narrower than the widths the user picked.
    /// </summary>
    public bool FitTo(double available)
    {
        double sum = Total;
        if (sum <= 0 || available <= 0 || sum <= available + 0.5)
        {
            return false;
        }

        double factor = available / sum;
        bool changed = false;
        for (int i = 0; i < MaxColumns; i++)
        {
            if (_widths[i] <= 0)
            {
                continue;
            }

            double target = Math.Max(Minimum, Math.Round(_widths[i] * factor));
            if (Math.Abs(_widths[i] - target) >= 0.5)
            {
                _widths[i] = target;
                changed = true;
            }
        }

        if (changed)
        {
            for (int i = 0; i < MaxColumns; i++)
            {
                Raise("W" + i.ToString(CultureInfo.InvariantCulture));
            }
            Raise("Item[]");
        }

        return changed;
    }

    public string Serialize()
        => string.Join(',', _widths.Select(w => w.ToString("0", CultureInfo.InvariantCulture)));

    public void Deserialize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        string[] parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length && i < MaxColumns; i++)
        {
            if (double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) && parsed >= Minimum)
            {
                _widths[i] = parsed;
            }
        }

        for (int i = 0; i < MaxColumns; i++)
        {
            Raise("W" + i.ToString(CultureInfo.InvariantCulture));
        }
        Raise("Item[]");
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
