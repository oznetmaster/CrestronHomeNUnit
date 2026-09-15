// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text;
using System.Text.Json;

using CrestronHomeDevTools;
using CrestronHomeNUnit.Workflow;

using NUnit.Framework;

namespace CrestronHomeNUnit.Workflow.Tests;

public sealed class DebugVersionTests
	{
	private static DriverInfo Driver (string? version, string model = "Example") => new () { Id = "catalogue", Model = model, Version = version };

	[TestCase ("1.2.3.0002", "1.002.003.0042", "1.2.3.42")]
	[TestCase ("1.2.3.0042", "1.2.3.2", "1.2.3.42")]
	[TestCase ("1.2.3.0002", "1.2.2.5000", "1.2.3.2")]
	public void ManualVersionsAndPaddingAreReconciledWithoutChangingRelease (string source, string catalogue, string expected)
		=> Assert.That (WorkflowDebugVersion.SelectBaseline ("example ", Version.Parse (source), [Driver (catalogue)]), Is.EqualTo (Version.Parse (expected)));

	[Test]
	public void HighestOfAllMatchingVersionsWins ()
		=> Assert.That (WorkflowDebugVersion.SelectBaseline ("Example", new (1, 2, 3, 2), [Driver ("1.2.3.4"), Driver ("1.2.3.19"), Driver ("1.2.3.8")]), Is.EqualTo (new Version (1, 2, 3, 19)));

	[Test]
	public void UnrelatedModelsCannotAffectTheRevision ()
		=> Assert.That (WorkflowDebugVersion.SelectBaseline ("Example", new (1, 2, 3, 2), [Driver (null, "Another model"), Driver ("99.0.0.0", "Other")]), Is.EqualTo (new Version (1, 2, 3, 2)));

	[TestCase ("2.0.0.0")]
	[TestCase ("1.3.0.0")]
	[TestCase ("1.2.4.0")]
	[TestCase ("1.2.3.65534")]
	public void NewerReleaseOrExhaustedRevisionRequiresDeliberateVersionChange (string catalogue)
		=> Assert.Throws<InvalidOperationException> (() => WorkflowDebugVersion.SelectBaseline ("Example", new (1, 2, 3, 2), [Driver (catalogue)]));

	[TestCase (null)]
	[TestCase ("1.2.3")]
	[TestCase ("unknown")]
	[TestCase ("1.2.3.65535")]
	public void UnreadableMatchingVersionCannotBeIgnored (string? catalogue)
		=> Assert.Throws<InvalidDataException> (() => WorkflowDebugVersion.SelectBaseline ("Example", new (1, 2, 3, 2), [Driver (catalogue)]));

	[TestCase (false)]
	[TestCase (true)]
	public async Task ManifestWritePreservesBomFormattingAndAllOtherFields (bool bom)
		{
		var path = Path.GetTempFileName ();
		try
			{
			var text = "{\r\n  \"GeneralInformation\": {\"Guid\":\"11111111-1111-1111-1111-111111111111\", \"BaseModel\":\"Example\", \"DriverVersion\":\"1.2.003.0002\"},\r\n  \"Description\": \"Temperature Â°C\"\r\n}";
			var encoding = new UTF8Encoding (bom);
			await File.WriteAllTextAsync (path, text, encoding);
			var result = await WorkflowDebugVersion.PrepareAsync (path, (_, _) => Task.FromResult<IReadOnlyList<DriverInfo>> ([Driver ("1.2.3.42")]), CancellationToken.None);
			var expected = encoding.GetPreamble ().Concat (encoding.GetBytes (text.Replace ("1.2.003.0002", "1.2.003.0042"))).ToArray ();
			Assert.That (await File.ReadAllBytesAsync (path), Is.EqualTo (expected));
			Assert.That (result!.Baseline, Is.EqualTo (new Version (1, 2, 3, 42)));
			}
		finally { File.Delete (path); }
		}

	[Test]
	public async Task CancellationDuringCatalogueLookupLeavesManifestUnchanged ()
		{
		var path = Path.GetTempFileName ();
		using var cancellation = new CancellationTokenSource ();
		try
			{
			var text = JsonSerializer.Serialize (new { GeneralInformation = new { Guid = Guid.NewGuid (), BaseModel = "Example", DriverVersion = "1.2.3.2" } });
			await File.WriteAllTextAsync (path, text);
			Assert.ThrowsAsync<OperationCanceledException> (() => WorkflowDebugVersion.PrepareAsync (path, (_, _) =>
				{
				cancellation.Cancel ();
				return Task.FromResult<IReadOnlyList<DriverInfo>> ([Driver ("1.2.3.42")]);
				}, cancellation.Token));
			Assert.That (await File.ReadAllTextAsync (path), Is.EqualTo (text));
			}
		finally { File.Delete (path); }
		}

	[Test]
	public async Task CustomManifestLayoutDoesNotGuessOrContactProcessor ()
		=> Assert.That (await WorkflowDebugVersion.PrepareAsync (Path.Combine (Path.GetTempPath (), Guid.NewGuid () + ".json"),
			(_, _) => throw new AssertionException ("No catalogue lookup expected"), CancellationToken.None), Is.Null);

	[TestCase ("1.2.3.42", "Example", false)]
	[TestCase ("1.2.3.43", "Example", true)]
	[TestCase ("1.2.4.0", "Example", false)]
	[TestCase ("1.2.3.43", "Another model", false)]
	public void BuiltBytesMustHaveTheExpectedIdentityAndAFreshRevision (string version, string model, bool passes)
		{
		var id = Guid.NewGuid ();
		var prepared = new PreparedDebugVersion (id, "Example", new (1, 2, 3, 42));
		var package = new DriverPackageInfo (id.ToString (), model, "Example", version);
		if (passes) Assert.DoesNotThrow (() => prepared.VerifyBuiltPackage (package));
		else Assert.Throws<InvalidDataException> (() => prepared.VerifyBuiltPackage (package));
		Assert.Throws<InvalidDataException> (() => prepared.VerifyBuiltPackage (package with { DriverId = Guid.NewGuid ().ToString () }));
		}
	}