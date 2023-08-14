namespace P1Monitor;

public record class ObisMapping(string Id, string FieldName, Func<string, P1Value> CreateValue);