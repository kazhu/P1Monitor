namespace P1Monitor.Model;

public record class ObisMapping(string Id, string FieldName, DsmrType DsmrType, DsmrUnit Unit = DsmrUnit.None, int Index = 0);