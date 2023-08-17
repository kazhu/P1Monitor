namespace P1Monitor;

public record struct ObisMapping(string Id, string FieldName, Func<string, P1Value> CreateValue);