using System.Collections.Concurrent;
using i25Core.Core.Storage;
using Microsoft.Extensions.Options;

namespace i25Core.Infrastructure.Storage
{
    /// <summary>Creates the IFileStorage for one disk. Register one per driver: Local is built in; add S3, Azure, ...</summary>
    public interface IStorageDriverFactory
    {
        /// <summary>The value of Storage:Disks:{name}:Driver this factory handles (case-insensitive).</summary>
        string Driver { get; }

        IFileStorage Create(string diskName, DiskOptions options, StorageOptions storage);
    }

    public class LocalStorageDriverFactory : IStorageDriverFactory
    {
        public string Driver => "Local";

        public IFileStorage Create(string diskName, DiskOptions options, StorageOptions storage)
        {
            if (string.IsNullOrWhiteSpace(options.Root))
                throw new InvalidOperationException($"Storage disk '{diskName}' uses the Local driver but has no Root folder.");

            return new LocalFileStorage(diskName, storage.Resolve(options.Root), options.Url);
        }
    }

    public class FileStorageManager : IFileStorageManager
    {
        private readonly StorageOptions _options;
        private readonly Dictionary<string, IStorageDriverFactory> _drivers;
        private readonly ConcurrentDictionary<string, Lazy<IFileStorage>> _disks = new(StringComparer.OrdinalIgnoreCase);

        public FileStorageManager(IOptions<StorageOptions> options, IEnumerable<IStorageDriverFactory> drivers)
        {
            _options = options.Value;
            _drivers = drivers.ToDictionary(d => d.Driver, StringComparer.OrdinalIgnoreCase);

            if (!_options.EffectiveDisks.ContainsKey(_options.Default))
            {
                throw new InvalidOperationException(
                    $"Storage:Default is '{_options.Default}' but no such disk is configured. Disks: {string.Join(", ", _options.EffectiveDisks.Keys)}.");
            }
        }

        public IReadOnlyCollection<string> DiskNames => _options.EffectiveDisks.Keys.ToList();

        public IFileStorage Disk() => Disk(_options.Default);

        public IFileStorage Disk(string name)
        {
            if (!_options.EffectiveDisks.TryGetValue(name, out var disk))
            {
                throw new InvalidOperationException(
                    $"Storage disk '{name}' is not configured. Configured disks: {string.Join(", ", _options.EffectiveDisks.Keys)}.");
            }

            // Lazy: each disk is created once, even when two requests ask for it at the same moment.
            return _disks.GetOrAdd(name, _ => new Lazy<IFileStorage>(() => Create(name, disk))).Value;
        }

        private IFileStorage Create(string name, DiskOptions disk)
        {
            if (!_drivers.TryGetValue(disk.Driver, out var factory))
            {
                throw new InvalidOperationException(
                    $"Storage disk '{name}' uses the driver '{disk.Driver}', which is not registered. " +
                    $"Available drivers: {string.Join(", ", _drivers.Keys)}.");
            }

            return factory.Create(name, disk, _options);
        }
    }
}
