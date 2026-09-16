// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text;
using System.Xml;

using NUnit.Framework;

namespace CrestronHomeNUnit.Android.Tests;

[TestFixture]
public sealed class AndroidNavigationTests
	{
	private const string Application = "example.app";
	private static AndroidSelector Selector => new (AndroidSelectorKind.ResourceId, "example.app:id/open");
	private const string Node = """
		<node package="example.app" resource-id="example.app:id/open" text="Open" content-desc="Navigate" bounds="[10,20][110,80]" enabled="true" password="false" />
		""";
	private static string Document (string nodes) => "<hierarchy>" + nodes + "</hierarchy>";

	[Test]
	public async Task TapUsesFreshObservedBoundsAfterPageValidation ()
		{
		var transport = new FakeTransport (Document (Node));
		var device = new AndroidDevice (transport, Application);
		var validated = false;
		await device.TapAsync (Selector, hierarchy =>
			{
				Assert.That (hierarchy.RequireUnique (Selector).Text, Is.EqualTo ("Open"));
				validated = true;
			});
		Assert.That (validated, Is.True);
		Assert.That (transport.Commands.Last (), Is.EqualTo (new[] { "shell", "input", "tap", "60", "50" }));
		var dumped = transport.Commands.Single (args => args.Contains ("uiautomator"))[^1];
		Assert.That (transport.Commands.Single (args => args.Contains ("rm"))[^1], Is.EqualTo (dumped));
		}

	[TestCase ("absent")]
	[TestCase ("duplicate")]
	[TestCase ("disabled")]
	[TestCase ("otherApp")]
	[TestCase ("password")]
	[TestCase ("invalidBounds")]
	public void UnsafeTargetDoesNotSendInput (string defect)
		{
		var xml = defect switch
			{
				"absent" => Document (""),
				"duplicate" => Document (Node + Node),
				"disabled" => Document (Node.Replace ("enabled=\"true\"", "enabled=\"false\"")),
				"otherApp" => Document (Node.Replace ("package=\"example.app\"", "package=\"other.app\"")),
				"password" => Document (Node.Replace ("password=\"false\"", "password=\"true\"")),
				_ => Document (Node.Replace ("[10,20][110,80]", "[-1,20][10,10]"))
				};
		var transport = new FakeTransport (xml);
		var device = new AndroidDevice (transport, Application);
		Assert.CatchAsync<Exception> (() => device.TapAsync (Selector, _ => { }));
		Assert.That (transport.Commands.Any (args => args.Contains ("input")), Is.False);
		}

	[Test]
	public void FailedPageGuardPreventsInput ()
		{
		var transport = new FakeTransport (Document (Node));
		var device = new AndroidDevice (transport, Application);
		Assert.ThrowsAsync<InvalidOperationException> (() => device.TapAsync (Selector, _ => throw new InvalidOperationException ("Wrong processor home")));
		Assert.That (transport.Commands.Any (args => args.Contains ("input")), Is.False);
		}

	[Test]
	public void TimedOutInputIsNeverReplayed ()
		{
		var transport = new FakeTransport (Document (Node)) { FailInput = true };
		var device = new AndroidDevice (transport, Application);
		Assert.ThrowsAsync<TimeoutException> (() => device.TapAsync (Selector, _ => { }));
		Assert.That (transport.Commands.Count (args => args.Contains ("input")), Is.EqualTo (1));
		}

	[Test]
	public void FailedCaptureDoesNotSendInput ()
		{
		var transport = new FakeTransport (Document (Node)) { FailDump = true };
		var device = new AndroidDevice (transport, Application);
		Assert.ThrowsAsync<IOException> (() => device.TapAsync (Selector, _ => { }));
		Assert.That (transport.Commands.Any (args => args.Contains ("input")), Is.False);
		Assert.That (transport.Commands.Count (args => args.Contains ("rm")), Is.EqualTo (1));
		}

	[Test]
	public void PasswordTextAndDescriptionAreMasked ()
		{
		var xml = Document (Node.Replace ("password=\"false\"", "password=\"true\"").Replace ("text=\"Open\"", "text=\"private-sentinel\"").Replace ("content-desc=\"Navigate\"", "content-desc=\"private-sentinel\""));
		Assert.That (new AndroidHierarchy (xml, Application).MaskedXml, Does.Not.Contain ("private-sentinel"));
		}

	[Test]
	public void HierarchyCannotReadExternalEntities () =>
		Assert.Throws<XmlException> (() => new AndroidHierarchy ("<!DOCTYPE hierarchy [<!ENTITY x SYSTEM 'file:///private'>]><hierarchy>&x;</hierarchy>", Application));

	[Test]
	public void UnexpectedHierarchyRootIsRejected () =>
		Assert.Throws<InvalidDataException> (() => new AndroidHierarchy ("<unexpected />", Application));

	[TestCase ("[10,20][10,80]")]
	[TestCase ("[10,20][110,20]")]
	[TestCase ("[10,20][999999,80]")]
	[TestCase ("not bounds")]
	[TestCase ("10,20,110,80")]
	public void InvalidCoordinatesCannotBecomeATap (string bounds) =>
		Assert.Throws<InvalidDataException> (() => new AndroidHierarchy (Document (Node.Replace ("[10,20][110,80]", bounds)), Application).RequireUnique (Selector));

	[TestCase (AndroidSelectorKind.ResourceId, "example.app:id/open")]
	[TestCase (AndroidSelectorKind.Text, "Open")]
	[TestCase (AndroidSelectorKind.ContentDescription, "Navigate")]
	public void SupportsSemanticSelectors (AndroidSelectorKind kind, string value) =>
		Assert.That (new AndroidHierarchy (Document (Node), Application).RequireUnique (new (kind, value)).Text, Is.EqualTo ("Open"));

	[Test]
	public void InvalidScreenshotCannotBeRetainedAsPng () =>
		Assert.ThrowsAsync<InvalidDataException> (() => new AndroidDevice (new FakeTransport (Document (Node)), Application).CaptureScreenshotAsync ());

	private sealed class FakeTransport (string hierarchy) : IAndroidCommandTransport
		{
		public List<string[]> Commands { get; } = [];
		public bool FailInput
			{
			get; init;
			}
		public bool FailDump
			{
			get; init;
			}

		public Task<byte[]> ExecuteAsync (IReadOnlyList<string> arguments, CancellationToken cancellationToken)
			{
			cancellationToken.ThrowIfCancellationRequested ();
			Commands.Add (arguments.ToArray ());
			if (arguments.Contains ("input") && FailInput)
				throw new TimeoutException ("Unknown input outcome");
			var response = arguments.Contains ("uiautomator") ? (FailDump ? "ERROR: null root node" : "UI hierarchy dumped to: " + arguments[^1]) : arguments.Contains ("cat") ? hierarchy : "";
			return Task.FromResult (Encoding.UTF8.GetBytes (response));
			}
		}
	}