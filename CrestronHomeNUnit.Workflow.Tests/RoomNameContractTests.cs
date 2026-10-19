// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text.Json;
using CrestronHomeDevTools;
using CrestronHomeNUnit.Android;
using NUnit.Framework;

namespace CrestronHomeNUnit.Workflow.Tests;

public sealed class RoomNameContractTests
{
    static readonly DriverInfo Catalogue = new() { Id="catalogue", Model="Example Tests", Version="1.1.0.0" };
    static DeviceInfo Device(int room) => new() { Id=17, ParentDeviceId=-6, Name="Example Tests", Model="Example Tests", LocationId=room,
        PropertyValues=new() { ["cp.driverInformation:version"]=JsonSerializer.SerializeToElement("1.1.0.0"),
            ["cp.driverConfiguration:driverLoadingStatus"]=JsonSerializer.SerializeToElement("Loaded") } };

    [TestCase(12)]
    [TestCase(53089)]
    public async Task SameNamedPlanProvisionsDifferentProcessorRoomIdentities(int processorRoom)
    {
        var connection=new Connection(new ProcessorLocation[] { new(processorRoom,"Equipment Room","Room") }, Catalogue,
            new Dictionary<string,DeviceInfo>(), "Success", new {Id=17,CommissioningResult="Success"}, Device(processorRoom));
        var result=await DriverInstanceLifecycle.EnsureInRoomAsync(new(connection),"catalogue","Example Tests","equipment room",null,TimeSpan.FromSeconds(2));
        Assert.That(result.DeviceId,Is.EqualTo(17));
        Assert.That(connection.CommissionParameters!.Value.GetProperty("locationId").GetString(),Is.EqualTo(processorRoom.ToString()));
        Assert.That(JsonSerializer.Serialize(result),Does.Not.Contain("LocationId").And.Not.Contain("RoomId"));
    }

    [TestCase("missing")]
    [TestCase("ambiguous")]
    [TestCase("not-room")]
    public async Task MissingOrAmbiguousRoomStopsBeforeAnyProcessorCommand(string fault)
    {
        ProcessorLocation[] rooms=fault switch {
            "ambiguous" => [new(12,"Office","Room"),new(99,"OFFICE","Room")],
            "not-room" => [new(12,"Office","Floor")], _ => [] };
        var connection=new Connection((object)rooms);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>DriverInstanceLifecycle.EnsureInRoomAsync(new(connection),
            "catalogue","Example Tests","Office",null,TimeSpan.FromSeconds(2)));
        Assert.That(connection.Commands,Is.Empty);
        Assert.That(connection.GetPaths,Is.EqualTo(new[]{"v2/Locations"}));
    }

    [TestCase(12)]
    [TestCase(52006)]
    public async Task InstalledTargetChecksResolveNameOnActualProcessor(int processorRoom)
    {
        var connection=new Connection(new ProcessorLocation[]{new(processorRoom,"Office","Room")},Device(processorRoom));
        await ProcessorTestRooms.VerifyDeviceAsync(new(connection),17,"Office");
        Assert.That(connection.Commands,Is.Empty);
    }

    [Test]
    public async Task SameDeviceInWrongNamedRoomIsRejected()
    {
        var connection=new Connection(new ProcessorLocation[]{new(12,"Office","Room")},Device(99));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>ProcessorTestRooms.VerifyDeviceAsync(new(connection),17,"Office"));
    }

    [Test]
    public async Task EveryManagedRoomIsResolvedBeforeFirstChildMutation()
    {
        var directory=Path.Combine(TestContext.CurrentContext.WorkDirectory,"named-managed-"+Guid.NewGuid().ToString("N"));
        var connection=new Connection((object)new ProcessorLocation[]{new(12,"Office","Room")});
        NamedManagedDeviceTestTarget Target(string alias,string room)=>new(alias,new(17,"Platform","1.0.0.0",alias,"Test "+alias,"Child",room));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>ManagedDeviceValidation.RunInRoomsAsync(
            _=>Task.FromResult(new ConfigurationClient(connection)),[Target("first","Office"),Target("second","Missing")],directory,
            TimeSpan.FromSeconds(2),_=>Task.CompletedTask,(_,_)=>throw new AssertionException("No tests before all rooms resolve")));
        Assert.That(connection.Commands,Is.Empty);
        Assert.That(Directory.Exists(directory),Is.False);
    }

    [Test]
    public void PublicWorkflowAndFixtureContractsContainNamesNotNativeRoomSelectors()
    {
        Type[] contracts=[typeof(PackageBuildPlan),typeof(InstalledDriverTestTarget),typeof(AndroidManagedChildPlan),
            typeof(NetworkLinkObservationPackage),typeof(AndroidManagedDeviceBinding),typeof(NamedManagedDeviceRequest)];
        foreach(var type in contracts) {
            Assert.That(type.GetProperty("RoomName")?.PropertyType,Is.EqualTo(typeof(string)),type.FullName);
            Assert.That(type.GetProperty("LocationId"),Is.Null,type.FullName);
            Assert.That(type.GetProperty("RoomId"),Is.Null,type.FullName);
        }
        var input=new PackageBuildPlan("project","package","Tests","Equipment Room");
        Assert.That(JsonSerializer.Serialize(input),Does.Contain("Equipment Room").And.Not.Contain("LocationId").And.Not.Contain("RoomId"));
    }

    [TestCase("{\"Project\":\"p\",\"PackagePath\":\"p\",\"InstanceName\":\"Tests\",\"LocationId\":52013}")]
    [TestCase("{\"Project\":\"p\",\"PackagePath\":\"p\",\"InstanceName\":\"Tests\",\"RoomName\":\"Office\",\"LocationId\":52013}")]
    public void OldNumericSelectorsHaveNoFallback(string json)
    {
        var options=new JsonSerializerOptions {PropertyNameCaseInsensitive=true,RespectRequiredConstructorParameters=true,
            UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow};
        Assert.Throws<JsonException>(()=>JsonSerializer.Deserialize<PackageBuildPlan>(json,options));
    }

    [Test]
    public async Task ProvisioningDeadlineIncludesRoomLookupBeforeCommands()
    {
        var connection = new BlockingRoomConnection();
        await Assert.CatchAsync<OperationCanceledException>(async () =>
            await DriverInstanceLifecycle.EnsureInRoomAsync(new(connection), "catalogue", "Example Tests", "Office",
                null, TimeSpan.FromMilliseconds(100)).WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.That(connection.Commands, Is.Zero);
    }

    [Test]
    public async Task PreservationReportsUseNamesAndIgnoreUnrelatedRoomAssignments()
    {
        var connection = new Connection((object)new ProcessorLocation[] {
            new(12, "Office", "Room"), new(22, "Unrelated", "Room"), new(23, "UNRELATED", "Room") });
        var devices = new[] { Device(12), new DeviceInfo { Id=99, LocationId=999, Model="Unrelated" } };
        var names = await ProcessorTestRooms.NamesForDevicesAsync(new(connection), devices, 17);
        Assert.That(names.Count, Is.EqualTo(1));
        Assert.That(names[17], Is.EqualTo("Office"));
        Assert.That(JsonSerializer.Serialize(names), Does.Not.Contain("LocationId").And.Not.Contain("RoomId"));
    }

    sealed class BlockingRoomConnection : IConfigurationConnection
    {
        public int Commands { get; private set; }
        public async Task<T?> GetAsync<T>(string path, CancellationToken token=default) {
            await Task.Delay(Timeout.Infinite, token);
            throw new AssertionException("Cancellation must settle the room lookup.");
        }
        public Task<T?> ExecuteAsync<T>(int device, string command, object? parameters=null, CancellationToken token=default) {
            Commands++;
            throw new AssertionException("No processor command before room lookup completes.");
        }
        public Task<OperationResult> WaitForOperationAsync(string id, TimeSpan timeout, CancellationToken token=default) =>
            throw new AssertionException("No operation submitted.");
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    sealed class Connection(params object[] responses):IConfigurationConnection
    {
        readonly Queue<object> pending=new(responses);
        public List<string> Commands {get;}=[];
        public List<string> GetPaths {get;}=[];
        public JsonElement? CommissionParameters {get;private set;}
        Task<T?> Next<T>()=>Task.FromResult(JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(pending.Dequeue())));
        public Task<T?> GetAsync<T>(string path,CancellationToken token=default) {GetPaths.Add(path);return Next<T>();}
        public Task<T?> ExecuteAsync<T>(int device,string command,object? parameters=null,CancellationToken token=default) {
            Commands.Add(command);if(command.EndsWith(":commissionDevice"))CommissionParameters=JsonSerializer.SerializeToElement(parameters);
            return Next<T>();
        }
        public Task<OperationResult> WaitForOperationAsync(string id,TimeSpan timeout,CancellationToken token=default)=>
            throw new AssertionException("No operation wait expected");
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }
}
