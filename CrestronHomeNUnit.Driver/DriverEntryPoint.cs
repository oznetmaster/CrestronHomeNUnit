// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using Crestron.DeviceDrivers.EntityModel;
using Crestron.DeviceDrivers.SDK;
using Crestron.DeviceDrivers.SDK.EntityModel;

[assembly: DriverAssemblyEntryPoint (typeof (CrestronHomeNUnit.Driver.EntryPoint))]

namespace CrestronHomeNUnit.Driver;

public sealed class EntryPoint : DriverAssemblyEntryPoint
	{
	public override DriverController CreateDriverControllerInstance (DriverControllerCreationArgs args)
		{
		var resources = DriverImplementationResources.FromCreationArgs (args, typeof (EntryPoint));
		var driver = new TestHostEntity (args, resources);
		var rootEntity = new ConfigurableDriverEntity (driver.ControllerId, driver, null);
		return new DispatchingDeviceController (rootEntity, args, null);
		}
	}