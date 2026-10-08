// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Net;
using System.Text.Json;
using NUnit.Framework;

namespace CrestronHomeNUnit.Mac.Tests;

public sealed class MacTests
{
    private string root = null!;
    private static readonly MacSelector Control = new("XCUIElementTypeButton", "power", "Demo");
    private static readonly MacTestIdentity Identity = new("192.0.2.1", "demo-driver", new string('a', 64), "4.12.11", "27.0");
    internal const string Button = "<XCUIElementTypeButton identifier='power' label='Demo' enabled='true' width='20' height='20' selected='false'/>";
    internal static string Window(string body) => "<AppiumAUT><XCUIElementTypeWindow identifier='SceneWindow'>" + body + "</XCUIElementTypeWindow></AppiumAUT>";
    [SetUp] public void Setup() => root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "mac-offline-" + Guid.NewGuid().ToString("N"));
    [TearDown] public void Cleanup() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    [Test] public void HiddenDuplicateDialogIsExcluded()
    {
        string xml = Window(Button).Replace("</AppiumAUT>", "<XCUIElementTypeDialog identifier='SceneWindow'>" + Button + "</XCUIElementTypeDialog></AppiumAUT>");
        Assert.That(new MacHierarchy(xml).Require(Control).Label, Is.EqualTo("Demo"));
    }
    [Test] public void DuplicateControlsAreRejected() => Assert.Throws<InvalidOperationException>(() => new MacHierarchy(Window(Button + Button)).Require(Control));
    [TestCase("enabled='true'", "enabled='false'")]
    [TestCase("width='20'", "width='0'")]
    [TestCase("height='20'", "height='NaN'")]
    [TestCase("enabled='true'", "enabled='true' visible='false'")]
    public void NonActionableControlsAreRejected(string before, string after) =>
        Assert.Throws<InvalidOperationException>(() => new MacHierarchy(Window(Button.Replace(before, after))).Require(Control));
    [Test] public void MissingWindowIsRejected() => Assert.Throws<InvalidDataException>(() => new MacHierarchy("<AppiumAUT/>"));
    [Test] public void MultipleWindowsAreRejected() => Assert.Throws<InvalidDataException>(() => new MacHierarchy(Window(Button).Replace("</AppiumAUT>", "<XCUIElementTypeWindow identifier='SceneWindow'/></AppiumAUT>")));
    [Test] public void DtdIsRejected() => Assert.Throws<System.Xml.XmlException>(() => new MacHierarchy("<!DOCTYPE foo [<!ENTITY e 'x'>]>" + Window(Button)));
    [TestCase("A'B")][TestCase("A\"B")][TestCase("A'B\"C")][TestCase("' or 1=1 or '")]
    public void SelectorLabelsAreLiterals(string label)
    {
        var node = new System.Xml.Linq.XElement("XCUIElementTypeButton", new System.Xml.Linq.XAttribute("identifier", "power"),
            new System.Xml.Linq.XAttribute("label", label), new System.Xml.Linq.XAttribute("enabled", "true"),
            new System.Xml.Linq.XAttribute("width", "20"), new System.Xml.Linq.XAttribute("height", "20"));
        Assert.That(new MacHierarchy(Window(node.ToString())).Require(Control with { Label = label }).Label, Is.EqualTo(label));
    }
    [Test] public void TypeInjectionIsRejected() => Assert.Throws<ArgumentException>(() => _ = (Control with { ElementType = "XCUIElementTypeButton//*" }).XPath);

    [Test] public void BinaryControlRejectsUnknownState()
    {
        var mapping = new MacBinaryControl(Control, "On", "Off", false, Control, Control);
        Assert.Throws<InvalidOperationException>(() => mapping.Read(new MacHierarchy(Window(Button))));
    }
    [Test] public void BinaryControlRejectsIdenticalStates()
    {
        var mapping = new MacBinaryControl(Control, "Demo", "Demo", false, Control, Control);
        Assert.Throws<ArgumentException>(() => mapping.Read(new MacHierarchy(Window(Button))));
    }
    [Test] public async Task SettingExistingStateDoesNotClick()
    {
        var transport = new Fake();
        await using var session = await Open(transport);
        await new MacBinaryControl(Control, "On", "Demo", false, Control, Control).SetAsync(session, false);
        Assert.That(transport.Clicks, Is.Zero);
    }
    [Test] public async Task WaitCancellationDoesNotSendInput()
    {
        var transport = new Fake();
        await using var session = await Open(transport);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => session.WaitAsync(_ => false, TimeSpan.FromSeconds(1), cancel.Token));
        Assert.That(transport.Clicks, Is.Zero);
    }
    [Test] public async Task InvalidScreenshotPreventsInput()
    {
        var transport = new Fake { InvalidScreenshot = true };
        await using var session = await Open(transport);
        await Assert.ThrowsAsync<InvalidDataException>(() => session.ClickAsync(Control));
        Assert.That(transport.Clicks, Is.Zero);
    }

    [Test] public async Task ClickRetainsEvidenceBeforeSingleInput()
    {
        var transport = new Fake();
        await using var session = await Open(transport);
        transport.BeforeClick = () => Assert.That(Directory.GetFiles(root, "*-input-intent.json"), Has.Length.EqualTo(1));
        await session.ClickAsync(Control);
        Assert.That(transport.Clicks, Is.EqualTo(1));
        Assert.That(Directory.GetFiles(root, "*.png"), Has.Length.EqualTo(1));
        using var inventory = JsonDocument.Parse(File.ReadAllText(Directory.GetFiles(root, "*-before-click.json").Single()));
        var xml = File.ReadAllBytes(Directory.GetFiles(root, "*.xml").Single());
        Assert.That(inventory.RootElement.GetProperty("XmlSha256").GetString(), Is.EqualTo(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(xml))));
    }
    [Test] public async Task AmbiguousRemoteElementPreventsInput()
    {
        var transport = new Fake { ElementCount = 2 };
        await using var session = await Open(transport);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ClickAsync(Control));
        Assert.That(transport.Clicks, Is.Zero);
    }
    [Test] public async Task FailedInputIsNeverReplayed()
    {
        var transport = new Fake { FailClick = true };
        await using var session = await Open(transport);
        await Assert.ThrowsAsync<IOException>(() => session.ClickAsync(Control));
        Assert.That(transport.Clicks, Is.EqualTo(1));
        Assert.That(Directory.GetFiles(root, "*-input-returned.json"), Is.Empty);
    }
    [Test] public async Task ChangedControlAfterScreenshotPreventsInput()
    {
        var transport = new Fake { ChangeAfterScreenshot = true };
        await using var session = await Open(transport);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ClickAsync(Control));
        Assert.That(transport.Clicks, Is.Zero);
    }
    [Test] public async Task LostOwnershipPreventsInput()
    {
        bool owned = true;
        var transport = new Fake();
        await using var session = await MacTestSession.OpenAsync(transport, root, Identity,
            _ => owned ? Task.CompletedTask : Task.FromException(new IOException("ownership lost")));
        owned = false;
        await Assert.ThrowsAsync<IOException>(() => session.ClickAsync(Control));
        Assert.That(transport.Clicks, Is.Zero);
    }
    [Test] public async Task SuccessRestoresDeviceAndHome()
    {
        bool state = false, home = false;
        var targets = new List<bool>();
        await using var session = await Open(new Fake());
        await session.VerifyBinaryControlAsync(_ => Task.FromResult(state), (target, _) => { targets.Add(target); state = target; return Task.CompletedTask; }, _ => { home = true; return Task.CompletedTask; });
        Assert.That(targets, Is.EqualTo(new[] { true, false }));
        Assert.That(state, Is.False);
        Assert.That(home, Is.True);
    }
    [Test] public async Task CancelledTestStillRestoresWithIndependentToken()
    {
        using var cancelled = new CancellationTokenSource();
        int calls = 0;
        bool home = false;
        await using var session = await Open(new Fake());
        await Assert.ThrowsAsync<AggregateException>(() => session.VerifyBinaryControlAsync(_ => Task.FromResult(false), (_, token) =>
        {
            if (++calls == 1) { cancelled.Cancel(); throw new OperationCanceledException(); }
            Assert.That(token.IsCancellationRequested, Is.False);
            return Task.CompletedTask;
        }, _ => { home = true; return Task.CompletedTask; }, cancelled.Token));
        Assert.That(calls, Is.EqualTo(2));
        Assert.That(home, Is.True);
    }
    [Test] public async Task OriginalFailureAndRestorationFailureAreBothRetained()
    {
        int calls = 0;
        bool home = false;
        await using var session = await Open(new Fake());
        var error = await Assert.ThrowsAsync<AggregateException>(() => session.VerifyBinaryControlAsync(_ => Task.FromResult(false),
            (_, _) => Task.FromException(++calls == 1 ? new IOException("original") : new InvalidOperationException("restore")),
            _ => { home = true; return Task.CompletedTask; }));
        Assert.That(error!.InnerExceptions.Select(e => e.Message), Is.EqualTo(new[] { "original", "restore" }));
        Assert.That(home, Is.True);
        Assert.That(File.ReadAllText(Directory.GetFiles(root, "*-control-result.json").Single()), Does.Contain("\"Passed\":false"));
    }
    [Test] public async Task UiAgreementCannotHideIndependentStateMismatch()
    {
        await using var session = await Open(new Fake());
        var error = await Assert.ThrowsAsync<AggregateException>(() => session.VerifyBinaryControlAsync(_ => Task.FromResult(false), (_, _) => Task.CompletedTask, _ => Task.CompletedTask));
        Assert.That(error!.InnerExceptions.Single().Message, Does.Contain("disagrees"));
    }
    [Test] public async Task SessionCleanupIsIdempotent()
    {
        var transport = new Fake();
        var session = await Open(transport);
        await session.DisposeAsync(); await session.DisposeAsync();
        Assert.That(transport.Deletes, Is.EqualTo(1));
    }
    [Test] public async Task OpeningFailureDeletesCreatedSession()
    {
        var transport = new Fake { FailActivate = true };
        await Assert.ThrowsAsync<IOException>(async () => await Open(transport));
        Assert.That(transport.Deletes, Is.EqualTo(1));
    }
    [Test] public async Task ExistingEvidenceCannotBeOverwritten()
    {
        await using var session = await Open(new Fake());
        await Assert.ThrowsAsync<ArgumentException>(async () => await Open(new Fake()));
    }
    [Test] public async Task CleanupFailureIsReported()
    {
        var session = await Open(new Fake { FailDelete = true });
        await Assert.ThrowsAsync<IOException>(async () => await session.DisposeAsync());
        Assert.That(Directory.GetFiles(root, "*-session-cleanup-failed.json"), Has.Length.EqualTo(1));
    }
    private Task<MacTestSession> Open(Fake fake) => MacTestSession.OpenAsync(fake, root, Identity, _ => Task.CompletedTask);

    internal sealed class Fake : IMacTransport
    {
        public int Clicks, Deletes, ElementCount = 1;
        public bool FailClick, FailActivate, FailDelete, ChangeAfterScreenshot, InvalidScreenshot;
        public Action? BeforeClick;
        private bool screenshot;
        public Task<JsonElement> SendAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            object? value = null;
            if (path == "session") value = new { sessionId = "test-session" };
            else if (method == HttpMethod.Delete) { Deletes++; if (FailDelete) throw new IOException("delete"); }
            else if (path.EndsWith("execute/sync") && FailActivate) throw new IOException("activate");
            else if (path.EndsWith("/source")) value = Window(screenshot && ChangeAfterScreenshot ? "" : Button);
            else if (path.EndsWith("/screenshot")) { screenshot = true; value = Convert.ToBase64String(InvalidScreenshot ? new byte[] {0} : new byte[] {137,80,78,71,13,10,26,10,0}); }
            else if (path.EndsWith("/elements")) value = Enumerable.Range(0, ElementCount).Select(i => new Dictionary<string,string> { ["element-6066-11e4-a52e-4f735466cecf"] = i.ToString() }).ToArray();
            else if (path.EndsWith("/click")) { BeforeClick?.Invoke(); Clicks++; if (FailClick) throw new IOException("click"); }
            return Task.FromResult(JsonSerializer.SerializeToElement(value));
        }
    }

    [TestCase("http://192.0.2.1:4727/")][TestCase("http://user@localhost:4727/")]
    [TestCase("http://localhost:4727/path")][TestCase("http://localhost:4727/?query")]
    public void RemoteOrAmbiguousEndpointsAreRejected(string url) => Assert.Throws<ArgumentException>(() => new MacHttpTransport(new Uri(url)));
    [TestCase(500)][TestCase(302)]
    public async Task FailedHttpCommandsAreNotRetried(int status)
    {
        var handler = new Handler(status, "{\"value\":{\"message\":\"secret\"}}");
        using var transport = new MacHttpTransport(new Uri("http://127.0.0.1:4727"), handler);
        var error = await Assert.ThrowsAsync<IOException>(() => transport.SendAsync(HttpMethod.Post, "session/x/element/e/click", new {}, CancellationToken.None));
        Assert.That(handler.Calls, Is.EqualTo(1));
        Assert.That(error!.Message, Does.Not.Contain("secret"));
    }
    [Test] public async Task WebDriverErrorInSuccessfulHttpIsRejected()
    {
        using var transport = new MacHttpTransport(new Uri("http://127.0.0.1:4727"), new Handler(200, "{\"value\":{\"error\":\"unknown error\",\"message\":\"private\"}}"));
        await Assert.ThrowsAsync<IOException>(() => transport.SendAsync(HttpMethod.Post, "session", new {}, CancellationToken.None));
    }
    private sealed class Handler(int status, string body) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Calls++; return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body) }); }
    }
}
