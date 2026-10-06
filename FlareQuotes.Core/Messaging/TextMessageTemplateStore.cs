using FlareQuotes.Core.Models;
using FlareQuotes.Core.Paths;
using FlareQuotes.Core.Security;

namespace FlareQuotes.Core.Messaging;

public sealed class TextMessageTemplateStore
{
    private readonly string _path;
    private readonly ProtectedJsonFileStore _protectedStore = new("Text Message Templates");

    public TextMessageTemplateStore(string? path = null)
    {
        _path = path ?? Path.Combine(AppPaths.Root, "text-message-templates.json.dpapi");
    }

    public TextMessageTemplateLibrary Load()
    {
        var saved = _protectedStore.LoadOrMigrate<TextMessageTemplateLibrary>(_path);
        if (saved is not null)
        {
            Validate(saved);
            return saved;
        }

        var defaults = CreateDefaults();
        Save(defaults);
        return defaults;
    }

    public void Save(TextMessageTemplateLibrary library)
    {
        Validate(library);
        _protectedStore.Save(_path, library);
    }

    public TextMessageTemplate SaveTemplate(TextMessageTemplateLibrary library, string? id, string name, string body)
    {
        ArgumentNullException.ThrowIfNull(library);
        var template = new TextMessageTemplate
        {
            Id = id ?? Guid.NewGuid().ToString("N"),
            Name = (name ?? string.Empty).Trim(),
            Body = (body ?? string.Empty).Trim()
        };
        var existing = id is null ? null : library.Templates.SingleOrDefault(item => item.Id == id);
        if (id is not null && existing is null)
            throw new InvalidOperationException("That template is no longer available.");

        var candidate = CopyLibrary(library);
        var index = existing is null ? candidate.Templates.Count : candidate.Templates.FindIndex(item => item.Id == id);
        if (existing is null)
            candidate.Templates.Add(template);
        else
            candidate.Templates[index] = template;
        candidate.SelectedTemplateId = template.Id;
        Save(candidate);

        library.Templates = candidate.Templates;
        library.SelectedTemplateId = candidate.SelectedTemplateId;
        return template;
    }

    public TextMessageTemplate DeleteTemplate(TextMessageTemplateLibrary library, string id)
    {
        var existing = library.Templates.SingleOrDefault(template => template.Id == id)
                           ?? throw new InvalidOperationException("That template is no longer available.");
        var candidate = CopyLibrary(library);
        candidate.Templates.RemoveAll(template => template.Id == id);
        if (candidate.SelectedTemplateId == id)
            candidate.SelectedTemplateId = candidate.Templates.FirstOrDefault()?.Id ?? string.Empty;
        Save(candidate);
        library.Templates = candidate.Templates;
        library.SelectedTemplateId = candidate.SelectedTemplateId;
        return existing;
    }

    public void RestoreTemplate(TextMessageTemplateLibrary library, TextMessageTemplate template)
    {
        var candidate = CopyLibrary(library);
        candidate.Templates.Add(template);
        candidate.SelectedTemplateId = template.Id;
        Save(candidate);
        library.Templates = candidate.Templates;
        library.SelectedTemplateId = template.Id;
    }

    public static TextMessageTemplateLibrary CreateDefaults() => new()
    {
        SelectedTemplateId = "quote-emailed",
        Templates =
        [
            new TextMessageTemplate
            {
                Id = "quote-emailed",
                Name = "Quote emailed",
                Body = "Hi {FirstName}, I emailed your {Model} quote and product information for {Project}. " +
                       "Please let me know if you have any questions or would like to discuss the next steps."
            },
            new TextMessageTemplate
            {
                Id = "follow-up",
                Name = "Quote follow-up",
                Body = "Hi {FirstName}, I'm following up on your fireplace quote for {Project}. " +
                       "Have you had a chance to review it? I'm happy to answer questions and help with the next steps."
            },
            new TextMessageTemplate
            {
                Id = "consultation",
                Name = "Project consultation",
                Body = "Hi {FirstName}, I'd be happy to discuss your fireplace plans for {Project}. " +
                       "You can schedule a project consultation here: {Consultation}"
            }
        ]
    };

    private static TextMessageTemplateLibrary CopyLibrary(TextMessageTemplateLibrary source) => new()
    {
        Templates = source.Templates.ToList(),
        SalesName = source.SalesName,
        SelectedTemplateId = source.SelectedTemplateId
    };

    private static void Validate(TextMessageTemplateLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);
        if (library.Templates is null)
            throw new InvalidDataException("The template library could not be read.");
        if (library.Templates.Count > 100)
            throw new ArgumentException("Keep the library to 100 templates or fewer.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var template in library.Templates)
        {
            if (template is null || string.IsNullOrWhiteSpace(template.Id) || !ids.Add(template.Id))
                throw new ArgumentException("Every template needs a unique ID.");
            if (string.IsNullOrWhiteSpace(template.Name))
                throw new ArgumentException("Name the template before saving.");
            if (template.Name.Trim().Length > 80)
                throw new ArgumentException("Use a template name of 80 characters or fewer.");
            if (!names.Add(template.Name.Trim()))
                throw new ArgumentException("A template with that name already exists. Choose another name.");
            if (string.IsNullOrWhiteSpace(template.Body))
                throw new ArgumentException("Write a message before saving the template.");
            if (template.Body.Length > 4000)
                throw new ArgumentException("Keep the template to 4,000 characters or fewer.");
        }
    }
}
