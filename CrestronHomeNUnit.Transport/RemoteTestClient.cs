// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;

namespace CrestronHomeNUnit.Transport;

public sealed class RemoteTestClient : IDisposable
	{
	private readonly TcpClient _client;
	private readonly MessageStream _messages;
	private readonly ConcurrentDictionary<string, TaskCompletionSource<WireMessage>> _pending = new ();
	private int _disposed;
	private byte[]? _sessionKey;
	public bool SupportsTestInputs => _sessionKey != null;
	public IReadOnlyList<TestSuiteInfo> Suites { get; private set; } = [];
	public event Action<WireMessage>? Progress;
	public event Action<string>? Disconnected;
	private RemoteTestClient (TcpClient client)
		{
		_client = client;
		client.NoDelay = true;
		client.SendTimeout = 5000;
		_messages = new MessageStream (client.GetStream ());
		_ = Task.Run (Receive);
		}

	public static async Task<RemoteTestClient> ConnectAsync (string host, int port, string key)
		{
		var socket = new TcpClient ();
		try
			{
			Task connecting = socket.ConnectAsync (host, port);
			if (await Task.WhenAny (connecting, Task.Delay (10000)).ConfigureAwait (false) != connecting)
				{
				socket.Close ();
				try
					{
					await connecting.ConfigureAwait (false);
					}
				catch (Exception)
					{
					}

				throw new TimeoutException ("The processor did not accept the TCP connection.");
				}

			await connecting.ConfigureAwait (false);
			var client = new RemoteTestClient (socket);
			try
				{
				string clientNonce = SecureTestData.Nonce ();
				Task<WireMessage> hello = client.SendAsync (new WireMessage { Kind = "hello", Version = 2, Text = clientNonce });
				if (await Task.WhenAny (hello, Task.Delay (10000)).ConfigureAwait (false) != hello)
					{
					client.Dispose ();
					try
						{
						await hello.ConfigureAwait (false);
						}
					catch (Exception)
						{
						}

					throw new TimeoutException ("The processor did not complete the handshake.");
					}

				WireMessage reply = await hello.ConfigureAwait (false);
				if (reply.Kind == "hello-challenge")
					{
					byte[] sessionKey = SecureTestData.Key (key, clientNonce, reply.Text);
					if (!SecureTestData.Verify (sessionKey, "server", reply.Xml))
						throw new AuthenticationException ("Pairing key was not accepted, or the host could not be authenticated.");
					Task<WireMessage> authentication = client.SendAsync (new WireMessage { Kind = "hello-auth", Version = 2, Text = SecureTestData.Proof (sessionKey, "client") });
					if (await Task.WhenAny (authentication, Task.Delay (10000)).ConfigureAwait (false) != authentication)
						throw new TimeoutException ("The processor did not complete authentication.");
					reply = await authentication.ConfigureAwait (false);
					if (reply.Kind == "hello-ok" && reply.Version == 2)
						client._sessionKey = sessionKey;
					}
				if (reply.Kind != "hello-ok")
					{
					throw new AuthenticationException ("The processor rejected the connection handshake.");
					}

				client.Suites = reply.Suites ?? [];
				return client;
				}
			catch
				{
				client.Dispose ();
				throw;
				}
			}
		catch
			{
			socket.Close ();
			throw;
			}
		}

	public async Task<WireMessage> SendAsync (WireMessage request)
		{
		if (Volatile.Read (ref _disposed) != 0)
			{
			throw new ObjectDisposedException (nameof (RemoteTestClient));
			}

		if (string.IsNullOrEmpty (request.RequestId))
			{
			request.RequestId = Guid.NewGuid ().ToString ("N");
			}

		if (request.TestInputs != null)
			{
			if (_sessionKey == null)
				throw new InvalidOperationException ("Update the processor package before sending test inputs.");
			request.ProtectedData = SecureTestData.Protect (_sessionKey, request, request.TestInputs);
			}
		var completion = new TaskCompletionSource<WireMessage> (TaskCreationOptions.RunContinuationsAsynchronously);
		if (!_pending.TryAdd (request.RequestId, completion))
			{
			throw new InvalidOperationException ("Duplicate request identifier.");
			}

		try
			{
			await Task.Run (() =>
			{
				if (Volatile.Read (ref _disposed) != 0)
					{
					throw new ObjectDisposedException (nameof (RemoteTestClient));
					}

				_messages.Write (request);
			}).ConfigureAwait (false);
			return await completion.Task.ConfigureAwait (false);
			}
		finally
			{
			_pending.TryRemove (request.RequestId, out _);
			}
		}

	private void Receive ()
		{
		string reason = "Disconnected from processor.";
		try
			{
			WireMessage? message;
			while ((message = _messages.Read ()) != null)
				{
				if (message.Kind is "complete" or "error" or "hello-ok" or "hello-challenge" or "cancelled")
					{
					if (_pending.TryRemove (message.RequestId, out TaskCompletionSource<WireMessage>? pending))
						{
						pending.TrySetResult (message);
						}
					}
				else
					{
					Progress?.Invoke (message);
					}
				}
			}
		catch (Exception exception)
			{
			reason = exception.Message;
			}
		finally
			{
			Dispose ();
			Disconnected?.Invoke (reason);
			}
		}

	public void Dispose ()
		{
		if (Interlocked.Exchange (ref _disposed, 1) != 0)
			{
			return;
			}

		_client.Close ();
		foreach (TaskCompletionSource<WireMessage> pending in _pending.Values)
			{
			pending.TrySetException (new IOException ("The TCP connection was closed."));
			}

		_pending.Clear ();
		}
	}