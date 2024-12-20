using Microsoft.Extensions.Options;
using SLS4All.Compact.Configuration;
using SLS4All.Compact.McuClient.Devices;
using System.Text;

namespace SLS4All.Compact.McuClient
{
    public class FakeMcuDeviceFactoryOptions
    {
        public class FakeDevice : IOptionsItemEnable
        {
            public bool IsEnabled { get; set; } = true;
            public required string Config { get; set; }
        }

        public string Name { get; set; } = "fake";
        public Dictionary<string, FakeDevice?> Devices { get; set; } = new();
    }

    public sealed class FakeMcuDeviceFactory : IMcuDeviceFactory
    {
        public sealed class FakeDevice : IMcuDevice
        {
            private readonly FakeMcuDeviceFactory _factory;
            private readonly McuDeviceInfo _info;
            private readonly McuConfig _config;

            public IMcuDeviceFactory Factory => _factory;
            public McuDeviceInfo Info => _info;
            public McuConfig Config => _config;

            public FakeDevice(FakeMcuDeviceFactory factory, McuDeviceInfo info, McuConfig config)
            {
                _factory = factory;
                _info = info;
                _config = config;
            }

            public ValueTask<int> ReadBlock(McuCodec codec, CancellationToken cancel = default)
                => throw new NotSupportedException();

            public ValueTask<int> Read(Memory<byte> buffer, CancellationToken cancel = default)
                => throw new NotSupportedException();

            public ValueTask Write(ReadOnlyMemory<byte> buffer, CancellationToken cancel = default)
                => throw new NotSupportedException();

            public Task Flush(CancellationToken cancel = default)
                => throw new NotSupportedException();

            public void Dispose()
            {
            }
        }

        private readonly IOptionsMonitor<FakeMcuDeviceFactoryOptions> _options;

        public string FactoryName => _options.CurrentValue.Name;

        public FakeMcuDeviceFactory(
            IOptionsMonitor<FakeMcuDeviceFactoryOptions> options)
        {
            _options = options;
        }

        public ValueTask<McuDeviceInfo[]> GetDeviceNames(CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();
            var options = _options.CurrentValue;
            var res = new List<McuDeviceInfo>();
            foreach (var item in options.Devices.GetEnabledKeyValues())
            {
                res.Add(CreateDeviceInfo(item.Key));
            }
            return ValueTask.FromResult(res.ToArray());
        }

        public static McuDeviceInfo CreateDeviceInfo(string name)
            => new McuDeviceInfo(name, name, "fake://" + name, 0);

        public Task<IMcuDevice> Open(McuDeviceInfo info, CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();
            var options = _options.CurrentValue;
            var item = options.Devices[info.Name]!;
            var config = McuConfig.Parse(info.Alias, new MemoryStream(Encoding.ASCII.GetBytes(item.Config), false));
            return Task.FromResult<IMcuDevice>(new FakeDevice(this, info, config));
        }
    }
}
