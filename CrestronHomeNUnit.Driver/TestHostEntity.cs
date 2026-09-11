// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Threading.Tasks;
using System.Net;

using Crestron.DeviceDrivers.EntityModel;
using Crestron.DeviceDrivers.EntityModel.Data;
using Crestron.DeviceDrivers.EntityModel.Logging;
using Crestron.DeviceDrivers.SDK;
using Crestron.DeviceDrivers.SDK.EntityModel;
using Crestron.DeviceDrivers.SDK.EntityModel.Attributes;

using CrestronHomeNUnit.Runtime;
using CrestronHomeNUnit.Transport;

namespace CrestronHomeNUnit.Driver;
/// <summary>Entity V2 host for the packaged suite. Commands execute away from Home callbacks.</summary>
public sealed class TestHostEntity : ReflectedAttributeDriverEntity
	{
	private readonly object _gate = new ();
	private readonly DriverControllerCreationArgs _args;
	private readonly string _workDirectory;
	private readonly TestExecutionService _service;
	private readonly PackageConfiguration _configuration;
	private readonly RemoteTestServer? _server;
	private readonly PackageAdvertisement? _advertisement;
	private readonly string _transportStatus;
	private bool _busy;
	private bool _disposed;
	private string _status = "Ready";
	private string _lastResult = "No tests run";
	private string _selfTestResult = "No tests run";
	private string _compatibilityResult = "No tests run";
	private string _discoveryResult = "Not discovered";
	public TestHostEntity (DriverControllerCreationArgs args, DriverImplementationResources resources) : base (DriverController.RootControllerId)
		{
		_args = args;
		_workDirectory = Path.Combine (args.DriverDataDirectoryPath, "TestResults");
		var ui = UiDefinitionProperty.LoadFromDirectoryIfExists (args.DriverDataDirectoryPath, resources.InitLogger, LogEntryLevel.Error) ?? throw new InvalidOperationException ("The test host UI definition is missing.");
		AddProperty (this, UiDefinitionProperty.Name, ui);
		AddCommand (this, ExtensionDoCommandExecutor.CommandName, new ExtensionDoCommandExecutor (GetCommand, resources.Logger));
		AddCommand (this, ExtensionSetPropertyValueExecutor.CommandName, new ExtensionSetPropertyValueExecutor (GetCommand, resources.Logger));
		_configuration = PackageConfiguration.Load ();
		_service = _configuration.CreateService (_workDirectory, args.DriverDataDirectoryPath);
		_service.StateChanged += OnOperationStateChanged;
		try
			{
			ProcessorIdentity identity = ProcessorIdentity.LoadOrCreate (ProcessorIdentity.SHARED_DIRECTORY);
			_server = new RemoteTestServer (_service, identity.PairingKey, _configuration.Port);
			_transportStatus = "TCP port " + _server.Port;
			try
				{
				_advertisement = new PackageAdvertisement (_configuration.Name, identity.Id, Dns.GetHostName (), _server.Port,
					message => _args.Logger?.Log (_args.DriverId, LogEntryLevel.Warning, "mDNS: " + message));
				_transportStatus += " — mDNS enabled";
				}
			catch (Exception exception)
				{
				_transportStatus += " — mDNS unavailable: " + exception.Message;
				_args.Logger?.Log (_args.DriverId, LogEntryLevel.Warning, _transportStatus);
				}
			}
		catch (Exception exception)
			{
			_transportStatus = "TCP unavailable: " + exception.Message;
			_args.Logger?.Log (_args.DriverId, LogEntryLevel.Error, _transportStatus);
			}
		}

	[EntityProperty (Id = "status", FriendlyName = "Test Host Status", Type = DriverEntityValueType.String)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string Status
		{
		get
			{
			lock (_gate)
				return _status;
			}
		}

	[EntityProperty (Id = "lastResult", FriendlyName = "Last Result", Type = DriverEntityValueType.String)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string LastResult
		{
		get
			{
			lock (_gate)
				return _lastResult;
			}
		}

	[EntityProperty (Id = "selfTestResult", FriendlyName = "Self-Test Result", Type = DriverEntityValueType.String)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string SelfTestResult
		{
		get
			{
			lock (_gate)
				{
				return _selfTestResult;
				}
			}
		}

	[EntityProperty (Id = "compatibilityResult", FriendlyName = "Compatibility Result", Type = DriverEntityValueType.String)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string CompatibilityResult
		{
		get
			{
			lock (_gate)
				{
				return _compatibilityResult;
				}
			}
		}

	[EntityProperty (Id = "discoveryResult", FriendlyName = "Discovery Result", Type = DriverEntityValueType.String)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string DiscoveryResult
		{
		get
			{
			lock (_gate)
				{
				return _discoveryResult;
				}
			}
		}

	[EntityProperty (Id = "transportStatus", FriendlyName = "Desktop Connection", Type = DriverEntityValueType.String)]
	[EntityPropertyMetadata (ExtensionUiProperty = true)]
	public string TransportStatus => _transportStatus;

	[EntityProperty (Id = "readyIndicator:isReady", Type = DriverEntityValueType.Boolean)]
	public bool IsReady
		{
		get
			{
			lock (_gate)
				return !_busy && !_disposed;
			}
		}

	[EntityProperty (Id = "onlineIndicator:isOnline", Type = DriverEntityValueType.Boolean)]
	public bool IsOnline
		{
		get
			{
			lock (_gate)
				return !_disposed;
			}
		}

	[EntityCommand (Id = "runTests", FriendlyName = "Run Tests")]
	public void RunTests () => StartOperation (false, _configuration.Suites[0].Id);
	[EntityCommand (Id = "runAdditionalTests", FriendlyName = "Run Additional Suite")]
	public void RunAdditionalTests () => StartOperation (false, _configuration.Suites.Count > 1 ? _configuration.Suites[1].Id : _configuration.Suites[0].Id);
	[EntityCommand (Id = "runCompatibilityTests", FriendlyName = "Run C# 13 Compatibility Tests")]
	public void RunCompatibilityTests () => StartOperation (false, "compatibility");
	[EntityCommand (Id = "discoverTests", FriendlyName = "Discover Tests")]
	public void DiscoverTests () => StartOperation (true, _configuration.Suites[0].Id);
	private void StartOperation (bool explore, string suite)
		{
		var request = new WireMessage
			{
			Kind = explore ? "discover" : "run",
			Suite = suite,
			RequestId = Guid.NewGuid ().ToString ("N")
			};
		_ = RunHomeAsync (request);
		}

	private async Task RunHomeAsync (WireMessage request)
		{
		try
			{
			WireMessage result = await _service.ExecuteAsync (request, _ =>
			{
			}).ConfigureAwait (false);
			if (result.Kind == "error")
				{
				_args.Logger?.Log (_args.DriverId, LogEntryLevel.Error, result.Text);
				}
			}
		catch (Exception exception)
			{
			_args.Logger?.Log (_args.DriverId, LogEntryLevel.Error, exception.ToString ());
			}
		}

	private void OnOperationStateChanged (WireMessage operation)
		{
		lock (_gate)
			{
			if (_disposed)
				{
				return;
				}

			_busy = operation.Kind == "started";
			PublishOperationResult (operation.Suite, operation.TargetId == "discover", operation.Text);
			PublishStatus (_busy ? operation.Text : operation.Kind == "error" ? "Test host error" : "Ready", operation.Suite + ": " + operation.Text);
			}

		if (operation.Kind != "started")
			{
			_args.Logger?.Log (_args.DriverId, LogEntryLevel.Info, operation.Suite + ": " + operation.Text + "; results: " + _workDirectory);
			}
		}

	// Called under _gate; each operation updates only its own result property.
	private void PublishOperationResult (string suite, bool explore, string result)
		{
		if (_disposed)
			{
			return;
			}

		string property;
		if (explore)
			{
			_discoveryResult = result;
			property = "discoveryResult";
			}
		else if (suite == _configuration.Suites[0].Id)
			{
			_selfTestResult = result;
			property = "selfTestResult";
			}
		else
			{
			_compatibilityResult = result;
			property = "compatibilityResult";
			}

		NotifyPropertyChanged (property, new DriverEntityValue (result));
		}

	// Called under _gate so disposal cannot race a property notification.
	private void PublishStatus (string status, string result)
		{
		if (_disposed)
			return;
		_status = status;
		_lastResult = result;
		NotifyPropertyChanged ("status", new DriverEntityValue (status));
		NotifyPropertyChanged ("lastResult", new DriverEntityValue (result));
		NotifyPropertyChanged ("readyIndicator:isReady", new DriverEntityValue (!_busy));
		}

	public override void Dispose ()
		{
		lock (_gate)
			_disposed = true;
		_service.StateChanged -= OnOperationStateChanged;
		try
			{
			_advertisement?.Dispose ();
			}
		catch (Exception exception) { _args.Logger?.Log (_args.DriverId, LogEntryLevel.Warning, exception.Message); }
		_server?.Dispose ();
		_service.Dispose ();
		base.Dispose ();
		}
	}