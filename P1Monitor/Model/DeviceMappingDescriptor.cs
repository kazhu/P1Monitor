namespace P1Monitor.Model;

public record class DeviceMappingDescriptor(string Country, string ProviderName, string Source, Dictionary<string, MappingDescriptor> Mapping);

public record class MappingDescriptor(string FieldName, DsmrType Type, DsmrUnit Unit = DsmrUnit.None);