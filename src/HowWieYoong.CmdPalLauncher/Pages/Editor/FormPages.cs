using HowWieYoong.CmdPalLauncher.Editing;
using HowWieYoong.CmdPalLauncher.Profiles;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace HowWieYoong.CmdPalLauncher.Pages.Editor;

/// <summary>A page that shows one form.</summary>
internal sealed partial class FormPage : ContentPage
{
    private readonly FormContent _form;

    public FormPage(string title, IconInfo icon, FormContent form)
    {
        Title = title;
        Name = "Open";
        Icon = icon;
        _form = form;
    }

    public override IContent[] GetContent() => [_form];
}

/// <summary>The "New profile" form.</summary>
internal sealed partial class NewProfileFormContent : FormContent
{
    private readonly ProfileStore _store;

    public NewProfileFormContent(ProfileStore store)
    {
        _store = store;
        TemplateJson = ProfileForm.Template(null);
    }

    public override CommandResult SubmitForm(string payload)
    {
        var (name, description, icon) = ProfileForm.Read(payload);
        return _store.Update(file => ProfileEditing.AddProfile(file, name, description, icon)) is { } error
            ? EditorResults.Failed(error)
            : EditorResults.SavedAndBack($"Created {name.Trim()}");
    }
}
