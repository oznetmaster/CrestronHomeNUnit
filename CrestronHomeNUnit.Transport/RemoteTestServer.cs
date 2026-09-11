// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace CrestronHomeNUnit.Transport;

public sealed class RemoteTestServer : IDisposable
	{
	public const int DefaultPort = 47261;
	private readonly TcpListener _listener;
	private readonly ITestExecutionHost _host;
	private readonly string _key;
	private readonly ConcurrentDictionary<TcpClient, byte> _clients = new ();
	private int _disposed;
	public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

	public RemoteTestServer (ITestExecutionHost host, string key, int port = DefaultPort, IPAddress? address = null)
		{
		if (string.IsNullOrWhiteSpace (key))
			{
			throw new ArgumentException ("A pairing key is required.", nameof (key));
			}

		_host = host;
		_key = key;
		_listener = new TcpListener (address ?? IPAddress.Any, port);
		_listener.Start (4);
		_ = Task.Run (AcceptClients);
		}

	private async Task AcceptClients ()
		{
		try
			{
			while (Volatile.Read (ref _disposed) == 0)
				{
				TcpClient client = await _listener.AcceptTcpClientAsync ().ConfigureAwait (false);
				if (Volatile.Read (ref _disposed) != 0 || _clients.Count >= 4)
					{
					client.Close ();
					continue;
					}

				_clients.TryAdd (client, 0);
				if (Volatile.Read (ref _disposed) != 0)
					{
					client.Close ();
					_clients.TryRemove (client, out _);
					continue;
					}

				_ = Task.Run (() => Serve (client));
				}
			}
		catch (ObjectDisposedException)
			{
			}
		catch (SocketException) when (Volatile.Read (ref _disposed) != 0)
			{
			}
		}

	private void Serve (TcpClient client)
		{
		var ownedOperations = new ConcurrentDictionary<string, byte> ();
		client.NoDelay = true;
		client.SendTimeout = 5000;
		client.ReceiveTimeout = 10000;
		try
			{
			var messages = new MessageStream (client.GetStream ());
			WireMessage hello = messages.Read () ?? throw new IOException ("Missing handshake.");
			byte[]? sessionKey = null;
			if (hello.Kind != "hello" || hello.Version != 2)
				{
				messages.Write (WireMessage.Reply (hello, "error", "This host requires the updated Windows runner (protocol 2)."));
				return;
				}
			string serverNonce = SecureTestData.Nonce ();
			sessionKey = SecureTestData.Key (_key, hello.Text, serverNonce);
			var challenge = WireMessage.Reply (hello, "hello-challenge", serverNonce);
			challenge.Version = 2;
			challenge.Xml = SecureTestData.Proof (sessionKey, "server");
			messages.Write (challenge);
			WireMessage proof = messages.Read () ?? throw new IOException ("Missing authentication proof.");
			if (proof.Kind != "hello-auth" || !SecureTestData.Verify (sessionKey, "client", proof.Text))
				{
				messages.Write (WireMessage.Reply (proof, "error", "Pairing key was not accepted."));
				return;
				}
			hello = proof;
			var reply = WireMessage.Reply (hello, "hello-ok", "Crestron Home NUnit protocol 2");
			reply.Version = 2;
			reply.Suites = (_host as ITestSuiteProvider)?.Suites.ToList ();
			messages.Write (reply);
			client.ReceiveTimeout = 0;
			WireMessage? request;
			while ((request = messages.Read ()) != null)
				{
				if (!string.IsNullOrEmpty (request.ProtectedData))
					{
					request.TestInputs = SecureTestData.Unprotect (sessionKey, request);
					request.ProtectedData = "";
					}
				if (request.Kind == "cancel")
					{
					bool cancelled = ownedOperations.ContainsKey (request.TargetId) && _host.Cancel (request.TargetId);
					messages.Write (WireMessage.Reply (request, "cancelled", cancelled ? "Cancellation requested; waiting for the active test." : "That operation is no longer active."));
					continue;
					}

				if (request.Kind is not ("run" or "discover"))
					{
					messages.Write (WireMessage.Reply (request, "error", "Unknown command."));
					continue;
					}

				if (!ownedOperations.TryAdd (request.RequestId, 0))
					{
					throw new InvalidDataException ("Duplicate active request identifier.");
					}

				WireMessage operation = request;
				_ = Task.Run (async () =>
				{
					void Send (WireMessage message)
						{
						try
							{
							messages.Write (message);
							}
						catch (Exception)
							{
							client.Close ();
							_host.Cancel (operation.RequestId);
							}
						}

					try
						{
						WireMessage result = await _host.ExecuteAsync (operation, Send).ConfigureAwait (false);
						Send (result);
						}
					catch (Exception exception)
						{
						Send (WireMessage.Reply (operation, "error", exception.Message));
						}
					finally
						{
						ownedOperations.TryRemove (operation.RequestId, out _);
						}
				});
				}
			}
		catch (Exception)
			{
			}
		finally
			{
			client.Close ();
			foreach (string operationId in ownedOperations.Keys)
				{
				_host.Cancel (operationId);
				}

			_clients.TryRemove (client, out _);
			}
		}

	private static bool KeysMatch (string? actual, string expected)
		{
		if (actual is null || actual.Length != expected.Length)
			{
			return false;
			}

		int difference = 0;
		for (int i = 0; i < actual.Length; i++)
			{
			difference |= actual[i] ^ expected[i];
			}

		return difference == 0;
		}

	public void Dispose ()
		{
		if (Interlocked.Exchange (ref _disposed, 1) != 0)
			{
			return;
			}

		_listener.Stop ();
		foreach (TcpClient client in _clients.Keys)
			{
			client.Close ();
			}
		}
	}