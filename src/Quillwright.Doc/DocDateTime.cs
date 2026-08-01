namespace Quillwright.Doc;

/// <summary>
/// The four-byte date and time of the binary format (<c>DTTM</c>, [MS-DOC] 2.9.75).
/// </summary>
/// <remarks>
/// Everything is packed into thirty-two bits, which costs the seconds — the field holds
/// minutes and no finer — and limits the year to a nine-bit offset from 1900. There is no time
/// zone: what is stored is the wall clock the author saw, which is also what Word writes into
/// the <c>w:date</c> of a <c>.docx</c> comment.
/// </remarks>
internal static class DocDateTime
{
    /// <summary>The first year the format can express.</summary>
    private const int Epoch = 1900;

    /// <summary>The last, being the epoch plus what nine bits hold.</summary>
    private const int LastYear = Epoch + 511;

    /// <summary>Reads a packed value, or <see langword="null"/> when it says nothing.</summary>
    /// <param name="value">The packed date.</param>
    public static DateTimeOffset? Unpack(uint value)
    {
        int minute = (int)(value & 0x3F);
        int hour = (int)((value >> 6) & 0x1F);
        int day = (int)((value >> 11) & 0x1F);
        int month = (int)((value >> 16) & 0x0F);
        int year = Epoch + (int)((value >> 20) & 0x1FF);

        // A zero day or month is how the format spells "no date"; the rest are ranges the
        // specification requires and a file is free to get wrong.
        if (day == 0 || month is 0 or > 12 || hour > 23 || minute > 59 || day > DateTime.DaysInMonth(year, month))
            return null;

        return new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero);
    }

    /// <summary>Packs a date, or zero when the format cannot express it.</summary>
    /// <param name="value">The date to write, whose wall clock is stored as it stands.</param>
    public static uint Pack(DateTimeOffset? value)
    {
        if (value is not { } date || date.Year < Epoch || date.Year > LastYear)
            return 0;

        return ((uint)date.Minute & 0x3F)
            | (((uint)date.Hour & 0x1F) << 6)
            | (((uint)date.Day & 0x1F) << 11)
            | (((uint)date.Month & 0x0F) << 16)
            | (((uint)(date.Year - Epoch) & 0x1FF) << 20)
            | (((uint)(int)date.DayOfWeek & 0x7) << 29);
    }
}
