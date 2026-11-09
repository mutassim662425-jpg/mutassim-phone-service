using Microsoft.Extensions.Logging;
using MUTASSIM.ToolPro.Core.Devices;
using MUTASSIM.ToolPro.Core.Modules;

namespace MUTASSIM.ToolPro.Infrastructure;

public sealed class ModuleRegistry(ILogger<ModuleRegistry> logger) : IModuleRegistry
{
    private readonly List<IServiceModule> _modules = [];
    private readonly object _gate = new();

    public IReadOnlyList<IServiceModule> Modules
    {
        get { lock (_gate) return _modules.ToArray(); }
    }

    public event EventHandler<IServiceModule>? ModuleRegistered;

    public void Register(IServiceModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentException.ThrowIfNullOrWhiteSpace(module.Id);

        lock (_gate)
        {
            if (_modules.Any(m => m.Id.Equals(module.Id, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"A module with id '{module.Id}' is already registered.");

            var dupService = module.Services.GroupBy(s => s.Id, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(g => g.Count() > 1);
            if (dupService is not null)
                throw new InvalidOperationException($"Module '{module.Id}' declares service '{dupService.Key}' twice.");

            _modules.Add(module);
        }

        logger.LogInformation("Module registered: {Id} ({Category}, {Count} services)",
            module.Id, module.Category, module.Services.Count);
        ModuleRegistered?.Invoke(this, module);
    }

    public IServiceModule? Find(string id)
    {
        lock (_gate) return _modules.FirstOrDefault(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<IServiceModule> ForDevice(DeviceInfo device)
    {
        var result = new List<IServiceModule>();
        foreach (var module in Modules)
        {
            try
            {
                if (module.CanHandle(device)) result.Add(module);
            }
            catch (Exception ex)
            {
                // A faulty (plugin) module must never break device handling.
                logger.LogError(ex, "Module {Id} failed in CanHandle", module.Id);
            }
        }
        return result;
    }
}
