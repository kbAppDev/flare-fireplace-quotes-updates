using System.Text;
using FlareQuotes.Core.Messaging;
using FlareQuotes.Core.Models;
using Xunit;

namespace FlareQuotes.Tests.MessagingTests;

public sealed class TextMessageTemplateStoreTests
{
    [Fact]
    public void SeedsReusableTemplatesAndPersistsCrudWithoutSavingRenderedCustomerContents()
    {
        using var storage = new TemporaryTemplateStore();
        var store = storage.Store;
        var library = store.Load();
        Assert.Equal(3, library.Templates.Count);
        Assert.Contains(library.Templates, template => template.Name == "Quote emailed");
        Assert.Contains(library.Templates, template => template.Name == "Quote follow-up");
        Assert.Contains(library.Templates, template => template.Name == "Project consultation");

        var added = store.SaveTemplate(library, null, " Dealer reminder ", "Hello {FirstName}, from {SalesName}.");
        library.SalesName = "Kyle";
        store.Save(library);
        var preview = TextMessageTemplateRenderer.Render(added.Body,
            new TextMessageContext { ClientName = "customer-unique-marker", SalesName = "Kyle" });
        Assert.Contains("customer-unique-marker", preview.Text);

        var updated = store.SaveTemplate(library, added.Id, "Dealer follow-up", "Hi {FirstName}, can we help?");
        Assert.Equal(added.Id, updated.Id);
        var loaded = store.Load();
        Assert.Equal("Kyle", loaded.SalesName);
        Assert.Equal(updated.Id, loaded.SelectedTemplateId);
        Assert.Equal("Hi {FirstName}, can we help?", loaded.Templates.Single(template => template.Id == updated.Id).Body);
        Assert.DoesNotContain("customer-unique-marker", string.Join(" ", loaded.Templates.Select(template => template.Body)));
        Assert.DoesNotContain("Hi {FirstName}", Encoding.UTF8.GetString(File.ReadAllBytes(storage.Path)));

        var deleted = store.DeleteTemplate(library, updated.Id);
        Assert.DoesNotContain(store.Load().Templates, template => template.Id == updated.Id);
        store.RestoreTemplate(library, deleted);
        Assert.Contains(store.Load().Templates, template => template.Id == updated.Id);
    }

    [Fact]
    public void DuplicateNameRejectionLeavesSavedAndInMemoryTemplatesUnchanged()
    {
        using var storage = new TemporaryTemplateStore();
        var library = storage.Store.Load();
        var savedBefore = File.ReadAllBytes(storage.Path);

        Assert.Throws<ArgumentException>(() => storage.Store.SaveTemplate(library, null, " quote EMAILED ", "Duplicate"));

        Assert.Equal(3, library.Templates.Count);
        Assert.Equal(savedBefore, File.ReadAllBytes(storage.Path));
        Assert.Equal(3, storage.Store.Load().Templates.Count);
    }

    [Theory]
    [InlineData("", "Hello")]
    [InlineData("   ", "Hello")]
    [InlineData("New message", "")]
    [InlineData("New message", " \n ")]
    public void BlankNameOrMessageCannotCreateTemplate(string name, string body)
    {
        using var storage = new TemporaryTemplateStore();
        var library = storage.Store.Load();

        Assert.Throws<ArgumentException>(() => storage.Store.SaveTemplate(library, null, name, body));
        Assert.Equal(3, storage.Store.Load().Templates.Count);
    }

    [Fact]
    public void DeletingEveryTemplateDoesNotRestoreDeletedDefaultsOnRestart()
    {
        using var storage = new TemporaryTemplateStore();
        var library = storage.Store.Load();
        foreach (var template in library.Templates.ToArray())
            storage.Store.DeleteTemplate(library, template.Id);

        Assert.Empty(storage.Store.Load().Templates);
    }

    [Fact]
    public void RenamingTemplateToAnExistingNameAndOverlongBodyAreRejected()
    {
        using var storage = new TemporaryTemplateStore();
        var library = storage.Store.Load();
        var template = library.Templates[0];

        Assert.Throws<ArgumentException>(() => storage.Store.SaveTemplate(library, template.Id,
            library.Templates[1].Name, template.Body));
        Assert.Throws<ArgumentException>(() => storage.Store.SaveTemplate(library, template.Id,
            template.Name, new string('x', 4001)));
        Assert.Equal(template.Body, storage.Store.Load().Templates.Single(item => item.Id == template.Id).Body);
    }

    private sealed class TemporaryTemplateStore : IDisposable
    {
        private readonly string _root;
        public string Path { get; }
        public TextMessageTemplateStore Store { get; }

        public TemporaryTemplateStore()
        {
            _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "flare-message-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            Path = System.IO.Path.Combine(_root, "templates.json.dpapi");
            Store = new TextMessageTemplateStore(Path);
        }

        public void Dispose()
        {
            var expectedParent = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "flare-message-tests"));
            if (string.Equals(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(_root)), expectedParent,
                              StringComparison.OrdinalIgnoreCase))
                Directory.Delete(_root, recursive: true);
        }
    }
}
