using System.Text.Json.Serialization;
using System.Text.Json;
using P1Monitor.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using P1Monitor.Options;
using System.Diagnostics.CodeAnalysis;

namespace P1Monitor;

public interface IObisMappingsProvider
{
	ObisMappingList Mappings { get; }
}

public class ObisMappingsProvider : IObisMappingsProvider
{
	private static readonly JsonSerializerOptions Options =
		new()
		{
			Converters = { new JsonStringEnumConverter<DsmrType>(), new JsonStringEnumConverter<DsmrUnit>() },
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
			TypeInfoResolver = SourceGenerationContext.Default
		};
	private readonly ILogger<ObisMappingsProvider> _logger;
	private readonly ObisMappingsOptions _options;
	private readonly Lazy<ObisMappingList> _mappings;

	public ObisMappingsProvider(ILogger<ObisMappingsProvider> logger, IOptions<ObisMappingsOptions> options)
	{
		_logger = logger;
		_options = options.Value;
		_mappings = new Lazy<ObisMappingList>(CreateMapping, true);
	}

    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "<Pending>")]
    [UnconditionalSuppressMessage("AOT", "IL3050:Calling members annotated with 'RequiresDynamicCodeAttribute' may break functionality when AOT compiling.", Justification = "<Pending>")]
    private ObisMappingList CreateMapping()
	{
		string filePath = _options.MappingFile;
		if (!Path.IsPathRooted(filePath))
		{
			filePath = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, filePath);
		}

		_logger.LogInformation("Loading mappings file {filePath}", filePath);
		string json = File.ReadAllText(filePath);
		var mappings = (JsonSerializer.Deserialize(json, typeof(Dictionary<string, DeviceMappingDescriptor>), Options) as Dictionary<string, DeviceMappingDescriptor>)!;
		DeviceMappingDescriptor mapping = mappings[_options.DeviceName];
		_logger.LogInformation("Using {Device} device mappings", _options.DeviceName);
		return new ObisMappingList(mapping.Mapping.Select((x, i) => new ObisMapping(x.Key, x.Value.FieldName, x.Value.Type, x.Value.Unit, i)).ToList());
	}

	public ObisMappingList Mappings => _mappings.Value;
}
