using Quillwright.Model;

namespace Quillwright.Tests;

/// <summary>
/// Taking a field instruction apart (ISO/IEC 29500-1 §17.16.1): the quoting is its own, and
/// a backslash means two different things depending on which side of a quote it is on.
/// </summary>
public class FieldInstructionTests
{
    [Fact]
    public void TheFirstWord_IsTheFieldsName()
    {
        FieldInstruction instruction = FieldInstruction.Parse("PAGE");

        Assert.Equal("PAGE", instruction.Name);
        Assert.Empty(instruction.Arguments);
        Assert.Empty(instruction.Switches);
    }

    [Fact]
    public void FieldNames_AreCaseInsensitive()
    {
        Assert.Equal("DATE", FieldInstruction.Parse("dAtE").Name);
    }

    [Fact]
    public void AQuotedArgument_KeepsItsSpaces()
    {
        FieldInstruction instruction = FieldInstruction.Parse("DOCPROPERTY \"Project Number\"");

        Assert.Equal("DOCPROPERTY", instruction.Name);
        Assert.Equal("Project Number", Assert.Single(instruction.Arguments));
    }

    /// <summary>The specification's own example: a quote inside a quoted argument is escaped.</summary>
    [Fact]
    public void AnEscapedQuote_IsPartOfTheArgument()
    {
        FieldInstruction instruction = FieldInstruction.Parse("QUOTE \"\\\"name\\\"\"");

        Assert.Equal("\"name\"", Assert.Single(instruction.Arguments));
    }

    /// <summary>The other example: a path keeps its separators because they are inside quotes.</summary>
    [Fact]
    public void AnEscapedBackslash_IsNotASwitch()
    {
        FieldInstruction instruction = FieldInstruction.Parse("INCLUDETEXT \"E:\\\\ReadMe.txt\"");

        Assert.Equal("E:\\ReadMe.txt", Assert.Single(instruction.Arguments));
        Assert.Empty(instruction.Switches);
    }

    [Fact]
    public void ASwitch_IsFoundByItsLetter()
    {
        FieldInstruction instruction = FieldInstruction.Parse("DATE \\@ \"dd.MM.yyyy\" \\h");

        Assert.Equal("dd.MM.yyyy", instruction.DatePicture);
        Assert.True(instruction.Has("h"));
        Assert.Null(instruction.Argument("h"));
    }

    [Fact]
    public void SwitchLetters_AreCaseInsensitive()
    {
        Assert.True(FieldInstruction.Parse("REF bookmark \\H").Has("h"));
    }

    /// <summary>A switch with nothing after it must not swallow the switch that follows it.</summary>
    [Fact]
    public void ASwitchFollowedByAnother_TakesNoArgument()
    {
        FieldInstruction instruction = FieldInstruction.Parse("TOC \\o \"1-3\" \\h \\z \\u");

        Assert.Equal("1-3", instruction.Argument("o"));
        Assert.Null(instruction.Argument("h"));
        Assert.Equal(4, instruction.Switches.Count);
    }

    [Fact]
    public void AFormula_KeepsItsExpressionWhole()
    {
        FieldInstruction instruction = FieldInstruction.Parse("=((-1 + X^2) * 3 - Y)/2 \\# 0.0");

        Assert.True(instruction.IsFormula);
        Assert.Equal("((-1 + X^2) * 3 - Y)/2", Assert.Single(instruction.Arguments));
        Assert.Equal("0.0", instruction.NumericPicture);
    }

    [Fact]
    public void AnInstruction_RemembersHowItWasWritten()
    {
        Assert.Equal("PAGE \\* MERGEFORMAT", FieldInstruction.Parse("  PAGE \\* MERGEFORMAT  ").ToString());
    }
}
