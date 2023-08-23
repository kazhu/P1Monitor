using System.Text.Json.Serialization;
using System.Text.Json;
using P1Monitor.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using P1Monitor.Options;

namespace P1Monitor;

public interface IObisMappingsProvider
{
	ObisMappingList Mappings { get; }
}

public class ObisMappingsProvider : IObisMappingsProvider
{
	private static readonly JsonSerializerOptions Options = new() { Converters = { new JsonStringEnumConverter() }, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
	private readonly ILogger<ObisMappingsProvider> _logger;
	private readonly ObisMappingsOptions _options;
	private readonly Lazy<ObisMappingList> _mappings;

	public ObisMappingsProvider(ILogger<ObisMappingsProvider> logger, IOptions<ObisMappingsOptions> options)
	{
		_logger = logger;
		_options = options.Value;
		_mappings = new Lazy<ObisMappingList>(CreateMapping, true);
	}

	private ObisMappingList CreateMapping()
	{
		string filePath = _options.MappingFile;
		if (!Path.IsPathRooted(filePath))
		{
			filePath = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, filePath);
		}

		_logger.LogInformation("Loading mappings file {filePath}", filePath);
		string json = File.ReadAllText(filePath);
		Dictionary<string, DeviceMappingDescriptor> mappings = JsonSerializer.Deserialize<Dictionary<string, DeviceMappingDescriptor>>(json, Options)!;
		DeviceMappingDescriptor mapping = mappings[_options.DeviceName];
		_logger.LogInformation("Using {Device} device mappings", _options.DeviceName);
		return new ObisMappingList(mapping.Mapping.Select(x => new ObisMapping(x.Key, x.Value.FieldName, x.Value.Type, x.Value.Unit)).ToList());
	}

	public ObisMappingList Mappings => _mappings.Value;
}
