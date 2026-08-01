# Rebuilds the macro fixtures with Word itself, so the compressed VBA source the tests decode
# is Microsoft's own output rather than a round trip through an encoder of ours. Both files are
# saved from one Word session, which is what lets a test require that the two formats yield
# identical source.
#
# Word will not let automation touch a VBA project unless "Trust access to the VBA project
# object model" is on, so turn it on for the duration and off again afterwards:
#
#   Set-ItemProperty "HKCU:\Software\Microsoft\Office\16.0\Word\Security" AccessVBOM 1 -Type DWord
#   pwsh -File .\build-fixtures.ps1
#   Remove-ItemProperty "HKCU:\Software\Microsoft\Office\16.0\Word\Security" AccessVBOM
#
# This script does not build macros-locked.doc / .docm, and cannot: Word refuses to set a project
# password through automation, which is the whole reason that fixture had to exist. It was made by
# hand — a document with a class module, then Tools > VBAProject Properties > Protection, "Lock
# project for viewing", password 123 — and saved as both formats from the one session. If it ever
# needs rebuilding, the password has to stay 123 or VbaLockedProjectTests will fail.

param(
    # "all", "macros" or "forms". The two pairs are independent, so the form fixtures can be
    # rebuilt without disturbing the source the extraction tests compare byte for byte.
    [ValidateSet("all", "macros", "forms")]
    [string]$Only = "all"
)

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$nl = "`r`n"

$greeting = @(
    'Option Explicit'
    ''
    "' A greeting for the reader."
    'Public Sub SayHello()'
    '    MsgBox "Hello from Quillwright"'
    'End Sub'
    ''
    'Function Doubled(ByVal value As Long) As Long'
    '    Doubled = value * 2'
    'End Function'
) -join $nl

# Long enough that the compressed container needs more than one chunk, and repetitive enough
# that the encoder reaches for back-references rather than emitting plain literals.
$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('Option Explicit')
for ($i = 0; $i -lt 220; $i++) {
    $lines.Add('')
    $lines.Add("Public Function Step$i(ByVal value As Long) As Long")
    $lines.Add("    Step$i = value + $i")
    $lines.Add('End Function')
}
$bulk = $lines -join $nl

$helper = @(
    'Option Explicit'
    ''
    'Private stored As String'
    ''
    'Public Property Let Value(ByVal text As String)'
    '    stored = text'
    'End Property'
    ''
    'Public Property Get Value() As String'
    '    Value = stored'
    'End Property'
) -join $nl

$word = New-Object -ComObject Word.Application
$word.Visible = $false
$word.DisplayAlerts = 0
try {
    if ($Only -ne "forms") {
    Remove-Item (Join-Path $here "macros.doc"), (Join-Path $here "macros.docm") -Force -ErrorAction SilentlyContinue

    $document = $word.Documents.Add()
    $document.Content.Text = "A document that carries macros."
    $project = $document.VBProject

    # 1 is a standard module, 2 a class module.
    $module = $project.VBComponents.Add(1)
    $module.Name = "Greeting"
    $module.CodeModule.AddFromString($greeting)

    $large = $project.VBComponents.Add(1)
    $large.Name = "Bulk"
    $large.CodeModule.AddFromString($bulk)

    $class = $project.VBComponents.Add(2)
    $class.Name = "Helper"
    $class.CodeModule.AddFromString($helper)

    # 13 is the macro-enabled package, 0 the Word 97-2003 binary format.
    $document.SaveAs2((Join-Path $here "macros.docm"), 13)
    $document.SaveAs2((Join-Path $here "macros.doc"), 0)
    $document.Close(0)
    }

    if ($Only -eq "macros") {
        Write-Host "macro fixtures written to $here"
        return
    }

    # A second project with a user form. A form obliges Word to write a control reference,
    # whose extended half is the record whose framing the specification states least plainly,
    # so this is what keeps that path covered by real data rather than by reasoning. The form
    # itself carries one of every control the reader has a layout table for, at deliberately
    # different positions and sizes, so a table that reads a property from the wrong offset
    # shows up as a wrong number rather than as a plausible one.
    $formCode = @(
        'Option Explicit'
        ''
        'Private Sub UserForm_Initialize()'
        '    Me.Caption = "Quillwright form"'
        'End Sub'
        ''
        'Private Sub GoButton_Click()'
        '    MsgBox "Go"'
        'End Sub'
    ) -join $nl

    $scripted = @(
        'Option Explicit'
        ''
        'Public Sub UsesScripting()'
        '    Dim files As Object'
        '    Set files = CreateObject("Scripting.FileSystemObject")'
        'End Sub'
    ) -join $nl

    $second = $word.Documents.Add()
    $second.Content.Text = "A document with a form."
    $secondProject = $second.VBProject

    # Microsoft Scripting Runtime, which lands as a registered reference.
    $secondProject.References.AddFromGuid("{420B2830-E718-11CF-893D-00A0C9054228}", 1, 0) | Out-Null

    $plain = $secondProject.VBComponents.Add(1)
    $plain.Name = "Scripted"
    $plain.CodeModule.AddFromString($scripted)

    # 3 is a user form, which drags in the MSForms control reference behind it.
    $form = $secondProject.VBComponents.Add(3)
    $form.Name = "Launcher"
    $form.CodeModule.AddFromString($formCode)

    $designer = $form.Designer
    $designer.Caption = "Quillwright launcher"

    function Place($control, $left, $top, $width, $height) {
        $control.Left = $left
        $control.Top = $top
        $control.Width = $width
        $control.Height = $height
    }

    # ControlSource and RowSource name a place to read a value from, and what counts as one
    # depends on the host. Word rejects the cell references Excel would take, so these are
    # attempted and let go: the fixture is worth more with them than without, and the reader
    # is not worth failing a build over.
    function TrySet($control, $property, $value) {
        try { $control.$property = $value } catch { Write-Host "  skipped $property on $($control.Name): $($_.Exception.Message)" }
    }

    $go = $designer.Controls.Add("Forms.CommandButton.1", "GoButton", $true)
    $go.Caption = "Go"
    $go.ControlTipText = "Runs the macro"
    Place $go 12 6 60 20

    $title = $designer.Controls.Add("Forms.Label.1", "TitleLabel", $true)
    $title.Caption = "Who is asking?"
    Place $title 12 36 90 14

    $name = $designer.Controls.Add("Forms.TextBox.1", "NameBox", $true)
    $name.Text = "Ada"
    TrySet $name "ControlSource" "A1"
    Place $name 108 34 96 18

    $agree = $designer.Controls.Add("Forms.CheckBox.1", "AgreeBox", $true)
    $agree.Caption = "Agree"
    $agree.Value = $true
    Place $agree 12 60 90 16

    $first = $designer.Controls.Add("Forms.OptionButton.1", "FirstOption", $true)
    $first.Caption = "First"
    $first.GroupName = "Choice"
    Place $first 12 82 72 16

    $secondOption = $designer.Controls.Add("Forms.OptionButton.1", "SecondOption", $true)
    $secondOption.Caption = "Second"
    $secondOption.GroupName = "Choice"
    Place $secondOption 96 82 72 16

    $pick = $designer.Controls.Add("Forms.ComboBox.1", "PickBox", $true)
    TrySet $pick "RowSource" "B1:B3"
    Place $pick 12 104 96 18

    $spin = $designer.Controls.Add("Forms.SpinButton.1", "Stepper", $true)
    Place $spin 120 104 18 18

    # A frame is the control that is not written beside the others: it gets a storage of its
    # own, named after its identifier, holding its children exactly as the form holds these.
    $frame = $designer.Controls.Add("Forms.Frame.1", "GroupFrame", $true)
    $frame.Caption = "Grouped"
    Place $frame 150 6 120 90

    $inner = $frame.Controls.Add("Forms.Label.1", "InnerLabel", $true)
    $inner.Caption = "Inside the frame"
    Place $inner 6 12 100 14

    $innerButton = $frame.Controls.Add("Forms.CommandButton.1", "InnerButton", $true)
    $innerButton.Caption = "Nested"
    Place $innerButton 6 36 84 22

    $second.SaveAs2((Join-Path $here "macros-forms.docm"), 13)
    $second.SaveAs2((Join-Path $here "macros-forms.doc"), 0)
    $second.Close(0)

    Write-Host "fixtures written to $here"
}
finally {
    $word.Quit(0)
}

# The editor unifies the spelling of an identifier across the whole project, so the "value"
# parameters above come back capitalised to match the "Value" property of the class module.
# Tests that assert on identifiers compare without regard to case for that reason.
