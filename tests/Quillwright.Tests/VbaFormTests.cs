using Quillwright.Vba;

namespace Quillwright.Tests;

/// <summary>
/// Reads the layout of the user form in the fixtures — one of every control the reader knows,
/// at places and sizes the build script chose to be all different, plus a frame with two
/// controls of its own.
/// </summary>
/// <remarks>
/// A form is stored twice over: what the container says about each control, and what each
/// control says about itself, in a different stream with nothing to tie the two together but
/// the order they appear in and a byte count. These tests are about both halves lining up.
/// </remarks>
public class VbaFormTests
{
    /// <summary>Both fixtures were saved from one Word session, so both must read the same.</summary>
    public static TheoryData<string> Fixtures => ["macros-forms.docm", "macros-forms.doc"];

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void TheForm_ListsEveryControlOnIt(string fixture)
    {
        VbaFormControl form = Layout(fixture);

        Assert.Equal("Quillwright launcher", form.Caption);
        Assert.Equal(
            ["GoButton", "TitleLabel", "NameBox", "AgreeBox", "FirstOption", "SecondOption", "PickBox", "Stepper", "GroupFrame"],
            form.Controls.Select(static control => control.Name));
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void EachControl_ComesBackAsTheKindItIs(string fixture)
    {
        Dictionary<string, VbaFormControlKind> kinds =
            Layout(fixture).AllControls.ToDictionary(static c => c.Name, static c => c.Kind);

        Assert.Equal(VbaFormControlKind.CommandButton, kinds["GoButton"]);
        Assert.Equal(VbaFormControlKind.Label, kinds["TitleLabel"]);
        Assert.Equal(VbaFormControlKind.TextBox, kinds["NameBox"]);
        Assert.Equal(VbaFormControlKind.CheckBox, kinds["AgreeBox"]);
        Assert.Equal(VbaFormControlKind.OptionButton, kinds["FirstOption"]);
        Assert.Equal(VbaFormControlKind.ComboBox, kinds["PickBox"]);
        Assert.Equal(VbaFormControlKind.SpinButton, kinds["Stepper"]);
        Assert.Equal(VbaFormControlKind.Frame, kinds["GroupFrame"]);
    }

    /// <summary>
    /// Six of the controls share one record and are told apart only by a byte inside it, so a
    /// text box read as a check box would mean that byte was read from the wrong offset.
    /// </summary>
    [Theory]
    [MemberData(nameof(Fixtures))]
    public void TheControlsThatShareARecord_AreStillToldApart(string fixture)
    {
        List<VbaFormControlKind> kinds =
        [
            .. Layout(fixture).Controls
                .Where(static control => control.Name is "NameBox" or "AgreeBox" or "FirstOption" or "PickBox")
                .Select(static control => control.Kind),
        ];

        Assert.Equal(
            [VbaFormControlKind.TextBox, VbaFormControlKind.CheckBox, VbaFormControlKind.OptionButton, VbaFormControlKind.ComboBox],
            kinds);
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void WhereAControlSits_IsWhereTheDesignerPutIt(string fixture)
    {
        VbaFormControlSite button = Find(fixture, "GoButton");

        Assert.Equal(12, button.Left.Points, 1);
        Assert.Equal(6, button.Top.Points, 1);
        Assert.Equal(60, button.Width.Points, 1);
        Assert.Equal(20, button.Height.Points, 1);
    }

    /// <summary>
    /// Left comes before top and width before height. Both pairs are two numbers of the same
    /// size in a row, so the only thing that catches them the wrong way round is a control
    /// whose two numbers differ — which is why the fixture has no square controls.
    /// </summary>
    [Theory]
    [MemberData(nameof(Fixtures))]
    public void TheTwoNumbersOfAPosition_AreNotTheOtherWayRound(string fixture)
    {
        VbaFormControlSite box = Find(fixture, "NameBox");

        Assert.Equal(108, box.Left.Points, 1);
        Assert.Equal(34, box.Top.Points, 1);
        Assert.Equal(96, box.Width.Points, 1);
        Assert.Equal(18, box.Height.Points, 1);
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void TheTabOrder_IsTheOrderTheControlsWereAdded(string fixture)
    {
        Assert.Equal([0, 1, 2, 3, 4, 5, 6, 7, 8], Layout(fixture).Controls.Select(static control => control.TabIndex));
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void ACaptionAndATooltip_ComeBackAsTyped(string fixture)
    {
        VbaFormControlSite button = Find(fixture, "GoButton");

        Assert.Equal("Go", button.Caption);
        Assert.Equal("Runs the macro", button.Tooltip);
        Assert.Equal("Who is asking?", Find(fixture, "TitleLabel").Caption);
    }

    /// <summary>
    /// What a control holds is a string whatever the control is: the text of a box, and a
    /// ticked or unticked box as "1" or "0".
    /// </summary>
    [Theory]
    [MemberData(nameof(Fixtures))]
    public void WhatAControlHolds_IsRead(string fixture)
    {
        Assert.Equal("Ada", Find(fixture, "NameBox").Value);
        Assert.Equal("1", Find(fixture, "AgreeBox").Value);
        Assert.Equal("0", Find(fixture, "FirstOption").Value);
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void OptionButtonsInOneGroup_NameTheSameGroup(string fixture)
    {
        Assert.Equal("Choice", Find(fixture, "FirstOption").GroupName);
        Assert.Equal("Choice", Find(fixture, "SecondOption").GroupName);
    }

    /// <summary>
    /// A frame is not written beside the controls around it. It gets a storage of its own,
    /// named after its identifier, holding a form stream and an object stream of the same
    /// shape as the ones the form itself uses.
    /// </summary>
    [Theory]
    [MemberData(nameof(Fixtures))]
    public void AFrame_CarriesTheControlsInsideIt(string fixture)
    {
        VbaFormControlSite frame = Find(fixture, "GroupFrame");

        Assert.NotNull(frame.Child);
        Assert.Equal("Grouped", frame.Caption);
        Assert.Equal(["InnerLabel", "InnerButton"], frame.Child.Controls.Select(static control => control.Name));
        Assert.Equal("Inside the frame", frame.Child.Controls[0].Caption);
        Assert.Equal("Nested", frame.Child.Controls[1].Caption);

        // Its own storage is the only place a frame's size is written down.
        Assert.Equal(120, frame.Width.Points, 1);
        Assert.Equal(90, frame.Height.Points, 1);
    }

    /// <summary>A control inside a frame is placed against the frame, not against the form.</summary>
    [Theory]
    [MemberData(nameof(Fixtures))]
    public void AControlInsideAFrame_IsPlacedAgainstTheFrame(string fixture)
    {
        VbaFormControlSite inner = Find(fixture, "InnerButton");

        Assert.Equal(6, inner.Left.Points, 1);
        Assert.Equal(36, inner.Top.Points, 1);
        Assert.Equal(0, inner.GroupId);
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void EveryControl_HasAnIdentifierOfItsOwn(string fixture)
    {
        List<int> ids = [.. Layout(fixture).AllControls.Select(static control => control.Id)];

        Assert.Equal(11, ids.Count);
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.DoesNotContain(0, ids);
    }

    /// <summary>The size the form was left at is the same in both places that record it.</summary>
    [Theory]
    [MemberData(nameof(Fixtures))]
    public void TheFormsOwnSize_AgreesWithTheTextItIsAlsoWrittenIn(string fixture)
    {
        VbaDesigner designer = Designer(fixture);

        Assert.NotNull(designer.Controls);
        Assert.Equal(designer.Width.Points, designer.Controls.Width.Points, 1);
        Assert.Equal(designer.Height.Points, designer.Controls.Height.Points, 1);
    }

    /// <summary>A module that is not a form has no layout to read.</summary>
    [Fact]
    public void APlainModule_HasNoDesigner()
    {
        VbaProject project = VbaFixtures.Read("macros-forms.docm");
        VbaModule module = project.Modules.Single(static m => m.Name == "Scripted");

        Assert.Null(module.Designer);
    }

    private static VbaFormControlSite Find(string fixture, string name) =>
        Layout(fixture).AllControls.Single(control => control.Name == name);

    private static VbaFormControl Layout(string fixture)
    {
        VbaFormControl? form = Designer(fixture).Controls;
        Assert.NotNull(form);
        return form;
    }

    private static VbaDesigner Designer(string fixture)
    {
        VbaProject project = fixture.EndsWith(".doc", StringComparison.Ordinal)
            ? VbaFixtures.ReadLegacy(fixture)
            : VbaFixtures.Read(fixture);

        VbaDesigner? designer = project.Modules.Single(static m => m.Name == "Launcher").Designer;
        Assert.NotNull(designer);
        return designer;
    }
}
