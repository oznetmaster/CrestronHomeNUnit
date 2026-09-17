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
	private static string LabelRow (string label, string child = Node) =>
		"<node package='example.app' resource-id='example.app:id/row'>" +
		"<node package='example.app' text='" + label + "' password='false' />" + child + "</node>";

	[Test]
	public async Task SiblingLabel_SelectsOnlyTheNamedRowAmongRepeatedButtons ()
		{
		var transport = new FakeTransport (Document (LabelRow ("First") + LabelRow ("Second", Node.Replace ("[10,20][110,80]", "[110,20][210,80]"))));
		var selector = Selector with { SiblingText = "Second", AncestorResourceId = "example.app:id/row" };
		await new AndroidDevice (transport, Application).TapAsync (selector, h => Assert.That (h.RequireUnique (selector).Left, Is.EqualTo (110)));
		Assert.That (transport.Commands.Last (), Is.EqualTo (new[] { "shell", "input", "tap", "160", "50" }));
		}

	[TestCase ("missing")]
	[TestCase ("duplicate-row")]
	[TestCase ("duplicate-label")]
	[TestCase ("nested-label")]
	[TestCase ("foreign-label")]
	[TestCase ("foreign-parent")]
	[TestCase ("password-label")]
	[TestCase ("wrong-ancestor")]
	[TestCase ("blank-label")]
	public void UnsafeSiblingLabel_SendsNoInput (string defect)
		{
		string row = LabelRow ("First");
		string label = "<node package='example.app' text='First' password='false' />";
		row = defect switch
			{
			"missing" => row.Replace (label, ""),
			"duplicate-row" => row + row,
			"duplicate-label" => row.Replace (label, label + label),
			"nested-label" => row.Replace (label, "<node package='example.app'>" + label + "</node>"),
			"foreign-label" => row.Replace (label, label.Replace ("example.app", "other.app")),
			"foreign-parent" => row.Replace ("package='example.app' resource-id", "package='other.app' resource-id"),
			"password-label" => row.Replace ("password='false'", "password='true'"),
			_ => row
			};
		var selector = Selector with { SiblingText = defect == "blank-label" ? " " : "First", AncestorResourceId = defect == "wrong-ancestor" ? "missing" : null };
		var transport = new FakeTransport (Document (row));
		Assert.CatchAsync<Exception> (() => new AndroidDevice (transport, Application).TapAsync (selector, _ => { }));
		Assert.That (transport.Commands.Any (c => c.Contains ("input")), Is.False);
		}

	[Test]
	public void SiblingScopedInputFailure_IsNotReplayed ()
		{
		var transport = new FakeTransport (Document (LabelRow ("First") + LabelRow ("Second"))) { FailInput = true };
		Assert.ThrowsAsync<TimeoutException> (() => new AndroidDevice (transport, Application).TapAsync (Selector with { SiblingText = "First" }, _ => { }));
		Assert.That (transport.Commands.Count (c => c.Contains ("input")), Is.EqualTo (1));
		}

	[Test]
	public async Task TransientCaptureFailureRepeatsOnlyReadsBeforeOneTap ()
		{
		var transport = new FakeTransport (Document (Node)) { FailedDumpsRemaining = 1 };
		await new AndroidDevice (transport, Application).TapAsync (Selector, _ => { });
		Assert.That (transport.Commands.Count (c => c.Contains ("uiautomator")), Is.EqualTo (2));
		Assert.That (transport.Commands.Count (c => c.Contains ("input")), Is.EqualTo (1));
		Assert.That (transport.Commands.Where (c => c.Contains ("rm")).All (c => c.Contains ("-f")), Is.True);
		}

	[Test]
	public void CaptureFailureSurvivesSecondaryCleanupFailure ()
		{
		var transport = new FakeTransport (Document (Node)) { FailDump = true, FailCleanup = true };
		var error = Assert.ThrowsAsync<IOException> (() => new AndroidDevice (transport, Application).CaptureAsync ());
		Assert.That (error!.Message, Does.Contain ("could not capture"));
		Assert.That (transport.Commands.Count (c => c.Contains ("uiautomator")), Is.EqualTo (3));
		Assert.That (transport.Commands.Any (c => c.Contains ("input")), Is.False);
		}

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
		var dumps = transport.Commands.Where (args => args.Contains ("uiautomator")).Select (args => args[^1]).ToArray ();
		Assert.That (dumps, Has.Length.EqualTo (3).And.Unique);
		Assert.That (transport.Commands.Where (args => args.Contains ("rm")).Select (args => args[^1]), Is.EquivalentTo (dumps));
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
		public int FailedDumpsRemaining;
		public bool FailCleanup;
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
			if (arguments.Contains ("rm") && FailCleanup) throw new IOException ("Cleanup failed");
			if (arguments.Contains ("uiautomator") && FailedDumpsRemaining-- > 0) throw new IOException ("Transient capture failure");
			if (arguments.Contains ("input") && FailInput)
				throw new TimeoutException ("Unknown input outcome");
			var response = arguments.Contains ("uiautomator") ? (FailDump ? "ERROR: null root node" : "UI hierarchy dumped to: " + arguments[^1]) : arguments.Contains ("cat") ? hierarchy : "";
			return Task.FromResult (Encoding.UTF8.GetBytes (response));
			}
		}
	}