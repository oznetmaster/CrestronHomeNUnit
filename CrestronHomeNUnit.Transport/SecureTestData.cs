// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CrestronHomeNUnit.Transport;

// Pairing keys are proved by challenge/response, never sent by protocol 2.
// Encrypt-then-MAC protects configuration files and binds them to this connection and request.
public static class SecureTestData
	{
	public const int MaximumFileBytes = 1024 * 1024;
	public const int MaximumTotalBytes = 4 * MaximumFileBytes;
	public static string Nonce ()
		{
		var bytes = new byte[32];
		using var random = RandomNumberGenerator.Create ();
		random.GetBytes (bytes);
		return Convert.ToBase64String (bytes);
		}
	public static byte[] Key (string pairingKey, string clientNonce, string serverNonce)
		{
		ValidateNonce (clientNonce);
		ValidateNonce (serverNonce);
		return Mac (Encoding.UTF8.GetBytes (pairingKey), Encoding.UTF8.GetBytes ("session\n" + clientNonce + "\n" + serverNonce));
		}
	public static string Proof (byte[] key, string role) => Convert.ToBase64String (Mac (key, Encoding.UTF8.GetBytes (role)));
	public static bool Verify (byte[] key, string role, string proof)
		{
		try
			{
			return Equal (Mac (key, Encoding.UTF8.GetBytes (role)), Convert.FromBase64String (proof));
			}
		catch (FormatException) { return false; }
		}
	public static string Protect (byte[] sessionKey, WireMessage request, IReadOnlyList<TestInputFile> files)
		{
		ValidateFiles (files);
		using var plain = new MemoryStream ();
		new DataContractJsonSerializer (typeof (List<TestInputFile>)).WriteObject (plain, new List<TestInputFile> (files));
		using var aes = Aes.Create ();
		aes.Key = Mac (sessionKey, Encoding.UTF8.GetBytes ("encryption"));
		aes.GenerateIV ();
		using var encryptor = aes.CreateEncryptor ();
		byte[] ciphertext = encryptor.TransformFinalBlock (plain.ToArray (), 0, (int)plain.Length);
		byte[] envelope = new byte[16 + ciphertext.Length + 32];
		Buffer.BlockCopy (aes.IV, 0, envelope, 0, 16);
		Buffer.BlockCopy (ciphertext, 0, envelope, 16, ciphertext.Length);
		byte[] tag = Tag (sessionKey, request, envelope, envelope.Length - 32);
		Buffer.BlockCopy (tag, 0, envelope, envelope.Length - 32, 32);
		return Convert.ToBase64String (envelope);
		}
	public static List<TestInputFile> Unprotect (byte[] sessionKey, WireMessage request)
		{
		byte[] envelope = Convert.FromBase64String (request.ProtectedData);
		if (envelope.Length < 64 || envelope.Length > MaximumTotalBytes * 2)
			throw new InvalidDataException ("Invalid test-input payload length.");
		byte[] tag = new byte[32];
		Buffer.BlockCopy (envelope, envelope.Length - 32, tag, 0, 32);
		if (!Equal (Tag (sessionKey, request, envelope, envelope.Length - 32), tag))
			throw new InvalidDataException ("Test-input authentication failed.");
		using var aes = Aes.Create ();
		aes.Key = Mac (sessionKey, Encoding.UTF8.GetBytes ("encryption"));
		var iv = new byte[16];
		Buffer.BlockCopy (envelope, 0, iv, 0, 16);
		aes.IV = iv;
		using var decryptor = aes.CreateDecryptor ();
		byte[] plain = decryptor.TransformFinalBlock (envelope, 16, envelope.Length - 48);
		using var stream = new MemoryStream (plain);
		var files = (List<TestInputFile>)new DataContractJsonSerializer (typeof (List<TestInputFile>)).ReadObject (stream)!;
		ValidateFiles (files);
		return files;
		}
	public static void ValidateFiles (IReadOnlyList<TestInputFile> files)
		{
		if (files == null || files.Count > 16)
			throw new InvalidDataException ("At most 16 test-input files are supported.");
		var names = new HashSet<string> (StringComparer.OrdinalIgnoreCase);
		int total = 0;
		foreach (TestInputFile file in files)
			{
			if (file == null || string.IsNullOrWhiteSpace (file.Name) || file.Name.Length > 120 ||
				 !Regex.IsMatch (file.Name, "^[A-Za-z0-9][A-Za-z0-9_.-]*[A-Za-z0-9]$") ||
				 Regex.IsMatch (file.Name, "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])([.]|$)", RegexOptions.IgnoreCase) ||
				 file.Name.IndexOfAny (new[] { '/', '\\', ':', '\0' }) >= 0 || file.Name == "." || file.Name == ".." ||
				 file.Name.IndexOfAny (Path.GetInvalidFileNameChars ()) >= 0 || !names.Add (file.Name) || file.Content == null)
				throw new InvalidDataException ("Test inputs require unique plain filenames.");
			if (file.Content.Length > MaximumFileBytes || (total += file.Content.Length) > MaximumTotalBytes)
				throw new InvalidDataException ("Test-input files exceed the 1 MB per-file or 4 MB total limit.");
			}
		}
	private static byte[] Tag (byte[] key, WireMessage request, byte[] envelope, int length)
		{
		byte[] context = Encoding.UTF8.GetBytes (request.RequestId + "\n" + request.Kind + "\n" + request.Suite + "\n" + (request.EnableLiveTests ? "EnableLiveTests=true\n" : ""));
		var data = new byte[context.Length + length];
		Buffer.BlockCopy (context, 0, data, 0, context.Length);
		Buffer.BlockCopy (envelope, 0, data, context.Length, length);
		return Mac (Mac (key, Encoding.UTF8.GetBytes ("authentication")), data);
		}
	private static byte[] Mac (byte[] key, byte[] data)
		{
		using var hmac = new HMACSHA256 (key);
		return hmac.ComputeHash (data);
		}
	private static bool Equal (byte[] expected, byte[] actual)
		{
		if (expected.Length != actual.Length)
			return false;
		int difference = 0;
		for (int i = 0; i < expected.Length; i++)
			difference |= expected[i] ^ actual[i];
		return difference == 0;
		}
	private static void ValidateNonce (string nonce)
		{
		if (Convert.FromBase64String (nonce).Length != 32)
			throw new InvalidDataException ("Invalid handshake nonce.");
		}
	}

[DataContract]
public sealed class TestInputFile
	{
	[DataMember]
	public string Name { get; set; } = "";
	[IgnoreDataMember]
	public byte[] Content { get; set; } = [];
	[DataMember (Name = "Content")]
	public string EncodedContent
		{
		get => Convert.ToBase64String (Content); set => Content = Convert.FromBase64String (value);
		}
	}