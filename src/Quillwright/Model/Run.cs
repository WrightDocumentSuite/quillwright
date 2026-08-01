using System.Collections;
using Quillwright.Styles;

namespace Quillwright.Model;

/// <summary>
/// A view over one formatted stretch of a paragraph. It is a struct holding the paragraph
/// and an index, so enumerating runs allocates nothing and writing through the view edits
/// the paragraph in place.
/// </summary>
public readonly struct Run : IEquatable<Run>
{
    private readonly Paragraph _paragraph;
    private readonly int _index;

    internal Run(Paragraph paragraph, int index)
    {
        _paragraph = paragraph;
        _index = index;
    }

    /// <summary>The paragraph this run belongs to.</summary>
    public Paragraph Paragraph => _paragraph;

    /// <summary>Position of the run among its paragraph's runs.</summary>
    public int Index => _index;

    /// <summary>Offset of the first character.</summary>
    public int Start => _paragraph.RunSpans[_index].Start;

    /// <summary>Number of characters covered.</summary>
    public int Length => _paragraph.RunSpans[_index].Length;

    /// <summary>How the text is written out.</summary>
    public RunKind Kind => _paragraph.RunSpans[_index].Kind;

    /// <summary>The characters of the run without copying.</summary>
    public ReadOnlySpan<char> Span => _paragraph.AsSpan().Slice(Start, Length);

    /// <summary>The characters of the run.</summary>
    public string Text => Span.ToString();

    /// <summary>Character formatting of the run.</summary>
    public RunFormat Format
    {
        get => _paragraph.RunSpans[_index].Format;
        set => _paragraph.RunSpans[_index].Format = value;
    }

    /// <summary>Replaces the formatting and returns the run, for chaining.</summary>
    /// <param name="format">The new formatting.</param>
    public Run SetFormat(RunFormat format)
    {
        Format = format;
        return this;
    }

    /// <summary>Applies a formatting change and returns the run, for chaining.</summary>
    /// <param name="transform">Produces the new formatting from the old.</param>
    public Run SetFormat(Func<RunFormat, RunFormat> transform)
    {
        ArgumentNullException.ThrowIfNull(transform);
        Format = transform(Format);
        return this;
    }

    /// <summary>Replaces the text of the run, keeping its formatting.</summary>
    /// <param name="text">The new text.</param>
    public void SetText(ReadOnlySpan<char> text) => _paragraph.ReplaceText(Start, Length, text, Format);

    /// <inheritdoc />
    public bool Equals(Run other) => ReferenceEquals(_paragraph, other._paragraph) && _index == other._index;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Run other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(_paragraph, _index);

    /// <summary>Compares two views for identity.</summary>
    public static bool operator ==(Run left, Run right) => left.Equals(right);

    /// <summary>Compares two views for identity.</summary>
    public static bool operator !=(Run left, Run right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => Text;
}

/// <summary>The runs of a paragraph, addressable by index and enumerable without allocation.</summary>
public readonly struct RunCollection : IReadOnlyList<Run>
{
    private readonly Paragraph _paragraph;

    internal RunCollection(Paragraph paragraph) => _paragraph = paragraph;

    /// <inheritdoc />
    public int Count => _paragraph.RunCount;

    /// <inheritdoc />
    public Run this[int index] => (uint)index < (uint)Count
        ? new Run(_paragraph, index)
        : throw new ArgumentOutOfRangeException(nameof(index));

    /// <summary>Returns an allocation-free enumerator.</summary>
    public Enumerator GetEnumerator() => new(_paragraph);

    IEnumerator<Run> IEnumerable<Run>.GetEnumerator()
    {
        for (int i = 0; i < Count; i++)
            yield return new Run(_paragraph, i);
    }

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<Run>)this).GetEnumerator();

    /// <summary>Walks the runs of a paragraph.</summary>
    public struct Enumerator
    {
        private readonly Paragraph _paragraph;
        private int _index;

        internal Enumerator(Paragraph paragraph)
        {
            _paragraph = paragraph;
            _index = -1;
        }

        /// <summary>The run at the current position.</summary>
        public readonly Run Current => new(_paragraph, _index);

        /// <summary>Advances to the next run.</summary>
        public bool MoveNext() => ++_index < _paragraph.RunCount;
    }
}
