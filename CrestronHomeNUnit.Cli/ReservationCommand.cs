// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Net;
using System.Text.Json;

using CrestronHomeNUnit.Client;

internal static class ReservationCommand
	{
	private sealed record Receipt (string Host, string SshFingerprint, string Owner);
	internal static async Task<int> RunAsync (string[] arguments)
		{
		try
			{
			if (arguments.Length != 5 || arguments[1] != "--settings" || arguments[3] != "--receipt")
				throw new ArgumentException ("Use reserve|release --settings private.json --receipt private-receipt.json.");
			var json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
			var settings = JsonSerializer.Deserialize<CliSettings> (await File.ReadAllTextAsync (arguments[2]), json) ?? throw new ArgumentException ("Invalid settings.");
			var user = Environment.GetEnvironmentVariable ("CRESTRON_HOME_USER") ?? settings.UserName;
			var password = Environment.GetEnvironmentVariable ("CRESTRON_HOME_PASSWORD") ?? settings.Password;
			if (string.IsNullOrWhiteSpace (user) || string.IsNullOrEmpty (password)) throw new ArgumentException ("Processor credentials are required.");
			using var deadline = new CancellationTokenSource (TimeSpan.FromSeconds (45));
			var credentials = new NetworkCredential (user, password);
			if (arguments[0] == "reserve")
				{
				var host = Environment.GetEnvironmentVariable ("CRESTRON_HOME_HOST") ?? settings.Host;
				var fingerprint = Environment.GetEnvironmentVariable ("CRESTRON_HOME_SSH_FINGERPRINT") ?? settings.SshFingerprint;
				if (string.IsNullOrWhiteSpace (host) || string.IsNullOrWhiteSpace (fingerprint)) throw new ArgumentException ("Reservation requires Host and a verified SshFingerprint.");
				var receipt = new Receipt (host, fingerprint, Guid.NewGuid ().ToString ("N"));
				// Persist the intended owner before claiming. Never overwrite an earlier receipt.
				await using (var output = new FileStream (arguments[4], FileMode.CreateNew, FileAccess.Write, FileShare.None))
					await JsonSerializer.SerializeAsync (output, receipt, cancellationToken: deadline.Token);
				using var lease = await ProcessorLease.AcquireAsync (host, credentials, fingerprint, receipt.Owner, deadline.Token);
				Console.WriteLine ("Processor reserved. Workflows will wait or fail busy until you release this exact receipt. No driver was changed.");
				}
			else
				{
				var receipt = JsonSerializer.Deserialize<Receipt> (await File.ReadAllTextAsync (arguments[4]), json) ?? throw new ArgumentException ("Invalid reservation receipt.");
				using var lease = await ProcessorLease.ResumeAsync (receipt.Host, credentials, receipt.SshFingerprint, receipt.Owner, deadline.Token);
				await lease.ReleaseAsync (deadline.Token);
				Console.WriteLine ("Manual reservation released. The receipt is retained for your records.");
				}
			return 0;
			}
		catch (Exception exception)
			{
			Console.Error.WriteLine (exception is ArgumentException or ProcessorBusyException ? exception.Message : "Reservation could not complete. Inspect its private receipt and the processor lease; no ownership was overridden.");
			return 2;
			}
		}
	}