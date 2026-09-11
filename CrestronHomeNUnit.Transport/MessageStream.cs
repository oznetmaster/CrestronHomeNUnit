// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Runtime.Serialization.Json;

namespace CrestronHomeNUnit.Transport;
/// <summary>Four-byte network-order length followed by UTF-8 JSON; one writer at a time.</summary>
public sealed class MessageStream (Stream stream)
	{
	public const int MaximumFrameBytes = 16 * 1024 * 1024;
	private readonly object _writeGate = new ();
	public WireMessage? Read ()
		{
		var header = new byte[4];
		int first = stream.ReadByte ();
		if (first == -1)
			{
			return null;
			}

		header[0] = (byte)first;
		ReadExactly (header, 1, 3);
		int length = (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3];
		if (length <= 0 || length > MaximumFrameBytes)
			{
			throw new InvalidDataException ("Invalid TCP message length.");
			}

		var bytes = new byte[length];
		ReadExactly (bytes, 0, length);
		using var payload = new MemoryStream (bytes, false);
		var message = (WireMessage?)new DataContractJsonSerializer (typeof (WireMessage)).ReadObject (payload) ?? throw new InvalidDataException ("Empty TCP message.");
		if (message.Version is not (1 or 2) || string.IsNullOrEmpty (message.Kind) || string.IsNullOrEmpty (message.RequestId) || message.RequestId.Length > 100 || message.TestNames is null || message.TestNames.Count > 4096)
			{
			throw new InvalidDataException ("Unsupported or invalid protocol message.");
			}

		return message;
		}

	public void Write (WireMessage message)
		{
		using var payload = new MemoryStream ();
		new DataContractJsonSerializer (typeof (WireMessage)).WriteObject (payload, message);
		if (payload.Length > MaximumFrameBytes)
			{
			throw new InvalidDataException ("TCP message exceeds the size limit.");
			}

		int length = (int)payload.Length;
		byte[] header = [(byte)(length >> 24), (byte)(length >> 16), (byte)(length >> 8), (byte)length];
		lock (_writeGate)
			{
			stream.Write (header, 0, header.Length);
			payload.Position = 0;
			payload.CopyTo (stream);
			stream.Flush ();
			}
		}

	private void ReadExactly (byte[] buffer, int offset, int length)
		{
		while (length > 0)
			{
			int read = stream.Read (buffer, offset, length);
			if (read == 0)
				{
				throw new EndOfStreamException ("Connection closed during a TCP message.");
				}

			offset += read;
			length -= read;
			}
		}
	}